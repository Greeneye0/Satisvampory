using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Per-plot conveyor / salvage / spawner / brazier work. Owned here so ConveyorService
    /// is only the territory-callback registrar + item-count snapshot used by .s conv.
    /// </summary>
    internal static class BeltRun
    {
        const int StationFeedMul = 5;
        const int SpawnerFeedMul = 2;
        const int BrazierMin = 10;

        static readonly HashSet<int> clanConsumed = new();
        static int clanGeneration = -1;
        static readonly HashSet<Entity> salvagerFull = new();
        static readonly HashSet<(Entity entity, PrefabGUID item)> salvagerFullOfItem = new();
        static readonly Dictionary<int, float> salvageDiagAt = new();
        const float SalvageDiagCooldown = 30f;

        static bool SkipClanPlot(int territoryId)
        {
            var generation = Core.WorkQueue != null ? Core.WorkQueue.DrainGeneration : 0;
            if (generation != clanGeneration)
            {
                clanConsumed.Clear();
                clanGeneration = generation;
                return false;
            }
            return clanConsumed.Contains(territoryId);
        }

        static void MarkClanSiblings(int territoryId, IReadOnlyList<int> plots)
        {
            var generation = Core.WorkQueue != null ? Core.WorkQueue.DrainGeneration : 0;
            if (generation != clanGeneration)
            {
                clanConsumed.Clear();
                clanGeneration = generation;
            }
            if (plots == null || plots.Count <= 1)
                return;
            for (var i = 0; i < plots.Count; i++)
            {
                if (plots[i] != territoryId)
                    clanConsumed.Add(plots[i]);
            }
        }

        public static IEnumerator Stations(int territoryId, Entity castleHeart)
        {
            if (!BeltOwner.TryPlatform(castleHeart, out var ownerId) || !BeltOwner.ConveyorOn(ownerId))
                yield break;
            if (SkipClanPlot(territoryId))
                yield break;

            var plots = Core.TerritoryService.GetLogisticsTerritoryIds(territoryId);
            MarkClanSiblings(territoryId, plots);

            var sgm = Core.ServerGameManager;
            var counts = Core.ConveyorService.CountTerritoryItems(plots);
            var book = new BeltBook();
            CollectStationWants(plots, ownerId, counts, book);
            CollectChestWants(plots, ownerId, counts, book, sgm);
            if (book.Count == 0)
                yield break;

            Dictionary<int, List<List<BeltSink>>> ungrouped = null;
            var overflow = CollectOverflow(plots);
            for (var i = 0; i < overflow.Length; i++)
            {
                var chest = overflow[i];
                if (!Core.EntityManager.Exists(chest))
                    continue;
                var plot = Core.TerritoryService.GetTerritoryId(chest);
                if (ClanTreasuryLend.HoldKitOverflow(plot))
                    continue;
                if (!StashRouting.TryGetExternalInventory(chest, out var inv))
                    continue;
                BeltSplit.FromOverflow(book, sgm, inv, ref ungrouped);
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }

            foreach (var plot in plots)
            {
                foreach (var (group, station) in Core.RefinementStations.GetAllSendingStations(plot))
                {
                    if (!Core.EntityManager.Exists(station) || !station.Has<Refinementstation>())
                        continue;
                    var output = station.Read<Refinementstation>().OutputInventoryEntity.GetEntityOnServer();
                    if (output == Entity.Null)
                        continue;
                    BeltSplit.FromGroup(book, sgm, group, output, overflow, ownerId, chest: false, applyReserve: false, sendingStash: default);
                    if (Core.TerritoryService.ShouldUpdateYield())
                        yield return null;
                }
            }

            var none = System.Array.Empty<Entity>();
            foreach (var plot in plots)
            {
                if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var sourceOwner))
                    sourceOwner = ownerId;
                foreach (var (group, sending) in Core.Stash.GetAllSendingStashes(plot))
                {
                    if (!Core.EntityManager.Exists(sending) || sending.Has<Refinementstation>())
                        continue;
                    if (!StashRouting.TryGetExternalInventory(sending, out var inv))
                        continue;
                    BeltSplit.FromGroup(book, sgm, group, inv, none, sourceOwner, chest: true, applyReserve: true, sendingStash: sending);
                    if (Core.TerritoryService.ShouldUpdateYield())
                        yield return null;
                }
            }
        }

        static void CollectStationWants(IReadOnlyList<int> plots, ulong ownerId, Dictionary<PrefabGUID, int> counts, BeltBook book)
        {
            foreach (var plot in plots)
            {
                foreach (var (group, station) in Core.RefinementStations.GetAllReceivingStations(plot))
                {
                    if (!station.Has<Refinementstation>() || !station.Has<CastleWorkstation>() || !station.Has<RefinementstationRecipesBuffer>())
                        continue;
                    var input = station.Read<Refinementstation>().InputInventoryEntity.GetEntityOnServer();
                    if (input == Entity.Null || !Core.EntityManager.Exists(input))
                        continue;
                    var floor = station.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
                    if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(input, out var inputSlots))
                        continue;
                    foreach (var recipe in station.ReadBuffer<RefinementstationRecipesBuffer>())
                    {
                        if (!recipe.Unlocked || recipe.Disabled)
                            continue;
                        if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var recipeEnt))
                            continue;
                        var remainingOutputs = int.MaxValue;
                        var outputPerCraft = 1;
                        var capped = false;
                        if (recipeEnt.Has<RecipeOutputBuffer>())
                        {
                            var outputs = recipeEnt.ReadBuffer<RecipeOutputBuffer>();
                            if (outputs.Length > 0 && outputs[0].Guid.GuidHash != 0
                                && Core.PlayerSettings.TryGetItemCap(ownerId, outputs[0].Guid, out var cap))
                            {
                                counts.TryGetValue(outputs[0].Guid, out var haveOut);
                                if (haveOut >= cap)
                                    continue;
                                remainingOutputs = cap - haveOut;
                                outputPerCraft = outputs[0].Amount > 0 ? outputs[0].Amount : 1;
                                capped = true;
                            }
                        }
                        if (!recipeEnt.Has<RecipeRequirementBuffer>())
                            continue;
                        foreach (var req in recipeEnt.ReadBuffer<RecipeRequirementBuffer>())
                        {
                            var perCraft = Mathf.RoundToInt(req.Amount * floor);
                            var want = StationFeedMul * perCraft;
                            if (capped)
                            {
                                var forRemaining = remainingOutputs * perCraft / outputPerCraft;
                                if (forRemaining < want)
                                    want = forRemaining;
                            }
                            for (var i = 0; i < inputSlots.Length; i++)
                            {
                                if (inputSlots[i].ItemType.Equals(req.Guid))
                                    want -= inputSlots[i].Amount;
                            }
                            if (want > 0)
                                book.Want(group, req.Guid, input, want, chest: false);
                        }
                    }
                }
            }
        }

        static void CollectChestWants(IReadOnlyList<int> plots, ulong ownerId, Dictionary<PrefabGUID, int> counts, BeltBook book, ProjectM.Scripting.ServerGameManager sgm)
        {
            var seen = new HashSet<PrefabGUID>();
            foreach (var plot in plots)
            {
                foreach (var (group, stash) in Core.Stash.GetAllReceivingStashes(plot))
                {
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                        continue;
                    if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var slots))
                        continue;
                    seen.Clear();
                    for (var i = 0; i < slots.Length; i++)
                    {
                        var item = slots[i].ItemType;
                        if (item.GuidHash == 0 || !seen.Add(item))
                            continue;
                        if (Core.PlayerSettings.TryGetItemCap(ownerId, item, out var cap))
                        {
                            counts.TryGetValue(item, out var have);
                            if (have >= cap)
                                continue;
                            book.Want(group, item, inv, cap - have, chest: true);
                            continue;
                        }
                        book.Want(group, item, inv, -1, chest: true);
                    }
                }
            }
        }

        static Entity[] CollectOverflow(IReadOnlyList<int> plots)
        {
            var list = new List<Entity>();
            foreach (var plot in plots)
            {
                foreach (var chest in Core.Stash.GetAllOverflowStashes(plot))
                    list.Add(chest);
            }
            return list.ToArray();
        }

        static bool SalvageEligible(Entity prefab)
        {
            if (prefab == Entity.Null)
                return false;
            if (prefab.Has<Salvageable>())
                return true;
            return prefab.Has<ItemData>() && prefab.Read<ItemData>().ItemType == ItemType.Tech;
        }

        static void LogSalvage(int territoryId, string message)
        {
            var now = Time.realtimeSinceStartup;
            if (salvageDiagAt.TryGetValue(territoryId, out var last) && now - last < SalvageDiagCooldown)
                return;
            salvageDiagAt[territoryId] = now;
            Core.Log.LogInfo(message);
        }

        public static IEnumerator Salvagers(int territoryId, Entity castleHeart)
        {
            if (!BeltOwner.TryPlatform(castleHeart, out var ownerId))
                yield break;
            if (!Core.PlayerSettings.IsGlobalSalvageEnabled() || !Core.PlayerSettings.GetPlotSalvageFlag(ownerId, territoryId))
                yield break;

            var suppliers = new List<Entity>(Core.Stash.GetAllSalvageStashes(territoryId));
            var stations = new List<(Entity entity, Salvagestation station, int index)>();
            var n = 0;
            foreach (var s in Core.SalvageService.GetAllSalvageStations(territoryId))
            {
                stations.Add((s, s.Read<Salvagestation>(), n));
                n++;
            }
            if (suppliers.Count == 0 && stations.Count == 0)
                yield break;

            var itemStashes = Utilities.GetItemStashesOnTerritory(territoryId);
            var overflows = new List<Entity>(Core.Stash.GetAllOverflowStashes(territoryId));
            for (var i = 0; i < stations.Count; i++)
            {
                var entity = stations[i].entity;
                if (!Core.EntityManager.Exists(entity))
                    continue;
                var output = stations[i].station.OutputInventoryEntity.GetEntityOnServer();
                if (output == Entity.Null)
                    continue;
                Utilities.StashInventoryEntity(output, itemStashes, overflows);
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }

            salvagerFull.Clear();
            salvagerFullOfItem.Clear();
            var pending = new Dictionary<PrefabGUID, (bool itemEntity, int amount)>();
            for (var s = 0; s < suppliers.Count; s++)
            {
                var supplier = suppliers[s];
                if (!StashRouting.TryGetExternalInventory(supplier, out var inv))
                    continue;
                var plate = supplier.Has<NameableInteractable>() ? supplier.Read<NameableInteractable>().Name.ToString() : "";
                var receiver = !string.IsNullOrEmpty(plate) && Core.Stash.ReceiverRegex.IsMatch(plate.ToLowerInvariant());
                pending.Clear();
                if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var slots))
                    continue;
                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot.ItemType.GuidHash == 0 || slot.Amount <= 0)
                        continue;
                    if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(slot.ItemType, out var prefab) || !SalvageEligible(prefab))
                        continue;
                    pending.TryGetValue(slot.ItemType, out var entry);
                    var amount = entry.amount;
                    if (amount == 0 && receiver)
                        amount = -1;
                    pending[slot.ItemType] = (!slot.ItemEntity.Equals(NetworkedEntity.Empty), amount + slot.Amount);
                }

                foreach (var (item, entry) in pending)
                {
                    var left = entry.amount;
                    if (left <= 0)
                        continue;
                    var hungry = stations.Count - salvagerFull.Count;
                    if (hungry == 0)
                        break;
                    for (var i = stations.Count - 1; i >= 0 && left > 0; i--)
                    {
                        if (Core.TerritoryService.ShouldUpdateYield())
                            yield return null;
                        if (!Core.EntityManager.Exists(inv))
                            break;
                        var row = stations[i];
                        if (salvagerFull.Contains(row.entity))
                            continue;
                        if (salvagerFullOfItem.Contains((row.entity, item)))
                        {
                            hungry--;
                            continue;
                        }
                        if (!Core.EntityManager.Exists(row.entity))
                        {
                            hungry--;
                            continue;
                        }
                        var input = row.station.InputInventoryEntity.GetEntityOnServer();
                        var start = 0;
                        var give = (left + (!row.station.IsWorking ? (hungry - 1) : Time.frameCount % hungry)) / hungry;
                        if (give == 0)
                            continue;
                        int got;
                        if (entry.itemEntity)
                            Utilities.TransferItemEntities(inv, input, item, give, ref start, out got);
                        else
                            got = Utilities.TransferItems(Core.ServerGameManager, inv, input, item, give);
                        hungry--;
                        if (got < give)
                        {
                            if (Core.ServerGameManager.HasFullInventory(input))
                                stations.RemoveAt(i);
                            else
                                salvagerFullOfItem.Add((row.entity, item));
                        }
                        if (got == 0)
                            continue;
                        left -= got;
                        if (!row.station.IsWorking)
                        {
                            var working = row.station;
                            working.IsWorking = true;
                            row.entity.Write(working);
                            stations[row.index] = (row.entity, working, row.index);
                        }
                    }
                }
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }

        public static IEnumerator Spawners(int territoryId, Entity castleHeart)
        {
            if (!Core.PlayerSettings.IsUnitSpawnerEnabled(0))
                yield break;
            if (!BeltOwner.TryPlatform(castleHeart, out var ownerId) || !Core.PlayerSettings.IsUnitSpawnerEnabled(ownerId))
                yield break;

            var sgm = Core.ServerGameManager;
            var wants = new Dictionary<PrefabGUID, List<(Entity receiver, int amount)>>();
            foreach (var station in Core.UnitSpawnerstationService.GetAllUnitSpawners(territoryId))
            {
                if (!station.Has<CastleWorkstation>() || !station.Has<RefinementstationRecipesBuffer>())
                    continue;
                var floor = station.Read<CastleWorkstation>().WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
                if (!StashRouting.TryGetExternalInventory(station, out var input))
                    continue;
                if (!sgm.TryGetBuffer<InventoryBuffer>(input, out var slots))
                    continue;
                foreach (var recipe in station.ReadBuffer<RefinementstationRecipesBuffer>())
                {
                    if (!recipe.Unlocked || recipe.Disabled)
                        continue;
                    if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var recipeEnt) || !recipeEnt.Has<RecipeRequirementBuffer>())
                        continue;
                    foreach (var req in recipeEnt.ReadBuffer<RecipeRequirementBuffer>())
                    {
                        var want = SpawnerFeedMul * Mathf.RoundToInt(req.Amount * floor);
                        for (var i = 0; i < slots.Length; i++)
                        {
                            if (slots[i].ItemType.Equals(req.Guid))
                                want -= slots[i].Amount;
                        }
                        if (want <= 0)
                            continue;
                        if (!wants.TryGetValue(req.Guid, out var list))
                        {
                            list = new List<(Entity, int)>();
                            wants[req.Guid] = list;
                        }
                        list.Add((input, want));
                    }
                }
            }
            if (wants.Count == 0)
                yield break;

            foreach (var chest in Core.Stash.GetAllSpawnerStashes(territoryId))
            {
                if (!StashRouting.TryGetExternalInventory(chest, out var inv))
                    continue;
                BeltSplit.Ungrouped(wants, sgm, inv, ownerId, applyReserve: true);
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }

        public static IEnumerator Braziers(int territoryId, Entity castleHeart)
        {
            if (!Core.PlayerSettings.IsBrazierEnabled(0))
                yield break;
            if (!BeltOwner.TryPlatform(castleHeart, out var ownerId) || !Core.PlayerSettings.IsBrazierEnabled(ownerId))
                yield break;

            var sgm = Core.ServerGameManager;
            var wants = new Dictionary<PrefabGUID, List<(Entity receiver, int amount)>>();
            foreach (var brazier in Core.BrazierService.GetAllBraziers(territoryId))
            {
                if (!brazier.Has<BurnContainer>() || !brazier.Has<Bonfire>())
                    continue;
                if (!brazier.Read<BurnContainer>().Enabled)
                    continue;
                if (!StashRouting.TryGetExternalInventory(brazier, out var input))
                    continue;
                if (!sgm.TryGetBuffer<InventoryBuffer>(input, out var slots))
                    continue;
                var fuel = brazier.Read<Bonfire>().InputItem;
                var have = 0;
                for (var i = 0; i < slots.Length; i++)
                {
                    if (slots[i].ItemType.Equals(fuel))
                        have += slots[i].Amount;
                }
                if (have > BrazierMin)
                    continue;
                if (!wants.TryGetValue(fuel, out var list))
                {
                    list = new List<(Entity, int)>();
                    wants[fuel] = list;
                }
                list.Add((input, BrazierMin - have));
            }
            if (wants.Count == 0)
                yield break;

            foreach (var chest in Core.Stash.GetAllBrazierStashes(territoryId))
            {
                if (!StashRouting.TryGetExternalInventory(chest, out var inv))
                    continue;
                BeltSplit.Ungrouped(wants, sgm, inv, ownerId, applyReserve: true);
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }
    }

    internal static class BeltCounts
    {
        public static Dictionary<PrefabGUID, int> OfPlot(int territoryId)
        {
            var counts = new Dictionary<PrefabGUID, int>();
            foreach (var stash in Core.Stash.GetStashesOnTerritory(territoryId))
            {
                if (StashRouting.TryGetExternalInventory(stash, out var inv))
                    Add(inv, counts);
            }
            if (Core.RefinementStations == null)
                return counts;
            foreach (var station in Core.RefinementStations.GetAllStationsOnTerritory(territoryId))
            {
                if (!station.Has<Refinementstation>())
                    continue;
                var rs = station.Read<Refinementstation>();
                Add(rs.InputInventoryEntity.GetEntityOnServer(), counts);
                Add(rs.OutputInventoryEntity.GetEntityOnServer(), counts);
            }
            return counts;
        }

        public static Dictionary<PrefabGUID, int> OfPlots(IReadOnlyList<int> plots)
        {
            var counts = new Dictionary<PrefabGUID, int>();
            if (plots == null)
                return counts;
            for (var i = 0; i < plots.Count; i++)
            {
                foreach (var (item, amount) in OfPlot(plots[i]))
                {
                    counts.TryGetValue(item, out var have);
                    counts[item] = have + amount;
                }
            }
            return counts;
        }

        static void Add(Entity inventory, Dictionary<PrefabGUID, int> counts)
        {
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return;
            for (var i = 0; i < slots.Length; i++)
            {
                var item = slots[i].ItemType;
                if (item.GuidHash == 0)
                    continue;
                counts.TryGetValue(item, out var have);
                counts[item] = have + slots[i].Amount;
            }
        }
    }
}
