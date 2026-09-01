using Il2CppInterop.Runtime;
using Satisvampory.Services;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory;

public class Utilities
{
    public static void StashServantInventory(Entity servant) { if (servant == Entity.Null || !Core.EntityManager.Exists(servant)) return; if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, servant, out var inventory)) return; if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory)) return; Core.Stash.StashServantLoot(servant); }

    public static Dictionary<PrefabGUID, List<Entity>> GetItemStashesOnTerritory(int territoryId) =>
        InventoryMove.CollectOccupiedChests(territoryId);

    public static void StashInventoryEntity(Entity inventory, Dictionary<PrefabGUID, List<Entity>> itemInventories, List<Entity> overflows) =>
        InventoryMove.DrainInventoryToMatches(inventory, itemInventories, overflows);

    public static void StashInventoryEntity(Entity entityWithTerritory, Entity inventory, string overflowStashName) =>
        InventoryMove.DrainInventoryByName(entityWithTerritory, inventory, overflowStashName);

    public static bool TerritoryCheck(Entity character, Entity target)
    {
        if (character == Entity.Null || target == Entity.Null) return false;
        if (!target.Has<CastleHeartConnection>() || !character.Has<TilePosition>()) return false;
        var heart = target.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
        if (heart == Entity.Null || !heart.Has<CastleHeart>()) return false;
        var tile = character.Read<TilePosition>().Tile;
        var plot = heart.Read<CastleHeart>().CastleTerritoryEntity;
        return CastleTerritoryExtensions.IsTileInTerritory(Core.EntityManager, tile, ref plot, out _);
    }

    public static bool SharedHeartConnection(Entity input, Entity output) { if (input == Entity.Null || output == Entity.Null) return false; if (!input.Has<CastleHeartConnection>() || !output.Has<CastleHeartConnection>()) return false; var a = input.Read<CastleHeartConnection>().CastleHeartEntity._Entity; var b = output.Read<CastleHeartConnection>().CastleHeartEntity._Entity; return a != Entity.Null && a.Equals(b); }

    public static bool TransferItemEntities(Entity outputInventory, Entity inputInventory, PrefabGUID itemPrefab, int transferAmount, ref int startInputSlot, out int amountTransferred)
    {
        if (transferAmount <= 0) { amountTransferred = 0; return false; }
        amountTransferred = InventoryMove.RelocateEntities(outputInventory, inputInventory, itemPrefab, transferAmount, ref startInputSlot);
        return startInputSlot >= inputInventory.ReadBuffer<InventoryBuffer>().Length && amountTransferred < transferAmount;
    }

    public static int TransferItems(ServerGameManager serverGameManager, Entity outputInventory, Entity inputInventory, PrefabGUID itemGuid, int transferAmount) =>
        InventoryMove.CopyStacks(serverGameManager, outputInventory, inputInventory, itemGuid, transferAmount);

    public static AddItemSettings GetAddItemSettings() => InventoryMove.BuildAddSettings();

    public static void SendSystemMessageToClient(EntityManager entityManager, User user, string message) =>
        InventoryMove.Tell(entityManager, user, message);

    public static bool IsRoomOfType(Entity roomEntity, CastleFloorTypes floorType) =>
        InventoryMove.EnclosedFloorsMatch(roomEntity, floorType);
}
