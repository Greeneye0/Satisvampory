using ProjectM;
using ProjectM.Behaviours;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services;

/// <summary>
/// Command/patch façade. Scan, leftover, and recipe retrieve live in PlayerWithdraw.
/// </summary>
internal class PullService
{
        public static void PullItem(Entity character, PrefabGUID item, int quantity) =>
            PlayerWithdraw.Pull(character, item, quantity);

        internal static int CountAlliedStores(Entity character, PrefabGUID item) =>
            PlayerWithdraw.CountStores(character, item);

        internal static int CountAlliedTakeable(Entity character, PrefabGUID item, ulong leftoverOwnerId) =>
            PlayerWithdraw.CountTakeable(character, item, leftoverOwnerId);

        public static void HandleRecipePull(Entity character, Entity workstation, PrefabGUID recipe) =>
            PlayerWithdraw.Recipe(character, workstation, recipe);

        public static void HandleRepairPull(Entity character, PrefabGUID recipe, float repairNeeded, PrefabGUID repairing) =>
            PlayerWithdraw.Repair(character, recipe, repairNeeded, repairing);

        public static void HandleForgePull(Entity character, Entity workstation, Entity item) =>
            PlayerWithdraw.Forge(character, workstation, item);

        public static void HandleForgeUpgradePull(Entity character, Entity workstation, Entity item) =>
            PlayerWithdraw.ForgeUpgrade(character, workstation, item);
}
