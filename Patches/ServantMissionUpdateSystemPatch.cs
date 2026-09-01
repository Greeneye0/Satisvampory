using ProjectM.Shared.Systems;
using Satisvampory.Services;
using System.Reflection;
using UnityEngine;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(ServantMissionUpdateSystem), nameof(ServantMissionUpdateSystem.OnUpdate))]
public static class ServantMissionUpdateSystemPatch
{
    static int lastStashFrame = -1;
    static readonly HashSet<int> stashed = new();

    static void Prefix(ServantMissionUpdateSystem __instance) => TryStash(__instance, "prefix");
    static void Postfix(ServantMissionUpdateSystem __instance) => TryStash(__instance, "postfix");

    internal static void TryStash(ServantMissionUpdateSystem instance, string via)
    {
        if (instance == null || !Core.HasInitialized)
            return;
        var frame = Time.frameCount;
        if (frame != lastStashFrame)
        {
            lastStashFrame = frame;
            stashed.Clear();
        }

        var missions = instance._TempFinishedMissions;
        var servants = instance._TempServantList;
        try
        {
            var any = false;
            if (missions.IsCreated && missions.Length > 0)
            {
                foreach (var mission in missions)
                {
                    any = true;
                    var steamId = 0UL;
                    try
                    {
                        var ownerEnt = mission.MissionOwner;
                        if (ownerEnt != Entity.Null && Core.EntityManager.Exists(ownerEnt) && ownerEnt.Has<UserOwner>())
                        {
                            var userEnt = ownerEnt.Read<UserOwner>().Owner._Entity;
                            if (userEnt != Entity.Null && Core.EntityManager.Exists(userEnt) && userEnt.Has<User>())
                                steamId = userEnt.Read<User>().PlatformId;
                        }
                    }
                    catch { }

                    if (steamId != 0 && !Core.PlayerSettings.IsAutoStashMissionsEnabled(steamId))
                    {
                        DestDebugLog.Miss("servant", -1, default, 0, 0, "asm-off steam=" + steamId + " via=" + via);
                        continue;
                    }

                    StashOne(mission.MissionOwner, steamId, via);
                }
            }

            if (servants.IsCreated && servants.Length > 0)
            {
                foreach (var servant in servants)
                {
                    any = true;
                    StashOne(servant, 0, via);
                }
            }

            if (any)
                DestDebugLog.Note("servant", -1, 0, "tick via=" + via + " stashed=" + stashed.Count);
        }
        catch (System.Exception e)
        {
            Core.Log.LogError($"Exited ServantMission stash ({via}): {e}");
        }
    }

    static void StashOne(Entity servant, ulong steamId, string via)
    {
        if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
            return;
        if (!stashed.Add(servant.Index))
            return;
        if (steamId != 0 && !Core.PlayerSettings.IsAutoStashMissionsEnabled(steamId))
            return;
        Utilities.StashServantInventory(servant);
    }
}

[HarmonyPatch]
public static class ServantMissionFinishMissionsPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(ServantMissionUpdateSystem), "FinishMissions")
            ?? AccessTools.DeclaredMethod(typeof(ServantMissionUpdateSystem), "FinishMissions");
    }

    static void Postfix(ServantMissionUpdateSystem __instance)
    {
        if (__instance == null)
            return;
        ServantMissionUpdateSystemPatch.TryStash(__instance, "finish");
    }
}
