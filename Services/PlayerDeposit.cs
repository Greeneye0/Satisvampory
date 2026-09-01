using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Player inventory → dest ranking. Unique-item slots copy the InventoryBuffer row;
    /// stacks use TryAddItem. Overflow is last-resort after OrderDepositDests.
    /// </summary>
    internal static class PlayerDeposit
    {
        const int ActionBarSlots = 8;

        public static void FromCharacter(Entity character, PlayerActionGate.Context ctx)
        {
            var user = ctx.User;
            var plot = ctx.StandingPlot;
            var clanWide = Core.TerritoryService.IsClanShareOn(user);
            ulong ownerId = 0;
            if (plot >= 0)
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out ownerId);
            else
            {
                var offPlot = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character);
                if (offPlot.Count > 0)
                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(offPlot[0], out ownerId);
            }

            var dests = new List<(Entity stash, Entity inventory)>(100);
            var any = false;
            var sources = clanWide ? Core.Stash.IslandChests(character) : Core.Stash.ChestsOnPlot(plot);
            foreach (var stash in sources)
            {
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                var plate = StashRouting.RawName(stash);
                if (StashRouting.IsNoShareName(plate))
                {
                    StashRouting.LogDestPick(StashRouting.SkipLabel(plate), plot, default, plate, "stash-filter");
                    continue;
                }
                if (StashRouting.IsSpecialName(plate))
                    continue;
                any = true;
                dests.Add((stash, inv));
            }
            if (!any)
            {
                PlayerActionGate.Deny(user, "Unable to stash as no available stashes found in your current territory!");
                return;
            }

            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var bag))
                return;
            var sgm = Core.ServerGameManager;
            if (!sgm.TryGetBuffer<InventoryBuffer>(bag, out var slots))
                return;

            var add = Utilities.GetAddItemSettings();
            var movedTypes = new HashSet<PrefabGUID>();
            var moved = new Dictionary<(Entity stash, PrefabGUID item), int>();
            var leftover = new Dictionary<PrefabGUID, int>();
            var overflowPlots = clanWide
                ? Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character)
                : (IReadOnlyList<int>)new[] { plot };
            var overflow = overflowPlots
                .Where(id => !TerritoryService.IsHeartRaided(Core.TerritoryService.GetCastleHeart(id)))
                .SelectMany(Core.Stash.OverflowChests)
                .OrderBy(s => StashRouting.IsSpecialName(StashRouting.RawName(s)) ? 1 : 0)
                .ThenBy(s => plot >= 0 && Core.TerritoryService.GetTerritoryId(s) == plot ? 0 : 1)
                .ToArray();

            for (var i = ActionBarSlots; i < slots.Length; i++)
            {
                var row = slots[i];
                var item = row.ItemType;
                if (item.GuidHash == 0)
                    continue;
                var unique = !row.ItemEntity.GetEntityOnServer().Equals(Entity.Null);
                if (unique)
                    PlaceUnique(row, i, bag, dests, overflow, ownerId, plot, movedTypes, moved, leftover);
                else
                    PlaceStack(ref row, i, bag, dests, overflow, ownerId, plot, add, movedTypes, moved, leftover, slots);
            }

            if (moved.Count > 0)
                PlayerActionGate.Deny(user, "Stashed items from your inventory to the current territory!");
            else
                PlayerActionGate.Deny(user, "No items were able to stash from your inventory!");

            if (Core.PlayerSettings.IsSilentStashEnabled(user.PlatformId))
                return;
            foreach (var ((stash, item), amount) in moved)
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                    $"Stashed <color=white>{amount}</color>x <color=green>{item.PrefabName()}</color> to <color=#FFC0CB>{stash.EntityName()}</color>{StashRouting.FormatBeltChat(stash, item)}");
                StashRouting.LogBeltTo(stash, item, plot, "stash");
            }
            foreach (var type in movedTypes)
            {
                if (leftover.TryGetValue(type, out var amount))
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                        $"Unable to stash <color=white>{amount}</color>x <color=green>{type.PrefabName()}</color> due to insufficient space in stashes!");
            }
        }

        static void PlaceUnique(InventoryBuffer row, int slot, Entity bag,
            List<(Entity stash, Entity inventory)> dests, Entity[] overflow, ulong ownerId, int plot,
            HashSet<PrefabGUID> movedTypes, Dictionary<(Entity stash, PrefabGUID item), int> moved,
            Dictionary<PrefabGUID, int> leftover)
        {
            var item = row.ItemType;
            var ranked = StashRouting.OrderDepositDests(dests, item, ownerId, plot);
            for (var d = 0; d < ranked.Count; d++)
            {
                try
                {
                    var dest = ranked[d];
                    var destSlots = dest.inventory.ReadBuffer<InventoryBuffer>();
                    for (var j = 0; j < destSlots.Length; j++)
                    {
                        if (!destSlots[j].ItemType.Equals(PrefabGUID.Empty))
                            continue;
                        destSlots[j] = row;
                        var ent = row.ItemEntity.GetEntityOnServer();
                        if (ent.Has<InventoryItem>())
                        {
                            var invItem = ent.Read<InventoryItem>();
                            invItem.ContainerEntity = dest.stash;
                            ent.Write(invItem);
                        }
                        Credit(dest.stash, item, 1, plot, ownerId, movedTypes, moved);
                        InventoryUtilitiesServer.ClearSlot(Core.EntityManager, bag, slot);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Core.LogException(e, "Item Entity Storage");
                }
            }

            if (TryOverflowUnique(row, slot, bag, overflow, item, plot, ownerId, movedTypes, moved))
                return;
            leftover.TryGetValue(item, out var n);
            leftover[item] = n + 1;
        }

        static bool TryOverflowUnique(InventoryBuffer row, int slot, Entity bag, Entity[] overflow,
            PrefabGUID item, int plot, ulong ownerId, HashSet<PrefabGUID> movedTypes,
            Dictionary<(Entity stash, PrefabGUID item), int> moved)
        {
            if (overflow == null || overflow.Length == 0)
                return false;
            ItemData data = default;
            if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
                data = prefab.Read<ItemData>();
            var soulshard = data.ItemCategory == ItemCategory.Soulshard;
            var sgm = Core.ServerGameManager;
            for (var o = 0; o < overflow.Length; o++)
            {
                try
                {
                    var chest = overflow[o];
                    if (!sgm.TryGetBuffer<InventoryInstanceElement>(chest, out var instances))
                        continue;
                    var inv = Entity.Null;
                    foreach (var inst in instances)
                    {
                        if (inst.RestrictedType != PrefabGUID.Empty && inst.RestrictedType != data.ItemTypeGUID
                            || inst.RestrictedCategory != 0 && (inst.RestrictedCategory & (long)data.ItemCategory) == 0
                            || soulshard && inst.RestrictedCategory == 0)
                            continue;
                        inv = inst.ExternalInventoryEntity.GetEntityOnServer();
                    }
                    if (inv == Entity.Null)
                        continue;
                    var destSlots = inv.ReadBuffer<InventoryBuffer>();
                    for (var j = 0; j < destSlots.Length; j++)
                    {
                        if (!destSlots[j].ItemType.Equals(PrefabGUID.Empty))
                            continue;
                        destSlots[j] = row;
                        var ent = row.ItemEntity.GetEntityOnServer();
                        if (ent.Has<InventoryItem>())
                        {
                            var invItem = ent.Read<InventoryItem>();
                            invItem.ContainerEntity = chest;
                            ent.Write(invItem);
                        }
                        movedTypes.Add(item);
                        moved.TryGetValue((chest, item), out var have);
                        moved[(chest, item)] = have + 1;
                        InventoryUtilitiesServer.ClearSlot(Core.EntityManager, bag, slot);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Core.LogException(e, "Overflow Item Entity Storage");
                }
            }
            return false;
        }

        static void PlaceStack(ref InventoryBuffer row, int slot, Entity bag,
            List<(Entity stash, Entity inventory)> dests, Entity[] overflow, ulong ownerId, int plot,
            AddItemSettings add, HashSet<PrefabGUID> movedTypes, Dictionary<(Entity stash, PrefabGUID item), int> moved,
            Dictionary<PrefabGUID, int> leftover, DynamicBuffer<InventoryBuffer> slots)
        {
            var item = row.ItemType;
            var ranked = StashRouting.OrderDepositDests(dests, item, ownerId, plot);
            for (var d = 0; d < ranked.Count && row.Amount > 0; d++)
            {
                try
                {
                    var dest = ranked[d];
                    var response = InventoryUtilitiesServer.TryAddItem(add, dest.inventory, row);
                    if (!response.Success)
                        continue;
                    var got = row.Amount - response.RemainingAmount;
                    Credit(dest.stash, item, got, plot, ownerId, movedTypes, moved);
                    row.Amount = response.RemainingAmount;
                    if (!response.ItemsRemaining)
                    {
                        InventoryUtilitiesServer.ClearSlot(Core.EntityManager, bag, slot);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Core.LogException(e, "Item Storage");
                }
            }

            if (row.Amount > 0)
                TryOverflowStack(ref row, slot, bag, overflow, item, plot, ownerId, add, movedTypes, moved);

            if (row.Amount > 0)
            {
                slots[slot] = row;
                leftover.TryGetValue(item, out var n);
                leftover[item] = n + row.Amount;
            }
        }

        static void TryOverflowStack(ref InventoryBuffer row, int slot, Entity bag, Entity[] overflow,
            PrefabGUID item, int plot, ulong ownerId, AddItemSettings add, HashSet<PrefabGUID> movedTypes,
            Dictionary<(Entity stash, PrefabGUID item), int> moved)
        {
            if (overflow == null || overflow.Length == 0 || row.Amount <= 0)
                return;
            ItemData data = default;
            if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
                data = prefab.Read<ItemData>();
            var soulshard = data.ItemCategory == ItemCategory.Soulshard;
            var sgm = Core.ServerGameManager;
            for (var o = 0; o < overflow.Length && row.Amount > 0; o++)
            {
                try
                {
                    var chest = overflow[o];
                    if (!sgm.TryGetBuffer<InventoryInstanceElement>(chest, out var instances))
                        continue;
                    var inv = Entity.Null;
                    foreach (var inst in instances)
                    {
                        if (inst.RestrictedType != PrefabGUID.Empty && inst.RestrictedType != data.ItemTypeGUID
                            || inst.RestrictedCategory != 0 && (inst.RestrictedCategory & (long)data.ItemCategory) == 0
                            || soulshard && inst.RestrictedCategory == 0)
                            continue;
                        inv = inst.ExternalInventoryEntity.GetEntityOnServer();
                    }
                    if (inv == Entity.Null)
                        continue;
                    var response = InventoryUtilitiesServer.TryAddItem(add, inv, row);
                    if (!response.Success)
                        continue;
                    var got = row.Amount - response.RemainingAmount;
                    movedTypes.Add(item);
                    moved.TryGetValue((chest, item), out var have);
                    moved[(chest, item)] = have + got;
                    DestDebugLog.Move("stash", plot, item, got, Entity.Null, chest, "overflow",
                        Core.PlayerSettings.GetPullReserve(ownerId, item), "stays");
                    row.Amount = response.RemainingAmount;
                    if (!response.ItemsRemaining)
                    {
                        InventoryUtilitiesServer.ClearSlot(Core.EntityManager, bag, slot);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Core.LogException(e, "Overflow Item Storage");
                }
            }
        }

        static void Credit(Entity stash, PrefabGUID item, int amount, int plot, ulong ownerId,
            HashSet<PrefabGUID> movedTypes, Dictionary<(Entity stash, PrefabGUID item), int> moved)
        {
            if (amount <= 0)
                return;
            movedTypes.Add(item);
            moved.TryGetValue((stash, item), out var have);
            moved[(stash, item)] = have + amount;
            var rank = StashRouting.RankDeposit(stash, item, ownerId, true);
            StashRouting.LogDestPickAlways(rank.Label + " class=" + rank.Class, plot, item, StashRouting.DestName(stash), "stash");
            var beltEnt = StashRouting.PredictNextBeltDest(stash, item);
            var belt = beltEnt == Entity.Null ? "stays" : StashRouting.RawName(beltEnt);
            DestDebugLog.Move("stash", plot, item, amount, Entity.Null, stash, rank.Label + "/c" + rank.Class,
                Core.PlayerSettings.GetPullReserve(ownerId, item), belt);
        }
    }
}
