using HarmonyLib;
using Satisvampory.Services;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using System;

namespace Satisvampory.Patches;

[HarmonyPatch]
public class CraftingPatch
{
    static bool CraftPullOn(FromCharacter from, out Entity character) { character = from.Character; return Core.PlayerSettings.IsCraftPullEnabled(from.User.Read<User>().PlatformId); }

    static void Visit(EntityQuery query, Action<Entity> visit)
    {
        var rows = query.ToEntityArray(Allocator.Temp);
        try { for (var i = 0; i < rows.Length; i++) visit(rows[i]); }
        finally { rows.Dispose(); }
    }

    static bool AlreadyQueued(Entity station, PrefabGUID recipe) { if (!station.Has<QueuedWorkstationCraftAction>()) return true; var queued = Core.EntityManager.GetBuffer<QueuedWorkstationCraftAction>(station); for (var i = 0; i < queued.Length; i++) if (queued[i].RecipeGuid.Equals(recipe)) return true; return false; }

    static void RepairIfDamaged(Entity character, Entity item, PrefabGUID prefab) { if (!item.Has<Durability>()) return; var durability = item.Read<Durability>(); if (durability.Value >= durability.MaxDurability) return; PullService.HandleRepairPull(character, durability.RepairRecipe, durability.Value / durability.MaxDurability, prefab); }

    static void OnStopCraft(Entity entity)
    {
        if (!entity.Has<StopCraftItemEvent>() || !entity.Has<FromCharacter>()) return;
        var from = entity.Read<FromCharacter>();
        if (!CraftPullOn(from, out var character)) return;
        var station = character.Read<Interactor>().Target;
        var recipe = entity.Read<StopCraftItemEvent>().RecipeGuid;
        if (!AlreadyQueued(station, recipe)) PullService.HandleRecipePull(character, station, recipe);
    }

    static void OnForgeCancel(Entity entity)
    {
        var from = entity.Read<FromCharacter>();
        if (!CraftPullOn(from, out var character)) return;
        var station = character.Read<Interactor>().Target;
        if (!station.Has<Forge_Shared>()) return;
        var forge = station.Read<Forge_Shared>();
        if (forge.State.Equals(ForgeState.Repairing)) return;
        var item = forge.ItemEntity._Entity;
        if (item.Has<ShatteredItem>()) PullService.HandleForgePull(character, station, item);
        else if (item.Has<UpgradeableLegendaryItem>()) PullService.HandleForgeUpgradePull(character, station, item);
    }

    static void OnRepairSlot(Entity entity)
    {
        var ev = entity.Read<RepairItemEvent>();
        var from = entity.Read<FromCharacter>();
        if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, from.Character, out var inventory)) return;
        if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots)) return;
        var row = slots[ev.Slot];
        RepairIfDamaged(from.Character, row.ItemEntity._Entity, row.ItemType);
    }

    static void OnRepairGear(Entity entity) { var ev = entity.Read<RepairEquippedItemEvent>(); var from = entity.Read<FromCharacter>(); var item = from.Character.Read<Equipment>().GetEquipmentEntity(ev.EquipmentType).GetEntityOnServer(); if (item.Has<PrefabGUID>()) RepairIfDamaged(from.Character, item, item.Read<PrefabGUID>()); }

    [HarmonyPatch(typeof(StopCraftingSystem), nameof(StopCraftingSystem.OnUpdate))]
    [HarmonyPrefix]
    static void StopCraftPrefix(StopCraftingSystem system) => Visit(system._EventQuery, OnStopCraft);

    [HarmonyPatch(typeof(ForgeSystem_Events), nameof(ForgeSystem_Events.OnUpdate))]
    [HarmonyPrefix]
    static void ForgePrefix(ForgeSystem_Events system) => Visit(system._CancelRepairEventQuery, OnForgeCancel);

    [HarmonyPatch(typeof(RepairItemSystem), nameof(RepairItemSystem.OnUpdate))]
    [HarmonyPrefix]
    static void RepairPrefix(RepairItemSystem system) { Visit(system._RepairItemEventQuery, OnRepairSlot); Visit(system._RepairEquippedItemEventQuery, OnRepairGear); }
}
