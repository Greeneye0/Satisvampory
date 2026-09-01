using Satisvampory.Services;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(CastleHasItemsOnSpawnSystem), nameof(CastleHasItemsOnSpawnSystem.OnUpdate))]
internal class CastleStationSpawnSystemPatch
{
    public static bool Prefix(CastleHasItemsOnSpawnSystem __instance)
    {
        Track(__instance.__query_60442477_0, spawn: true);
        return true;
    }

    internal static void Track(EntityQuery query, bool spawn)
    {
        var rows = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++)
                Register(rows[i], spawn);
        }
        finally
        {
            rows.Dispose();
        }
    }

    static void Register(Entity station, bool spawn)
    {
        if (station.Has<Bonfire>())
        {
            if (spawn) Core.BrazierService.AddBrazier(station);
            else Core.BrazierService.RemoveBrazier(station);
        }
        if (station.Has<Refinementstation>())
        {
            if (spawn) Core.RefinementStations.AddRefinementStation(station);
            else Core.RefinementStations.RemoveRefinementStation(station);
        }
        if (station.Has<Salvagestation>())
        {
            if (spawn) Core.SalvageService.AddSalvageStation(station);
            else Core.SalvageService.RemoveSalvageStation(station);
        }
        if (station.Has<UnitSpawnerstation>())
        {
            if (spawn) Core.UnitSpawnerstationService.AddUnitSpawnerStation(station);
            else Core.UnitSpawnerstationService.RemoveUnitSpawnerStation(station);
        }
        if (station.Has<WorkstationRecipesBuffer>())
        {
            if (spawn) ClanTreasuryLend.AddWorkstation(station);
            else ClanTreasuryLend.RemoveWorkstation(station);
        }
        if (spawn)
            Core.WorkQueue?.EnqueueOwner(station);
    }
}

[HarmonyPatch(typeof(CastleHasItemsOnDestroySystem), nameof(CastleHasItemsOnDestroySystem.OnUpdate))]
internal class CastleStationDestroySystemPatch
{
    public static bool Prefix(CastleHasItemsOnDestroySystem __instance)
    {
        CastleStationSpawnSystemPatch.Track(__instance._DestroyConnectedCastleItem, spawn: false);
        return true;
    }
}
