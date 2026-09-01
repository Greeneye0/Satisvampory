using HarmonyLib;
using ProjectM.Shared.Systems;
using Satisvampory.Services;
using System;
using Unity.Entities;

namespace Satisvampory.Patches
{
    /// <summary>
    /// Retarget throne info/hunt events to the plot chosen by .s throne.
    /// Do not touch GetResponseEntries / GetAllServants (Burst abort on sit).
    /// </summary>
    [HarmonyPatch(typeof(ServantInfoEventSystem_Server), nameof(ServantInfoEventSystem_Server.OnUpdate))]
    public static class ClanThroneInfoRewritePatch
    {
        static void Prefix(ServantInfoEventSystem_Server __instance)
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                ClanThroneServants.RewriteInfoRequests(__instance);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(ServantMissionActionSystem), nameof(ServantMissionActionSystem.OnUpdate))]
    public static class ClanThroneMissionRewritePatch
    {
        static void Prefix(ServantMissionActionSystem __instance)
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                ClanThroneServants.RewriteMissionEvents(__instance);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(ServantMissionActionSystem), nameof(ServantMissionActionSystem.ValidateThroneAndServants))]
    public static class ClanThroneValidatePatch
    {
        static void Postfix(Entity throneEntity, Entity fromCharacter, ref bool __result)
        {
            if (__result || !Core.HasInitialized)
                return;
            try
            {
                if (ClanThroneServants.MayManageFrom(fromCharacter, throneEntity))
                    __result = true;
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }
}
