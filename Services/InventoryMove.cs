using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class InventoryMove
    {
        public static bool IsExternalInventory(Entity child)
        {
            if (child == Entity.Null || !child.Has<PrefabGUID>())
                return false;
            return child.Read<PrefabGUID>().Equals(StashService.ChestBagGuid);
        }

        public static Dictionary<PrefabGUID, List<Entity>> CollectOccupiedChests(int plot)
        {
            var byItem = new Dictionary<PrefabGUID, List<Entity>>(100);
            var seenThisChild = new HashSet<PrefabGUID>(32);
            var sgm = Core.ServerGameManager;
            foreach (var chest in Core.Stash.ChestsOnPlot(plot))
            {
                if (!sgm.TryGetBuffer<AttachedBuffer>(chest, out var attached))
                    continue;
                var n = attached.Length;
                var i = 0;
                while (i < n)
                {
                    var child = attached[i++].Entity;
                    if (!IsExternalInventory(child))
                        continue;
                    seenThisChild.Clear();
                    var slots = child.ReadBuffer<InventoryBuffer>();
                    for (var s = 0; s < slots.Length; s++)
                    {
                        var item = slots[s].ItemType;
                        if (item.GuidHash == 0 || !seenThisChild.Add(item))
                            continue;
                        if (!byItem.TryGetValue(item, out var list))
                        {
                            list = new List<Entity>();
                            byItem[item] = list;
                        }
                        list.Add(child);
                    }
                }
            }
            return byItem;
        }

        public static void DrainInventoryToMatches(Entity inventory, Dictionary<PrefabGUID, List<Entity>> matches, List<Entity> overflows)
        {
            var sgm = Core.ServerGameManager;
            if (!sgm.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return;

            var i = 0;
            while (i < slots.Length)
            {
                var slot = slots[i];
                var item = slot.ItemType;
                if (item.IsEmpty())
                {
                    i++;
                    continue;
                }

                var entityBacked = !slot.ItemEntity.Equals(NetworkedEntity.Empty);
                var remaining = sgm.GetInventoryItemCount(inventory, item);
                if (matches.TryGetValue(item, out var dests))
                    remaining = PushIntoList(sgm, inventory, item, remaining, entityBacked, dests, ref i);

                if (remaining > 0)
                    remaining = PushIntoOverflows(sgm, inventory, item, remaining, entityBacked, overflows, ref i);
                i++;
            }
        }

        static int PushIntoList(ServerGameManager sgm, Entity from, PrefabGUID item, int remaining, bool entityBacked, List<Entity> dests, ref int slotIndex)
        {
            var j = dests.Count;
            while (j > 0)
            {
                j--;
                var dest = dests[j];
                if (!Core.EntityManager.Exists(dest))
                {
                    dests.RemoveAt(j);
                    continue;
                }

                var moved = entityBacked
                    ? RelocateEntities(from, dest, item, remaining, ref slotIndex)
                    : CopyStacks(sgm, from, dest, item, remaining);
                remaining -= moved;
                if (remaining > 0)
                    dests.RemoveAt(j);
                else
                    break;
            }
            return remaining;
        }

        static int PushIntoOverflows(ServerGameManager sgm, Entity from, PrefabGUID item, int remaining, bool entityBacked, List<Entity> overflows, ref int slotIndex)
        {
            if (overflows == null || overflows.Count == 0)
                return remaining;

            ItemData data = default;
            var haveData = Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab);
            if (haveData)
                data = prefab.Read<ItemData>();

            foreach (var overflow in overflows)
            {
                if (!Core.EntityManager.Exists(overflow))
                    continue;
                if (!sgm.TryGetBuffer<InventoryInstanceElement>(overflow, out var instances))
                    continue;

                foreach (var instance in instances)
                {
                    if (!OverflowAccepts(instance, item, data))
                        continue;
                    var dest = instance.ExternalInventoryEntity.GetEntityOnServer();
                    var moved = entityBacked
                        ? RelocateEntities(from, dest, item, remaining, ref slotIndex)
                        : CopyStacks(sgm, from, dest, item, remaining);
                    remaining -= moved;
                }
                if (remaining <= 0)
                    break;
            }
            return remaining;
        }

        static bool OverflowAccepts(InventoryInstanceElement instance, PrefabGUID item, ItemData data)
        {
            if (instance.RestrictedType != PrefabGUID.Empty && instance.RestrictedType != item)
                return false;
            if (instance.RestrictedCategory != 0 && (instance.RestrictedCategory & (long)data.ItemCategory) == 0)
                return false;
            return true;
        }

        public static void DrainInventoryByName(Entity territoryHint, Entity inventory, string overflowToken)
        {
            var sgm = Core.ServerGameManager;
            var matches = new Dictionary<PrefabGUID, List<(Entity chest, Entity inv)>>(100);
            var namedOverflow = (chest: Entity.Null, inv: Entity.Null);
            var destOverflow = (chest: Entity.Null, inv: Entity.Null);
            try
            {
                var plot = Core.TerritoryService.GetTerritoryId(territoryHint);
                if (plot < 0)
                    plot = Core.TerritoryService.GetStandingTerritoryId(territoryHint);
                IEnumerable<Entity> chests = plot >= 0
                    ? Core.Stash.ChestsOnPlot(plot)
                    : Core.Stash.IslandChests(territoryHint);
                if (plot < 0)
                    DestDebugLog.Miss("servant", -1, default, 0, 0, "no-plot");

                foreach (var chest in chests)
                {
                    var plate = chest.Has<NameableInteractable>()
                        ? chest.Read<NameableInteractable>().Name.ToString().ToLower()
                        : "";
                    if (TryClaimOverflow(sgm, chest, plate, overflowToken, ref namedOverflow))
                        continue;
                    TryClaimOverflowDest(sgm, chest, plate, ref destOverflow);
                    IndexChestContents(sgm, chest, matches);
                }

                if (!sgm.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                    return;

                for (var i = 0; i < slots.Length; i++)
                {
                    var item = slots[i].ItemType;
                    var remaining = sgm.GetInventoryItemCount(inventory, item);
                    if (matches.TryGetValue(item, out var dests))
                    {
                        foreach (var dest in dests)
                        {
                            remaining -= CopyStacks(sgm, inventory, dest.inv, item, remaining);
                            if (remaining <= 0)
                                break;
                        }
                    }
                    if (remaining > 0 && namedOverflow.chest != Entity.Null)
                        remaining -= CopyStacks(sgm, inventory, namedOverflow.inv, item, remaining);
                    if (remaining > 0 && destOverflow.chest != Entity.Null)
                        CopyStacks(sgm, inventory, destOverflow.inv, item, remaining);
                }
            }
            catch (Exception e)
            {
                Core.Log.LogError($"Servant stash aborted: {e}");
            }
        }

        static bool TryClaimOverflow(ServerGameManager sgm, Entity chest, string plate, string token, ref (Entity chest, Entity inv) slot)
        {
            if (slot.chest != Entity.Null)
                return false;
            if (string.IsNullOrEmpty(token) || !plate.Contains(token))
                return false;
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, chest, out var inv))
                return true;
            if (sgm.HasFullInventory(inv))
                return true;
            slot = (chest, inv);
            return true;
        }

        static void TryClaimOverflowDest(ServerGameManager sgm, Entity chest, string plate, ref (Entity chest, Entity inv) slot)
        {
            if (slot.chest != Entity.Null)
                return;
            if (!StashRouting.IsOverflowDestName(plate))
                return;
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, chest, out var inv))
                return;
            if (sgm.HasFullInventory(inv))
                return;
            slot = (chest, inv);
        }

        static void IndexChestContents(ServerGameManager sgm, Entity chest, Dictionary<PrefabGUID, List<(Entity chest, Entity inv)>> matches)
        {
            if (!sgm.TryGetBuffer<AttachedBuffer>(chest, out var attached))
                return;
            foreach (var row in attached)
            {
                var child = row.Entity;
                if (!IsExternalInventory(child))
                    continue;
                var slots = child.ReadBuffer<InventoryBuffer>();
                for (var s = 0; s < slots.Length; s++)
                {
                    var item = slots[s].ItemType;
                    if (item.GuidHash == 0)
                        continue;
                    if (!matches.TryGetValue(item, out var list))
                    {
                        list = new List<(Entity, Entity)>();
                        matches[item] = list;
                    }
                    else
                    {
                        var already = false;
                        for (var k = 0; k < list.Count; k++)
                        {
                            if (list[k].chest == chest)
                            {
                                already = true;
                                break;
                            }
                        }
                        if (already)
                            continue;
                    }
                    list.Add((chest, child));
                }
            }
        }

        public static int CopyStacks(ServerGameManager sgm, Entity from, Entity to, PrefabGUID item, int amount)
        {
            if (amount <= 0)
                return 0;
            Core.WorkQueue?.BeginSelfTransfer();
            try
            {
                if (!sgm.TryRemoveInventoryItem(from, item, amount))
                    return 0;
                var added = sgm.TryAddInventoryItem(to, item, amount);
                int moved;
                if (added.Result == AddItemResult.Success_Complete)
                {
                    moved = amount;
                }
                else
                {
                    sgm.TryAddInventoryItem(from, item, added.RemainingAmount);
                    moved = amount - added.RemainingAmount;
                }
                if (moved > 0 && to.Has<InventoryConnection>()
                    && (Core.WorkQueue == null || !Core.WorkQueue.IsSelfTransferring))
                    Core.WorkQueue?.EnqueueOwner(to.Read<InventoryConnection>().InventoryOwner);
                return moved;
            }
            finally
            {
                Core.WorkQueue?.EndSelfTransfer();
            }
        }

        public static int RelocateEntities(Entity from, Entity to, PrefabGUID item, int amount, ref int destSlot)
        {
            if (amount <= 0)
                return 0;
            var moved = 0;
            Core.WorkQueue?.BeginSelfTransfer();
            try
            {
                var src = from.ReadBuffer<InventoryBuffer>();
                var dst = to.ReadBuffer<InventoryBuffer>();
                for (var i = 0; i < src.Length && moved < amount; i++)
                {
                    var row = src[i];
                    if (!row.ItemType.Equals(item))
                        continue;
                    if (!TryParkInEmpty(dst, ref destSlot, row, out var vacated))
                    {
                        MarkEmptyIfNeeded(from);
                        return moved;
                    }
                    src[i] = vacated;
                    BindContainer(row, to);
                    destSlot++;
                    moved++;
                }
                MarkEmptyIfNeeded(from);
                return moved;
            }
            finally
            {
                if (moved > 0 && to.Has<InventoryConnection>())
                    Core.WorkQueue?.EnqueueOwner(to.Read<InventoryConnection>().InventoryOwner);
                Core.WorkQueue?.EndSelfTransfer();
            }
        }

        static bool TryParkInEmpty(DynamicBuffer<InventoryBuffer> dst, ref int destSlot, InventoryBuffer row, out InventoryBuffer vacated)
        {
            vacated = default;
            while (destSlot < dst.Length)
            {
                if (dst[destSlot].ItemType.Equals(PrefabGUID.Empty))
                {
                    vacated = dst[destSlot];
                    dst[destSlot] = row;
                    return true;
                }
                destSlot++;
            }
            return false;
        }

        static void BindContainer(InventoryBuffer row, Entity destInventory)
        {
            var itemEntity = row.ItemEntity.GetEntityOnServer();
            if (!itemEntity.Has<InventoryItem>())
                return;
            var link = itemEntity.Read<InventoryItem>();
            link.ContainerEntity = destInventory;
            itemEntity.Write(link);
        }

        static void MarkEmptyIfNeeded(Entity inventory)
        {
            var slots = inventory.ReadBuffer<InventoryBuffer>();
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Amount != 0)
                    return;
            }

            var owner = inventory.Read<InventoryOwner>();
            owner.HasItems = false;
            inventory.Write(owner);

            var parent = inventory.Read<InventoryConnection>().InventoryOwner;
            var parentOwner = parent.Read<InventoryOwner>();
            parentOwner.HasItems = false;
            parent.Write(parentOwner);
        }

        public static AddItemSettings BuildAddSettings()
        {
            return new AddItemSettings
            {
                EntityManager = Core.EntityManager,
                ItemDataMap = Core.ServerGameManager.ItemLookupMap
            };
        }

        public static void Tell(EntityManager em, User user, string text)
        {
            var msg = new FixedString512Bytes(text);
            ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
        }

        public static bool EnclosedFloorsMatch(Entity room, CastleFloorTypes wanted)
        {
            if (!room.Has<CastleRoom>() || !room.Has<CastleRoomFloorsBuffer>())
                return false;
            if (!room.Read<CastleRoom>().IsEnclosedRoom)
                return false;

            var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(room);
            var n = floors.Length;
            for (var i = 0; i < n; i++)
            {
                var floor = floors[i].FloorEntity.GetEntityOnServer();
                if (floor.Equals(Entity.Null))
                    continue;
                if (!floor.Has<CastleFloor>())
                    return false;
                var kind = floor.Read<CastleFloor>().FloorType;
                if (kind != wanted || kind == CastleFloorTypes.UniversalFloor)
                    return false;
            }
            return true;
        }
    }
}
