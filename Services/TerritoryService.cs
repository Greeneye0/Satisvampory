using System;
using Il2CppInterop.Runtime;
using ProjectM.Terrain;
using System.Collections;
using UnityEngine;

namespace Satisvampory.Services
{
    internal class TerritoryService
    {
        readonly CastleHeartMap map;
        readonly List<Func<int, Entity, IEnumerator>> territoryUpdateCallbacks = [];
        readonly ClanPlots clan = new();
        readonly float timeBudget;
        double sliceStart;

        public const int MIN_TERRITORY_ID = 0;
        public const int MAX_TERRITORY_ID = 146;

        public TerritoryService()
        {
            map = new CastleHeartMap(InvalidateClanPlotCache);
            var fps = SettingsManager.ServerHostSettings.ServerFps;
            timeBudget = (1f / fps) * 0.15f;
        }

        public void RegisterTerritoryUpdateCallback(Func<int, Entity, IEnumerator> callback)
            => territoryUpdateCallbacks.Add(callback);

        internal IReadOnlyList<Func<int, Entity, IEnumerator>> UpdateCallbacks => territoryUpdateCallbacks;

        internal bool IsTerritoryRebuilding(int territoryId) => map.Rebuilding(territoryId);

        internal void StartTimer() => sliceStart = Time.realtimeSinceStartup;

        internal bool ShouldUpdateYield() => Time.realtimeSinceStartup - sliceStart > timeBudget;

        public Entity GetCastleHeart(int territoryId) => map.HeartAt(territoryId);

        internal void AddCastleHeart(Entity castleHeartEntity) => map.Remember(castleHeartEntity);

        internal void RemoveCastleHeart(Entity castleHeartEntity) => map.ForgetHeart(castleHeartEntity);

        public void FlushTerritoryCache() => map.ForgetEntityCache();

        public int GetTerritoryId(Entity entity)
        {
            if (entity != Entity.Null && Core.EntityManager.Exists(entity) && entity.Has<PlayerCharacter>())
                return GetStandingTerritoryId(entity);
            return map.ResolvePlot(entity);
        }

        public int GetStandingTerritoryId(Entity entity)
        {
            if (entity == Entity.Null || !Core.EntityManager.Exists(entity) || !entity.Has<TilePosition>())
                return -1;
            return map.PlotUnderTile(entity);
        }

        public void MarkTerritoryRebuilding(int territoryId)
        {
            Core.Log.LogInfo($"Marking territory {territoryId} as rebuilding.");
            map.MarkRebuilding(territoryId);
        }

        static bool TryGetHeartOwner(Entity castleHeart, out Entity userEntity, out User user)
            => ClanPlots.TryGetHeartOwner(castleHeart, out userEntity, out user);

        public bool TryGetTerritoryOwnerPlatformId(int territoryId, out ulong platformId)
            => clan.TryGetTerritoryOwnerPlatformId(territoryId, out platformId);

        public bool TryGetHeartLevel(int territoryId, out int level)
            => clan.TryGetHeartLevel(territoryId, out level);

        public string FormatPlotLabel(int territoryId) => clan.FormatPlotLabel(territoryId);

        public bool IsSameClanAsHeart(Entity character, Entity heart) => clan.IsSameClanAsHeart(character, heart);

        public bool ClanMemberStandingOnHeart(Entity heart) => clan.ClanMemberStandingOnHeart(heart);

        public bool ClanMemberStandingOnPlot(int plot) => clan.ClanMemberStandingOnPlot(plot);

        public bool IsSameClanAsHeartOwner(User user, Entity heart) => clan.IsSameClanAsHeartOwner(user, heart);

        public bool TryGetClanKey(User user, out string key) => clan.TryGetClanKey(user, out key);

        public bool AnyClanMemberHasLegacyClanShare(Entity clanEntity) => clan.AnyClanMemberHasLegacyClanShare(clanEntity);

        public bool IsClanShareOn(User standingOwner) => clan.IsClanShareOn(standingOwner);

        public void InvalidateClanPlotCache() => clan.InvalidateClanPlotCache();

        public IReadOnlyList<int> GetLogisticsTerritoryIds(int standingTerritoryId)
            => clan.GetLogisticsTerritoryIds(standingTerritoryId);

        public IReadOnlyList<int> GetClanLogisticsTerritoryIds(User user)
            => clan.GetClanLogisticsTerritoryIds(user);

        public IReadOnlyList<int> GetLogisticsTerritoryIdsForCharacter(Entity character)
            => clan.GetLogisticsTerritoryIdsForCharacter(character);

        public IReadOnlyList<int> GetServantStashPlotIds(int homePlot)
            => clan.GetServantStashPlotIds(homePlot);

        public bool TryFindOwnedTerritory(ulong platformId, out int territoryId, out User owner)
            => clan.TryFindOwnedTerritory(platformId, out territoryId, out owner);

        public static bool IsHeartRaided(Entity heart) => ClanPlots.IsHeartRaided(heart);
    }
}
