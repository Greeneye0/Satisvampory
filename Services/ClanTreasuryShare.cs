using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Clanshare ON: vanilla territory inventory (treasury-linked chests) counts and consumes
    /// across clan plots. Does NOT merge SharedCastleInventories buffers on castle hearts
    /// (that clones coins). Clan-wide ClanShare via GetLogisticsTerritoryIds
    /// (pull/stash/find/caps/conveyors).
    /// </summary>
    internal static class ClanTreasuryShare
    {
        // Prevents re-entry when we call vanilla MergedInventoriesUtility for sibling plots.
        [ThreadStatic] static bool suppress;

        internal static bool Suppress
        {
            get => suppress;
            set => suppress = value;
        }

        internal static int TerritoryIdFromHeart(Entity castleHeart)
        {
            if (castleHeart == Entity.Null || !Core.EntityManager.Exists(castleHeart) || !castleHeart.Has<CastleHeart>())
                return -1;
            var territoryEntity = castleHeart.Read<CastleHeart>().CastleTerritoryEntity;
            if (territoryEntity == Entity.Null || !Core.EntityManager.Exists(territoryEntity) || !territoryEntity.Has<CastleTerritory>())
                return -1;
            return territoryEntity.Read<CastleTerritory>().CastleTerritoryIndex;
        }

        internal static Entity HeartFromTarget(Entity target)
        {
            if (target == Entity.Null || !Core.EntityManager.Exists(target))
                return Entity.Null;
            if (target.Has<PlayerCharacter>())
            {
                var standing = Core.TerritoryService.GetStandingTerritoryId(target);
                if (standing >= 0)
                    return Core.TerritoryService.GetCastleHeart(standing);
                return Entity.Null;
            }
            if (target.Has<CastleHeartConnection>())
            {
                var connected = target.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
                if (connected != Entity.Null && Core.EntityManager.Exists(connected) && connected.Has<CastleHeart>())
                    return connected;
            }
            var territoryId = Core.TerritoryService.GetTerritoryId(target);
            if (territoryId < 0)
                return Entity.Null;
            return Core.TerritoryService.GetCastleHeart(territoryId);
        }
        internal static bool ShouldShare(Entity standingHeart)
        {
            if (!Core.HasInitialized || standingHeart == Entity.Null)
                return false;
            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
                return false;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            return ids != null && ids.Count > 1;
        }

        internal static bool IsTreasuryLinked(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                return false;
            // Vanilla HUD counts chests on treasury tiles even when the room is mixed
            // or not fully enclosed. MatchingFloorType is the workstation bonus flag.
            if (stash.Has<CastleWorkstation>()
                && stash.Read<CastleWorkstation>().MatchingFloorType == CastleFloorTypes.Treasury)
                return true;
            if (!stash.Has<CastleRoomConnection>())
                return false;
            var room = stash.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
            if (room == Entity.Null || !Core.EntityManager.Exists(room))
                return false;
            if (Utilities.IsRoomOfType(room, CastleFloorTypes.Treasury))
                return true;
            if (!room.Has<CastleRoomFloorsBuffer>())
                return false;
            var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(room);
            for (var i = 0; i < floors.Length; i++)
            {
                var floorEntity = floors[i].FloorEntity.GetEntityOnServer();
                if (floorEntity == Entity.Null || !Core.EntityManager.Exists(floorEntity) || !floorEntity.Has<CastleFloor>())
                    continue;
                if (floorEntity.Read<CastleFloor>().FloorType == CastleFloorTypes.Treasury)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Temporary concatenation of vanilla per-heart merged inventory copies.
        /// Does not write SharedCastleInventories on any heart.
        /// </summary>
        internal static NativeArray<InventoryBuffer> CombineMerged(
            EntityManager entityManager,
            Entity standingHeart,
            Entity target,
            bool includeCurrentInteractingInventory,
            bool includeCastleSharedInventories)
        {
            var parts = new List<NativeArray<InventoryBuffer>>();
            try
            {
                var local = MergedInventoriesUtility.GetCastleMergedInventoryDatas(
                    entityManager, standingHeart, target,
                    includeCurrentInteractingInventory, includeCastleSharedInventories);
                parts.Add(local);

                var standingId = TerritoryIdFromHeart(standingHeart);
                var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
                if (ids != null)
                {
                    foreach (var id in ids)
                    {
                        if (id == standingId)
                            continue;
                        var otherHeart = Core.TerritoryService.GetCastleHeart(id);
                        if (otherHeart == Entity.Null)
                            continue;
                        // Sibling plots: shared treasury only. Do not re-count the acting inventory.
                        var extra = MergedInventoriesUtility.GetCastleMergedInventoryDatas(
                            entityManager, otherHeart, target, false, true);
                        parts.Add(extra);
                    }
                }

                var total = 0;
                foreach (var part in parts)
                {
                    if (part.IsCreated)
                        total += part.Length;
                }

                var combined = new NativeArray<InventoryBuffer>(total, Allocator.Temp);
                var offset = 0;
                foreach (var part in parts)
                {
                    if (!part.IsCreated)
                        continue;
                    for (var i = 0; i < part.Length; i++)
                        combined[offset++] = part[i];
                }
                return combined;
            }
            finally
            {
                foreach (var part in parts)
                {
                    if (part.IsCreated)
                        part.Dispose();
                }
            }
        }

        /// <summary>
        /// Spend remainder from clan dest chests. Standing plot first (no reserve), then
        /// siblings (source-plot reserve). Named belts included. Remove only; never add.
        /// </summary>
        internal static void ConsumeRemainderFromClanDests(Entity standingHeart, PrefabGUID type, ref int remainder)
        {
            if (remainder <= 0)
                return;

            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
                return;

            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            if (ids == null || ids.Count == 0)
                ids = new[] { standingId };

            ConsumeRemainderFromPlots(ids, standingId, type, standingFirst: true, ref remainder);
        }

        /// Spend remainder from sibling clan treasury-linked chests. Remove only; never add.
        /// Per-chest leftover uses the SOURCE plot heart-owner reserve so we do not drain
        /// B's reserved mats. Standing plot is skipped (vanilla already consumed it).
        /// </summary>
        internal static void ConsumeRemainderFromOtherTreasuries(Entity standingHeart, PrefabGUID type, ref int remainder)
        {
            ConsumeRemainderFromClanDests(standingHeart, type, ref remainder);
        }

        static void ConsumeRemainderFromPlots(IReadOnlyList<int> ids, int standingId, PrefabGUID type, bool standingFirst, ref int remainder)
        {
            var serverGameManager = Core.ServerGameManager;
            for (var pass = 0; pass < 2 && remainder > 0; pass++)
            {
                foreach (var id in ids)
                {
                    if (remainder <= 0)
                        break;
                    var isStanding = id == standingId;
                    if (standingFirst)
                    {
                        if (pass == 0 && !isStanding)
                            continue;
                        if (pass == 1 && isStanding)
                            continue;
                    }
                    else if (isStanding)
                        continue;

                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                    var reserve = isStanding ? 0 : Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);

                    foreach (var stash in Core.Stash.GetStashesOnTerritory(id))
                    {
                        if (remainder <= 0)
                            break;
                        if (stash.Has<Refinementstation>())
                            continue;
                        if (StashRouting.IsNoShare(stash))
                            continue;
                        if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                            continue;

                        foreach (var attachedBuffer in buffer)
                        {
                            if (remainder <= 0)
                                break;

                            var inventory = attachedBuffer.Entity;
                            if (!inventory.Has<PrefabGUID>())
                                continue;
                            if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                                continue;

                            var count = serverGameManager.GetInventoryItemCount(inventory, type);
                            if (reserve > 0)
                                count -= reserve;
                            if (count <= 0)
                                continue;

                            var take = count < remainder ? count : remainder;
                            if (take <= 0)
                                continue;

                            if (serverGameManager.TryRemoveInventoryItem(inventory, type, take))
                                remainder -= take;
                        }
                    }
                }
            }
        }

        static int CountMergedAmount(NativeArray<InventoryBuffer> merged, PrefabGUID type)
        {
            var count = 0;
            if (!merged.IsCreated)
                return 0;
            for (var i = 0; i < merged.Length; i++)
            {
                if (merged[i].ItemType.Equals(type))
                    count += merged[i].Amount;
            }
            return count;
        }

        /// <summary>
        /// Sibling treasury-floor chests only. Named wood/ore chests are ignored (vanilla build
        /// cannot see them either). Leftover reserve is subtracted per SOURCE plot owner.
        /// </summary>
        internal static int CountSiblingTreasuryAvailable(Entity standingHeart, PrefabGUID type)
        {
            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
                return 0;

            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            if (ids == null || ids.Count <= 1)
                return 0;

            var total = 0;
            var serverGameManager = Core.ServerGameManager;
            foreach (var id in ids)
            {
                if (id == standingId)
                    continue;

                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);

                foreach (var stash in Core.Stash.GetStashesOnTerritory(id))
                {
                    if (!IsTreasuryLinked(stash))
                        continue;
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                        continue;

                    foreach (var attachedBuffer in buffer)
                    {
                        var inventory = attachedBuffer.Entity;
                        if (!inventory.Has<PrefabGUID>())
                            continue;
                        if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                            continue;

                        var count = serverGameManager.GetInventoryItemCount(inventory, type);
                        var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                        if (reserve > 0)
                            count -= reserve;
                        if (count > 0)
                            total += count;
                    }
                }
            }
            return total;
        }

        internal const int ReinforcedPlankHash = -1397591435;
        // GroundScoop PrefabNames.json / English.json "Iron Ingot"
        internal const int IronIngotHash = -1750550553;

        /// <summary>
        /// ALL clan plots (including standing). Treasury-floor chests only, leftover
        /// subtracted per SOURCE plot the same way HasEnough/consume does.
        /// Keyed by PrefabGUID.GuidHash. Does not write SharedCastleInventories.
        /// Always unions vanilla MergedInventoriesUtility per clan heart
        /// (includeCastleSharedInventories true) plus leftover, then spreads
        /// Iron Ingot / Reinforced Plank counts across name/GUID aliases.
        /// </summary>
        internal static Dictionary<int, int> CountAllClanTreasuryAvailable(Entity standingHeart)
        {
            return CountAllClanTreasuryAvailable(standingHeart, Entity.Null);
        }

        internal static Dictionary<int, int> CountAllClanTreasuryAvailable(Entity standingHeart, Entity target)
        {
            var totals = new Dictionary<int, int>();
            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
                return totals;

            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            if (ids == null || ids.Count == 0)
                return totals;

            var serverGameManager = Core.ServerGameManager;
            foreach (var id in ids)
            {
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);

                foreach (var stash in Core.Stash.GetStashesOnTerritory(id))
                {
                    if (!IsTreasuryLinked(stash))
                        continue;
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                        continue;

                    foreach (var attachedBuffer in buffer)
                    {
                        var inventory = attachedBuffer.Entity;
                        if (!inventory.Has<PrefabGUID>())
                            continue;
                        if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                            continue;
                        if (!serverGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var items))
                            continue;

                        var seen = new HashSet<int>();
                        for (var i = 0; i < items.Length; i++)
                        {
                            var type = items[i].ItemType;
                            if (type.GuidHash == 0)
                                continue;
                            if (!seen.Add(type.GuidHash))
                                continue;

                            var count = serverGameManager.GetInventoryItemCount(inventory, type);
                            var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                            if (reserve > 0)
                                count -= reserve;
                            if (count <= 0)
                                continue;

                            if (totals.TryGetValue(type.GuidHash, out var existing))
                                totals[type.GuidHash] = existing + count;
                            else
                                totals[type.GuidHash] = count;
                        }
                    }
                }
            }

            MergeSharedCastleInventoryItems(ids, totals);
            MergeVanillaMergedAvailable(standingHeart, target, totals);
            HarvestHeartAttachedInventories(ids, totals);
            HarvestAllStashes(ids, totals);
            UnionNameAliases(totals);

            return totals;
        }

        /// <summary>
        /// Same per-heart merged inventory vanilla build HUD uses
        /// (includeCastleSharedInventories true). Suppress so ClanShare patch does
        /// not concatenate siblings (we sum hearts ourselves). Leftover subtracted
        /// per SOURCE plot owner.
        /// </summary>
        static void MergeSharedCastleInventoryItems(System.Collections.Generic.IReadOnlyList<int> ids, Dictionary<int, int> totals)
        {
            if (ids == null || ids.Count == 0 || totals == null)
                return;

            var shared = new Dictionary<int, int>();
            foreach (var id in ids)
            {
                var heart = Core.TerritoryService.GetCastleHeart(id);
                if (heart == Entity.Null)
                    continue;
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                var local = new Dictionary<int, int>();
                CollectSharedCastleItems(heart, local);
                foreach (var kv in local)
                {
                    var count = kv.Value;
                    var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, new PrefabGUID(kv.Key));
                    if (reserve > 0)
                        count -= reserve;
                    if (count <= 0)
                        continue;
                    if (shared.TryGetValue(kv.Key, out var existing))
                        shared[kv.Key] = existing + count;
                    else
                        shared[kv.Key] = count;
                }
            }

            foreach (var kv in shared)
            {
                if (totals.TryGetValue(kv.Key, out var existing))
                {
                    if (kv.Value > existing)
                        totals[kv.Key] = kv.Value;
                }
                else
                    totals[kv.Key] = kv.Value;
            }
        }

        static int _sharedLog;

        static void CollectSharedCastleItems(Entity heart, Dictionary<int, int> local)
        {
            var nInst = 0;
            var nAdded = 0;
            try
            {
                if (Core.ServerGameManager.TryGetBuffer<SharedCastleInventoryInstances>(heart, out var insts))
                {
                    nInst = insts.Length;
                    for (var i = 0; i < insts.Length; i++)
                        nAdded += AddSharedItemBuffer(insts[i].Entity.GetEntityOnServer(), local);
                }
            }
            catch (Exception e)
            {
                if (_sharedLog < 4)
                    Core.Log.LogWarning("[ClanTreasuryHUD] SharedCastleInventoryInstances: " + e.Message);
            }
            try
            {
                nAdded += AddSharedItemBuffer(heart, local);
            }
            catch
            {
            }
            try
            {
                if (Core.ServerGameManager.TryGetBuffer<SharedCastleInventories>(heart, out var srcs))
                {
                    for (var i = 0; i < srcs.Length; i++)
                    {
                        var src = srcs[i].InventorySource;
                        if (src == Entity.Null || !Core.EntityManager.Exists(src))
                            continue;
                        nAdded += AddSharedItemBuffer(src, local);
                    }
                }
            }
            catch (Exception e)
            {
                if (_sharedLog < 4)
                    Core.Log.LogWarning("[ClanTreasuryHUD] SharedCastleInventories: " + e.Message);
            }

            if (_sharedLog < 8)
            {
                _sharedLog++;
                local.TryGetValue(IronIngotHash, out var iron);
                local.TryGetValue(ReinforcedPlankHash, out var plank);
                var ironish = 0;
                foreach (var kv in local)
                {
                    var name = PrefabName(kv.Key);
                    if (IsIronName(name))
                        ironish++;
                }
                Core.Log.LogInfo($"[ClanTreasuryHUD] sharedHeart inst={nInst} added={nAdded} keys={local.Count} ironCanonical={iron} plankCanonical={plank} ironNameKeys={ironish}");
            }
        }

        static int AddSharedItemBuffer(Entity e, Dictionary<int, int> local)
        {
            if (e == Entity.Null || !Core.EntityManager.Exists(e))
                return 0;
            var added = 0;
            try
            {
                if (Core.ServerGameManager.TryGetBuffer<SharedCastleInventoryItems>(e, out var items))
                {
                    for (var i = 0; i < items.Length; i++)
                    {
                        var type = items[i].ItemType;
                        var amt = items[i].Amount;
                        if (type.GuidHash == 0 || amt <= 0)
                            continue;
                        if (local.TryGetValue(type.GuidHash, out var n))
                            local[type.GuidHash] = n + amt;
                        else
                            local[type.GuidHash] = amt;
                        added++;
                    }
                }
            }
            catch { }
            try
            {
                if (Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(e, out var inv))
                {
                    for (var i = 0; i < inv.Length; i++)
                    {
                        var type = inv[i].ItemType;
                        var amt = inv[i].Amount;
                        if (type.GuidHash == 0 || amt <= 0)
                            continue;
                        if (local.TryGetValue(type.GuidHash, out var n))
                            local[type.GuidHash] = n + amt;
                        else
                            local[type.GuidHash] = amt;
                        added++;
                    }
                }
            }
            catch { }
            return added;
        }

        internal static string DebugIronKeys(Dictionary<int, int> totals)
        {
            if (totals == null || totals.Count == 0)
                return "ironKeys=0";
            var parts = new List<string>();
            foreach (var kv in totals)
            {
                var name = PrefabName(kv.Key);
                if (kv.Key == IronIngotHash || IsIronName(name))
                    parts.Add(kv.Key + "=" + kv.Value + ":" + (name ?? "?"));
            }
            return "ironKeys=" + parts.Count + (parts.Count == 0 ? "" : " " + string.Join(",", parts));
        }

        static int _harvestLog;

        static void HarvestHeartAttachedInventories(System.Collections.Generic.IReadOnlyList<int> ids, Dictionary<int, int> totals)
        {
            if (ids == null || totals == null)
                return;
            var extra = new Dictionary<int, int>();
            foreach (var id in ids)
            {
                var heart = Core.TerritoryService.GetCastleHeart(id);
                if (heart == Entity.Null)
                    continue;
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                var local = new Dictionary<int, int>();
                try
                {
                    if (Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(heart, out var buffer))
                    {
                        foreach (var attachedBuffer in buffer)
                            AddSharedItemBuffer(attachedBuffer.Entity, local);
                    }
                }
                catch { }
                AddSharedItemBuffer(heart, local);
                foreach (var kv in local)
                {
                    var count = kv.Value;
                    var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, new PrefabGUID(kv.Key));
                    if (reserve > 0)
                        count -= reserve;
                    if (count <= 0)
                        continue;
                    if (extra.TryGetValue(kv.Key, out var existing))
                        extra[kv.Key] = existing + count;
                    else
                        extra[kv.Key] = count;
                }
            }
            MaxInto(totals, extra);
            if (_harvestLog < 4)
            {
                extra.TryGetValue(IronIngotHash, out var ironCanon);
                var ironish = 0;
                var ironN = 0;
                foreach (var kv in extra)
                {
                    var name = PrefabName(kv.Key);
                    if (kv.Key == IronIngotHash || IsIronName(name))
                    {
                        ironish++;
                        if (kv.Value > ironN)
                            ironN = kv.Value;
                    }
                }
                Core.Log.LogInfo($"[ClanTreasuryHUD] heartAttached keys={extra.Count} ironCanonical={ironCanon} ironNamed={ironish} ironBest={ironN}");
            }
        }

        static void HarvestAllStashes(System.Collections.Generic.IReadOnlyList<int> ids, Dictionary<int, int> totals)
        {
            if (ids == null || totals == null)
                return;
            var extra = new Dictionary<int, int>();
            var serverGameManager = Core.ServerGameManager;
            foreach (var id in ids)
            {
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                foreach (var stash in Core.Stash.GetStashesOnTerritory(id))
                {
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                        continue;
                    foreach (var attachedBuffer in buffer)
                    {
                        var inventory = attachedBuffer.Entity;
                        AddSharedItemBuffer(inventory, extra);
                    }
                    AddSharedItemBuffer(stash, extra);
                }
            }
            // leftover already applied in treasury walk; this is MAX so we pick up iron
            // sitting in non-treasury chests / unnamed buffers vanilla HUD can see.
            var before = totals.Count;
            MaxInto(totals, extra);
            if (_harvestLog < 4)
            {
                _harvestLog++;
                var ironN = 0;
                var ironish = 0;
                foreach (var kv in extra)
                {
                    var name = PrefabName(kv.Key);
                    if (kv.Key == IronIngotHash || IsIronName(name))
                    {
                        ironish++;
                        if (kv.Value > ironN)
                            ironN = kv.Value;
                    }
                }
                Core.Log.LogInfo($"[ClanTreasuryHUD] allStash keys={extra.Count} ironNamed={ironish} ironBest={ironN} totals={totals.Count} (was {before})");
            }
        }

        static void MaxInto(Dictionary<int, int> totals, Dictionary<int, int> extra)
        {
            if (totals == null || extra == null)
                return;
            foreach (var kv in extra)
            {
                if (kv.Value <= 0)
                    continue;
                if (totals.TryGetValue(kv.Key, out var existing))
                {
                    if (kv.Value > existing)
                        totals[kv.Key] = kv.Value;
                }
                else
                    totals[kv.Key] = kv.Value;
            }
        }

        static void MergeVanillaMergedAvailable(Entity standingHeart, Entity target, Dictionary<int, int> totals)
        {
            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
                return;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            if (ids == null || ids.Count == 0)
                return;

            if (target == Entity.Null || !Core.EntityManager.Exists(target))
                target = standingHeart;

            var saved = Suppress;
            Suppress = true;
            try
            {
                var mergedTotals = new Dictionary<int, int>();
                foreach (var id in ids)
                {
                    var heart = Core.TerritoryService.GetCastleHeart(id);
                    if (heart == Entity.Null)
                        continue;
                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);

                    var merged = MergedInventoriesUtility.GetCastleMergedInventoryDatas(
                        Core.EntityManager, heart, target, id == standingId, true);
                    try
                    {
                        var local = new Dictionary<int, int>();
                        if (merged.IsCreated)
                        {
                            for (var i = 0; i < merged.Length; i++)
                            {
                                var type = merged[i].ItemType;
                                if (type.GuidHash == 0 || merged[i].Amount <= 0)
                                    continue;
                                if (local.TryGetValue(type.GuidHash, out var n))
                                    local[type.GuidHash] = n + merged[i].Amount;
                                else
                                    local[type.GuidHash] = merged[i].Amount;
                            }
                        }
                        foreach (var kv in local)
                        {
                            var count = kv.Value;
                            var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, new PrefabGUID(kv.Key));
                            if (reserve > 0)
                                count -= reserve;
                            if (count <= 0)
                                continue;
                            if (mergedTotals.TryGetValue(kv.Key, out var existing))
                                mergedTotals[kv.Key] = existing + count;
                            else
                                mergedTotals[kv.Key] = count;
                        }
                    }
                    finally
                    {
                        if (merged.IsCreated)
                            merged.Dispose();
                    }
                }

                foreach (var kv in mergedTotals)
                {
                    if (totals.TryGetValue(kv.Key, out var existing))
                    {
                        if (kv.Value > existing)
                            totals[kv.Key] = kv.Value;
                    }
                    else
                        totals[kv.Key] = kv.Value;
                }
            }
            finally
            {
                Suppress = saved;
            }
        }


        static bool IsIronName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.Equals("Iron Ingot", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Iron Ingot", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("IronIngot", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // 1.1 prefab is Item_Ingredient_Mineral_IronBar, not IronIngot.
            if (name.IndexOf("IronBar", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Mineral_IronBar", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        static bool IsPlankName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.Equals("Reinforced Plank", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Reinforced Plank", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return name.IndexOf("ReinforcedPlank", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string PrefabName(int guid)
        {
            try
            {
                var pg = new PrefabGUID(guid);
                var n = Core.Localization?.GetPrefabName(pg);
                if (!string.IsNullOrEmpty(n))
                    return n;
                return pg.LookupName();
            }
            catch
            {
                return null;
            }
        }

        static void UnionNameAliases(Dictionary<int, int> totals)
        {
            if (totals == null || totals.Count == 0)
                return;

            var iron = new HashSet<int> { IronIngotHash };
            var plank = new HashSet<int> { ReinforcedPlankHash };
            foreach (var key in new List<int>(totals.Keys))
            {
                var name = PrefabName(key);
                if (IsIronName(name))
                    iron.Add(key);
                if (IsPlankName(name))
                    plank.Add(key);
            }
            SpreadMax(totals, iron);
            SpreadMax(totals, plank);
        }

        static void SpreadMax(Dictionary<int, int> totals, HashSet<int> group)
        {
            var best = 0;
            foreach (var k in group)
            {
                if (totals.TryGetValue(k, out var n) && n > best)
                    best = n;
            }
            if (best <= 0)
                return;
            foreach (var k in group)
                totals[k] = best;
        }

        static IEnumerable<int> AliasesFor(int guid, Dictionary<int, int> totals)
        {
            yield return guid;
            if (guid == IronIngotHash || guid == ReinforcedPlankHash)
            {
                if (totals != null)
                {
                    foreach (var key in totals.Keys)
                    {
                        if (key == guid)
                            continue;
                        var name = PrefabName(key);
                        if (guid == IronIngotHash && IsIronName(name))
                            yield return key;
                        if (guid == ReinforcedPlankHash && IsPlankName(name))
                            yield return key;
                    }
                }
                yield break;
            }

            var reqName = PrefabName(guid);
            if (IsIronName(reqName))
            {
                yield return IronIngotHash;
                if (totals != null)
                {
                    foreach (var key in totals.Keys)
                    {
                        if (key != guid && IsIronName(PrefabName(key)))
                            yield return key;
                    }
                }
            }
            if (IsPlankName(reqName))
            {
                yield return ReinforcedPlankHash;
                if (totals != null)
                {
                    foreach (var key in totals.Keys)
                    {
                        if (key != guid && IsPlankName(PrefabName(key)))
                            yield return key;
                    }
                }
            }
        }

        static int CountMergedAmountAliased(NativeArray<InventoryBuffer> merged, PrefabGUID type)
        {
            var count = CountMergedAmount(merged, type);
            if (!merged.IsCreated)
                return count;
            var reqName = PrefabName(type.GuidHash);
            var iron = type.GuidHash == IronIngotHash || IsIronName(reqName);
            var plank = type.GuidHash == ReinforcedPlankHash || IsPlankName(reqName);
            if (!iron && !plank)
                return count;
            var extra = 0;
            for (var i = 0; i < merged.Length; i++)
            {
                var other = merged[i].ItemType;
                if (other.Equals(type) || other.GuidHash == 0)
                    continue;
                var name = PrefabName(other.GuidHash);
                if ((iron && (other.GuidHash == IronIngotHash || IsIronName(name)))
                    || (plank && (other.GuidHash == ReinforcedPlankHash || IsPlankName(name))))
                    extra += merged[i].Amount;
            }
            return count + extra;
        }

        static DateTime bagAvailAt;
        static Entity bagAvailChar;
        static readonly Dictionary<int, int> bagAvail = new();
        static readonly Dictionary<int, int> buildAvail = new();
        static DateTime buildAvailAt;
        static Entity buildAvailHeart;
        static Entity buildAvailChar;

        struct PlotCountCache
        {
            public DateTime At;
            public Dictionary<int, int> Counts;
        }
        static readonly Dictionary<int, PlotCountCache> plotAvail = new();

        internal static void InvalidateBuildAvailable()
        {
            bagAvailAt = DateTime.MinValue;
            bagAvail.Clear();
            buildAvail.Clear();
            buildAvailAt = DateTime.MinValue;
            plotAvail.Clear();
        }

        static void AddStacks(Entity inventory, Dictionary<int, int> dest)
        {
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var buf))
                return;
            for (var i = 0; i < buf.Length; i++)
            {
                var g = buf[i].ItemType.GuidHash;
                var n = buf[i].Amount;
                if (g == 0 || n <= 0)
                    continue;
                if (dest.TryGetValue(g, out var have))
                    dest[g] = have + n;
                else
                    dest[g] = n;
            }
        }

        static Dictionary<int, int> EnsurePlotCounts(int plotId)
        {
            var now = DateTime.UtcNow;
            if (plotAvail.TryGetValue(plotId, out var cached)
                && (now - cached.At).TotalSeconds < 0.25 && cached.Counts != null)
                return new Dictionary<int, int>(cached.Counts);

            var counts = new Dictionary<int, int>();
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plotId))
            {
                if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                    continue;
                if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var inventory = attachedBuffer.Entity;
                    if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                        continue;
                    if (!inventory.Has<PrefabGUID>())
                        continue;
                    if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                        continue;
                    AddStacks(inventory, counts);
                }
            }
            plotAvail[plotId] = new PlotCountCache { At = now, Counts = counts };
            return new Dictionary<int, int>(counts);
        }

        static void EnsureBuildAvailable(Entity standingHeart, Entity character)
        {
            var now = DateTime.UtcNow;
            if (standingHeart == buildAvailHeart && character == buildAvailChar
                && (now - buildAvailAt).TotalSeconds < 0.25 && buildAvail.Count > 0)
                return;

            if (character != bagAvailChar || (now - bagAvailAt).TotalSeconds >= 0.25)
            {
                bagAvail.Clear();
                bagAvailChar = character;
                bagAvailAt = now;
                if (character != Entity.Null && Core.EntityManager.Exists(character)
                    && InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var bag)
                    && bag != Entity.Null)
                    AddStacks(bag, bagAvail);
            }

            buildAvail.Clear();
            foreach (var kv in bagAvail)
                buildAvail[kv.Key] = kv.Value;

            var standingId = TerritoryIdFromHeart(standingHeart);
            if (standingId < 0)
            {
                buildAvailHeart = standingHeart;
                buildAvailChar = character;
                buildAvailAt = now;
                return;
            }
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standingId);
            if (ids == null || ids.Count == 0)
                ids = new[] { standingId };

            foreach (var id in ids)
            {
                var plotCounts = EnsurePlotCounts(id);
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                foreach (var kv in plotCounts)
                {
                    var n = kv.Value;
                    if (id != standingId)
                    {
                        var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, new PrefabGUID(kv.Key));
                        if (reserve > 0)
                            n -= reserve;
                    }
                    if (n <= 0)
                        continue;
                    if (buildAvail.TryGetValue(kv.Key, out var have))
                        buildAvail[kv.Key] = have + n;
                    else
                        buildAvail[kv.Key] = n;
                }
            }
            buildAvailHeart = standingHeart;
            buildAvailChar = character;
            buildAvailAt = now;
        }

        static int CountBuildAvailable(Entity standingHeart, Entity character, PrefabGUID type)
        {
            EnsureBuildAvailable(standingHeart, character);
            if (buildAvail.TryGetValue(type.GuidHash, out var n))
                return n;
            return 0;
        }

        static int CountBuildAvailableAliased(Entity standingHeart, Entity character, PrefabGUID type)
        {
            var total = CountBuildAvailable(standingHeart, character, type);
            var reqName = PrefabName(type.GuidHash);
            var iron = type.GuidHash == IronIngotHash || IsIronName(reqName);
            var plank = type.GuidHash == ReinforcedPlankHash || IsPlankName(reqName);
            if (!iron && !plank)
                return total;
            if (iron && type.GuidHash != IronIngotHash)
                total += CountBuildAvailable(standingHeart, character, new PrefabGUID(IronIngotHash));
            if (plank && type.GuidHash != ReinforcedPlankHash)
                total += CountBuildAvailable(standingHeart, character, new PrefabGUID(ReinforcedPlankHash));
            return total;
        }

        internal static bool HasEnoughForBuild(
            EntityManager entityManager,
            Entity standingHeart,
            Entity character,
            NativeParallelHashMap<PrefabGUID, int> requirements)
        {
            if (!requirements.IsCreated)
                return false;

            var keys = requirements.GetKeyArray(Allocator.Temp);
            try
            {
                // Standing plot: bags + every dest (named included), no reserve.
                // Sibling plots: dests minus that plot's reserve. 1.1 HUD still only
                // paints treasury; this yes-vote is what lets the server accept the place.
                for (var i = 0; i < keys.Length; i++)
                {
                    var type = keys[i];
                    if (!requirements.TryGetValue(type, out var needed) || needed <= 0)
                        continue;
                    var have = CountBuildAvailableAliased(standingHeart, character, type);
                    if (have < needed)
                    {
                        DestDebugLog.Miss("build", TerritoryIdFromHeart(standingHeart), type, have, needed, "has-enough");
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                if (keys.IsCreated)
                    keys.Dispose();
            }
        }

        internal static bool ConsumeBuildRequirements(
            EntityManager entityManager,
            MapZoneCollection mapZoneCollection,
            Entity standingHeart,
            Entity character,
            NativeParallelHashMap<PrefabGUID, int> requirements)
        {
            if (!requirements.IsCreated)
                return true;

            var paid = true;
            InvalidateBuildAvailable();
            var keys = requirements.GetKeyArray(Allocator.Temp);
            try
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    var type = keys[i];
                    if (!requirements.TryGetValue(type, out var needed) || needed <= 0)
                        continue;

                    // Force includeCastleSharedInventories so local treasury is consumed and
                    // existing RemoveItemGetRemainder ClanShare remainder hook can run.
                    // 1.6.1.21: if prison iron GUID differs from canonical, spend aliases.
                    MergedInventoriesUtility.RemoveItemGetRemainder(
                        entityManager, mapZoneCollection, character, type, needed,
                        out var remainder, true, true, true);
                    if (remainder > 0)
                        ConsumeRemainderFromClanDests(standingHeart, type, ref remainder);
                    if (remainder > 0)
                    {
                        foreach (var alias in AliasesFor(type.GuidHash, null))
                        {
                            if (alias == type.GuidHash || remainder <= 0)
                                continue;
                            var at = new PrefabGUID(alias);
                            MergedInventoriesUtility.RemoveItemGetRemainder(
                                entityManager, mapZoneCollection, character, at, remainder,
                                out var rem2, true, true, true);
                            remainder = rem2;
                            if (remainder > 0)
                                ConsumeRemainderFromClanDests(standingHeart, at, ref remainder);
                        }
                    }
                    if (remainder > 0)
                    {
                        paid = false;
                        Core.Log.LogWarning($"[ClanTreasuryHUD] consume leftover guid={type.GuidHash} remainder={remainder} needed={needed}");
                    }

                }
            }
            finally
            {
                if (keys.IsCreated)
                    keys.Dispose();
            }
            return paid;
        }
    }
}


