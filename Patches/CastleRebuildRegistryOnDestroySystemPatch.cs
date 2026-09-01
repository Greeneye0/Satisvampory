using HarmonyLib;
using ProjectM.CastleBuilding.Rebuilding;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(CastleRebuildRegistryOnDestroySystem), nameof(CastleRebuildRegistryOnDestroySystem.OnUpdate))]
class CastleRebuildRegistryOnDestroySystemPatch
{
    static void Prefix() { Core.TerritoryService?.FlushTerritoryCache(); Core.WorkQueue?.FlushRebuildDeferred(); }
}
