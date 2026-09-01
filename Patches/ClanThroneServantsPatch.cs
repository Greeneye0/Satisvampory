using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared.Systems;
using Satisvampory.Services;
using System;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Patches
{
    [HarmonyPatch(typeof(ServantHelper), nameof(ServantHelper.GetAllServants))]
    public static class ClanThroneGetAllServantsPatch
    {
        static void Postfix(Entity throneEntity, NativeList<Entity> result)
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                ClanThroneServants.AddMissingServants(throneEntity, ref result);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(ServantInfoEventSystem_Server), nameof(ServantInfoEventSystem_Server.GetResponseEntries))]
    public static class ClanThroneServantInfoPatch
    {
        static void Postfix(ServantInfoEventSystem_Server __instance, Entity throneEntity,
            ref FixedList4096Bytes<ServantInfoEvent.Response.Entry> entries)
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                ClanThroneServants.AddMissingEntries(__instance, throneEntity, ref entries);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(ServantInfoEventSystem_Server), nameof(ServantInfoEventSystem_Server.GetServantState))]
    public static class ClanThroneServantStatePatch
    {
        static void Postfix(Entity servantEntity, ref ServantInfoEvent.Response.ServantState __result)
        {
            try
            {
                ClanThroneServants.HonorMissionState(servantEntity, ref __result);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(ServantMissionActionSystem), nameof(ServantMissionActionSystem.SendOnMissionJob_Execute))]
    public static class ClanThroneSendOnMissionPatch
    {
        static void Prefix(ServantMissionActionSystem __instance, ref bool floorPlacementRestrictionsDisabled)
        {
            if (floorPlacementRestrictionsDisabled || !Core.HasInitialized)
                return;
            try
            {
                if (ClanThroneServants.StartQueryNeedsShare(__instance))
                    floorPlacementRestrictionsDisabled = true;
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }
    }
}
