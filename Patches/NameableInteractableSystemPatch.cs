using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(NameableInteractableSystem), nameof(NameableInteractableSystem.OnUpdate))]
internal static class NameableInteractableSystemPatch
{
    [HarmonyPrefix]
    static void Prefix(NameableInteractableSystem __instance) => Drain(__instance);

    static void Drain(NameableInteractableSystem system)
    {
        if (!Core.HasInitialized || Core.WorkQueue == null) return;
        var events = system._RenameQuery.ToComponentDataArray<InteractEvents_Client.RenameInteractable>(Allocator.Temp);
        try { for (var i = 0; i < events.Length; i++) if (Core.TryGetEntityFromNetworkId(events[i].InteractableId, out var interactable)) Core.WorkQueue.EnqueueOwner(interactable); }
        finally { events.Dispose(); }
    }
}
