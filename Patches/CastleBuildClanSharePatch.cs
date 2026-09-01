using HarmonyLib;
using Satisvampory.Services;
using ProjectM;
using ProjectM.CastleBuilding.Placement;
using Stunlock.Core;
using System;
using System.Reflection;
using Unity.Entities;

namespace Satisvampory.Patches
{
    /// <summary>
    /// Castle BUILD (hotbar place / prison cell) goes through HasEnoughResources and
    /// RemoveItemsDueToResourceRequirements with MapZoneCollection, not always through
    /// MergedInventoriesUtility with includeCastleSharedInventories=true. 1.6.1.13 hooks
    /// on GetCastleMergedInventoryDatas / RemoveItemGetRemainder are then a no-op for build.
    /// </summary>
    public static class CastleBuildClanSharePatch
    {
        static bool CanShareBuild(Entity character, BuildResourceConsumeType resourceConsumeType, out Entity heart)
        {
            heart = Entity.Null;
            if (resourceConsumeType != BuildResourceConsumeType.SharedInventory)
                return false;
            if (!Core.HasInitialized)
                return false;
            heart = ClanTreasuryShare.HeartFromTarget(character);
            return ClanTreasuryShare.ShouldShare(heart);
        }

        [HarmonyPatch(typeof(GetPlacementResourcesResult), nameof(GetPlacementResourcesResult.HasEnoughResources))]
        public static class HasEnough_Patch
        {
            static void Postfix(MapZoneCollection mapZoneCollection, PlacementResourcesResult resourcesResult,
                EntityManager entityManager, Entity character, BuildResourceConsumeType resourceConsumeType,
                ref bool __result)
            {
                if (__result)
                    return;
                if (!CanShareBuild(character, resourceConsumeType, out var heart))
                    return;
                try
                {
                    __result = ClanTreasuryShare.HasEnoughForBuild(
                        entityManager, heart, character, resourcesResult.ResourceRequirements);
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                }
            }
        }

        [HarmonyPatch]
        public static class RemoveItems_Patch
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(ApplyPlacementResourcesResult),
                "RemoveItemsDueToResourceRequirements");

            static bool Prefix(PlacementResourcesResult result, MapZoneCollection mapZoneCollection,
                EntityManager entityManager, Entity character, BuildResourceConsumeType resourceConsumeType)
            {
                if (!CanShareBuild(character, resourceConsumeType, out var heart))
                    return true;
                try
                {
                    // Unpaid named-only mats must not place. If clan treasury cannot cover
                    // the remainder, do not swallow vanilla consume (return true) so the
                    // original method fails the placement.
                    if (!ClanTreasuryShare.HasEnoughForBuild(
                        entityManager, heart, character, result.ResourceRequirements))
                        return true;
                    var paid = ClanTreasuryShare.ConsumeBuildRequirements(
                        entityManager, mapZoneCollection, heart, character, result.ResourceRequirements);
                    if (!paid)
                        return true;
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
            }
        }
    }
}
