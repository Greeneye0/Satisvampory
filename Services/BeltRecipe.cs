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

        public struct SourceChests
        {
            public Dictionary<int, List<(Entity stash, Entity inventory)>> Groups;
            public List<(Entity stash, Entity inventory)> Overflow;
        }

        public static SourceChests ScanSourceChests(IReadOnlyList<int> plots)
        {
            var sources = new SourceChests
            {
                Groups = new Dictionary<int, List<(Entity stash, Entity inventory)>>(),
                Overflow = new List<(Entity stash, Entity inventory)>()
            };
            if (plots == null)
                return sources;
            for (var p = 0; p < plots.Count; p++)
            {
                var plot = plots[p];
                var heart = Core.TerritoryService.GetCastleHeart(plot);
                if (heart == Entity.Null || TerritoryService.IsHeartRaided(heart))
                    continue;
                foreach (var (group, sending) in Core.Stash.SendChests(plot))
                {
                    if (!Core.EntityManager.Exists(sending) || sending.Has<Refinementstation>())
                        continue;
                    var name = StashRouting.RawName(sending);
                    if (StashRouting.IsNoShareName(name) || StashRouting.IsSpecialName(name))
                        continue;
                    if (!StashRouting.TryGetExternalInventory(sending, out var inv))
                        continue;
                    if (!sources.Groups.TryGetValue(group, out var list))
                    {
                        list = new List<(Entity stash, Entity inventory)>();
                        sources.Groups[group] = list;
                    }
                    list.Add((sending, inv));
                }
                foreach (var chest in Core.Stash.OverflowChests(plot))
                {
                    if (!StashRouting.TryGetExternalInventory(chest, out var inv))
                        continue;
                    sources.Overflow.Add((chest, inv));
                }
            }
            return sources;
        }

        public static void DumpLeftover(Entity station, Entity input, Dictionary<PrefabGUID, int> leftover,
            SourceChests sources, int group, int plot)
        {
            if (leftover == null || leftover.Count == 0 || input == Entity.Null)
                return;
            List<(Entity stash, Entity inventory)> line = null;
            sources.Groups?.TryGetValue(group, out line);
            var overflow = sources.Overflow;
            if ((line == null || line.Count == 0) && (overflow == null || overflow.Count == 0))
                return;
            var sgm = Core.ServerGameManager;
            foreach (var kv in leftover)
            {
                var item = kv.Key;
                var left = kv.Value;
                if (item.GuidHash == 0 || left <= 0)
                    continue;
                if (line != null)
                    left = PushToSources(sgm, station, input, item, left, line, plot, requireSeeded: true);
                if (left > 0 && line != null)
                    left = PushToSources(sgm, station, input, item, left, line, plot, requireSeeded: false);
                if (left > 0 && overflow != null)
                    PushToSources(sgm, station, input, item, left, overflow, plot, requireSeeded: null);
            }
        }

        static int PushToSources(ProjectM.Scripting.ServerGameManager sgm, Entity station, Entity input, PrefabGUID item, int left,
            List<(Entity stash, Entity inventory)> chests, int plot, bool? requireSeeded)
        {
            for (var i = 0; i < chests.Count && left > 0; i++)
            {
                var chest = chests[i];
                if (chest.inventory == input || chest.stash == Entity.Null || chest.inventory == Entity.Null)
                    continue;
                if (!Core.EntityManager.Exists(chest.stash) || !Core.EntityManager.Exists(chest.inventory))
                    continue;
                if (requireSeeded.HasValue)
                {
                    var has = StashRouting.InventoryHasItem(chest.inventory, item);
                    if (requireSeeded.Value != has)
                        continue;
                }
                var got = Utilities.TransferItems(sgm, input, chest.inventory, item, left);
                if (got <= 0)
                    continue;
                DestDebugLog.Move("station-input", plot, item, got, station, chest.stash, "source/s#", left - got, "stays");
                left -= got;
            }
            return left;
        }
    }
}
