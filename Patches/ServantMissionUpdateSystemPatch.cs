using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared.Systems;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
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
        if (instance == null || !Core.HasInitialized) return;
        var frame = Time.frameCount;
        if (frame != lastStashFrame) { lastStashFrame = frame; stashed.Clear(); }

        var missions = instance._TempFinishedMissions;
        var servants = instance._TempServantList;
        try
        {
            var any = false;
            if (missions.IsCreated)
                for (var i = 0; i < missions.Length; i++)
                {
                    any = true;
                    var steamId = SteamOf(missions[i].MissionOwner);
                    if (steamId != 0 && !Core.PlayerSettings.IsAutoStashMissionsEnabled(steamId)) { DestDebugLog.Miss("servant", -1, default, 0, 0, "asm-off steam=" + steamId + " via=" + via); continue; }
                    StashOne(missions[i].MissionOwner, steamId);
                }
            if (servants.IsCreated)
                for (var i = 0; i < servants.Length; i++) { any = true; StashOne(servants[i], 0); }
            if (any) DestDebugLog.Note("servant", -1, 0, "tick via=" + via + " stashed=" + stashed.Count);
        }
        catch (System.Exception e) { Core.Log.LogError($"Exited ServantMission stash ({via}): {e}"); }
    }

    static ulong SteamOf(Entity owner)
    {
        try { if (owner == Entity.Null || !Core.EntityManager.Exists(owner) || !owner.Has<UserOwner>()) return 0; var userEnt = owner.Read<UserOwner>().Owner._Entity; return userEnt != Entity.Null && Core.EntityManager.Exists(userEnt) && userEnt.Has<User>() ? userEnt.Read<User>().PlatformId : 0; }
        catch { return 0; }
    }

    static void StashOne(Entity servant, ulong steamId) { if (servant == Entity.Null || !Core.EntityManager.Exists(servant)) return; if (!stashed.Add(servant.Index)) return; if (steamId != 0 && !Core.PlayerSettings.IsAutoStashMissionsEnabled(steamId)) return; Utilities.StashServantInventory(servant); }
}

[HarmonyPatch]
public static class ServantMissionFinishMissionsPatch
{
    static bool Prepare() => TargetMethod() != null;
    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(ServantMissionUpdateSystem), "FinishMissions")
        ?? AccessTools.DeclaredMethod(typeof(ServantMissionUpdateSystem), "FinishMissions");

    static void Prefix(NativeList<ServantMissionUpdateSystem.MissionIdentifier> finishedMissisons)
    {
        if (!Core.HasInitialized)
            return;
        try { RepeatHunts.SnapshotFinishing(finishedMissisons); }
        catch (Exception e) { Core.Log.LogError("RepeatHunt snapshot: " + e); }
    }

    static void Postfix(ServantMissionUpdateSystem __instance)
    {
        if (__instance == null)
            return;
        try { RepeatHunts.AfterReturn(); }
        catch (Exception e) { Core.Log.LogError("RepeatHunt return: " + e); }
        ServantMissionUpdateSystemPatch.TryStash(__instance, "finish");
    }
}
