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
    /// <summary>
    /// Sit crashed the dedicated server: Burst SerializeAndSendServerEventsSystem
    /// aborted after GetAllServants postfix wrote a native NativeList. Listing only
    /// patches GetResponseEntries (vanilla Entry.Create into the response list).
    /// </summary>
    [HarmonyPatch(typeof(ServantInfoEventSystem_Server), nameof(ServantInfoEventSystem_Server.GetResponseEntries))]
    public static class ClanThroneServantInfoPatch
    {
        static void Postfix(Entity throneEntity, ref FixedList4096Bytes<ServantInfoEvent.Response.Entry> entries)
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                ClanThroneServants.AddMissingEntries(throneEntity, ref entries);
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
