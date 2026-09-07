using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Unity.Entities;

namespace Satisvampory.Services
{
    // Raw evidence for offline analysis, not another conveyor planner. Never claims or moves stock.
    internal static class NeedDebug
    {
        public static string Snapshot(int plot)
        {
            if (plot < 0) return JsonSerializer.Serialize(new { error = "no plot" });
            var plots = Core.TerritoryService.GetLogisticsTerritoryIds(plot);
            if (plots == null || plots.Count == 0)
                return JsonSerializer.Serialize(new { error = "no logistics plots" });
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var owner);
            var inventories = new Dictionary<Entity, Dictionary<PrefabGUID, int>>();
            Dictionary<PrefabGUID, int> Count(Entity inv)
            {
                if (!inventories.TryGetValue(inv, out var counts))
                    inventories[inv] = counts = BeltRecipe.CountAll(inv);
                return counts;
            }
            object Items(Dictionary<PrefabGUID, int> counts)
            {
                var rows = new List<object>();
                foreach (var kv in counts)
                    rows.Add(new { guid = kv.Key.GuidHash, name = StashRouting.ItemLabel(kv.Key), amount = kv.Value });
                return rows;
            }
            var stations = new List<object>();
            var sources = new List<object>();
            var plotRows = new List<object>();
            foreach (var id in plots)
            {
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwner);
                var receivers = new Dictionary<Entity, List<int>>();
                var senders = new Dictionary<Entity, List<int>>();
                void Group(Dictionary<Entity, List<int>> map, Entity entity, int group)
                {
                    if (!map.TryGetValue(entity, out var groups)) map[entity] = groups = new List<int>();
                    if (!groups.Contains(group)) groups.Add(group);
                }
                foreach (var (group, station) in Core.RefinementStations.ReceiveBenches(id)) Group(receivers, station, group);
                foreach (var (group, station) in Core.RefinementStations.SendBenches(id)) Group(senders, station, group);
                foreach (var (group, chest) in Core.Stash.SendChests(id)) Group(senders, chest, group);
                var overflow = new HashSet<Entity>(Core.Stash.OverflowChests(id));
                var seen = new HashSet<Entity>();
                foreach (var chest in Core.Stash.ChestsOnPlot(id))
                {
                    if (!seen.Add(chest) || !Core.EntityManager.Exists(chest) || chest.Has<Refinementstation>()) continue;
                    if (!StashRouting.TryGetExternalInventory(chest, out var inv)) continue;
                    var counts = Count(inv);
                    var stackable = BeltSplit.CountStackable(inv);
                    var amounts = new List<object>();
                    var isOverflow = overflow.Contains(chest);
                    foreach (var kv in counts)
                    {
                        stackable.TryGetValue(kv.Key, out var movable);
                        var reserve = Core.PlayerSettings.GetPullReserve(sourceOwner == 0 ? owner : sourceOwner, kv.Key);
                        amounts.Add(new { guid = kv.Key.GuidHash, name = StashRouting.ItemLabel(kv.Key), amount = kv.Value,
                            stackable = movable, reserve, availableAfterReserve = isOverflow ? movable : Math.Max(0, movable - reserve) });
                    }
                    senders.TryGetValue(chest, out var groups);
                    sources.Add(new { plot = id, entity = chest.ToString(), inventory = inv.ToString(), name = StashRouting.RawName(chest),
                        senderGroups = groups ?? new List<int>(), overflow = isOverflow, noShare = StashRouting.IsNoShare(chest), items = amounts });
                }
                seen.Clear();
                foreach (var station in Core.RefinementStations.BenchesOnPlot(id))
                {
                    if (!seen.Add(station) || !Core.EntityManager.Exists(station) || !station.Has<Refinementstation>()) continue;
                    var rs = station.Read<Refinementstation>();
                    var input = rs.InputInventoryEntity.GetEntityOnServer();
                    var output = rs.OutputInventoryEntity.GetEntityOnServer();
                    var held = Count(input);
                    var outputHeld = Count(output);
                    receivers.TryGetValue(station, out var receiveGroups);
                    senders.TryGetValue(station, out var sendGroups);
                    var floor = BeltRecipe.FloorScale(station);
                    var recipes = new List<object>();
                    if (station.Has<RefinementstationRecipesBuffer>())
                    foreach (var recipe in station.ReadBuffer<RefinementstationRecipesBuffer>())
                    {
                        if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var ent)
                            || !Core.EntityManager.Exists(ent))
                        {
                            recipes.Add(new { guid = recipe.RecipeGuid.GuidHash, unlocked = recipe.Unlocked, disabled = recipe.Disabled, error = "recipe prefab unavailable" });
                            continue;
                        }
                        var requirements = new List<object>();
                        if (ent.Has<RecipeRequirementBuffer>())
                        foreach (var req in ent.ReadBuffer<RecipeRequirementBuffer>())
                        {
                            if (req.Guid.GuidHash == 0) continue;
                            held.TryGetValue(req.Guid, out var have);
                            requirements.Add(new { guid = req.Guid.GuidHash, name = StashRouting.ItemLabel(req.Guid),
                                perCraft = BeltRecipe.PerCraft(req.Amount, floor), inStation = have });
                        }
                        var outputs = new List<object>();
                        if (ent.Has<RecipeOutputBuffer>())
                        foreach (var result in ent.ReadBuffer<RecipeOutputBuffer>())
                        {
                            var hasCap = Core.PlayerSettings.TryGetItemCap(owner, result.Guid, out var cap);
                            outputs.Add(new { guid = result.Guid.GuidHash, name = StashRouting.ItemLabel(result.Guid),
                                perCraft = Math.Max(1, result.Amount), cap = hasCap ? (int?)cap : null });
                        }
                        recipes.Add(new { guid = recipe.RecipeGuid.GuidHash, unlocked = recipe.Unlocked, disabled = recipe.Disabled, requirements, outputs });
                    }
                    stations.Add(new { plot = id, entity = station.ToString(), name = StashRouting.RawName(station), prefabName = station.EntityName(),
                        disabled = station.Has<Disabled>(), receiverGroups = receiveGroups ?? new List<int>(), senderGroups = sendGroups ?? new List<int>(),
                        floorScale = floor, inputInventory = input.ToString(), outputInventory = output.ToString(),
                        inputValid = Core.EntityManager.Exists(input) && input.Has<InventoryBuffer>(),
                        input = Items(held), output = Items(outputHeld), recipes });
                }
                plotRows.Add(new { plot = id, conveyorEnabled = Core.PlayerSettings.IsConveyorEnabled(sourceOwner),
                    excluded = Core.PlayerSettings.IsTerritoryClanShareExcluded(id) });
            }
            var inventoryRows = new List<object>();
            foreach (var inv in inventories.Keys)
            {
                var slots = new List<object>();
                if (Core.EntityManager.Exists(inv) && Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buffer))
                {
                    for (var i = 0; i < buffer.Length; i++)
                        slots.Add(new { slot = i, guid = buffer[i].ItemType.GuidHash, amount = buffer[i].Amount,
                            unique = !buffer[i].ItemEntity.Equals(NetworkedEntity.Empty) });
                }
                inventoryRows.Add(new { entity = inv.ToString(), slots });
            }
            return JsonSerializer.Serialize(new { schemaVersion = 1, capturedUtc = DateTime.UtcNow, plot,
                serverConveyorEnabled = Core.PlayerSettings.IsConveyorEnabled(0),
                plannerOwnerConveyorEnabled = Core.PlayerSettings.IsConveyorEnabled(owner), plots = plotRows,
                semantics = "Raw snapshot, not allocated demand. Deduplicate inventory IDs across conveyor groups. Apply shared stock claims and alternative-recipe targets before aggregating shortages. Output caps use the requested plot owner, matching the planner. Sources include non-senders for wrong-line analysis; group/overflow eligibility still applies.",
                sources, stations, inventories = inventoryRows });
        }
    }
}
