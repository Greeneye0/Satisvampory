using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Terrain;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal sealed class ClanPlots
    {
        readonly Dictionary<string, List<int>> clanPlotCache = new();
        readonly Dictionary<string, DateTime> clanPlotCacheAt = new();

        internal static bool TryGetHeartOwner(Entity castleHeart, out Entity userEntity, out User user)
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
            var heart = Core.TerritoryService.GetCastleHeart(territoryId);
            if (!TryGetHeartOwner(heart, out _, out var user))
                return false;
            platformId = user.PlatformId;
            return true;
        }

        public bool TryGetHeartLevel(int territoryId, out int level)
        {
            level = -1;
            var heart = Core.TerritoryService.GetCastleHeart(territoryId);
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>())
                return false;
            level = heart.Read<CastleHeart>().Level;
            return true;
        }

        public string FormatPlotLabel(int territoryId)
        {
            if (territoryId < 0)
                return "no plot";
            if (TryGetHeartLevel(territoryId, out var level) && level >= 0)
                return $"plot {territoryId} L{level}";
            return $"plot {territoryId}";
        }

        public bool IsSameClanAsHeart(Entity character, Entity heart)
        {
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>())
                return false;
            var userEntity = character.Read<PlayerCharacter>().UserEntity;
            if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                return false;
            return IsSameClanAsHeartOwner(userEntity.Read<User>(), heart);
        }

        DateTime clanStandingAt;
        readonly Dictionary<int, bool> clanStanding = new();

        public bool ClanMemberStandingOnHeart(Entity heart)
        {
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>())
                return false;
            var territoryEntity = heart.Read<CastleHeart>().CastleTerritoryEntity;
            if (territoryEntity == Entity.Null || !Core.EntityManager.Exists(territoryEntity) || !territoryEntity.Has<CastleTerritory>())
                return false;
            return ClanMemberStandingOnPlot(territoryEntity.Read<CastleTerritory>().CastleTerritoryIndex);
        }

        public bool ClanMemberStandingOnPlot(int plot)
        {
            EnsureClanStanding();
            return plot >= 0 && clanStanding.ContainsKey(plot);
        }

        void EnsureClanStanding()
        {
            var now = DateTime.UtcNow;
            if (clanStandingAt != default && (now - clanStandingAt).TotalSeconds < 0.25)
                return;
            clanStanding.Clear();
            clanStandingAt = now;
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
                    var user = userEntity.Read<User>();
                    if (!user.IsConnected)
                        continue;
                    var character = user.LocalCharacter.GetEntityOnServer();
                    if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>())
                        continue;
                    var plot = Core.TerritoryService.GetStandingTerritoryId(character);
                    if (plot < 0)
                        continue;
                    var heart = Core.TerritoryService.GetCastleHeart(plot);
                    if (heart == Entity.Null)
                        continue;
                    if (!IsSameClanAsHeart(character, heart))
                        continue;
                    clanStanding[plot] = true;
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();
                query.Dispose();
            }
        }

        public bool IsSameClanAsHeartOwner(User user, Entity heart)
        {
            if (!TryGetHeartOwner(heart, out _, out var owner))
                return false;
            var playerClan = user.ClanEntity.GetEntityOnServer();
            var ownerClan = owner.ClanEntity.GetEntityOnServer();
            if (playerClan == Entity.Null || !Core.EntityManager.Exists(playerClan))
                return false;
            if (ownerClan == Entity.Null || !Core.EntityManager.Exists(ownerClan))
                return false;
            return playerClan == ownerClan;
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

        public void InvalidateClanPlotCache()
        {
            clanPlotCache.Clear();
            clanPlotCacheAt.Clear();
        }

        List<int> GetCachedClanPlots(string clanKey, Entity clanEntity)
        {
            if (clanPlotCache.TryGetValue(clanKey, out var cached)
                && clanPlotCacheAt.TryGetValue(clanKey, out var at)
                && (DateTime.UtcNow - at).TotalSeconds < 5)
                return cached;
            var list = new List<int>();
            for (var id = TerritoryService.MIN_TERRITORY_ID; id <= TerritoryService.MAX_TERRITORY_ID; id++)
            {
                var heart = Core.TerritoryService.GetCastleHeart(id);
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
            clanPlotCacheAt[clanKey] = DateTime.UtcNow;
            return list;
        }

        public IReadOnlyList<int> GetLogisticsTerritoryIds(int standingTerritoryId)
        {
            var result = new List<int>();
            if (standingTerritoryId < 0)
                return result;

            result.Add(standingTerritoryId);

            var standingHeart = Core.TerritoryService.GetCastleHeart(standingTerritoryId);
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

        public IReadOnlyList<int> GetClanLogisticsTerritoryIds(User user)
        {
            var result = new List<int>();
            if (!IsClanShareOn(user))
                return result;
            if (!TryGetClanKey(user, out var clanKey))
                return result;
            var clanEntity = user.ClanEntity.GetEntityOnServer();
            if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity))
                return result;
            foreach (var id in GetCachedClanPlots(clanKey, clanEntity))
            {
                if (Core.PlayerSettings.IsTerritoryClanShareExcluded(id))
                    continue;
                result.Add(id);
            }
            return result;
        }

        public IReadOnlyList<int> GetLogisticsTerritoryIdsForCharacter(Entity character)
        {
            var standing = Core.TerritoryService.GetStandingTerritoryId(character);
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

            Entity standingHeart = Entity.Null;
            if (standing >= 0)
                standingHeart = Core.TerritoryService.GetCastleHeart(standing);
            var sameClan = haveUser && standingHeart != Entity.Null && IsSameClanAsHeartOwner(user, standingHeart);

            if (haveUser && IsClanShareOn(user))
            {
                if (sameClan && standing >= 0 && Core.PlayerSettings.IsTerritoryClanShareExcluded(standing)
                    && Core.ServerGameManager.IsAllies(standingHeart, character))
                    return new List<int> { standing };
                return GetClanLogisticsTerritoryIds(user);
            }

            if (standing >= 0 && standingHeart != Entity.Null && haveUser
                && Core.ServerGameManager.IsAllies(standingHeart, character))
                return new List<int> { standing };

            return new List<int>();
        }

        public IReadOnlyList<int> GetServantStashPlotIds(int homePlot)
        {
            if (homePlot < 0)
                return new List<int>();
            var heart = Core.TerritoryService.GetCastleHeart(homePlot);
            if (heart == Entity.Null)
                return new List<int> { homePlot };
            if (IsHeartRaided(heart))
                return new List<int>();
            if (!TryGetHeartOwner(heart, out _, out var owner))
                return new List<int> { homePlot };
            if (!IsClanShareOn(owner))
                return new List<int> { homePlot };
            if (Core.PlayerSettings.IsTerritoryClanShareExcluded(homePlot))
                return new List<int> { homePlot };
            var ids = GetClanLogisticsTerritoryIds(owner);
            if (ids == null || ids.Count == 0)
                return new List<int> { homePlot };
            return ids;
        }

        public bool TryFindOwnedTerritory(ulong platformId, out int territoryId, out User owner)
        {
            territoryId = -1;
            owner = default;
            for (var id = TerritoryService.MIN_TERRITORY_ID; id <= TerritoryService.MAX_TERRITORY_ID; id++)
            {
                var heart = Core.TerritoryService.GetCastleHeart(id);
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
