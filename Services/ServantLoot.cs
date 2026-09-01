using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class ServantLoot
    {
        public static int Deposit(Entity servant)
        {
            try
            {
                if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                    return 0;
                if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, servant, out var inventory)
                    || inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                    return 0;
                if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var inventoryBuffer))
                    return 0;

                var home = Core.TerritoryService.GetTerritoryId(servant);
                if (home < 0)
                    home = Core.TerritoryService.GetStandingTerritoryId(servant);
                if (home < 0)
                {
                    DestDebugLog.Miss("servant", -1, default, 0, 0, "no-plot");
                    return 0;
                }

                var plotIds = Core.TerritoryService.GetServantStashPlotIds(home);
                if (plotIds == null || plotIds.Count == 0)
                {
                    DestDebugLog.Miss("servant", home, default, 0, 0, "no-dest-plots");
                    return 0;
                }

                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(home, out var ownerId);
                var destCandidates = new List<(Entity stash, Entity inventory)>(capacity: 100);
                foreach (var plot in plotIds)
                {
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
                        if (!Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                            continue;
                        foreach (var attachedBuffer in buffer)
                        {
                            var attachedEntity = attachedBuffer.Entity;
                            if (!attachedEntity.Has<PrefabGUID>()) continue;
                            if (!attachedEntity.Read<PrefabGUID>().Equals(StashService.ChestBagGuid)) continue;
                            destCandidates.Add((stash, attachedEntity));
                        }
                    }
                }

                if (destCandidates.Count == 0)
                {
                    DestDebugLog.Miss("servant", home, default, 0, 0, "no-dest");
                    return 0;
                }

                var overflowStashes = plotIds
                    .Where(id => !TerritoryService.IsHeartRaided(Core.TerritoryService.GetCastleHeart(id)))
                    .SelectMany(Core.Stash.OverflowChests)
                    .OrderBy(s => StashRouting.IsSpecialName(StashRouting.RawName(s)) ? 1 : 0)
                    .ThenBy(s => Core.TerritoryService.GetTerritoryId(s) == home ? 0 : 1)
                    .ToArray();

                var addItemSettings = Utilities.GetAddItemSettings();
                var moved = 0;
                for (var i = 0; i < inventoryBuffer.Length; i++)
                {
                    var itemEntry = inventoryBuffer[i];
                    var item = itemEntry.ItemType;
                    if (item.GuidHash == 0) continue;
                    var hasItemEntity = !itemEntry.ItemEntity.GetEntityOnServer().Equals(Entity.Null);
                    var dests = StashRouting.OrderDepositDests(destCandidates, item, ownerId, home);
                    if (hasItemEntity)
                    {
                        var placed = false;
                        foreach (var dest in dests)
                        {
                            var buf = dest.inventory.ReadBuffer<InventoryBuffer>();
                            for (var j = 0; j < buf.Length; j++)
                            {
                                if (!buf[j].ItemType.Equals(PrefabGUID.Empty)) continue;
                                buf[j] = itemEntry;
                                var itemEntity = itemEntry.ItemEntity.GetEntityOnServer();
                                if (itemEntity.Has<InventoryItem>())
                                {
                                    var inventoryItem = itemEntity.Read<InventoryItem>();
                                    inventoryItem.ContainerEntity = dest.stash;
                                    itemEntity.Write(inventoryItem);
                                }
                                InventoryUtilitiesServer.ClearSlot(Core.EntityManager, inventory, i);
                                var rank = StashRouting.RankDeposit(dest.stash, item, ownerId, true, home);
                                DestDebugLog.Move("servant", home, item, 1, servant, dest.stash, rank.Label + "/c" + rank.Class, 0, "stays");
                                moved++;
                                placed = true;
                                break;
                            }
                            if (placed) break;
                        }
                        continue;
                    }

                    foreach (var dest in dests)
                    {
                        var add = InventoryUtilitiesServer.TryAddItem(addItemSettings, dest.inventory, itemEntry);
                        if (!add.Success) continue;
                        var n = itemEntry.Amount - add.RemainingAmount;
                        if (n > 0)
                        {
                            var rank = StashRouting.RankDeposit(dest.stash, item, ownerId, true, home);
                            DestDebugLog.Move("servant", home, item, n, servant, dest.stash, rank.Label + "/c" + rank.Class, 0, "stays");
                            moved += n;
                        }
                        itemEntry.Amount = add.RemainingAmount;
                        if (!add.ItemsRemaining)
                        {
                            InventoryUtilitiesServer.ClearSlot(Core.EntityManager, inventory, i);
                            break;
                        }
                    }

                    if (itemEntry.Amount <= 0)
                        continue;

                    ItemData itemData = default;
                    if (overflowStashes.Length > 0
                        && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
                        itemData = prefab.Read<ItemData>();
                    var isSoulshard = itemData.ItemCategory == ItemCategory.Soulshard;
                    foreach (var overflowStash in overflowStashes)
                    {
                        if (!Core.ServerGameManager.TryGetBuffer<InventoryInstanceElement>(overflowStash, out var iieBuffer))
                            continue;
                        Entity overflowInventory = Entity.Null;
                        foreach (var iie in iieBuffer)
                        {
                            if (iie.RestrictedType != PrefabGUID.Empty && iie.RestrictedType != itemData.ItemTypeGUID
                                || iie.RestrictedCategory != 0 && (iie.RestrictedCategory & (long)itemData.ItemCategory) == 0
                                || isSoulshard && iie.RestrictedCategory == 0)
                                continue;
                            overflowInventory = iie.ExternalInventoryEntity.GetEntityOnServer();
                        }
                        if (overflowInventory == Entity.Null) continue;
                        var add = InventoryUtilitiesServer.TryAddItem(addItemSettings, overflowInventory, itemEntry);
                        if (!add.Success) continue;
                        var n = itemEntry.Amount - add.RemainingAmount;
                        if (n > 0)
                        {
                            DestDebugLog.Move("servant", home, item, n, servant, overflowStash, "overflow", 0, "stays");
                            moved += n;
                        }
                        itemEntry.Amount = add.RemainingAmount;
                        if (!add.ItemsRemaining)
                        {
                            InventoryUtilitiesServer.ClearSlot(Core.EntityManager, inventory, i);
                            break;
                        }
                    }
                    if (itemEntry.Amount > 0)
                        inventoryBuffer[i] = itemEntry;
                }

                DestDebugLog.Note("servant", home, ownerId, "stash plots=" + plotIds.Count + " dests=" + destCandidates.Count + " moved=" + moved);
                return moved;
            }
            catch (Exception e)
            {
                Core.LogException(e, "StashServantLoot");
                return 0;
            }
        }


        public static string List(int plotFilter)
        {
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plotFilter).Append(",\"servants\":[");
            var first = true;
            var coffinQb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantCoffinstation>(), ComponentType.AccessMode.ReadOnly));
            var coffinQuery = Core.EntityManager.CreateEntityQuery(ref coffinQb);
            coffinQb.Dispose();
            NativeArray<Entity> coffinArr = default;
            try
            {
                coffinArr = coffinQuery.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < coffinArr.Length; i++)
                {
                    var coffin = coffinArr[i];
                    if (coffin == Entity.Null || !Core.EntityManager.Exists(coffin) || !coffin.Has<ServantCoffinstation>())
                        continue;
                    var plot = Core.TerritoryService.GetTerritoryId(coffin);
                    if (plotFilter >= 0 && plot != plotFilter)
                        continue;
                    var station = coffin.Read<ServantCoffinstation>();
                    var servant = station.ConnectedServant.GetEntityOnServer();
                    var name = station.ServantName.ToString();
                    var stacks = 0;
                    var kinds = 0;
                    Entity inv = Entity.Null;
                    if (servant != Entity.Null && Core.EntityManager.Exists(servant)
                        && InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, servant, out inv)
                        && inv != Entity.Null && Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                    {
                        var seen = new HashSet<int>();
                        for (var s = 0; s < buf.Length; s++)
                        {
                            if (buf[s].ItemType.GuidHash == 0 || buf[s].Amount <= 0)
                                continue;
                            stacks += buf[s].Amount;
                            if (seen.Add(buf[s].ItemType.GuidHash))
                                kinds++;
                        }
                    }
                    if (!first) sb.Append(',');
                    first = false;
                    var onMission = servant != Entity.Null && Core.EntityManager.Exists(servant)
                        && servant.Has<ServantData>() && servant.Read<ServantData>().IsOnMission;
                    sb.Append("{\"plot\":").Append(plot)
                        .Append(",\"name\":\"").Append(DebugPeekServiceEsc(name)).Append('"')
                        .Append(",\"state\":\"").Append(station.State.ToString()).Append('"')
                        .Append(",\"stacks\":").Append(stacks)
                        .Append(",\"kinds\":").Append(kinds)
                        .Append(",\"onMission\":").Append(onMission ? "true" : "false")
                        .Append(",\"hasServant\":").Append(servant != Entity.Null && Core.EntityManager.Exists(servant) ? "true" : "false")
                        .Append('}');
                }
            }
            finally
            {
                if (coffinArr.IsCreated)
                    coffinArr.Dispose();
                coffinQuery.Dispose();
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string DebugPeekServiceEsc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }


        public static string StashAll(int plotFilter)
        {
            var seen = new HashSet<int>();
            var servants = 0;
            var coffins = 0;
            var moved = 0;
            var skipped = 0;

            void Consider(Entity e)
            {
                if (e == Entity.Null || !Core.EntityManager.Exists(e))
                    return;
                if (!seen.Add(e.Index))
                    return;
                if (plotFilter >= 0)
                {
                    var p = Core.TerritoryService.GetTerritoryId(e);
                    if (p < 0)
                        p = Core.TerritoryService.GetStandingTerritoryId(e);
                    if (p != plotFilter)
                    {
                        skipped++;
                        return;
                    }
                }
                moved += Deposit(e);
                servants++;
            }

            var coffinQb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantCoffinstation>(), ComponentType.AccessMode.ReadOnly));
            var coffinQuery = Core.EntityManager.CreateEntityQuery(ref coffinQb);
            coffinQb.Dispose();
            NativeArray<Entity> coffinArr = default;
            try
            {
                coffinArr = coffinQuery.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < coffinArr.Length; i++)
                {
                    var coffin = coffinArr[i];
                    coffins++;
                    Consider(coffin);
                    if (coffin == Entity.Null || !Core.EntityManager.Exists(coffin) || !coffin.Has<ServantCoffinstation>())
                        continue;
                    var station = coffin.Read<ServantCoffinstation>();
                    var connected = station.ConnectedServant.GetEntityOnServer();
                    Consider(connected);
                }
            }
            finally
            {
                if (coffinArr.IsCreated)
                    coffinArr.Dispose();
                coffinQuery.Dispose();
            }

            var itemQb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantHasItemsInInventory>(), ComponentType.AccessMode.ReadOnly));
            var itemQuery = Core.EntityManager.CreateEntityQuery(ref itemQb);
            itemQb.Dispose();
            NativeArray<Entity> itemArr = default;
            try
            {
                itemArr = itemQuery.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < itemArr.Length; i++)
                    Consider(itemArr[i]);
            }
            finally
            {
                if (itemArr.IsCreated)
                    itemArr.Dispose();
                itemQuery.Dispose();
            }

            return "{\"plot\":" + plotFilter
                + ",\"coffins\":" + coffins
                + ",\"stashed\":" + servants
                + ",\"moved\":" + moved
                + ",\"skipped\":" + skipped + "}";
        }
    }
}
