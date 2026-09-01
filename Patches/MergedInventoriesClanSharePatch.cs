using HarmonyLib;
using Satisvampory.Services;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Patches
{
    /// <summary>
    /// Vanilla 1.1 territory inventory consume path. When the standing castle heart-owner
    /// has .l clanshare ON, sibling clan treasuries are included in the merged count and
    /// in the consume remainder. Hearts do not share one SharedCastleInventories buffer.
    /// </summary>
    public static class MergedInventoriesClanSharePatch
    {
        static bool TryBeginShare(Entity standingHeart, bool includeCastleSharedInventories)
        {
            if (ClanTreasuryShare.Suppress)
                return false;
            if (!includeCastleSharedInventories)
                return false;
            if (!Core.HasInitialized)
                return false;
            return ClanTreasuryShare.ShouldShare(standingHeart);
        }

        static MethodBase FindRemove(params Type[] args)
        {
            return AccessTools.Method(typeof(MergedInventoriesUtility), nameof(MergedInventoriesUtility.RemoveItemGetRemainder), args);
        }

        [HarmonyPatch(typeof(MergedInventoriesUtility), nameof(MergedInventoriesUtility.GetCastleMergedInventoryDatas),
            new Type[] { typeof(EntityManager), typeof(Entity), typeof(Entity), typeof(bool), typeof(bool) })]
        public static class GetMerged_Heart
        {
            static bool Prefix(EntityManager entityManager, Entity castleHeartEntity, Entity target,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories,
                ref NativeArray<InventoryBuffer> __result)
            {
                if (!TryBeginShare(castleHeartEntity, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    __result = ClanTreasuryShare.CombineMerged(
                        entityManager, castleHeartEntity, target,
                        includeCurrentInteractingInventory, includeCastleSharedInventories);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }

        [HarmonyPatch(typeof(MergedInventoriesUtility), nameof(MergedInventoriesUtility.GetCastleMergedInventoryDatas),
            new Type[] { typeof(EntityManager), typeof(MapZoneCollection), typeof(Entity), typeof(bool), typeof(bool) })]
        public static class GetMerged_MapZone
        {
            static bool Prefix(EntityManager entityManager, Entity target,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories,
                ref NativeArray<InventoryBuffer> __result)
            {
                if (ClanTreasuryShare.Suppress || !includeCastleSharedInventories || !Core.HasInitialized)
                    return true;

                var heart = ClanTreasuryShare.HeartFromTarget(target);
                if (!TryBeginShare(heart, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    __result = ClanTreasuryShare.CombineMerged(
                        entityManager, heart, target,
                        includeCurrentInteractingInventory, includeCastleSharedInventories);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }

        [HarmonyPatch]
        public static class Remove_Heart
        {
            static MethodBase TargetMethod() => FindRemove(
                typeof(EntityManager), typeof(Entity), typeof(Entity), typeof(PrefabGUID), typeof(int),
                typeof(int).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool));

            static bool Prefix(EntityManager entityManager, Entity castleHeartEntity, Entity target,
                PrefabGUID type, int amount, ref int remainder,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories, bool destroyItem)
            {
                if (!TryBeginShare(castleHeartEntity, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    MergedInventoriesUtility.RemoveItemGetRemainder(
                        entityManager, castleHeartEntity, target, type, amount, out remainder,
                        includeCurrentInteractingInventory, includeCastleSharedInventories, destroyItem);
                    if (remainder > 0)
                        ClanTreasuryShare.ConsumeRemainderFromOtherTreasuries(castleHeartEntity, type, ref remainder);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }

        [HarmonyPatch]
        public static class Remove_Heart_ItemEntity
        {
            static MethodBase TargetMethod() => FindRemove(
                typeof(EntityManager), typeof(Entity), typeof(Entity), typeof(PrefabGUID), typeof(int),
                typeof(int).MakeByRefType(), typeof(Entity).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool));

            static bool Prefix(EntityManager entityManager, Entity castleHeartEntity, Entity target,
                PrefabGUID type, int amount, ref int remainder, ref Entity itemEntity,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories, bool destroyItem)
            {
                if (!TryBeginShare(castleHeartEntity, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    MergedInventoriesUtility.RemoveItemGetRemainder(
                        entityManager, castleHeartEntity, target, type, amount, out remainder, out itemEntity,
                        includeCurrentInteractingInventory, includeCastleSharedInventories, destroyItem);
                    if (remainder > 0)
                        ClanTreasuryShare.ConsumeRemainderFromOtherTreasuries(castleHeartEntity, type, ref remainder);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }

        [HarmonyPatch]
        public static class Remove_MapZone
        {
            static MethodBase TargetMethod() => FindRemove(
                typeof(EntityManager), typeof(MapZoneCollection), typeof(Entity), typeof(PrefabGUID), typeof(int),
                typeof(int).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool));

            static bool Prefix(EntityManager entityManager, MapZoneCollection mapZoneCollection, Entity target,
                PrefabGUID type, int amount, ref int remainder,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories, bool destroyItem)
            {
                if (ClanTreasuryShare.Suppress || !includeCastleSharedInventories || !Core.HasInitialized)
                    return true;

                var heart = ClanTreasuryShare.HeartFromTarget(target);
                if (!TryBeginShare(heart, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    MergedInventoriesUtility.RemoveItemGetRemainder(
                        entityManager, mapZoneCollection, target, type, amount, out remainder,
                        includeCurrentInteractingInventory, includeCastleSharedInventories, destroyItem);
                    if (remainder > 0)
                        ClanTreasuryShare.ConsumeRemainderFromOtherTreasuries(heart, type, ref remainder);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }

        [HarmonyPatch]
        public static class Remove_MapZone_ItemEntity
        {
            static MethodBase TargetMethod() => FindRemove(
                typeof(EntityManager), typeof(MapZoneCollection), typeof(Entity), typeof(PrefabGUID), typeof(int),
                typeof(int).MakeByRefType(), typeof(Entity).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool));

            static bool Prefix(EntityManager entityManager, MapZoneCollection mapZoneCollection, Entity target,
                PrefabGUID type, int amount, ref int remainder, ref Entity itemEntity,
                bool includeCurrentInteractingInventory, bool includeCastleSharedInventories, bool destroyItem)
            {
                if (ClanTreasuryShare.Suppress || !includeCastleSharedInventories || !Core.HasInitialized)
                    return true;

                var heart = ClanTreasuryShare.HeartFromTarget(target);
                if (!TryBeginShare(heart, includeCastleSharedInventories))
                    return true;

                ClanTreasuryShare.Suppress = true;
                try
                {
                    MergedInventoriesUtility.RemoveItemGetRemainder(
                        entityManager, mapZoneCollection, target, type, amount, out remainder, out itemEntity,
                        includeCurrentInteractingInventory, includeCastleSharedInventories, destroyItem);
                    if (remainder > 0)
                        ClanTreasuryShare.ConsumeRemainderFromOtherTreasuries(heart, type, ref remainder);
                    return false;
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                    return true;
                }
                finally
                {
                    ClanTreasuryShare.Suppress = false;
                }
            }
        }
    }
}
