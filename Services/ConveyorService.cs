using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    internal partial class ConveyorService
    {
        readonly Dictionary<PrefabGUID, int> amountToDistribute = [];

        // Sibling clan plots already handled in this work-queue drain wave.
        readonly HashSet<int> clanConveyorConsumed = [];
        int clanConveyorConsumedGeneration = -1;

        bool ShouldSkipClanConveyor(int territoryId)
        {
            var generation = Core.WorkQueue != null ? Core.WorkQueue.DrainGeneration : 0;
            if (generation != clanConveyorConsumedGeneration)
            {
                clanConveyorConsumed.Clear();
                clanConveyorConsumedGeneration = generation;
                return false;
            }
            return clanConveyorConsumed.Contains(territoryId);
        }

        void BeginClanConveyorPass(int territoryId, IReadOnlyList<int> logisticsIds)
        {
            var generation = Core.WorkQueue != null ? Core.WorkQueue.DrainGeneration : 0;
            if (generation != clanConveyorConsumedGeneration)
            {
                clanConveyorConsumed.Clear();
                clanConveyorConsumedGeneration = generation;
            }

            if (logisticsIds == null || logisticsIds.Count <= 1)
                return;

            foreach (var id in logisticsIds)
            {
                if (id != territoryId)
                    clanConveyorConsumed.Add(id);
            }
        }


        PrefabGUID Item_Building_Siege_Golem_T02 = new(-1461326411);

        public ConveyorService()
        {
            Core.TerritoryService.RegisterTerritoryUpdateCallback(ProcessConveyors);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(ProcessSalvagers);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(ProcessUnitSpawners);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(ProcessBraziers);
        }

        IEnumerator ProcessConveyors(int territoryId, Entity castleHeartEntity)
        {
            if (!Core.PlayerSettings.IsConveyorEnabled(0)) yield break;

            var userOwner = castleHeartEntity.Read<UserOwner>();
            if (userOwner.Owner.GetEntityOnServer() == Entity.Null) yield break;

            var platformID = userOwner.Owner.GetEntityOnServer().Read<User>().PlatformId;
            if (!Core.PlayerSettings.IsConveyorEnabled(platformID)) yield break;

            if (ShouldSkipClanConveyor(territoryId))
                yield break;

            var logisticsIds = Core.TerritoryService.GetLogisticsTerritoryIds(territoryId);
            BeginClanConveyorPass(territoryId, logisticsIds);

            var serverGameManager = Core.ServerGameManager;
            var territoryCounts = CountTerritoryItems(logisticsIds);

            // Determine what is needed for each station
            var receivingNeeds = new Dictionary<(int group, PrefabGUID item), List<(Entity receiver, int amount, bool chest)>>();
            foreach (var logisticsId in logisticsIds)
            foreach (var (group, station) in Core.RefinementStations.GetAllReceivingStations(logisticsId))
            {
                var receivingStation = station.Read<Refinementstation>();
                var castleWorkstation = station.Read<CastleWorkstation>();
                var matchFloorReduction = castleWorkstation.WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
                var inputInventoryEntity = receivingStation.InputInventoryEntity.GetEntityOnServer();
                var inventoryBuffer = inputInventoryEntity.ReadBuffer<InventoryBuffer>();
                var recipesBuffer = station.ReadBuffer<RefinementstationRecipesBuffer>();
                foreach (var recipe in recipesBuffer)
                {
                    if (!recipe.Unlocked) continue;
                    if (recipe.Disabled) continue;

                    Entity recipeEntity = Core.PrefabCollectionSystem._PrefabGuidToEntityMap[recipe.RecipeGuid];
                    var remainingOutputs = int.MaxValue;
                    var outputPerCraft = 1;
                    var recipeCapped = false;
                    if (recipeEntity.Has<RecipeOutputBuffer>())
                    {
                        var recipeOutputBuffer = recipeEntity.ReadBuffer<RecipeOutputBuffer>();
                        if (recipeOutputBuffer.Length > 0)
                        {
                            var recipeOutput = recipeOutputBuffer[0];
                            if (recipeOutput.Guid.GuidHash != 0 &&
                                Core.PlayerSettings.TryGetItemCap(platformID, recipeOutput.Guid, out var outputCap))
                            {
                                territoryCounts.TryGetValue(recipeOutput.Guid, out var haveOutput);
                                if (haveOutput >= outputCap)
                                    continue;

                                remainingOutputs = outputCap - haveOutput;
                                outputPerCraft = recipeOutput.Amount;
                                if (outputPerCraft <= 0)
                                    outputPerCraft = 1;
                                recipeCapped = true;
                            }
                        }
                    }
                    var requirements = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
                    foreach (var requirement in requirements)
                    {
                        // Always desire 5x the transferring so the moment it finishes it immediately starts again
                        var inputPerCraft = Mathf.RoundToInt(requirement.Amount * matchFloorReduction);
                        var amountWanted = 5 * inputPerCraft;
                        if (recipeCapped)
                        {
                            var inputForRemaining = remainingOutputs * inputPerCraft / outputPerCraft;
                            if (inputForRemaining < amountWanted)
                                amountWanted = inputForRemaining;
                        }

                        // Check how much is already in the inventory
                        int has = 0;
                        foreach (var item in inventoryBuffer)
                        {
                            if (item.ItemType.Equals(requirement.Guid))
                            {
                                amountWanted -= item.Amount;
                                has = item.Amount;
                            }
                        }

                        if (amountWanted <= 0) continue;

                        if (!receivingNeeds.TryGetValue((group, requirement.Guid), out var needs))
                        {
                            needs = [];
                            receivingNeeds[(group, requirement.Guid)] = needs;
                        }

                        needs.Add((inputInventoryEntity, amountWanted, false));
                    }
                }
            }

            // Determine what is desired by each receiving stash
            var alreadyAdded = new HashSet<PrefabGUID>();
            foreach (var logisticsId in logisticsIds)
            foreach (var (group, stash) in Core.Stash.GetAllReceivingStashes(logisticsId))
            {
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    alreadyAdded.Clear();
                    var inventoryBuffer = attachedEntity.ReadBuffer<InventoryBuffer>();
                    foreach (var item in inventoryBuffer)
                    {
                        if (item.ItemType.GuidHash == 0) continue;

                        if (alreadyAdded.Contains(item.ItemType)) continue;
                        alreadyAdded.Add(item.ItemType);

                        if (Core.PlayerSettings.TryGetItemCap(platformID, item.ItemType, out var itemCap))
                        {
                            territoryCounts.TryGetValue(item.ItemType, out var haveItem);
                            if (haveItem >= itemCap) continue;
                            if (!receivingNeeds.TryGetValue((group, item.ItemType), out var cappedNeeds))
                            {
                                cappedNeeds = [];
                                receivingNeeds[(group, item.ItemType)] = cappedNeeds;
                            }
                            cappedNeeds.Add((attachedEntity, itemCap - haveItem, true));
                            continue;
                        }

                        if (!receivingNeeds.TryGetValue((group, item.ItemType), out var needs))
                        {
                            needs = [];
                            receivingNeeds[(group, item.ItemType)] = needs;
                        }

                        needs.Add((attachedEntity, -1, true));
                    }
                }
            }

            if (receivingNeeds.Count == 0) yield break;

            Dictionary<PrefabGUID, List<List<(Entity receiver, int amount, bool chest)>>> ungroupedItemLookup = null;
            // First distribute from overflow stashes
            var overflowStashes = logisticsIds.SelectMany(id => Core.Stash.GetAllOverflowStashes(id)).ToArray();
            foreach (var overflowStash in overflowStashes)
            {
                if (!Core.EntityManager.Exists(overflowStash)) continue;
                var overflowPlot = Core.TerritoryService.GetTerritoryId(overflowStash);
                if (ClanTreasuryLend.HoldKitOverflow(overflowPlot))
                    continue;
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(overflowStash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                    DistributeInventoryFromOverflow(receivingNeeds, serverGameManager, attachedEntity, ref ungroupedItemLookup);
                }
                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }

            // Now distribute from all the sender stations to the stations in need
            foreach (var logisticsId in logisticsIds)
            foreach (var (group, sendingStation) in Core.RefinementStations.GetAllSendingStations(logisticsId).ToArray())
            {
                if (!Core.EntityManager.Exists(sendingStation)) continue;

                var refinementStation = sendingStation.Read<Refinementstation>();
                var outputInventoryEntity = refinementStation.OutputInventoryEntity.GetEntityOnServer();
                if (outputInventoryEntity.Equals(Entity.Null)) continue;
                // Station output: overflow extras as stock does, but never clamp to leftover/reserve.
                DistributeInventory(receivingNeeds, serverGameManager, group, outputInventoryEntity, overflowStashes, applyReserve: false);

                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }

            // Next distribute from all the send stashes
            var emptyArray = System.Array.Empty<Entity>();
            foreach (var logisticsId in logisticsIds)
            {
                // Leftover/reserve is the heart owner of the plot this send chest sits on.
                if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(logisticsId, out var sourceOwnerId))
                    sourceOwnerId = platformID;

                foreach (var (group, sendingStash) in Core.Stash.GetAllSendingStashes(logisticsId))
                {
                    if (!Core.EntityManager.Exists(sendingStash)) continue;
                    if (sendingStash.Has<Refinementstation>()) continue;
                    if (!serverGameManager.TryGetBuffer<AttachedBuffer>(sendingStash, out var buffer))
                        continue;
                    foreach (var attachedBuffer in buffer)
                    {
                        var attachedEntity = attachedBuffer.Entity;
                        if (!attachedEntity.Has<PrefabGUID>()) continue;
                        if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                        DistributeInventory(receivingNeeds, serverGameManager, group, attachedEntity, emptyArray, platformID: sourceOwnerId, chest: true, applyReserve: true, sendingStash: sendingStash);
                    }

                    if (Core.TerritoryService.ShouldUpdateYield())
                        yield return null;
                }
            }
        }

        readonly HashSet<Entity> salvagerFull = [];
        readonly HashSet<(Entity entity, PrefabGUID item)> salvagerFullOfItem = [];
        readonly Dictionary<int, float> salvageDiagAt = [];
        const float SalvageDiagCooldown = 30f;

        void LogSalvageDiag(int territoryId, string message)
        {
            var now = Time.realtimeSinceStartup;
            if (salvageDiagAt.TryGetValue(territoryId, out var last) && now - last < SalvageDiagCooldown)
                return;
            salvageDiagAt[territoryId] = now;
            Core.Log.LogInfo(message);
        }

        // Gear: Salvageable ECS component. Knowledge/tech/recipe books: ItemType.Tech
        // (same check GroundScoop uses for act1-act5). UI-Salvageable books often lack the ECS component.
        // ItemData has no Salvageable/recipe flag (verified on 1.1 ProjectM.ItemData).
        static bool IsSalvageFeedEligible(Entity prefabEntity)
        {
            if (prefabEntity == Entity.Null)
                return false;
            if (prefabEntity.Has<Salvageable>())
                return true;
            if (prefabEntity.Has<ItemData>() && prefabEntity.Read<ItemData>().ItemType == ItemType.Tech)
                return true;
            return false;
        }

        IEnumerator ProcessSalvagers(int territoryId, Entity castleHeartEntity)
        {
            var globalOn = Core.PlayerSettings.IsGlobalSalvageEnabled();

            var userOwner = castleHeartEntity.Read<UserOwner>();
            if (userOwner.Owner.GetEntityOnServer() == Entity.Null) yield break;

            var ownerUser = userOwner.Owner.GetEntityOnServer().Read<User>();
            var platformID = ownerUser.PlatformId;
            var plotOn = Core.PlayerSettings.GetPlotSalvageFlag(platformID, territoryId);

            if (!globalOn || !plotOn)
                yield break;

            var salvageStashes = Core.Stash.GetAllSalvageStashes(territoryId).ToList();
            var salvagers = Core.SalvageService.GetAllSalvageStations(territoryId)
                            .Select((s, i) => (entity: s, station: s.Read<Salvagestation>(), index: i))
                            .ToList();
            if (salvageStashes.Count == 0 && salvagers.Count == 0)
                yield break;

            // Empty all salvage outputs first. Leftover/reserve is not applied to devourer feed or output.
            var itemStashes = Utilities.GetItemStashesOnTerritory(territoryId);
            var overflows = Core.Stash.GetAllOverflowStashes(territoryId).ToList();
            foreach (var salvager in salvagers)
            {
                if (!Core.EntityManager.Exists(salvager.entity)) continue;
                
                var salvageStation = salvager.station;
                var outputInventoryEntity = salvageStation.OutputInventoryEntity.GetEntityOnServer();

                var inventoryBuffer = Core.EntityManager.GetBuffer<InventoryBuffer>(outputInventoryEntity).ToNativeArray(Allocator.Temp);
                try
                {
                    if (InventoryUtilities.IsInventoryEmpty(inventoryBuffer)) continue;

                    Utilities.StashInventoryEntity(outputInventoryEntity, itemStashes, overflows);
                }
                finally
                {
                    inventoryBuffer.Dispose();
                }

                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }

            // Now fill all the salvagers
            salvagerFull.Clear();
            salvagerFullOfItem.Clear();
            var itemAmountsToTransfer = new Dictionary<PrefabGUID, (bool itemEntity, int amount)>();
            var skippedNotSalvageable = 0;
            var eligibleCount = 0;
            foreach (var salvageSupplier in salvageStashes)
            {
                if (!Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(salvageSupplier, out var buffer))
                    continue;

                var name = salvageSupplier.Read<NameableInteractable>().Name.ToString().ToLower();
                var isReceiverStash = Core.Stash.ReceiverRegex.IsMatch(name);
                foreach (var attachedBuffer in buffer)
                {
                    var salvageSupplierInventory = attachedBuffer.Entity;
                    if (!salvageSupplierInventory.Has<PrefabGUID>()) continue;
                    if (!salvageSupplierInventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    itemAmountsToTransfer.Clear();
                    var inventoryBuffer = salvageSupplierInventory.ReadBuffer<InventoryBuffer>();
                    foreach (var item in inventoryBuffer)
                    {
                        if (item.ItemType.GuidHash == 0 || item.Amount <= 0) continue;
                        if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(item.ItemType, out var prefabEntity))
                        {
                            skippedNotSalvageable += item.Amount;
                            continue;
                        }
                        if (!IsSalvageFeedEligible(prefabEntity))
                        {
                            skippedNotSalvageable += item.Amount;
                            continue;
                        }

                        eligibleCount += item.Amount;
                        var amount = 0;
                        if (itemAmountsToTransfer.TryGetValue(item.ItemType, out var entry))
                        {
                            amount = entry.amount;
                        }
                        else if (isReceiverStash)
                        {
                            amount = -1;
                        }
                        amount += item.Amount;
                        itemAmountsToTransfer[item.ItemType] = (!item.ItemEntity.Equals(NetworkedEntity.Empty), amount);
                    }

                    foreach(var (itemType, entry) in itemAmountsToTransfer)
                    { 
                        var totalAmountToTransfer = entry.amount;
                        if (totalAmountToTransfer <= 0) continue;
                        var leftToGetTrash = salvagers.Count - salvagerFull.Count;
                        if (leftToGetTrash == 0) break;
                        for (var i = salvagers.Count - 1; i >= 0; i--)
                        {
                            if (Core.TerritoryService.ShouldUpdateYield())
                                yield return null;

                            if (!Core.EntityManager.Exists(salvageSupplierInventory)) break;

                            var salvager = salvagers[i];
                            if (salvagerFull.Contains(salvager.entity)) continue;

                            var salvagerKey = (salvager.entity, itemType);
                            if (salvagerFullOfItem.Contains(salvagerKey))
                            {
                                leftToGetTrash--;
                                continue;
                            }
                            if (!Core.EntityManager.Exists(salvager.entity))
                            {
                                leftToGetTrash--;
                                continue;
                            }

                            var salvageStation = salvager.station;
                            var inputInventoryEntity = salvageStation.InputInventoryEntity.GetEntityOnServer();

                            var startInputSlot = 0;
                            var amountTransferred = 0;

                            // Ensure non working ones get at least one otherwise distribute somewhat randomly based on current frame
                            var amountToTransfer = (totalAmountToTransfer + (!salvageStation.IsWorking ? (leftToGetTrash - 1) : Time.frameCount % leftToGetTrash)) / leftToGetTrash;
                            if (amountToTransfer == 0) continue;

                            if (entry.itemEntity)
                                Utilities.TransferItemEntities(salvageSupplierInventory, inputInventoryEntity, itemType, amountToTransfer, ref startInputSlot, out amountTransferred);
                            else
                                amountTransferred = Utilities.TransferItems(Core.ServerGameManager, salvageSupplierInventory, inputInventoryEntity, itemType, amountToTransfer);
                            leftToGetTrash--;

                            if (amountTransferred < amountToTransfer)
                            {
                                if (Core.ServerGameManager.HasFullInventory(inputInventoryEntity))
                                {
                                    salvagers.RemoveAt(i);
                                }
                                else
                                {
                                    salvagerFullOfItem.Add(salvagerKey);
                                }
                            }

                            if (amountTransferred == 0)
                            {
                                continue;
                            }

                            totalAmountToTransfer -= amountTransferred;

                            if (!salvageStation.IsWorking)
                            {
                                salvageStation.IsWorking = true;
                                salvager.entity.Write(salvageStation);
                                salvagers[salvager.index] = (salvager.entity, salvageStation, salvager.index);
                            }

                            if (totalAmountToTransfer <= 0) break;
                        }
                    }
                }

                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }

        IEnumerator ProcessUnitSpawners(int territoryId, Entity castleHeartEntity)
        {
            if (!Core.PlayerSettings.IsUnitSpawnerEnabled(0)) yield break;

            var userOwner = castleHeartEntity.Read<UserOwner>();
            if (userOwner.Owner.GetEntityOnServer() == Entity.Null) yield break;

            var platformID = userOwner.Owner.GetEntityOnServer().Read<User>().PlatformId;
            if (!Core.PlayerSettings.IsUnitSpawnerEnabled(platformID)) yield break;

            var serverGameManager = Core.ServerGameManager;

            // Determine what is needed for each brazier
            var receivingNeeds = new Dictionary<PrefabGUID, List<(Entity, int)>>();
            foreach (var station in Core.UnitSpawnerstationService.GetAllUnitSpawners(territoryId))
            {
                var castleWorkstation = station.Read<CastleWorkstation>();
                var matchFloorReduction = castleWorkstation.WorkstationLevel.HasFlag(WorkstationLevel.MatchingFloor) ? 0.75f : 1f;
                var inputInventoryEntity = Entity.Null;
                DynamicBuffer<InventoryBuffer> inventoryBuffer = new();
                var recipesBuffer = station.ReadBuffer<RefinementstationRecipesBuffer>();
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(station, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    inputInventoryEntity = attachedEntity;
                    inventoryBuffer = attachedEntity.ReadBuffer<InventoryBuffer>();
                }

                foreach (var recipe in recipesBuffer)
                {
                    if (!recipe.Unlocked) continue;
                    if (recipe.Disabled) continue;

                    Entity recipeEntity = Core.PrefabCollectionSystem._PrefabGuidToEntityMap[recipe.RecipeGuid];
                    var requirements = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
                    foreach (var requirement in requirements)
                    {
                        // Always desire 2x the transferring so the moment it finishes it immediately starts again
                        var amountWanted = 2 * Mathf.RoundToInt(requirement.Amount * matchFloorReduction);

                        // Check how much is already in the inventory
                        int has = 0;
                        foreach (var item in inventoryBuffer)
                        {
                            if (item.ItemType.Equals(requirement.Guid))
                            {
                                amountWanted -= item.Amount;
                                has = item.Amount;
                            }
                        }

                        if (amountWanted <= 0) continue;

                        if (!receivingNeeds.TryGetValue(requirement.Guid, out var needs))
                        {
                            needs = [];
                            receivingNeeds[requirement.Guid] = needs;
                        }

                        needs.Add((inputInventoryEntity, amountWanted));
                    }
                }
            }

            if (receivingNeeds.Count == 0) yield break;

            // Distribute from all the spawner stashes
            foreach (var sendingStash in Core.Stash.GetAllSpawnerStashes(territoryId))
            {
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(sendingStash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    DistributeInventory(receivingNeeds, serverGameManager, attachedEntity, platformID: platformID);
                }

                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }

        IEnumerator ProcessBraziers(int territoryId, Entity castleHeartEntity)
        {
            if (!Core.PlayerSettings.IsBrazierEnabled(0)) yield break;

            const int minAmount = 10;

            var userOwner = castleHeartEntity.Read<UserOwner>();
            if (userOwner.Owner.GetEntityOnServer() == Entity.Null) yield break;

            var platformID = userOwner.Owner.GetEntityOnServer().Read<User>().PlatformId;
            if (!Core.PlayerSettings.IsBrazierEnabled(platformID)) yield break;

            var serverGameManager = Core.ServerGameManager;

            // Determine what is needed for each brazier
            var receivingNeeds = new Dictionary<PrefabGUID, List<(Entity, int)>>();
            foreach (var brazier in Core.BrazierService.GetAllBraziers(territoryId))
            {
                var burnContainer = brazier.Read<BurnContainer>();
                if (!burnContainer.Enabled) continue;

                var inputInventoryEntity = Entity.Null;
                DynamicBuffer<InventoryBuffer> inventoryBuffer = new();
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(brazier, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    inputInventoryEntity = attachedEntity;
                    inventoryBuffer = attachedEntity.ReadBuffer<InventoryBuffer>();
                }

                // Check how much is already in the inventory
                var bonfire = brazier.Read<Bonfire>();
                var has = 0;
                foreach (var item in inventoryBuffer)
                {
                    if (item.ItemType.Equals(bonfire.InputItem))
                    {
                        has += item.Amount;
                    }
                }

                if (has > minAmount) continue;

                if (!receivingNeeds.TryGetValue(bonfire.InputItem, out var needs))
                {
                    needs = [];
                    receivingNeeds[bonfire.InputItem] = needs;
                }

                needs.Add((inputInventoryEntity, minAmount - has));
            }

            if (receivingNeeds.Count == 0) yield break;

            // Distribute from all the spawner stashes
            foreach (var sendingStash in Core.Stash.GetAllBrazierStashes(territoryId))
            {
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(sendingStash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;

                    DistributeInventory(receivingNeeds, serverGameManager, attachedEntity, platformID: platformID);
                }

                if (Core.TerritoryService.ShouldUpdateYield())
                    yield return null;
            }
        }

        public Dictionary<PrefabGUID, int> CountTerritoryItems(int territoryId)
        {
            var counts = new Dictionary<PrefabGUID, int>();
            var serverGameManager = Core.ServerGameManager;

            foreach (var stash in Core.Stash.GetStashesOnTerritory(territoryId))
            {
                if (!Core.EntityManager.Exists(stash)) continue;
                if (!serverGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var attachedEntity = attachedBuffer.Entity;
                    if (!attachedEntity.Has<PrefabGUID>()) continue;
                    if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                    AddInventoryCounts(attachedEntity, counts);
                }
            }

            if (Core.RefinementStations != null)
            {
                foreach (var station in Core.RefinementStations.GetAllStationsOnTerritory(territoryId))
                {
                    if (!Core.EntityManager.Exists(station)) continue;
                    if (!station.Has<Refinementstation>()) continue;
                    var refinementStation = station.Read<Refinementstation>();
                    AddInventoryCounts(refinementStation.InputInventoryEntity.GetEntityOnServer(), counts);
                    AddInventoryCounts(refinementStation.OutputInventoryEntity.GetEntityOnServer(), counts);
                }
            }

            return counts;
        }

        public Dictionary<PrefabGUID, int> CountTerritoryItems(IReadOnlyList<int> territoryIds)
        {
            var counts = new Dictionary<PrefabGUID, int>();
            if (territoryIds == null)
                return counts;

            foreach (var territoryId in territoryIds)
            {
                foreach (var (item, amount) in CountTerritoryItems(territoryId))
                {
                    counts.TryGetValue(item, out var have);
                    counts[item] = have + amount;
                }
            }
            return counts;
        }

        static void AddInventoryCounts(Entity inventoryEntity, Dictionary<PrefabGUID, int> counts)
        {
            if (inventoryEntity.Equals(Entity.Null)) return;
            if (!Core.EntityManager.Exists(inventoryEntity)) return;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventoryEntity, out var inventoryBuffer))
                return;
            foreach (var item in inventoryBuffer)
            {
                if (item.ItemType.GuidHash == 0) continue;
                counts.TryGetValue(item.ItemType, out var have);
                counts[item.ItemType] = have + item.Amount;
            }
        }

        void DistributeInventoryFromOverflow(Dictionary<(int group, PrefabGUID item), List<(Entity receiver, int amount, bool chest)>> receivingNeeds,
                                 ServerGameManager serverGameManager, Entity inventoryEntity,
                                 ref Dictionary<PrefabGUID, List<List<(Entity receiver, int amount, bool chest)>>> ungroupedItemLookup)
        {
            amountToDistribute.Clear();

            var anythingToDistribute = false;
            var inventoryBuffer = inventoryEntity.ReadBuffer<InventoryBuffer>();
            foreach (var item in inventoryBuffer)
            {
                if (item.ItemType.GuidHash == 0) continue;
                if (!item.ItemEntity.Equals(NetworkedEntity.Empty) && item.ItemType != Item_Building_Siege_Golem_T02) continue;

                if (!amountToDistribute.TryGetValue(item.ItemType, out var totalAmountDistribute))
                    totalAmountDistribute = item.Amount;
                else
                    totalAmountDistribute += item.Amount;
                amountToDistribute[item.ItemType] = totalAmountDistribute;
                anythingToDistribute = true;
            }

            if (!anythingToDistribute) return;

            if (ungroupedItemLookup == null)
            {
                ungroupedItemLookup = new();
                foreach (var ((group, item), needs) in receivingNeeds)
                {
                    if (!ungroupedItemLookup.TryGetValue(item, out var list))
                    {
                        list = [];
                        ungroupedItemLookup[item] = list;
                    }
                    list.Add(needs);
                }
            }

            foreach ((var item, var totalAmount) in amountToDistribute)
            {
                // Does anyone need this item?
                if (!ungroupedItemLookup.TryGetValue(item, out var needs)) continue;

                // Flatten the needs into a single list
                var totalNeeds = needs.SelectMany((list, listIndex) => list.Select((entry, index) => (listIndex, index, entry)).Reverse());

                var totalWanted = totalNeeds.Where(x => x.entry.amount > 0).Sum(x => x.entry.amount);

                // If we have more than enough, distribute evenly
                if (totalWanted <= totalAmount)
                {
                    var leftoverAmount = totalAmount - totalWanted;
                    foreach (var (listIndex, index, (receivingInventoryEntity, wanted, receiverChest)) in totalNeeds)
                    {
                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            needs[listIndex].RemoveAt(index);
                            continue;
                        }

                        if (wanted > 0)
                        {
                            Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, wanted);
                            needs[listIndex].RemoveAt(index);
                        }
                        else
                        {
                            var amountActuallyGiven = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, leftoverAmount);

                            if (amountActuallyGiven < leftoverAmount)
                            {
                                needs[listIndex].RemoveAt(index);
                            }

                            leftoverAmount -= amountActuallyGiven;
                        }
                    }
                }
                else
                {
                    var remainder = 0;
                    // Give out proportionally
                    foreach (var (listIndex, index, (receivingInventoryEntity, wanted, receiverChest)) in totalNeeds)
                    {
                        if (wanted <= 0) continue;

                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            totalWanted -= wanted;
                            needs[listIndex].RemoveAt(index);
                            continue;
                        }

                        var numerator = (long)wanted * totalAmount;
                        var transferring = (int)(numerator / totalWanted);
                        remainder += (int)(numerator % totalWanted);
                        if (remainder >= totalWanted && transferring < wanted)
                        {
                            transferring++;
                            remainder -= totalWanted;
                        }
                        var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, transferring);
                        if (transferred < transferring)
                        {
                            remainder += (transferring - transferred)*totalWanted;
                            needs[listIndex].RemoveAt(index);
                        }
                        else if (transferred >= wanted)
                        {
                            needs[listIndex].RemoveAt(index);
                        }
                        else
                        {
                            needs[listIndex][index] = (receivingInventoryEntity, wanted - transferred, receiverChest);
                        }
                    }
                }
            }
        }

        void DistributeInventory(Dictionary<(int group, PrefabGUID item), List<(Entity receiver, int amount, bool chest)>> receivingNeeds,
                                 ServerGameManager serverGameManager, int group, Entity inventoryEntity, Entity[] overflowStashes, ulong platformID = 0, int retain = 0, bool chest=false, bool applyReserve = true, Entity sendingStash = default)
        {
            amountToDistribute.Clear();

            var inventoryBuffer = inventoryEntity.ReadBuffer<InventoryBuffer>();
            foreach (var item in inventoryBuffer)
            {
                if (item.ItemType.GuidHash == 0) continue;
                if (!item.ItemEntity.Equals(NetworkedEntity.Empty) && item.ItemType != Item_Building_Siege_Golem_T02) continue;

                amountToDistribute.TryGetValue(item.ItemType, out var totalAmountDistribute);
                amountToDistribute[item.ItemType] = totalAmountDistribute + item.Amount;
            }

            if (applyReserve)
            {
                foreach (var item in amountToDistribute.Keys.ToArray())
                {
                    var retainForItem = platformID != 0
                        ? Core.PlayerSettings.GetPullReserve(platformID, item)
                        : retain;
                    amountToDistribute[item] = Math.Max(0, amountToDistribute[item] - retainForItem);
                }
            }

            foreach ((var item, var totalAmount) in amountToDistribute)
            {
                if (totalAmount <= 0) continue;
                // Named sinks still feed matching stations; they do not drain to overflow or r# chests.
                // Does anyone need this item?
                if (!receivingNeeds.TryGetValue((group, item), out var needs))
                {
                    if (chest) continue;

                    var totalForOverflow = totalAmount;
                    foreach (var overflowStash in overflowStashes)
                    {
                        if (!Core.EntityManager.Exists(overflowStash)) continue;
                        if (!serverGameManager.TryGetBuffer<AttachedBuffer>(overflowStash, out var buffer))
                            continue;
                        foreach (var attachedBuffer in buffer)
                        {
                            var attachedEntity = attachedBuffer.Entity;
                            if (!attachedEntity.Has<PrefabGUID>()) continue;
                            if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                            var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, attachedEntity, item, totalForOverflow);
                            totalForOverflow -= transferred;
                            if (totalForOverflow <= 0) break;
                        }
                        if (totalForOverflow <= 0) break;
                    }
                    continue;
                }
                
                var totalWanted = needs.Where(x => x.amount > 0 && (!chest || !x.chest)).Sum(x => x.amount);

                // If we have more than enough, distribute evenly
                if (totalWanted <= totalAmount)
                {
                    var leftoverAmount = 0;

                    // Only handling leftovers if not a chest
                    if (!chest)
                    {
                        leftoverAmount = totalAmount - totalWanted;
                    }

                    for (int i = needs.Count - 1; i >= 0; i--)
                    {
                        var (receivingInventoryEntity, wanted, receiverChest) = needs[i];

                        if (chest && receiverChest) continue;

                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            needs.RemoveAt(i);
                            continue;
                        }

                        if (wanted > 0)
                        {
                            var gotWanted = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, wanted);
                            if (chest && gotWanted > 0 && sendingStash != Entity.Null)
                            {
                                var plot = Core.TerritoryService.GetTerritoryId(sendingStash);
                                DestDebugLog.Move("conveyor", plot, item, gotWanted, sendingStash, Entity.Null, "station", Core.PlayerSettings.GetPullReserve(platformID, item), "station");
                            }
                            needs.RemoveAt(i);
                        }
                        else if (!chest && leftoverAmount > 0)
                        {
                            var amountActuallyGiven = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, leftoverAmount);

                            if (amountActuallyGiven < leftoverAmount)
                            {
                                needs.RemoveAt(i);
                            }
                            leftoverAmount -= amountActuallyGiven;
                        }
                    }


                    // Distribute any remaining leftovers to overflow stashes
                    if (leftoverAmount > 0)
                    {
                        foreach (var overflowStash in overflowStashes)
                        {
                            if (!Core.EntityManager.Exists(overflowStash)) continue;
                            if (!serverGameManager.TryGetBuffer<AttachedBuffer>(overflowStash, out var buffer))
                                continue;
                            foreach (var attachedBuffer in buffer)
                            {
                                var attachedEntity = attachedBuffer.Entity;
                                if (!attachedEntity.Has<PrefabGUID>()) continue;
                                if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                                var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, attachedEntity, item, leftoverAmount);
                                leftoverAmount -= transferred;
                                if (leftoverAmount <= 0) break;
                            }
                            if (leftoverAmount <= 0) break;
                        }
                    }
                }
                else
                {
                    var totalTransferred = 0;
                    var remainder = 0;
                    // Give out proportionally
                    for (int i = needs.Count - 1; i >= 0; i--)
                    {
                        var (receivingInventoryEntity, wanted, receiverChest) = needs[i];

                        if (chest && receiverChest) continue;
                        if (wanted <= 0) continue;

                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            totalWanted -= wanted;
                            needs.RemoveAt(i);
                            continue;
                        }

                        var numerator = (long)wanted * totalAmount;
                        var transferring = (int)(numerator / totalWanted);
                        remainder += (int)(numerator % totalWanted);
                        if (remainder >= totalWanted && transferring < wanted)
                        {
                            transferring++;
                            remainder -= totalWanted;
                        }
                        var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, transferring);
                        if (chest && transferred > 0 && sendingStash != Entity.Null)
                        {
                            var plot = Core.TerritoryService.GetTerritoryId(sendingStash);
                            DestDebugLog.Move("conveyor", plot, item, transferred, sendingStash, Entity.Null, "station", Core.PlayerSettings.GetPullReserve(platformID, item), "station");
                        }
                        totalTransferred += transferred;
                        if (transferred < transferring)
                        {
                            remainder += totalWanted * (transferring - transferred);
                            needs.RemoveAt(i);
                        }
                        else if (transferred >= wanted)
                        {
                            needs.RemoveAt(i);
                        }
                        else
                        {
                            needs[i] = (receivingInventoryEntity, wanted - transferred, receiverChest);
                        }
                    }

                    if (totalTransferred < totalAmount && !chest)
                    {
                        var leftoverAmount = totalAmount - totalTransferred;
                        // Distribute any remaining leftovers to overflow stashes
                        foreach (var overflowStash in overflowStashes)
                        {
                            if (!Core.EntityManager.Exists(overflowStash)) continue;
                            if (!serverGameManager.TryGetBuffer<AttachedBuffer>(overflowStash, out var buffer))
                                continue;
                            foreach (var attachedBuffer in buffer)
                            {
                                var attachedEntity = attachedBuffer.Entity;
                                if (!attachedEntity.Has<PrefabGUID>()) continue;
                                if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab)) continue;
                                var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, attachedEntity, item, leftoverAmount);
                                leftoverAmount -= transferred;
                                if (leftoverAmount <= 0) break;
                            }
                            if (leftoverAmount <= 0) break;
                        }
                    }
                }
            }
        }

        void DistributeInventory(Dictionary<PrefabGUID, List<(Entity receiver, int amount)>> receivingNeeds,
                                 ServerGameManager serverGameManager, Entity inventoryEntity, ulong platformID = 0, int retain = 0, bool applyReserve = true)
        {
            amountToDistribute.Clear();

            var inventoryBuffer = inventoryEntity.ReadBuffer<InventoryBuffer>();
            foreach (var item in inventoryBuffer)
            {
                if (item.ItemType.GuidHash == 0) continue;
                if (!item.ItemEntity.Equals(NetworkedEntity.Empty) && item.ItemType != Item_Building_Siege_Golem_T02) continue;

                amountToDistribute.TryGetValue(item.ItemType, out var totalAmountDistribute);
                amountToDistribute[item.ItemType] = totalAmountDistribute + item.Amount;
            }

            if (applyReserve)
            {
                foreach (var item in amountToDistribute.Keys.ToArray())
                {
                    var retainForItem = platformID != 0
                        ? Core.PlayerSettings.GetPullReserve(platformID, item)
                        : retain;
                    amountToDistribute[item] = Math.Max(0, amountToDistribute[item] - retainForItem);
                }
            }

            foreach ((var item, var totalAmount) in amountToDistribute)
            {
                if (totalAmount <= 0) continue;
                // Does anyone need this item?
                if (!receivingNeeds.TryGetValue(item, out var needs)) continue;

                var totalWanted = needs.Sum(x => x.amount);
                if (totalWanted <= 0) continue;

                // If we have more than enough, distribute evenly
                if (totalWanted <= totalAmount)
                {

                    for (int i = needs.Count - 1; i >= 0; i--)
                    {
                        var (receivingInventoryEntity, wanted) = needs[i];
                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            needs.RemoveAt(i);
                            continue;
                        }
                        Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, wanted);
                    }
                    needs.Clear();
                }
                else
                {
                    var remainder = 0;
                    // Give out proportionally

                    for (int i = needs.Count - 1; i >= 0; i--)
                    {
                        var (receivingInventoryEntity, wanted) = needs[i];

                        if (!Core.EntityManager.Exists(receivingInventoryEntity))
                        {
                            totalWanted -= wanted;
                            needs.RemoveAt(i);
                            continue;
                        }

                        var numerator = (long)wanted * totalAmount;
                        var transferring = (int)(numerator / totalWanted);
                        remainder += (int)(numerator % totalWanted);
                        if (remainder >= totalWanted)
                        {
                            transferring++;
                            remainder -= totalWanted;
                        }
                        var transferred = Utilities.TransferItems(serverGameManager, inventoryEntity, receivingInventoryEntity, item, transferring);
                        if (transferred < transferring)
                        {
                            remainder += transferring - transferred;
                            needs.RemoveAt(i);
                        }
                        else if (transferred >= wanted)
                        {
                            needs.RemoveAt(i);
                        }
                        else
                        {
                            needs[i] = (receivingInventoryEntity, wanted - transferred);
                        }
                    }
                }
            }
        }
    }
}
