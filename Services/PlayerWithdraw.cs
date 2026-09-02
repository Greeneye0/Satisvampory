using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Chest → player bag. Three SourcePass ranks (unnamed/treasury, named, s#/r#).
    /// .pull itself never honors leftover; recipe/repair/forge retrieve does.
    /// </summary>
    internal static class PlayerWithdraw
    {
        public static void Pull(Entity character, PrefabGUID item, int quantity)
        {
            if (!TryBeginPull(character, out var ctx, out var bag))
                return;
            if (!Core.GameDataSystem.ItemHashLookupMap.TryGetValue(item, out _))
            {
                PlayerActionGate.Deny(ctx.User, "Invalid item specified.");
                return;
            }

            var silent = Core.PlayerSettings.IsSilentPullEnabled(ctx.User.PlatformId);
            var got = PullFromIsland(character, ctx, bag, item, quantity, chatEachChest: !silent, out var found, out var full);
            if (!found)
                PlayerActionGate.Deny(ctx.User, "Unable to pull as no available stashes found in your current territory!");
            else if (got >= quantity)
                PlayerActionGate.Deny(ctx.User, $"Pulled {quantity}x {item.PrefabName()} from containers.");
            else
            {
                PlayerActionGate.Deny(ctx.User, $"Was able to only pull {got}x out of desired {quantity}x {item.PrefabName()} from containers.");
                DestDebugLog.Miss("pull", ctx.StandingPlot, item, got, quantity, "short steam=" + ctx.User.PlatformId);
            }
            if (full)
                PlayerActionGate.Deny(ctx.User, "Inventory is full, unable to pull more items.");
            if (found)
                PlayerActionGate.Deny(ctx.User, $"Remaining in stores: {CountStores(character, item)}");
        }

        public static void PullGroup(Entity character, string groupName, IReadOnlyList<ItemGroupService.GroupMember> members, int quantity)
        {
            if (!TryBeginPull(character, out var ctx, out var bag))
                return;
            if (members == null || members.Count == 0)
            {
                PlayerActionGate.Deny(ctx.User, $"Group <color=green>{groupName}</color> is empty.");
                return;
            }

            var silent = Core.PlayerSettings.IsSilentPullEnabled(ctx.User.PlatformId);
            var stackEach = quantity <= 1;
            var ok = 0;
            var missing = new List<string>();
            var pulled = new List<string>();
            var foundAnyChest = false;
            var full = false;
            foreach (var member in members)
            {
                if (full)
                    break;
                if (member.GuidHash == 0 || !Core.GameDataSystem.ItemHashLookupMap.TryGetValue(member.Prefab, out _))
                    continue;
                var want = stackEach ? ItemGroupService.MaxStack(member.Prefab) : quantity;
                if (want <= 0)
                    want = 1;
                var got = PullFromIsland(character, ctx, bag, member.Prefab, want, chatEachChest: false, out var found, out full);
                if (found)
                    foundAnyChest = true;
                var label = string.IsNullOrEmpty(member.Name) ? member.Prefab.PrefabName() : member.Name;
                if (got <= 0)
                    missing.Add(label);
                else
                {
                    ok++;
                    pulled.Add($"{got}x {label}");
                    if (got < want)
                        missing.Add($"{label} {got}/{want}");
                }
            }

            if (!foundAnyChest)
            {
                PlayerActionGate.Deny(ctx.User, "Unable to pull as no available stashes found in your current territory!");
                return;
            }

            var how = stackEach ? "1 stack of each" : quantity + " of each";
            PlayerActionGate.Deny(ctx.User, $"Pulled {how} from <color=green>{groupName}</color> ({ok}/{members.Count}).");
            if (!silent && pulled.Count > 0 && pulled.Count <= 16)
                PlayerActionGate.Deny(ctx.User, ItemGroupService.FormatMemberNames(pulled, 280));
            if (missing.Count > 0 && missing.Count <= 16)
                PlayerActionGate.Deny(ctx.User, "Short: " + ItemGroupService.FormatMemberNames(missing, 220));
            if (full)
                PlayerActionGate.Deny(ctx.User, "Inventory is full, unable to pull more items.");
        }

        static bool TryBeginPull(Entity character, out PlayerActionGate.Context ctx, out Entity bag)
        {
            bag = Entity.Null;
            ctx = default;
            if (Core.PlayerSettings.IsPullEnabled())
            {
                var user = character.Has<PlayerCharacter>() ? character.Read<PlayerCharacter>().UserEntity.Read<User>() : default;
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Pulling is globally disabled.");
                return false;
            }
            if (!PlayerActionGate.TryOpen(character, "pull", requireAlliedHeart: true, out ctx, out var deny))
            {
                if (ctx.UserEntity != Entity.Null)
                    PlayerActionGate.Deny(ctx.User, deny);
                return false;
            }
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out bag))
            {
                Core.Log.LogWarning($"No inventory found for character {character}.");
                return false;
            }
            return true;
        }

        static int PullFromIsland(Entity character, PlayerActionGate.Context ctx, Entity bag, PrefabGUID item, int quantity, bool chatEachChest, out bool found, out bool full)
        {
            var remaining = quantity;
            found = false;
            full = false;
            var slot = 0;
            var seen = new HashSet<Entity>();
            var sgm = Core.ServerGameManager;
            for (var pass = 0; pass < 3 && remaining > 0 && !full; pass++)
            {
                foreach (var stash in Core.Stash.IslandChests(character))
                {
                    if (remaining <= 0)
                        break;
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (StashRouting.SourcePass(stash) != pass)
                        continue;
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv) || !seen.Add(inv))
                        continue;
                    found = true;
                    var have = sgm.GetInventoryItemCount(inv, item);
                    if (have <= 0)
                        continue;
                    var take = Mathf.Min(have, remaining);
                    var got = Take(sgm, inv, bag, item, take, ref slot, ref full);
                    if (got <= 0)
                    {
                        if (full)
                            break;
                        continue;
                    }
                    if (chatEachChest)
                        Utilities.SendSystemMessageToClient(Core.EntityManager, ctx.User,
                            $"<color=white>{got}</color>x <color=green>{item.PrefabName()}</color> fetched from <color=#FFC0CB>{stash.EntityName()}</color>");
                    DestDebugLog.Move("pull", ctx.StandingPlot, item, got, stash, Entity.Null, "player", 0, "stays");
                    remaining -= got;
                    if (remaining <= 0 || full)
                        break;
                }
            }
            return quantity - remaining;
        }

        static int Take(ServerGameManager sgm, Entity from, Entity to, PrefabGUID item, int amount, ref int slot, ref bool full)
        {
            if (SlotIsEntity(sgm, from, item))
            {
                if (Utilities.TransferItemEntities(from, to, item, amount, ref slot, out var got))
                    full = true;
                return got;
            }
            return Utilities.TransferItems(sgm, from, to, item, amount);
        }

        static bool SlotIsEntity(ServerGameManager sgm, Entity inventory, PrefabGUID item)
        {
            if (!sgm.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return false;
            for (var i = 0; i < slots.Length; i++)
            {
                if (!slots[i].ItemType.Equals(item))
                    continue;
                var ent = slots[i].ItemEntity.GetEntityOnServer();
                if (!ent.Equals(Entity.Null) && Core.EntityManager.Exists(ent))
                    return true;
            }
            return false;
        }

        internal static int CountStores(Entity character, PrefabGUID item)
        {
            var n = 0;
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.IslandChests(character))
            {
                if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                    continue;
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                n += sgm.GetInventoryItemCount(inv, item);
            }
            return n;
        }

        internal static int CountTakeable(Entity character, PrefabGUID item, ulong leftoverOwnerId)
        {
            var n = 0;
            var reserve = Core.PlayerSettings.GetPullReserve(leftoverOwnerId, item);
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.IslandChests(character))
            {
                if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                    continue;
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                var have = sgm.GetInventoryItemCount(inv, item);
                if (have > reserve)
                    n += have - reserve;
            }
            return n;
        }

        public static void Recipe(Entity character, Entity workstation, PrefabGUID recipe)
        {
            if (!TryBag(character, out var user, out var bag))
                return;
            var sgm = Core.ServerGameManager;
            var stationInv = StationInventory(workstation);
            var floor = FloorMul(workstation);
            if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe, out var recipeEnt) || !recipeEnt.Has<RecipeRequirementBuffer>())
                return;
            var reqs = recipeEnt.ReadBuffer<RecipeRequirementBuffer>();
            var multiple = -1;
            foreach (var req in reqs)
            {
                var have = sgm.GetInventoryItemCount(bag, req.Guid);
                if (stationInv != Entity.Null)
                    have += sgm.GetInventoryItemCount(stationInv, req.Guid);
                var need = (int)Math.Round(req.Amount * floor, MidpointRounding.ToPositiveInfinity);
                var m = need <= 0 ? int.MaxValue : have / need;
                multiple = multiple < 0 ? m : Mathf.Min(multiple, m);
            }
            var name = RecipeLabel(recipeEnt);
            var fetchedAny = false;
            var fetchedAll = true;
            var want = multiple + 1;
            var reserve = Core.PlayerSettings.GetPullReserve(user.PlatformId);
            var silent = Core.PlayerSettings.IsSilentPullEnabled(user.PlatformId);
            foreach (var req in reqs)
            {
                Retrieve(character, workstation, user, bag, stationInv, name, reserve, silent,
                    ref fetchedAll, ref fetchedAny, req.Guid, req.Amount, want, floor, "crafting");
            }
            if (!fetchedAny)
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Couldn't find any materials for crafting additional <color=yellow>{name}</color>!");
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Have enough materials for crafting <color=white>{(fetchedAll ? want : multiple)}</color>x <color=yellow>{name}</color>.");
        }

        public static void Repair(Entity character, PrefabGUID recipe, float repairNeeded, PrefabGUID repairing)
        {
            var user = character.Read<PlayerCharacter>().UserEntity.Read<User>();
            if (recipe == PrefabGUID.Empty)
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"{repairing.PrefabName()} has no repair recipe.");
                return;
            }
            if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe, out var recipeEnt) || !recipeEnt.Has<ItemRepairBuffer>())
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"{repairing.PrefabName()} has an invalid repair recipe.");
                return;
            }
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var bag))
                return;
            var sgm = Core.ServerGameManager;
            var reqs = recipeEnt.ReadBuffer<ItemRepairBuffer>();
            var missing = false;
            foreach (var req in reqs)
            {
                var have = sgm.GetInventoryItemCount(bag, req.Guid);
                if (have < req.Stacks)
                {
                    missing = true;
                    break;
                }
            }
            if (!missing)
                return;
            if (Core.PlayerSettings.IsPullEnabled())
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Pulling is globally disabled.");
                return;
            }
            if (!PlayerActionGate.TryOpen(character, "pull", requireAlliedHeart: true, out var ctx, out var deny))
            {
                if (ctx.UserEntity != Entity.Null)
                    PlayerActionGate.Deny(ctx.User, deny);
                return;
            }
            var name = RecipeLabel(recipeEnt);
            var fetchedAny = false;
            var fetchedAll = true;
            var reserve = Core.PlayerSettings.GetPullReserve(user.PlatformId);
            var silent = Core.PlayerSettings.IsSilentPullEnabled(user.PlatformId);
            foreach (var req in reqs)
            {
                var amount = (int)Math.Ceiling(req.Stacks * (1 - repairNeeded));
                Retrieve(character, Entity.Null, user, bag, Entity.Null, name, reserve, silent,
                    ref fetchedAll, ref fetchedAny, req.Guid, amount, 1, 1, "repairing", excludeTreasuryRoom: true);
            }
        }

        public static void Forge(Entity character, Entity workstation, Entity item)
        {
            if (!TryBag(character, out var user, out var bag))
                return;
            var sgm = Core.ServerGameManager;
            var stationInv = StationInventory(workstation);
            var floor = FloorMul(workstation);
            var reqs = item.ReadBuffer<ShatteredItemRepairCost>();
            var multiple = -1;
            foreach (var req in reqs)
            {
                var have = sgm.GetInventoryItemCount(bag, req.ItemId);
                if (stationInv != Entity.Null)
                    have += sgm.GetInventoryItemCount(stationInv, req.ItemId);
                var need = (int)Math.Round(req.Amount * floor, MidpointRounding.ToPositiveInfinity);
                var m = need <= 0 ? int.MaxValue : have / need;
                multiple = multiple < 0 ? m : Mathf.Min(multiple, m);
            }
            var name = item.Read<PrefabGUID>().PrefabName();
            var fetchedAny = false;
            var fetchedAll = true;
            var want = multiple + 1;
            var reserve = Core.PlayerSettings.GetPullReserve(user.PlatformId);
            var silent = Core.PlayerSettings.IsSilentPullEnabled(user.PlatformId);
            foreach (var req in reqs)
            {
                Retrieve(character, workstation, user, bag, stationInv, name, reserve, silent,
                    ref fetchedAll, ref fetchedAny, req.ItemId, req.Amount, want, floor, "forging");
            }
            if (!fetchedAny)
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Couldn't find any materials for forging additional <color=yellow>{name}</color>!");
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Have enough materials for forging <color=white>{(fetchedAll ? want : multiple)}</color>x <color=yellow>{name}</color>.");
        }

        public static void ForgeUpgrade(Entity character, Entity workstation, Entity item)
        {
            if (!TryBag(character, out var user, out var bag))
                return;
            var sgm = Core.ServerGameManager;
            var stationInv = StationInventory(workstation);
            var tiers = item.ReadBuffer<UpgradeableLegendaryItemTiers>();
            var next = item.Read<UpgradeableLegendaryItem>();
            var req = tiers[next.NextTier];
            var have = sgm.GetInventoryItemCount(bag, req.TierPrefab);
            if (stationInv != Entity.Null)
                have += sgm.GetInventoryItemCount(stationInv, req.TierPrefab);
            var name = item.Read<PrefabGUID>().PrefabName();
            var fetchedAny = false;
            var fetchedAll = true;
            var want = have + 1;
            var reserve = Core.PlayerSettings.GetPullReserve(user.PlatformId);
            var silent = Core.PlayerSettings.IsSilentPullEnabled(user.PlatformId);
            Retrieve(character, workstation, user, bag, stationInv, name, reserve, silent,
                ref fetchedAll, ref fetchedAny, req.TierPrefab, 1, want, 1, "upgrading");
            if (!fetchedAny)
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Couldn't find any materials for upgrading additional <color=yellow>{name}</color>!");
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Have enough materials for upgrading <color=white>{(fetchedAll ? want : have)}</color>x <color=yellow>{name}</color>.");
        }

        static bool TryBag(Entity character, out User user, out Entity bag)
        {
            user = character.Read<PlayerCharacter>().UserEntity.Read<User>();
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out bag))
            {
                Core.Log.LogWarning($"No inventory found for character {character}.");
                return false;
            }
            return true;
        }

        static Entity StationInventory(Entity workstation)
        {
            if (workstation == Entity.Null)
                return Entity.Null;
            return StashRouting.TryGetExternalInventory(workstation, out var inv) ? inv : Entity.Null;
        }

        static double FloorMul(Entity workstation)
        {
            if (workstation == Entity.Null || !workstation.Has<CastleWorkstation>())
                return 1;
            return workstation.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75 : 1;
        }

        static string RecipeLabel(Entity recipeEnt)
        {
            var name = recipeEnt.Read<PrefabGUID>().LookupName();
            if (recipeEnt.Has<RecipeOutputBuffer>())
            {
                var outputs = recipeEnt.ReadBuffer<RecipeOutputBuffer>();
                if (outputs.Length > 0)
                    name = outputs[0].Guid.PrefabName();
            }
            return name;
        }

        static bool BusyWith(Entity stash, PrefabGUID item)
        {
            if (stash.Has<UnitSpawnerstation>())
            {
                var spawner = stash.Read<UnitSpawnerstation>();
                if (spawner.IsWorking && Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(spawner.CurrentRecipeGuid, out var recipe))
                {
                    if (recipe.Has<RecipeRequirementBuffer>())
                    {
                        foreach (var req in recipe.ReadBuffer<RecipeRequirementBuffer>())
                        {
                            if (req.Guid.Equals(item))
                                return true;
                        }
                    }
                }
            }
            if (stash.Has<Refinementstation>())
            {
                var rs = stash.Read<Refinementstation>();
                if (rs.IsWorking && Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(rs.CurrentRecipeGuid, out var recipe))
                {
                    if (recipe.Has<RecipeRequirementBuffer>())
                    {
                        foreach (var req in recipe.ReadBuffer<RecipeRequirementBuffer>())
                        {
                            if (req.Guid.Equals(item))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        static void Retrieve(Entity character, Entity workstation, User user, Entity bag, Entity stationInv,
            string recipeName, int reserve, bool silent, ref bool fetchedAll, ref bool fetchedAny,
            PrefabGUID item, int perCraft, int wantMultiple, double floor, string verb, bool excludeTreasuryRoom = false)
        {
            reserve = Core.PlayerSettings.GetPullReserve(user.PlatformId, item);
            var sgm = Core.ServerGameManager;
            var have = sgm.GetInventoryItemCount(bag, item);
            if (stationInv != Entity.Null)
                have += sgm.GetInventoryItemCount(stationInv, item);
            var need = wantMultiple * (int)Math.Round(perCraft * floor, MidpointRounding.ToPositiveInfinity);
            if (have >= need)
                return;
            if (!fetchedAny)
            {
                fetchedAny = true;
                if (!silent)
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Fetching materials for {verb} <color=yellow>{recipeName}</color>...");
            }
            var remaining = need - have;
            var slot = 0;
            var full = false;
            var seen = new HashSet<Entity>();
            for (var pass = 0; pass < 3 && remaining > 0 && !full; pass++)
            {
                foreach (var stash in Core.Stash.IslandChests(character))
                {
                    if (full || remaining <= 0)
                        break;
                    if (stash.Has<Refinementstation>() || stash.Equals(workstation))
                        continue;
                    if (StashRouting.SourcePass(stash) != pass)
                        continue;
                    if (BusyWith(stash, item))
                        continue;
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv) || !seen.Add(inv))
                        continue;
                    var treasury = false;
                    if (excludeTreasuryRoom && stash.Has<CastleRoomConnection>())
                    {
                        var room = stash.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
                        treasury = room != Entity.Null && Utilities.IsRoomOfType(room, CastleFloorTypes.Treasury);
                    }
                    var count = sgm.GetInventoryItemCount(inv, item);
                    if (reserve > 0)
                        count -= reserve;
                    if (count <= 0)
                        continue;
                    var take = Mathf.Min(count, remaining);
                    var got = 0;
                    if (!excludeTreasuryRoom || !treasury)
                        got = Take(sgm, inv, bag, item, take, ref slot, ref full);
                    else
                        got = take;
                    if (got <= 0)
                        continue;
                    if (!silent)
                    {
                        if (excludeTreasuryRoom && treasury)
                            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"<color=white>{got}</color>x <color=green>{item.PrefabName()}</color> used from <color=#FFC0CB>{stash.EntityName()}</color> in the Treasury Room");
                        else
                            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"<color=white>{got}</color>x <color=green>{item.PrefabName()}</color> fetched from <color=#FFC0CB>{stash.EntityName()}</color>");
                    }
                    remaining -= got;
                }
            }
            if (remaining > 0)
            {
                fetchedAll = false;
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Couldn't find <color=white>{remaining}</color>x <color=green>{item.PrefabName()}</color>");
            }
        }
    }
}
