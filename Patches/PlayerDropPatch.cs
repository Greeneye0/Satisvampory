using System;
using Satisvampory.Services;
using HarmonyLib;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Satisvampory.Patches;

/// <summary>
/// ItemPickup has no owner/source field on 1.1. Tag player inventory-drops by
/// watching the drop-command events the server processes.
/// </summary>
[HarmonyPatch(typeof(DropItemSystem), nameof(DropItemSystem.OnUpdate))]
static class DropItemSystem_PlayerDropPatch
{
    static void Prefix() => DropTrackerHooks.ScanPlayerDropEvents();
}

[HarmonyPatch(typeof(DropInventoryItemSystem), nameof(DropInventoryItemSystem.OnUpdate))]
static class DropInventoryItemSystem_PlayerDropPatch
{
    static void Prefix() => DropTrackerHooks.ScanPlayerDropEvents();
}

[HarmonyPatch(typeof(DropItemThrowSystem), nameof(DropItemThrowSystem.OnUpdate))]
static class DropItemThrowSystem_PlayerDropPatch
{
    static void Prefix() => DropTrackerHooks.ScanPlayerDropEvents();
}

static class DropTrackerHooks
{
    static float lastScanUnscaled = float.NaN;

    public static void ScanPlayerDropEvents()
    {
        if (!Core.HasInitialized) return;

        // The three systems can run in one frame; events last that frame.
        // One scan sees every Drop* event entity.
        var t = UnityEngine.Time.unscaledTime;
        if (t == lastScanUnscaled) return;
        lastScanUnscaled = t;

        try
        {
            Scan<DropItemAtSlotEvent>(ReadSlotDrop);
            Scan<DropInventoryItemEvent>(ReadInventoryDrop);
            Scan<DropEntireInventoryEvent>(ReadEntireDrop);
        }
        catch (Exception e)
        {
            Core.LogException(e);
        }
    }

    static void Scan<TEvent>(Action<Entity> read) where TEvent : struct
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(new(Il2CppType.Of<TEvent>(), ComponentType.AccessMode.ReadOnly))
            .AddAll(new(Il2CppType.Of<FromCharacter>(), ComponentType.AccessMode.ReadOnly));
        var query = Core.EntityManager.CreateEntityQuery(ref builder);
        builder.Dispose();
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (entity == Entity.Null || !Core.EntityManager.Exists(entity)) continue;
                if (!entity.Has<FromCharacter>()) continue;
                read(entity);
            }
        }
        finally
        {
            entities.Dispose();
            query.Dispose();
        }
    }

    static void ReadSlotDrop(Entity entity)
    {
        var ev = entity.Read<DropItemAtSlotEvent>();
        NoteFromCharacter(entity.Read<FromCharacter>(), ev.SlotIndex);
    }

    static void ReadInventoryDrop(Entity entity)
    {
        var ev = entity.Read<DropInventoryItemEvent>();
        NoteFromCharacter(entity.Read<FromCharacter>(), ev.SlotIndex);
    }

    static void ReadEntireDrop(Entity entity)
    {
        NoteFromCharacter(entity.Read<FromCharacter>(), slotIndex: -1);
    }

    static void NoteFromCharacter(FromCharacter from, int slotIndex)
    {
        var character = from.Character;
        if (character == Entity.Null || !Core.EntityManager.Exists(character))
            return;
        if (!character.Has<Translation>())
            return;

        var pos = character.Read<Translation>().Value;
        var item = default(PrefabGUID);
        var amount = 0;
        if (slotIndex >= 0
            && InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var inventory)
            && inventory != Entity.Null
            && Core.EntityManager.Exists(inventory)
            && Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var buffer)
            && slotIndex < buffer.Length)
        {
            var entry = buffer[slotIndex];
            item = entry.ItemType;
            amount = entry.Amount;
        }

        DropTracker.NotePlayerDrop(pos, item, amount);
        var who = "?";
        if (from.User != Entity.Null && Core.EntityManager.Exists(from.User) && from.User.Has<User>())
            who = from.User.Read<User>().CharacterName.ToString();
        Core.Log.LogInfo($"Player drop noted {who} item={item.GuidHash} amount={amount} slot={slotIndex}");
    }
}
