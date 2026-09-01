using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// One conveyor sink: a station input or an r# chest inventory that still wants an item.
    /// Wanted -1 means "fill this seeded chest" (no numeric cap).
    /// </summary>
    internal struct BeltSink
    {
        public Entity Inventory;
        public PrefabGUID Item;
        public int Wanted;
        public bool Chest;

        public BeltSink(Entity inventory, PrefabGUID item, int wanted, bool chest)
        {
            Inventory = inventory;
            Item = item;
            Wanted = wanted;
            Chest = chest;
        }

        public bool Unlimited => Wanted < 0;
        public bool StillOpen => Wanted != 0;
    }

    /// <summary>
    /// Grouped station/chest demand for one conveyor pass. Keyed by (s#/r# group, item hash)
    /// so overflow can flatten across groups while senders stay group-local.
    /// </summary>
    internal sealed class BeltBook
    {
        readonly Dictionary<(int group, int hash), List<BeltSink>> grouped = new();

        public int Count => grouped.Count;

        public void Want(int group, PrefabGUID item, Entity inventory, int amount, bool chest)
        {
            if (inventory == Entity.Null || item.GuidHash == 0)
                return;
            var key = (group, item.GuidHash);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<BeltSink>();
                grouped[key] = list;
            }
            list.Add(new BeltSink(inventory, item, amount, chest));
        }

        public bool TryGrouped(int group, PrefabGUID item, out List<BeltSink> sinks)
        {
            return grouped.TryGetValue((group, item.GuidHash), out sinks);
        }

        public Dictionary<int, List<List<BeltSink>>> FlattenByItem()
        {
            var map = new Dictionary<int, List<List<BeltSink>>>();
            foreach (var kv in grouped)
            {
                var hash = kv.Key.hash;
                if (!map.TryGetValue(hash, out var lists))
                {
                    lists = new List<List<BeltSink>>();
                    map[hash] = lists;
                }
                lists.Add(kv.Value);
            }
            return map;
        }
    }

    internal static class BeltOwner
    {
        public static bool TryPlatform(Entity castleHeart, out ulong platformId)
        {
            platformId = 0;
            if (castleHeart == Entity.Null || !castleHeart.Has<UserOwner>())
                return false;
            var owner = castleHeart.Read<UserOwner>().Owner.GetEntityOnServer();
            if (owner == Entity.Null || !owner.Has<User>())
                return false;
            platformId = owner.Read<User>().PlatformId;
            return true;
        }

        public static bool ConveyorOn(ulong platformId)
        {
            return Core.PlayerSettings.IsConveyorEnabled(0) && Core.PlayerSettings.IsConveyorEnabled(platformId);
        }
    }

    internal static class BeltInspect
    {
        const int ConvFeedMultiplier = 5;

        /// <summary>
        /// READ-ONLY snapshot of conveyor feed for crafted product X.
        /// Does not call TransferItems, DistributeInventory, or write stashes.
        /// Territory set matches conveyor feed: GetLogisticsTerritoryIds(standingPlot).
        /// </summary>
        public static List<string> Product(int standingTerritoryId, PrefabGUID product)
        {
            var lines = new List<string>();
            var itemName = StashRouting.ItemLabel(product);

            if (standingTerritoryId < 0)
            {
                lines.Add("You must stand on a castle plot to troubleshoot conveyors.");
                return lines;
            }

            var logisticsIds = Core.TerritoryService.GetLogisticsTerritoryIds(standingTerritoryId);
            if (logisticsIds == null || logisticsIds.Count == 0)
            {
                lines.Add("You must stand on a castle plot to troubleshoot conveyors.");
                return lines;
            }

            var castleHeartEntity = Core.TerritoryService.GetCastleHeart(standingTerritoryId);
            ulong platformID = 0;
            if (castleHeartEntity != Entity.Null && Core.EntityManager.Exists(castleHeartEntity) && castleHeartEntity.Has<UserOwner>())
            {
                var ownerEntity = castleHeartEntity.Read<UserOwner>().Owner.GetEntityOnServer();
                if (ownerEntity != Entity.Null && Core.EntityManager.Exists(ownerEntity) && ownerEntity.Has<User>())
                    platformID = ownerEntity.Read<User>().PlatformId;
            }

            var conveyorOn = Core.PlayerSettings.IsConveyorEnabled(0) &&
                             (platformID == 0 || Core.PlayerSettings.IsConveyorEnabled(platformID));

            var territoryCounts = BeltCounts.OfPlots(logisticsIds);
            territoryCounts.TryGetValue(product, out var haveProduct);
            var hasPlotCap = Core.PlayerSettings.TryGetItemCap(platformID, product, out var plotCapAmt);

            var stations = new List<Entity>();
            var seenStations = new HashSet<Entity>();
            foreach (var logisticsId in logisticsIds)
            {
                foreach (var station in Core.RefinementStations.GetAllStationsOnTerritory(logisticsId))
                {
                    if (!Core.EntityManager.Exists(station)) continue;
                    if (station.Has<Disabled>()) continue;
                    if (!station.Has<Refinementstation>() || !station.Has<RefinementstationRecipesBuffer>()) continue;
                    if (!seenStations.Add(station)) continue;
                    stations.Add(station);
                }
            }

            var matched = new List<(Entity station, string stationName, HashSet<int> groups, bool recipeOn, bool plotCap, bool outputFull, int? capNumber, List<PrefabGUID> inputs, Dictionary<PrefabGUID, bool> inputAtFeedCap, Dictionary<PrefabGUID, int> stationHave)>();

            foreach (var station in stations)
            {
                if (!TryDescribeStationRecipe(station, product, platformID, haveProduct, hasPlotCap, plotCapAmt,
                        out var recipeOn, out var plotCap, out var outputFull, out var capNumber, out var inputs, out var inputAtFeedCap, out var stationHave))
                    continue;

                var stationName = station.EntityName();
                var groups = CollectReceiverGroups(station);
                matched.Add((station, stationName, groups, recipeOn, plotCap, outputFull, capNumber, inputs, inputAtFeedCap, stationHave));
            }

            if (matched.Count == 0)
            {
                lines.Add($"No fabricator/station on these plots is set up to make {itemName}.");
                return lines;
            }

            // Recipe-ON stations first so an off recipe on another plot does not lead the chat.
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var (station, stationName, groups, recipeOn, plotCap, outputFull, capNumber, inputs, inputAtFeedCap, stationHave) in matched)
                {
                    if (pass == 0 && !recipeOn) continue;
                    if (pass == 1 && recipeOn) continue;

                    if (!recipeOn)
                        lines.Add($"{stationName} is not making {itemName}. Recipe is off.");
                    else if (plotCap)
                    {
                        if (capNumber.HasValue)
                            lines.Add($"{stationName} is making {itemName}. Not taking more, plot cap {capNumber.Value}.");
                        else
                            lines.Add($"{stationName} is making {itemName}. Not taking more, plot cap.");
                    }
                    else if (outputFull)
                        lines.Add($"{stationName} is making {itemName}. Not taking more, output full.");
                    else
                        lines.Add($"{stationName} is making {itemName}.");

                    if (groups.Count == 0)
                        continue;

                    var senders = CollectMatchingSenders(logisticsIds, groups);
                    var receivingStation = station.Read<Refinementstation>();
                    var inputInventory = receivingStation.InputInventoryEntity.GetEntityOnServer();

                    foreach (var input in inputs)
                    {
                        var inputName = StashRouting.ItemLabel(input);
                        var thisInputAtFeedCap = inputAtFeedCap.TryGetValue(input, out var atCap) && atCap;
                        var canAccept = InventoryCanAccept(inputInventory, input);

                        foreach (var (stash, stashName, stashTerritoryId) in senders)
                        {
                            SnapshotStashItem(stash, input, out var have, out var stackableHave);
                            if (have <= 0) continue;

                            if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(stashTerritoryId, out var sourceOwnerId))
                                sourceOwnerId = platformID;
                            var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, input);
                            var sendable = Math.Max(0, have - reserve);
                            var uniqueBlocked = stackableHave <= 0;

                            string why;
                            if (!recipeOn)
                                why = "Not moving, recipe off.";
                            else if (plotCap)
                                why = "Not moving, plot cap.";
                            else if (outputFull)
                                why = "Not moving, output full.";
                            else if (thisInputAtFeedCap)
                            {
                                stationHave.TryGetValue(input, out var haveInStation);
                                why = $"Ready to move more, station already has {haveInStation}.";
                            }
                            else if (sendable <= 0)
                                why = $"Not moving, reserve {reserve}.";
                            else if (!conveyorOn)
                                why = "Not moving, conveyor off.";
                            else if (uniqueBlocked)
                                why = "Not moving, unique.";
                            else if (!canAccept)
                                why = "Not moving, no room.";
                            else
                                why = $"Sending to {stationName}.";

                            lines.Add($"{inputName}: {stashName} has {have}. {why}");
                        }
                    }
                }
            }

            return lines;
        }

        static bool TryDescribeStationRecipe(Entity station, PrefabGUID product, ulong platformID, int haveProduct, bool hasPlotCap, int plotCapAmt,
            out bool recipeOn, out bool plotCap, out bool outputFull, out int? capNumber, out List<PrefabGUID> inputs, out Dictionary<PrefabGUID, bool> inputAtFeedCap, out Dictionary<PrefabGUID, int> stationHave)
        {
            recipeOn = false;
            plotCap = false;
            outputFull = false;
            capNumber = null;
            inputs = new List<PrefabGUID>();
            inputAtFeedCap = new Dictionary<PrefabGUID, bool>();
            stationHave = new Dictionary<PrefabGUID, int>();

            var recipesBuffer = station.ReadBuffer<RefinementstationRecipesBuffer>();
            var matchFloorReduction = 1f;
            if (station.Has<CastleWorkstation>())
            {
                var castleWorkstation = station.Read<CastleWorkstation>();
                if (castleWorkstation.WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor))
                    matchFloorReduction = 0.75f;
            }

            var receivingStation = station.Read<Refinementstation>();
            var inputInventoryEntity = receivingStation.InputInventoryEntity.GetEntityOnServer();
            var outputInventoryEntity = receivingStation.OutputInventoryEntity.GetEntityOnServer();
            DynamicBuffer<InventoryBuffer> inputInv = default;
            var haveInputInv = Core.EntityManager.Exists(inputInventoryEntity) &&
                               Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inputInventoryEntity, out inputInv);

            var enabledInputs = new List<PrefabGUID>();
            var disabledInputs = new List<PrefabGUID>();
            var seenEnabled = new HashSet<int>();
            var seenDisabled = new HashSet<int>();
            var anyEnabledUncapped = false;
            var anyEnabledCapped = false;
            var anyDisabled = false;
            var wantedByInput = new Dictionary<PrefabGUID, int>();

            foreach (var recipe in recipesBuffer)
            {
                if (!recipe.Unlocked) continue;
                if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var recipeEntity))
                    continue;
                if (recipeEntity == Entity.Null || !Core.EntityManager.Exists(recipeEntity)) continue;
                if (!recipeEntity.Has<RecipeOutputBuffer>()) continue;

                var outputs = recipeEntity.ReadBuffer<RecipeOutputBuffer>();
                var produces = false;
                var outputPerCraft = 1;
                for (var i = 0; i < outputs.Length; i++)
                {
                    var recipeOutput = outputs[i];
                    if (recipeOutput.Guid.Equals(product))
                    {
                        produces = true;
                        outputPerCraft = recipeOutput.Amount;
                        if (outputPerCraft <= 0)
                            outputPerCraft = 1;
                        break;
                    }
                }
                if (!produces) continue;

                if (recipe.Disabled)
                {
                    anyDisabled = true;
                    CollectRequirements(recipeEntity, disabledInputs, seenDisabled);
                    continue;
                }

                recipeOn = true;
                CollectRequirements(recipeEntity, enabledInputs, seenEnabled);

                var remainingOutputs = int.MaxValue;
                var recipeCapped = false;
                if (hasPlotCap)
                {
                    if (haveProduct >= plotCapAmt)
                    {
                        anyEnabledCapped = true;
                        continue;
                    }
                    remainingOutputs = plotCapAmt - haveProduct;
                    recipeCapped = true;
                }

                anyEnabledUncapped = true;
                if (!recipeEntity.Has<RecipeRequirementBuffer>()) continue;
                var requirements = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
                foreach (var requirement in requirements)
                {
                    var inputPerCraft = Mathf.RoundToInt(requirement.Amount * matchFloorReduction);
                    var amountWanted = ConvFeedMultiplier * inputPerCraft;
                    if (recipeCapped)
                    {
                        var inputForRemaining = remainingOutputs * inputPerCraft / outputPerCraft;
                        if (inputForRemaining < amountWanted)
                            amountWanted = inputForRemaining;
                    }

                    if (haveInputInv)
                    {
                        foreach (var item in inputInv)
                        {
                            if (item.ItemType.Equals(requirement.Guid))
                                amountWanted -= item.Amount;
                        }
                    }

                    wantedByInput.TryGetValue(requirement.Guid, out var currentWanted);
                    if (amountWanted > currentWanted)
                        wantedByInput[requirement.Guid] = amountWanted;
                }
            }

            if (!recipeOn && !anyDisabled)
                return false;

            inputs = recipeOn ? enabledInputs : disabledInputs;
            foreach (var input in inputs)
            {
                wantedByInput.TryGetValue(input, out var wanted);
                inputAtFeedCap[input] = recipeOn && wanted <= 0;
                var haveInStation = 0;
                if (haveInputInv)
                {
                    foreach (var item in inputInv)
                    {
                        if (item.ItemType.Equals(input))
                            haveInStation += item.Amount;
                    }
                }
                stationHave[input] = haveInStation;
            }

            // Three stop reasons. Never label input-stocked (wanted<=0) as cap.
            outputFull = recipeOn && !InventoryCanAccept(outputInventoryEntity, product);
            var productAtCap = hasPlotCap && haveProduct >= plotCapAmt;
            plotCap = recipeOn && (productAtCap || (anyEnabledCapped && !anyEnabledUncapped));
            if (plotCap && hasPlotCap)
                capNumber = plotCapAmt;

            return true;
        }

        static void CollectRequirements(Entity recipeEntity, List<PrefabGUID> inputs, HashSet<int> seen)
        {
            if (!recipeEntity.Has<RecipeRequirementBuffer>()) return;
            var requirements = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
            foreach (var requirement in requirements)
            {
                if (requirement.Guid.GuidHash == 0) continue;
                if (!seen.Add(requirement.Guid.GuidHash)) continue;
                inputs.Add(requirement.Guid);
            }
        }

        static HashSet<int> CollectReceiverGroups(Entity station)
        {
            var groups = new HashSet<int>();
            var name = StashRouting.RawName(station);
            if (string.IsNullOrEmpty(name) || Core.Stash?.ReceiverPattern == null)
                return groups;
            foreach (Match match in Core.Stash.ReceiverPattern.Matches(name.ToLower()))
            {
                if (int.TryParse(match.Groups[1].Value, out var group))
                    groups.Add(group);
            }
            return groups;
        }

        static List<(Entity stash, string name, int territoryId)> CollectMatchingSenders(IReadOnlyList<int> logisticsIds, HashSet<int> receiverGroups)
        {
            var senders = new List<(Entity stash, string name, int territoryId)>();
            var seen = new HashSet<Entity>();
            foreach (var logisticsId in logisticsIds)
            {
                foreach (var (group, stash) in Core.Stash.GetAllSendingStashes(logisticsId))
                {
                    if (!receiverGroups.Contains(group)) continue;
                    if (!Core.EntityManager.Exists(stash)) continue;
                    if (!seen.Add(stash)) continue;
                    senders.Add((stash, stash.EntityName(), logisticsId));
                }
            }
            return senders;
        }

        static void SnapshotStashItem(Entity stash, PrefabGUID item, out int have, out int stackableHave)
        {
            have = 0;
            stackableHave = 0;
            if (!Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                return;
            foreach (var attachedBuffer in buffer)
            {
                var attachedEntity = attachedBuffer.Entity;
                if (!attachedEntity.Has<PrefabGUID>()) continue;
                if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(attachedEntity, out var inventoryBuffer))
                    continue;
                foreach (var entry in inventoryBuffer)
                {
                    if (!entry.ItemType.Equals(item)) continue;
                    have += entry.Amount;
                    if (entry.ItemEntity.Equals(NetworkedEntity.Empty) || entry.ItemType.Equals(BeltSplit.SiegeGolemT02))
                        stackableHave += entry.Amount;
                }
            }
        }

        static bool InventoryCanAccept(Entity inventory, PrefabGUID item)
        {
            if (inventory.Equals(Entity.Null) || !Core.EntityManager.Exists(inventory))
                return false;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var inventoryBuffer))
                return false;
            foreach (var entry in inventoryBuffer)
            {
                if (entry.ItemType.GuidHash == 0)
                    return true;
                if (entry.ItemType.Equals(item) && entry.ItemEntity.Equals(NetworkedEntity.Empty))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Station-input shortfall on the logistics island (same 5x feed as conveyors).
        /// Sorted higher refinement tier first, then lowest island stock after reserve.
        /// </summary>
        public static List<string> Need(int standingTerritoryId, int topN = 10)
        {
            var lines = new List<string>();
            if (standingTerritoryId < 0)
            {
                lines.Add("You must stand on a castle plot to list conveyor need.");
                return lines;
            }

            var logisticsIds = Core.TerritoryService.GetLogisticsTerritoryIds(standingTerritoryId);
            if (logisticsIds == null || logisticsIds.Count == 0)
            {
                lines.Add("You must stand on a castle plot to list conveyor need.");
                return lines;
            }

            var castleHeartEntity = Core.TerritoryService.GetCastleHeart(standingTerritoryId);
            ulong platformID = 0;
            if (castleHeartEntity != Entity.Null && Core.EntityManager.Exists(castleHeartEntity) && castleHeartEntity.Has<UserOwner>())
            {
                var ownerEntity = castleHeartEntity.Read<UserOwner>().Owner.GetEntityOnServer();
                if (ownerEntity != Entity.Null && Core.EntityManager.Exists(ownerEntity) && ownerEntity.Has<User>())
                    platformID = ownerEntity.Read<User>().PlatformId;
            }

            var wantByItem = new Dictionary<int, int>();
            var depthByItem = new Dictionary<int, int>();
            var recipes = new List<(PrefabGUID output, List<PrefabGUID> inputs)>();
            var plotCounts = new Dictionary<int, Dictionary<PrefabGUID, int>>();
            foreach (var id in logisticsIds)
                plotCounts[id] = BeltCounts.OfPlot(id);
            var islandCounts = BeltCounts.OfPlots(logisticsIds);

            foreach (var logisticsId in logisticsIds)
            foreach (var (group, station) in Core.RefinementStations.GetAllReceivingStations(logisticsId))
            {
                if (!Core.EntityManager.Exists(station) || station.Has<Disabled>())
                    continue;
                if (!station.Has<Refinementstation>() || !station.Has<RefinementstationRecipesBuffer>() || !station.Has<CastleWorkstation>())
                    continue;

                var receivingStation = station.Read<Refinementstation>();
                var castleWorkstation = station.Read<CastleWorkstation>();
                var matchFloorReduction = castleWorkstation.WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
                var inputInventoryEntity = receivingStation.InputInventoryEntity.GetEntityOnServer();
                var haveInputInv = inputInventoryEntity != Entity.Null && Core.EntityManager.Exists(inputInventoryEntity)
                    && inputInventoryEntity.Has<InventoryBuffer>();
                NativeArray<InventoryBuffer> inventoryBuffer = default;
                if (haveInputInv)
                    inventoryBuffer = inputInventoryEntity.ReadBuffer<InventoryBuffer>().ToNativeArray(Allocator.Temp);

                try
                {
                    var recipesBuffer = station.ReadBuffer<RefinementstationRecipesBuffer>();
                    foreach (var recipe in recipesBuffer)
                    {
                        if (!recipe.Unlocked || recipe.Disabled)
                            continue;
                        if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var recipeEntity))
                            continue;
                        if (recipeEntity == Entity.Null || !Core.EntityManager.Exists(recipeEntity))
                            continue;

                        var remainingOutputs = int.MaxValue;
                        var outputPerCraft = 1;
                        var recipeCapped = false;
                        PrefabGUID outputGuid = default;
                        if (recipeEntity.Has<RecipeOutputBuffer>())
                        {
                            var recipeOutputBuffer = recipeEntity.ReadBuffer<RecipeOutputBuffer>();
                            if (recipeOutputBuffer.Length > 0)
                            {
                                var recipeOutput = recipeOutputBuffer[0];
                                outputGuid = recipeOutput.Guid;
                                outputPerCraft = recipeOutput.Amount > 0 ? recipeOutput.Amount : 1;
                                if (outputGuid.GuidHash != 0 &&
                                    Core.PlayerSettings.TryGetItemCap(platformID, outputGuid, out var outputCap))
                                {
                                    islandCounts.TryGetValue(outputGuid, out var haveOutput);
                                    if (haveOutput >= outputCap)
                                        continue;
                                    remainingOutputs = outputCap - haveOutput;
                                    recipeCapped = true;
                                }
                            }
                        }

                        var inputs = new List<PrefabGUID>();
                        if (!recipeEntity.Has<RecipeRequirementBuffer>())
                            continue;
                        var requirements = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
                        foreach (var requirement in requirements)
                        {
                            if (requirement.Guid.GuidHash == 0)
                                continue;
                            inputs.Add(requirement.Guid);
                            var inputPerCraft = Mathf.RoundToInt(requirement.Amount * matchFloorReduction);
                            var amountWanted = ConvFeedMultiplier * inputPerCraft;
                            if (recipeCapped)
                            {
                                var inputForRemaining = remainingOutputs * inputPerCraft / outputPerCraft;
                                if (inputForRemaining < amountWanted)
                                    amountWanted = inputForRemaining;
                            }
                            if (haveInputInv)
                            {
                                for (var i = 0; i < inventoryBuffer.Length; i++)
                                {
                                    if (inventoryBuffer[i].ItemType.Equals(requirement.Guid))
                                        amountWanted -= inventoryBuffer[i].Amount;
                                }
                            }
                            if (amountWanted <= 0)
                                continue;
                            wantByItem.TryGetValue(requirement.Guid.GuidHash, out var haveWant);
                            wantByItem[requirement.Guid.GuidHash] = haveWant + amountWanted;
                        }
                        if (outputGuid.GuidHash != 0)
                            recipes.Add((outputGuid, inputs));
                    }
                }
                finally
                {
                    if (inventoryBuffer.IsCreated)
                        inventoryBuffer.Dispose();
                }
            }

            if (wantByItem.Count == 0)
            {
                lines.Add("No hungry receiving stations (name them r# with unlocked recipes, or they are already fed / capped).");
                return lines;
            }

            for (var pass = 0; pass < 8; pass++)
            {
                var changed = false;
                foreach (var (output, inputs) in recipes)
                {
                    var maxIn = 0;
                    foreach (var input in inputs)
                    {
                        depthByItem.TryGetValue(input.GuidHash, out var d);
                        if (d > maxIn)
                            maxIn = d;
                    }
                    var next = maxIn + 1;
                    depthByItem.TryGetValue(output.GuidHash, out var cur);
                    if (next > cur)
                    {
                        depthByItem[output.GuidHash] = next;
                        changed = true;
                    }
                }
                if (!changed)
                    break;
            }

            var rows = new List<(int guid, int want, int have, int takeable, int reserve, int depth, float silver)>();
            foreach (var kv in wantByItem)
            {
                var type = new PrefabGUID(kv.Key);
                var have = 0;
                var takeable = 0;
                var reserveShown = 0;
                foreach (var id in logisticsIds)
                {
                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var sourceOwnerId);
                    if (sourceOwnerId == 0)
                        sourceOwnerId = platformID;
                    var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                    if (id == standingTerritoryId)
                        reserveShown = reserve;
                    plotCounts.TryGetValue(id, out var counts);
                    var c = 0;
                    counts?.TryGetValue(type, out c);
                    have += c;
                    var t = c - reserve;
                    if (t > 0)
                        takeable += t;
                }
                depthByItem.TryGetValue(kv.Key, out var depth);
                var silver = 0f;
                try
                {
                    if (Core.GameDataSystem.ItemHashLookupMap.TryGetValue(type, out var itemData))
                        silver = itemData.SilverValue;
                }
                catch { }
                rows.Add((kv.Key, kv.Value, have, takeable, reserveShown, depth, silver));
            }

            rows.Sort((a, b) =>
            {
                var c = b.depth.CompareTo(a.depth);
                if (c != 0) return c;
                c = b.silver.CompareTo(a.silver);
                if (c != 0) return c;
                c = a.takeable.CompareTo(b.takeable);
                if (c != 0) return c;
                return b.want.CompareTo(a.want);
            });

            var scope = logisticsIds.Count > 1 ? "clan island" : "this plot";
            var n = rows.Count < topN ? rows.Count : topN;
            lines.Add($"Top {n} conveyor need on {scope} (higher tier, then lowest stock after reserve):");
            for (var i = 0; i < n; i++)
            {
                var row = rows[i];
                var name = StashRouting.ItemLabel(new PrefabGUID(row.guid));
                lines.Add($"{i + 1}. <color=green>{name}</color> T{row.depth}  demand <color=white>{row.want}</color>  total <color=white>{row.have}</color>  reserve <color=white>{row.reserve}</color>");
            }
            return lines;
        }    }
}
