using HarmonyLib;
using ProjectM;
using Unity.Collections;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(BonfireSystem_Server), "OnUpdate")]
public static class BonfireSystem_ServerPatch
{
    static bool duskArmed;

    public static void Prefix(BonfireSystem_Server __instance)
    {
        if (!Core.HasInitialized || !Core.PlayerSettings.IsSolarEnabled(0)) return;
        if (Core.ServerGameManager.DayNightCycle.TimeOfDay == TimeOfDay.Night) { duskArmed = true; return; }
        if (!duskArmed) return;
        duskArmed = false;
        SetLit(__instance, lit: false);
    }

    public static void Postfix(BonfireSystem_Server __instance) { if (!Core.HasInitialized || !Core.PlayerSettings.IsSolarEnabled(0)) return; if (Core.ServerGameManager.DayNightCycle.TimeOfDay != TimeOfDay.Day) SetLit(__instance, lit: true); }

    static void SetLit(BonfireSystem_Server system, bool lit)
    {
        var rows = system.__query_1818188685_0.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var brazier = rows[i];
                if (!brazier.Has<BurnContainer>() || !brazier.Has<NameableInteractable>() || !brazier.Has<Bonfire>()) continue;
                if (!brazier.Read<BurnContainer>().Enabled) continue;
                if (brazier.Read<NameableInteractable>().Name.ToString().IndexOf("night", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var fire = brazier.Read<Bonfire>();
                fire.IsActive = lit;
                brazier.Write(fire);
            }
        }
        finally { rows.Dispose(); }
    }
}
