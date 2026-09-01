using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using Unity.Collections;
using Unity.Entities;
using Satisvampory.Services;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(CastleHasItemsOnSpawnSystem), nameof(CastleHasItemsOnSpawnSystem.OnUpdate))]
internal class CastleStationSpawnSystemPatch
{
    public static bool Prefix(CastleHasItemsOnSpawnSystem system) { Track(system.__query_60442477_0, spawn: true); return true; }

    internal static void Track(EntityQuery query, bool spawn)
    {
        var rows = query.ToEntityArray(Allocator.Temp);
        try { for (var i = 0; i < rows.Length; i++) Register(rows[i], spawn); }
        finally { rows.Dispose(); }
    }

    static void Register(Entity station, bool spawn)
    {
        if (station.Has<Bonfire>()) Bind(spawn, () => Core.BrazierService.AddBrazier(station), () => Core.BrazierService.RemoveBrazier(station));
        if (station.Has<Refinementstation>()) Bind(spawn, () => Core.RefinementStations.AddRefinementStation(station), () => Core.RefinementStations.RemoveRefinementStation(station));
        if (station.Has<Salvagestation>()) Bind(spawn, () => Core.SalvageService.AddSalvageStation(station), () => Core.SalvageService.RemoveSalvageStation(station));
        if (station.Has<UnitSpawnerstation>()) Bind(spawn, () => Core.UnitSpawnerstationService.AddUnitSpawnerStation(station), () => Core.UnitSpawnerstationService.RemoveUnitSpawnerStation(station));
        if (station.Has<WorkstationRecipesBuffer>()) Bind(spawn, () => ClanTreasuryLend.AddWorkstation(station), () => ClanTreasuryLend.RemoveWorkstation(station));
        if (spawn) Core.WorkQueue?.EnqueueOwner(station);
    }

    static void Bind(bool spawn, System.Action add, System.Action remove) { if (spawn) add(); else remove(); }
}

[HarmonyPatch(typeof(CastleHasItemsOnDestroySystem), nameof(CastleHasItemsOnDestroySystem.OnUpdate))]
internal class CastleStationDestroySystemPatch
{
    public static bool Prefix(CastleHasItemsOnDestroySystem system) { CastleStationSpawnSystemPatch.Track(system._DestroyConnectedCastleItem, spawn: false); return true; }
}
