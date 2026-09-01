using System;
using ProjectM.Shared;
using Satisvampory.Services;

namespace Satisvampory.Patches;

public class CraftingPatch
{
    static bool CraftPullOn(FromCharacter from, out Entity character)
    {
        character = from.Character;
        return Core.PlayerSettings.IsCraftPullEnabled(from.User.Read<User>().PlatformId);
    }

    static void Visit(EntityQuery query, Action<Entity> visit)
    {
        var rows = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++)
                visit(rows[i]);
        }
        finally
        {
            rows.Dispose();
        }
    }

    static bool AlreadyQueued(Entity station, PrefabGUID recipe)
    {
        if (!station.Has<QueuedWorkstationCraftAction>())
            return true;
        var queued = Core.EntityManager.GetBuffer<QueuedWorkstationCraftAction>(station);
        for (var i = 0; i < queued.Length; i++)
        {
            if (queued[i].RecipeGuid.Equals(recipe))
                return true;
        }
        return false;
    }

    static void RepairIfDamaged(Entity character, Entity item, PrefabGUID prefab)
    {
        if (!item.Has<Durability>())
            return;
        var durability = item.Read<Durability>();
        if (durability.Value >= durability.MaxDurability)
            return;
        PullService.HandleRepairPull(character, durability.RepairRecipe, durability.Value / durability.MaxDurability, prefab);
    }

    [HarmonyPatch(typeof(StopCraftingSystem), nameof(StopCraftingSystem.OnUpdate))]
    public static class StopCraftHook
    {
        public static void Prefix(StopCraftingSystem __instance)
        {
            Visit(__instance._EventQuery, entity =>
            {
                if (!entity.Has<StopCraftItemEvent>() || !entity.Has<FromCharacter>())
                    return;
                var from = entity.Read<FromCharacter>();
                if (!CraftPullOn(from, out var character))
                    return;
                var station = character.Read<Interactor>().Target;
                var recipe = entity.Read<StopCraftItemEvent>().RecipeGuid;
                if (AlreadyQueued(station, recipe))
                    return;
                PullService.HandleRecipePull(character, station, recipe);
            });
        }
    }

    [HarmonyPatch(typeof(ForgeSystem_Events), nameof(ForgeSystem_Events.OnUpdate))]
    public static class ForgeHook
    {
        public static void Prefix(ForgeSystem_Events __instance)
        {
            Visit(__instance._CancelRepairEventQuery, entity =>
            {
                var from = entity.Read<FromCharacter>();
                if (!CraftPullOn(from, out var character))
                    return;
                var station = character.Read<Interactor>().Target;
                if (!station.Has<Forge_Shared>())
                    return;
                var forge = station.Read<Forge_Shared>();
                if (forge.State.Equals(ForgeState.Repairing))
                    return;
                var item = forge.ItemEntity._Entity;
                if (item.Has<ShatteredItem>())
                    PullService.HandleForgePull(character, station, item);
                else if (item.Has<UpgradeableLegendaryItem>())
                    PullService.HandleForgeUpgradePull(character, station, item);
            });
        }
    }

    [HarmonyPatch(typeof(RepairItemSystem), nameof(RepairItemSystem.OnUpdate))]
    public static class RepairHook
    {
        public static void Prefix(RepairItemSystem __instance)
        {
            Visit(__instance._RepairItemEventQuery, entity =>
            {
                var ev = entity.Read<RepairItemEvent>();
                var from = entity.Read<FromCharacter>();
                if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, from.Character, out var inventory))
                    return;
                if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                    return;
                var row = slots[ev.Slot];
                RepairIfDamaged(from.Character, row.ItemEntity._Entity, row.ItemType);
            });

            Visit(__instance._RepairEquippedItemEventQuery, entity =>
            {
                var ev = entity.Read<RepairEquippedItemEvent>();
                var from = entity.Read<FromCharacter>();
                var gear = from.Character.Read<Equipment>().GetEquipmentEntity(ev.EquipmentType);
                var item = gear.GetEntityOnServer();
                if (!item.Has<PrefabGUID>())
                    return;
                RepairIfDamaged(from.Character, item, item.Read<PrefabGUID>());
            });
        }
    }
}
