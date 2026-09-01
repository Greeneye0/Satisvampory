using ProjectM.Gameplay.Systems;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(SpawnCastleTeamSystem), nameof(SpawnCastleTeamSystem.OnUpdate))]
internal class CastleHeartSpawnSystemPatch
{
    public static bool Prefix(SpawnCastleTeamSystem __instance)
    {
        Visit(__instance._MainQuery, heart => Core.TerritoryService.AddCastleHeart(heart));
        return true;
    }

    internal static void Visit(EntityQuery query, System.Action<Entity> visit)
    {
        var rows = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++)
                visit(rows[i]);
        }
        finally { rows.Dispose(); }
    }
}

[HarmonyPatch(typeof(CastleHeartClearRaidStateSystem), nameof(CastleHeartClearRaidStateSystem.OnUpdate))]
internal class CastleHeartDestroySystemPatch
{
    public static bool Prefix(CastleHeartClearRaidStateSystem __instance)
    {
        CastleHeartSpawnSystemPatch.Visit(__instance._DestroyedCastleHeartQuery, heart => Core.TerritoryService.RemoveCastleHeart(heart));
        return true;
    }
}
