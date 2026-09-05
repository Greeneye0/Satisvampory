using ProjectM;
using ProjectM.CastleBuilding;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Conveyor station recipes: flooring discount, complete-craft wants, leftover dump.
    /// </summary>
    internal static class BeltRecipe
    {
        public const int StationFeedMul = 5;

        public static float FloorScale(Entity station)
        {
            if (station == Entity.Null || !station.Has<CastleWorkstation>())
                return 1f;
            return station.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor)
                ? 0.75f
                : 1f;
        }

        public static int PerCraft(int recipeAmount, float floorScale)
        {
            if (recipeAmount <= 0)
                return 0;
            var n = Mathf.RoundToInt(recipeAmount * floorScale);
            return n < 1 ? 1 : n;
        }

        public static int Count(Entity inventory, PrefabGUID item)
        {
            if (inventory == Entity.Null || item.GuidHash == 0 || !Core.EntityManager.Exists(inventory))
                return 0;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return 0;
            var n = 0;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].ItemType.Equals(item))
                    n += slots[i].Amount;
            }
            return n;
        }

        public static Dictionary<PrefabGUID, int> CountAll(Entity inventory)
        {
            var amounts = new Dictionary<PrefabGUID, int>();
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return amounts;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return amounts;
            for (var i = 0; i < slots.Length; i++)
            {
                var item = slots[i].ItemType;
                if (item.GuidHash == 0)
                    continue;
                amounts.TryGetValue(item, out var have);
                amounts[item] = have + slots[i].Amount;
            }
            return amounts;
        }

        public struct SenderPools
        {
            public Dictionary<PrefabGUID, int> Overflow;
            public Dictionary<int, Dictionary<PrefabGUID, int>> Groups;

            public int Of(int group, PrefabGUID item)
            {
                var n = 0;
                if (Overflow != null && Overflow.TryGetValue(item, out var overflow))
                    n += overflow;
                if (Groups != null && Groups.TryGetValue(group, out var grouped) && grouped.TryGetValue(item, out var send))
                    n += send;
                return n;
            }
        }

        public static SenderPools ScanSenders(IReadOnlyList<int> plots, ulong fallbackOwner)
        {
            var pools = new SenderPools
            {
                Overflow = new Dictionary<PrefabGUID, int>(),
                Groups = new Dictionary<int, Dictionary<PrefabGUID, int>>()
            };
            if (plots == null)
                return pools;
            for (var p = 0; p < plots.Count; p++)
            {
                var plot = plots[p];
                if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var ownerId) || ownerId == 0)
                    ownerId = fallbackOwner;
                foreach (var chest in Core.Stash.OverflowChests(plot))
                    AddInventory(pools.Overflow, StashRouting.TryGetExternalInventory(chest, out var overflowInv) ? overflowInv : Entity.Null, ownerId, applyReserve: false);
                foreach (var (group, station) in Core.RefinementStations.SendBenches(plot))
                {
                    if (!Core.EntityManager.Exists(station) || !station.Has<Refinementstation>())
                        continue;
                    var output = station.Read<Refinementstation>().OutputInventoryEntity.GetEntityOnServer();
                    AddInventory(Stock(pools.Groups, group), output, ownerId, applyReserve: false);
                }
                foreach (var (group, sending) in Core.Stash.SendChests(plot))
                {
                    if (!Core.EntityManager.Exists(sending) || sending.Has<Refinementstation>())
                        continue;
                    if (!StashRouting.TryGetExternalInventory(sending, out var inv))
                        continue;
                    AddInventory(Stock(pools.Groups, group), inv, ownerId, applyReserve: true);
                }
            }
            return pools;
        }

        static Dictionary<PrefabGUID, int> Stock(Dictionary<int, Dictionary<PrefabGUID, int>> groups, int group)
        {
            if (!groups.TryGetValue(group, out var stock))
            {
                stock = new Dictionary<PrefabGUID, int>();
                groups[group] = stock;
            }
            return stock;
        }

        static void AddInventory(Dictionary<PrefabGUID, int> into, Entity inventory, ulong ownerId, bool applyReserve)
        {
            if (into == null || inventory == Entity.Null)
                return;
            var amounts = BeltSplit.CountStackable(inventory);
            BeltSplit.HonorReserve(amounts, ownerId, applyReserve);
            foreach (var kv in amounts)
            {
                if (kv.Value <= 0)
                    continue;
                into.TryGetValue(kv.Key, out var have);
                into[kv.Key] = have + kv.Value;
            }
        }

        public static List<(Entity stash, Entity inventory)> DestCandidates(IReadOnlyList<int> plots)
        {
            var dests = new List<(Entity stash, Entity inventory)>(100);
            if (plots == null)
                return dests;
            for (var p = 0; p < plots.Count; p++)
            {
                var plot = plots[p];
                var heart = Core.TerritoryService.GetCastleHeart(plot);
                if (heart == Entity.Null || TerritoryService.IsHeartRaided(heart))
                    continue;
                foreach (var stash in Core.Stash.ChestsOnPlot(plot))
                {
                    if (stash.Has<Refinementstation>())
                        continue;
                    var name = StashRouting.RawName(stash);
                    if (StashRouting.IsNoShareName(name) || StashRouting.IsSpecialName(name))
                        continue;
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                        continue;
                    dests.Add((stash, inv));
                }
            }
            return dests;
        }

        public static void DumpLeftover(Entity station, Entity input, Dictionary<PrefabGUID, int> leftover,
            List<(Entity stash, Entity inventory)> dests, ulong ownerId, int plot)
        {
            if (leftover == null || leftover.Count == 0 || input == Entity.Null)
                return;
            if (dests == null || dests.Count == 0)
                return;
            var sgm = Core.ServerGameManager;
            foreach (var kv in leftover)
            {
                var item = kv.Key;
                var left = kv.Value;
                if (item.GuidHash == 0 || left <= 0)
                    continue;
                var ranked = StashRouting.OrderDepositDests(dests, item, ownerId, plot);
                for (var d = 0; d < ranked.Count && left > 0; d++)
                {
                    var dest = ranked[d];
                    if (dest.inventory == input)
                        continue;
                    var got = Utilities.TransferItems(sgm, input, dest.inventory, item, left);
                    if (got <= 0)
                        continue;
                    var rank = StashRouting.RankDeposit(dest.stash, item, ownerId, true, plot);
                    DestDebugLog.Move("station-input", plot, item, got, station, dest.stash, rank.Label + "/c" + rank.Class, left - got, "stays");
                    left -= got;
                }
            }
        }
    }
}
