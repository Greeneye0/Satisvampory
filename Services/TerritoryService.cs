using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using ProjectM.CastleBuilding;
using ProjectM.Terrain;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    internal class TerritoryService
    {
        readonly Dictionary<WorldRegionType, List<Entity>> territories = [];
        readonly Dictionary<Entity, int> territoryCache = [];

        readonly List<Func<int, Entity, IEnumerator>> territoryUpdateCallbacks = [];

        public const int MIN_TERRITORY_ID = 0;
        public const int MAX_TERRITORY_ID = 146;

        readonly Dictionary<int, Entity> territoryToCastleHeart = [];
        readonly HashSet<int> territoriesRebuilding = [];

        readonly float timeBudget;
        readonly Dictionary<string, List<int>> clanPlotCache = new();

        public TerritoryService()
        {
            // Load Territories
            var entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp);
            entityQueryBuilder.AddAll(new(Il2CppType.Of<CastleTerritory>(), ComponentType.AccessMode.ReadWrite));

            var query = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
            entityQueryBuilder.Dispose();

            foreach (var territoryEntity in query.ToEntityArray(Allocator.Temp))
            {
                var region = territoryEntity.Read<TerritoryWorldRegion>().Region;

                if (!territories.TryGetValue(region, out var territoriesInRegion))
                {
                    territoriesInRegion = [];
                    territories[region] = territoriesInRegion;
                }
                territoriesInRegion.Add(territoryEntity);
            }

            entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<CastleHeart>(), ComponentType.AccessMode.ReadOnly));

            var castleHeartQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
            entityQueryBuilder.Dispose();

            var castleHeartEntities = castleHeartQuery.ToEntityArray(Allocator.Temp);
            try
            {
                territoryToCastleHeart.Clear();
                foreach (var castleHeartEntity in castleHeartEntities)
                {
                    var castleHeart = castleHeartEntity.Read<CastleHeart>();
                    var territoryEntity = castleHeart.CastleTerritoryEntity;
                    var territory = territoryEntity.Read<CastleTerritory>();
                    territoryToCastleHeart[territory.CastleTerritoryIndex] = castleHeartEntity;
                }
            }
            finally
            {
                castleHeartEntities.Dispose();
            }
            castleHeartQuery.Dispose();

            int serverFps = SettingsManager.ServerHostSettings.ServerFps;
            timeBudget = (1f / serverFps) * 0.15f;

            // The work loop now lives in WorkQueueService and is driven by events, not a poll.
        }

        public void RegisterTerritoryUpdateCallback(Func<int, Entity, IEnumerator> callback)
        {
            territoryUpdateCallbacks.Add(callback);
        }

        // Per-territory callbacks the WorkQueueService worker runs when draining a dirty territory.
        internal IReadOnlyList<Func<int, Entity, IEnumerator>> UpdateCallbacks => territoryUpdateCallbacks;

        internal bool IsTerritoryRebuilding(int territoryId) => territoriesRebuilding.Contains(territoryId);

        float startTime = 0;
        internal void StartTimer()
        {
            startTime = Time.realtimeSinceStartup;
        }

        internal bool ShouldUpdateYield()
        {
            return Time.realtimeSinceStartup - startTime > timeBudget;
        }

        public Entity GetCastleHeart(int territoryId)
        {
            if (!territoryToCastleHeart.TryGetValue(territoryId, out var castleHeartEntity))
                return Entity.Null;

            if (!Core.EntityManager.Exists(castleHeartEntity))
            {
                territoryToCastleHeart.Remove(territoryId);
                territoriesRebuilding.Remove(territoryId);
                return Entity.Null;
            }

            var castleHeart = castleHeartEntity.Read<CastleHeart>();
            var territoryEntity = castleHeart.CastleTerritoryEntity;
            if (castleHeart.CastleTerritoryEntity == Entity.Null || !Core.EntityManager.Exists(territoryEntity))
            {
                territoryToCastleHeart.Remove(territoryId);
                territoriesRebuilding.Remove(territoryId);
                return Entity.Null;
            }

            var territory = territoryEntity.Read<CastleTerritory>();
            if (territory.CastleTerritoryIndex != territoryId)
            {
                territoryToCastleHeart.Remove(territoryId);
                territoriesRebuilding.Remove(territoryId);
                AddCastleHeart(castleHeartEntity);
                return Entity.Null;
            }

            return castleHeartEntity;
        }

        internal void AddCastleHeart(Entity castleHeartEntity)
        {
            if (!Core.EntityManager.Exists(castleHeartEntity)) return;
            var castleHeart = castleHeartEntity.Read<CastleHeart>();
            var territoryEntity = castleHeart.CastleTerritoryEntity;
            if (!Core.EntityManager.Exists(territoryEntity)) return;
            var territory = territoryEntity.Read<CastleTerritory>();
            territoryToCastleHeart[territory.CastleTerritoryIndex] = castleHeartEntity;
            InvalidateClanPlotCache();
        }

        internal void RemoveCastleHeart(Entity castleHeartEntity)
        {
            if (!Core.EntityManager.Exists(castleHeartEntity)) return;
            var castleHeart = castleHeartEntity.Read<CastleHeart>();
            var territoryEntity = castleHeart.CastleTerritoryEntity;
            if (!Core.EntityManager.Exists(territoryEntity)) return;
            var territory = territoryEntity.Read<CastleTerritory>();
            territoryToCastleHeart.Remove(territory.CastleTerritoryIndex);
            territoriesRebuilding.Remove(territory.CastleTerritoryIndex);
            InvalidateClanPlotCache();
        }

        public void FlushTerritoryCache()
        {
            territoryCache.Clear();
        }

        public int GetTerritoryId(Entity entity)
        {
            if (territoryCache.TryGetValue(entity, out var territoryId))
            {
                return territoryId;
            }

            if (entity.Has<CastleHeartConnection>())
            {
                var heart = entity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();

                if (Core.EntityManager.Exists(heart) && heart != Entity.Null)
                {
                    var castleHeart = heart.Read<CastleHeart>();
                    var castleTerritory = castleHeart.CastleTerritoryEntity;

                    // Cache the territory id of buildings as they don't change
                    if (castleTerritory.Has<CastleTerritory>())
                    {
                        territoryId = castleTerritory.Read<CastleTerritory>().CastleTerritoryIndex;
                        territoryCache[entity] = territoryId;
                        return territoryId;
                    }
                }
            }

            if (entity.Has<TilePosition>())
            {
                var region = Core.RegionService.GetRegion(entity);
                var tilePos = entity.Read<TilePosition>();
                if (territories.TryGetValue(region, out var territoriesInRegion))
                {
                    for (int i = 0; i < territoriesInRegion.Count; i++)
                    {
                        var territory = territoriesInRegion[i];
                        if (CastleTerritoryExtensions.IsTileInTerritory(Core.EntityManager, tilePos.Tile, ref territory, out var _))
                        {
                            if (territory.Has<CastleTerritory>()) return territory.Read<CastleTerritory>().CastleTerritoryIndex;
                        }
                    }
                }
            }
            return -1;
        }


        /// <summary>
        /// Territory the entity is STANDING on (TilePosition). Does not use
        /// CastleHeartConnection (that is the home castle, which made empty-server
        /// lends treat offline/home plots as occupied).
        /// </summary>
        public int GetStandingTerritoryId(Entity entity)
        {
            if (entity == Entity.Null || !Core.EntityManager.Exists(entity) || !entity.Has<TilePosition>())
                return -1;

            var region = Core.RegionService.GetRegion(entity);
            var tilePos = entity.Read<TilePosition>();
            if (!territories.TryGetValue(region, out var territoriesInRegion))
                return -1;

            for (int i = 0; i < territoriesInRegion.Count; i++)
            {
                var territory = territoriesInRegion[i];
                if (CastleTerritoryExtensions.IsTileInTerritory(Core.EntityManager, tilePos.Tile, ref territory, out var _))
                {
                    if (territory.Has<CastleTerritory>())
                        return territory.Read<CastleTerritory>().CastleTerritoryIndex;
                }
            }
            return -1;
        }

                public void MarkTerritoryRebuilding(int territoryId)
        {
            Core.Log.LogInfo($"Marking territory {territoryId} as rebuilding.");
            territoriesRebuilding.Add(territoryId);
        }

        static bool TryGetHeartOwner(Entity castleHeart, out Entity userEntity, out User user)
        {
            userEntity = Entity.Null;
            user = default;
            if (castleHeart == Entity.Null || !Core.EntityManager.Exists(castleHeart) || !castleHeart.Has<UserOwner>())
                return false;

            var userOwner = castleHeart.Read<UserOwner>();
            userEntity = userOwner.Owner.GetEntityOnServer();
            if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                return false;

            user = userEntity.Read<User>();
            return true;
        }

        public bool TryGetTerritoryOwnerPlatformId(int territoryId, out ulong platformId)
        {
            platformId = 0;
            var heart = GetCastleHeart(territoryId);
            if (!TryGetHeartOwner(heart, out _, out var user))
                return false;
            platformId = user.PlatformId;
            return true;
        }

        public bool TryGetClanKey(User user, out string key)
        {
            key = null;
            var clanEntity = user.ClanEntity.GetEntityOnServer();
            if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity))
                return false;
            if (clanEntity.Has<NetworkId>())
            {
                key = "c" + clanEntity.Read<NetworkId>().ToString();
                return true;
            }
            key = "e" + clanEntity.Index + ":" + clanEntity.Version;
            return true;
        }

        public bool AnyClanMemberHasLegacyClanShare(Entity clanEntity)
        {
            if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity))
                return false;

            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();

            NativeArray<Entity> users = default;
            try
            {
                users = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < users.Length; i++)
                {
                    var userEntity = users[i];
                    if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                        continue;
                    var other = userEntity.Read<User>();
                    var otherClan = other.ClanEntity.GetEntityOnServer();
                    if (otherClan == Entity.Null || !Core.EntityManager.Exists(otherClan))
                        continue;
                    if (otherClan != clanEntity)
                        continue;
                    if (Core.PlayerSettings.IsClanShareEnabled(other.PlatformId))
                        return true;
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();
                query.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Clan-wide ClanShare. If the clan flag is not persisted yet, any current
        /// clan member with the old per-owner ClanShare ON migrates the clan to ON.
        /// </summary>
        public bool IsClanShareOn(User standingOwner)
        {
            if (!TryGetClanKey(standingOwner, out var clanKey))
                return false;
            if (Core.PlayerSettings.TryGetClanShareFlag(clanKey, out var flagged))
                return flagged;
            var clanEntity = standingOwner.ClanEntity.GetEntityOnServer();
            if (AnyClanMemberHasLegacyClanShare(clanEntity))
            {
                Core.PlayerSettings.SetClanShareForClan(clanKey, true);
                return true;
            }
            return false;
        }

        public void InvalidateClanPlotCache() => clanPlotCache.Clear();

        List<int> GetCachedClanPlots(string clanKey, Entity clanEntity)
        {
            if (clanPlotCache.TryGetValue(clanKey, out var cached))
                return cached;
            var list = new List<int>();
            for (var id = MIN_TERRITORY_ID; id <= MAX_TERRITORY_ID; id++)
            {
                var heart = GetCastleHeart(id);
                if (heart == Entity.Null)
                    continue;
                if (!TryGetHeartOwner(heart, out _, out var otherOwner))
                    continue;
                var otherClan = otherOwner.ClanEntity.GetEntityOnServer();
                if (otherClan == Entity.Null || !Core.EntityManager.Exists(otherClan))
                    continue;
                if (otherClan != clanEntity)
                    continue;
                list.Add(id);
            }
            clanPlotCache[clanKey] = list;
            return list;
        }

        public IReadOnlyList<int> GetLogisticsTerritoryIds(int standingTerritoryId)
        {
            var result = new List<int>();
            if (standingTerritoryId < 0)
                return result;

            result.Add(standingTerritoryId);

            var standingHeart = GetCastleHeart(standingTerritoryId);
            if (!TryGetHeartOwner(standingHeart, out _, out var standingOwner))
                return result;

            if (!IsClanShareOn(standingOwner))
                return result;

            var clanEntity = standingOwner.ClanEntity.GetEntityOnServer();
            if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity))
                return result;
            if (!TryGetClanKey(standingOwner, out var clanKey))
                return result;

            foreach (var id in GetCachedClanPlots(clanKey, clanEntity))
            {
                if (id == standingTerritoryId)
                    continue;
                if (Core.PlayerSettings.IsTerritoryClanShareExcluded(id))
                    continue;
                result.Add(id);
            }

            return result;
        }

        /// <summary>
        /// Clan plots except .l cse excluded, from the player's clan (not standing plot).
        /// Empty if ClanShare is off or the player has no clan.
        /// </summary>
        public IReadOnlyList<int> GetClanLogisticsTerritoryIds(User user)
        {
            var result = new List<int>();
            if (!IsClanShareOn(user))
                return result;
            var clanEntity = user.ClanEntity.GetEntityOnServer();
            if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity))
                return result;

            for (var id = MIN_TERRITORY_ID; id <= MAX_TERRITORY_ID; id++)
            {
                if (Core.PlayerSettings.IsTerritoryClanShareExcluded(id))
                    continue;
                var heart = GetCastleHeart(id);
                if (heart == Entity.Null)
                    continue;
                if (!TryGetHeartOwner(heart, out _, out var otherOwner))
                    continue;
                var otherClan = otherOwner.ClanEntity.GetEntityOnServer();
                if (otherClan == Entity.Null || !Core.EntityManager.Exists(otherClan))
                    continue;
                if (otherClan != clanEntity)
                    continue;
                result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// CS OFF: standing plot only (empty if not on a plot).
        /// CS ON: player's clan plots except cse excluded. Standing on an excluded
        /// allied plot stays local-only. Do not use the plot they are standing on
        /// when it is not a clan plot.
        /// </summary>
        public IReadOnlyList<int> GetLogisticsTerritoryIdsForCharacter(Entity character)
        {
            var standing = GetStandingTerritoryId(character);
            User user = default;
            var haveUser = false;
            if (character != Entity.Null && Core.EntityManager.Exists(character) && character.Has<PlayerCharacter>())
            {
                var userEntity = character.Read<PlayerCharacter>().UserEntity;
                if (userEntity != Entity.Null && Core.EntityManager.Exists(userEntity) && userEntity.Has<User>())
                {
                    user = userEntity.Read<User>();
                    haveUser = true;
                }
            }

            if (!haveUser || !IsClanShareOn(user))
            {
                if (standing < 0)
                    return new List<int>();
                return GetLogisticsTerritoryIds(standing);
            }

            if (standing >= 0 && Core.PlayerSettings.IsTerritoryClanShareExcluded(standing))
            {
                var heart = GetCastleHeart(standing);
                if (heart != Entity.Null && Core.ServerGameManager.IsAllies(heart, character))
                    return new List<int> { standing };
            }

            return GetClanLogisticsTerritoryIds(user);
        }

        public bool TryFindOwnedTerritory(ulong platformId, out int territoryId, out User owner)
        {
            territoryId = -1;
            owner = default;
            for (var id = MIN_TERRITORY_ID; id <= MAX_TERRITORY_ID; id++)
            {
                var heart = GetCastleHeart(id);
                if (!TryGetHeartOwner(heart, out _, out var user))
                    continue;
                if (user.PlatformId != platformId)
                    continue;
                territoryId = id;
                owner = user;
                return true;
            }
            return false;
        }

        public static bool IsHeartRaided(Entity heart)
        {
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>())
                return false;
            return heart.Read<CastleHeart>().ActiveEvent >= CastleHeartEvent.Attacked;
        }
    }
}
