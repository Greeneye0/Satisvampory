using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(ToggleRefiningRecipeSystem), nameof(ToggleRefiningRecipeSystem.OnUpdate))]
internal static class ToggleRefiningRecipeSystemPatch
{
    [HarmonyPrefix]
    static void Prefix(ToggleRefiningRecipeSystem __instance) => Drain(__instance);

    static void Drain(ToggleRefiningRecipeSystem system)
    {
        if (!Core.HasInitialized || Core.WorkQueue == null) return;
        var events = system._EventQuery.ToComponentDataArray<ToggleRefiningRecipeEvent>(Allocator.Temp);
        try { for (var i = 0; i < events.Length; i++) if (Core.TryGetEntityFromNetworkId(events[i].RefinementStation, out var station)) Core.WorkQueue.EnqueueOwner(station); }
        finally { events.Dispose(); }
    }
}
