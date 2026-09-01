using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(ReactToInventoryChangedSystem), nameof(ReactToInventoryChangedSystem.OnUpdate))]
internal static class ReactToInventoryChangedSystemPatch
{
    [HarmonyPrefix]
    static void Prefix(ReactToInventoryChangedSystem __instance) => Drain(__instance);

    static void Drain(ReactToInventoryChangedSystem system)
    {
        if (!Core.HasInitialized || Core.WorkQueue is not { IsSelfTransferring: false })
            return;
        var events = system.EntityQueries[0].ToComponentDataArray<InventoryChangedEvent>(Allocator.Temp);
        try { for (var i = 0; i < events.Length; i++) EnqueueCastle(events[i].InventoryEntity); }
        finally { events.Dispose(); }
    }

    static void EnqueueCastle(Entity inventory) { if (!inventory.Has<InventoryConnection>()) return; var owner = inventory.Read<InventoryConnection>().InventoryOwner; if (owner != Entity.Null && owner.Has<CastleHeartConnection>()) Core.WorkQueue.EnqueueOwner(owner); }
}
