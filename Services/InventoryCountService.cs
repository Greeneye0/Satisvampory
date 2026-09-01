using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services;

/// <summary>
/// Counts items for the scooping player's remaining-cap budget.
/// Always uses the scooping player's settings (never a castle-heart owner).
/// Guild mode counts THAT player's bags plus clan-wide castle stashes.
/// </summary>
internal static class InventoryCountService
{
    public const int MinTerritoryId = 0;
    public const int MaxTerritoryId = 146;
    public static readonly PrefabGUID ChestBagGuid = new(1183666186);

    public static int CountForCap(Entity character, User user, PrefabGUID item, CapMode mode)
    {
        var bags = CountInPlayerBags(character, item);
        if (mode != CapMode.Guild)
            return bags;
        return bags + CountInClanStashes(character, user, item);
    }

    public static int CountInPlayerBags(Entity character, PrefabGUID item)
    {
        if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var inventory))
            return 0;
        if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
            return 0;
        return Core.ServerGameManager.GetInventoryItemCount(inventory, item);
    }

    public static bool TryGetPlayerInventory(Entity character, out Entity inventory)
    {
        inventory = Entity.Null;
        return InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out inventory)
               && inventory != Entity.Null
               && Core.EntityManager.Exists(inventory);
    }

    public static HashSet<int> GetCarriedGuidHashes(Entity character)
    {
        var result = new HashSet<int>();
        if (!TryGetPlayerInventory(character, out var inventory))
            return result;
        if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var buffer))
            return result;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].ItemType.GuidHash == 0) continue;
            result.Add(buffer[i].ItemType.GuidHash);
        }
        return result;
    }

    static int CountInClanStashes(Entity character, User user, PrefabGUID item)
    {
        var total = 0;
        foreach (var id in Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character))
        {
            var heart = Core.TerritoryService.GetCastleHeart(id);
            if (heart == Entity.Null) continue;
            total += CountStashesOnHeart(heart, item);
        }
        return total;
    }

    static int CountStashesOnHeart(Entity castleHeart, PrefabGUID item)
    {
        if (!castleHeart.Has<SharedCastleInventoryConnection>())
            return 0;
        var manager = castleHeart.Read<SharedCastleInventoryConnection>().SharedInventoryManager.GetEntityOnServer();
        if (manager == Entity.Null || !Core.EntityManager.Exists(manager))
            return 0;
        if (!Core.EntityManager.HasBuffer<SharedCastleInventories>(manager))
            return 0;

        var total = 0;
        var shared = Core.EntityManager.GetBuffer<SharedCastleInventories>(manager);
        for (var i = 0; i < shared.Length; i++)
        {
            var stash = shared[i].InventorySource;
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash)) continue;
            if (stash.Has<Refinementstation>()) continue;
            if (stash.Has<UnitSpawnerstation>()) continue;
            if (!Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(stash, out var attached))
                continue;
            for (var a = 0; a < attached.Length; a++)
            {
                var attachedEntity = attached[a].Entity;
                if (attachedEntity == Entity.Null || !Core.EntityManager.Exists(attachedEntity)) continue;
                if (!attachedEntity.Has<PrefabGUID>()) continue;
                if (!attachedEntity.Read<PrefabGUID>().Equals(ChestBagGuid)) continue;
                total += Core.ServerGameManager.GetInventoryItemCount(attachedEntity, item);
            }
        }
        return total;
    }
}

