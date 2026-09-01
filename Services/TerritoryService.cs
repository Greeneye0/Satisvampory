using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Terrain;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using ProjectM.Network;

namespace Satisvampory.Services;

internal class TerritoryService {
        readonly Dictionary<WorldRegionType, List<Entity>> plotsByRegion = new();
        readonly Dictionary<int, Entity> heartByPlot = new();
        readonly HashSet<int> rebuilding = new();
        readonly Dictionary<Entity, int> entityPlot = new();
        readonly Dictionary<string, List<int>> clanPlotCache = new();
        readonly Dictionary<string, DateTime> clanPlotCacheAt = new();
        DateTime clanStandingAt;
        readonly Dictionary<int, bool> clanStanding = new();
        readonly List<Func<int, Entity, IEnumerator>> territoryUpdateCallbacks = [];
        readonly float timeBudget;
        double sliceStart;

        public const int MIN_TERRITORY_ID = 0;
        public const int MAX_TERRITORY_ID = 146;

        public TerritoryService() { ScanPlots(); ScanHearts(); var fps = SettingsManager.ServerHostSettings.ServerFps; timeBudget = (1f / fps) * 0.15f; }

        public void RegisterTerritoryUpdateCallback(Func<int, Entity, IEnumerator> callback)
            => territoryUpdateCallbacks.Add(callback);

        internal IReadOnlyList<Func<int, Entity, IEnumerator>> UpdateCallbacks => territoryUpdateCallbacks;

        internal bool IsTerritoryRebuilding(int territoryId) => rebuilding.Contains(territoryId);

        internal void StartTimer() => sliceStart = Time.realtimeSinceStartup;

        internal bool ShouldUpdateYield() => Time.realtimeSinceStartup - sliceStart > timeBudget;

        public Entity GetCastleHeart(int territoryId) =>
            heartByPlot.TryGetValue(territoryId, out var heart) && HeartStillMapsTo(territoryId, heart) ? heart : Entity.Null;

        internal void AddCastleHeart(Entity castleHeartEntity) => Remember(castleHeartEntity);

        internal void RemoveCastleHeart(Entity castleHeartEntity) { if (!Core.EntityManager.Exists(castleHeartEntity)) return; var territory = castleHeartEntity.Read<CastleHeart>().CastleTerritoryEntity; if (Core.EntityManager.Exists(territory)) Forget(territory.Read<CastleTerritory>().CastleTerritoryIndex); }

        public void FlushTerritoryCache() => entityPlot.Clear();

        public int GetTerritoryId(Entity entity) { if (entity != Entity.Null && Core.EntityManager.Exists(entity) && entity.Has<PlayerCharacter>()) return GetStandingTerritoryId(entity); return ResolvePlot(entity); }

        public int GetStandingTerritoryId(Entity entity) { if (entity == Entity.Null || !Core.EntityManager.Exists(entity) || !entity.Has<TilePosition>()) return -1; return PlotUnderTile(entity); }

        public void MarkTerritoryRebuilding(int territoryId) { Core.Log.LogInfo($"Plot {territoryId} entered rebuild; logistics deferred."); rebuilding.Add(territoryId); }

        void ScanPlots() { plotsByRegion.Clear(); var builder = new EntityQueryBuilder(Allocator.Temp).AddAll(ComponentType.ReadWrite(Il2CppType.Of<CastleTerritory>())); var query = Core.EntityManager.CreateEntityQuery(ref builder); builder.Dispose(); var rows = query.ToEntityArray(Allocator.Temp); try { for (var i = 0; i < rows.Length; i++) IndexPlot(rows[i]); } finally { rows.Dispose(); query.Dispose(); } }

        void ScanHearts() { heartByPlot.Clear(); var builder = new EntityQueryBuilder(Allocator.Temp).AddAll(ComponentType.ReadOnly(Il2CppType.Of<CastleHeart>())); var query = Core.EntityManager.CreateEntityQuery(ref builder); builder.Dispose(); var rows = query.ToEntityArray(Allocator.Temp); try { for (var i = 0; i < rows.Length; i++) Remember(rows[i], notify: false); } finally { rows.Dispose(); query.Dispose(); } }

        bool HeartStillMapsTo(int plot, Entity heart)
        { if (!Core.EntityManager.Exists(heart)) { Forget(plot); return false; } var territory = heart.Read<CastleHeart>().CastleTerritoryEntity; if (territory == Entity.Null || !Core.EntityManager.Exists(territory)) { Forget(plot); return false; } if (territory.Read<CastleTerritory>().CastleTerritoryIndex == plot) return true; Forget(plot); Remember(heart, notify: true); return false; }

        void IndexPlot(Entity plot) { var region = plot.Read<TerritoryWorldRegion>().Region; if (!plotsByRegion.TryGetValue(region, out var list)) plotsByRegion[region] = list = new List<Entity>(); list.Add(plot); }

        void Remember(Entity heart, bool notify = true) { if (!Core.EntityManager.Exists(heart)) return; var territory = heart.Read<CastleHeart>().CastleTerritoryEntity; if (!Core.EntityManager.Exists(territory)) return; heartByPlot[territory.Read<CastleTerritory>().CastleTerritoryIndex] = heart; if (notify) InvalidateClanPlotCache(); }

        void Forget(int plot) { heartByPlot.Remove(plot); rebuilding.Remove(plot); InvalidateClanPlotCache(); }

        int ResolvePlot(Entity entity)
        { if (entityPlot.TryGetValue(entity, out var cached)) return cached; if (!entity.Has<CastleHeartConnection>()) return PlotUnderTile(entity); var heart = entity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer(); if (!Core.EntityManager.Exists(heart) || heart == Entity.Null) return PlotUnderTile(entity); var territory = heart.Read<CastleHeart>().CastleTerritoryEntity; if (!territory.Has<CastleTerritory>()) return PlotUnderTile(entity); var plot = territory.Read<CastleTerritory>().CastleTerritoryIndex; entityPlot[entity] = plot; return plot; }

        int PlotUnderTile(Entity entity)
        { if (entity == Entity.Null || !entity.Has<TilePosition>()) return -1; var region = Core.RegionService.GetRegion(entity); if (!plotsByRegion.TryGetValue(region, out var plots)) return -1; var tile = entity.Read<TilePosition>(); for (var i = 0; i < plots.Count; i++) { var plot = plots[i]; if (CastleTerritoryExtensions.IsTileInTerritory(Core.EntityManager, tile.Tile, ref plot, out _) && plot.Has<CastleTerritory>()) return plot.Read<CastleTerritory>().CastleTerritoryIndex; } return -1; }

        static bool TryGetHeartOwner(Entity castleHeart, out Entity userEntity, out User user)
        { userEntity = Entity.Null; user = default; if (castleHeart == Entity.Null || !Core.EntityManager.Exists(castleHeart) || !castleHeart.Has<UserOwner>()) return false; userEntity = castleHeart.Read<UserOwner>().Owner.GetEntityOnServer(); if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>()) return false; user = userEntity.Read<User>(); return true; }

        public bool TryGetTerritoryOwnerPlatformId(int territoryId, out ulong platformId)
        { platformId = 0; var heart = GetCastleHeart(territoryId); if (!TryGetHeartOwner(heart, out _, out var user)) return false; platformId = user.PlatformId; return true; }

        public bool TryGetHeartLevel(int territoryId, out int level)
        { level = -1; var heart = GetCastleHeart(territoryId); if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>()) return false; level = heart.Read<CastleHeart>().Level; return true; }

        public string FormatPlotLabel(int territoryId) => territoryId < 0 ? "no plot" : TryGetHeartLevel(territoryId, out var level) && level >= 0 ? $"plot {territoryId} L{level}" : $"plot {territoryId}";

        public bool IsSameClanAsHeart(Entity character, Entity heart)
        { if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>()) return false; var userEntity = character.Read<PlayerCharacter>().UserEntity; if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>()) return false; return IsSameClanAsHeartOwner(userEntity.Read<User>(), heart); }

        public bool ClanMemberStandingOnHeart(Entity heart)
        { if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>()) return false; var territoryEntity = heart.Read<CastleHeart>().CastleTerritoryEntity; if (territoryEntity == Entity.Null || !Core.EntityManager.Exists(territoryEntity) || !territoryEntity.Has<CastleTerritory>()) return false; return ClanMemberStandingOnPlot(territoryEntity.Read<CastleTerritory>().CastleTerritoryIndex); }

        public bool ClanMemberStandingOnPlot(int plot) { EnsureClanStanding(); return plot >= 0 && clanStanding.ContainsKey(plot); }

        void EachUser(Action<Entity, User> visit)
        { var builder = new EntityQueryBuilder(Allocator.Temp).AddAll(ComponentType.ReadOnly(Il2CppType.Of<User>())); var query = Core.EntityManager.CreateEntityQuery(ref builder); builder.Dispose(); NativeArray<Entity> users = default; try { users = query.ToEntityArray(Allocator.Temp); for (var i = 0; i < users.Length; i++) { var userEntity = users[i]; if (userEntity != Entity.Null && Core.EntityManager.Exists(userEntity) && userEntity.Has<User>()) visit(userEntity, userEntity.Read<User>()); } } finally { if (users.IsCreated) users.Dispose(); query.Dispose(); } }

        void EnsureClanStanding()
        { var now = DateTime.UtcNow; if (clanStandingAt != default && (now - clanStandingAt).TotalSeconds < 0.25) return; clanStanding.Clear(); clanStandingAt = now; EachUser((_, user) => { if (!user.IsConnected) return; var character = user.LocalCharacter.GetEntityOnServer(); if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>()) return; var plot = GetStandingTerritoryId(character); if (plot < 0) return; var heart = GetCastleHeart(plot); if (heart != Entity.Null && IsSameClanAsHeart(character, heart)) clanStanding[plot] = true; }); }

        public bool IsSameClanAsHeartOwner(User user, Entity heart)
        { if (!TryGetHeartOwner(heart, out _, out var owner)) return false; var playerClan = user.ClanEntity.GetEntityOnServer(); var ownerClan = owner.ClanEntity.GetEntityOnServer(); return playerClan != Entity.Null && Core.EntityManager.Exists(playerClan) && ownerClan != Entity.Null && Core.EntityManager.Exists(ownerClan) && playerClan == ownerClan; }

        public bool TryGetClanKey(User user, out string key) { key = null; var clanEntity = user.ClanEntity.GetEntityOnServer(); if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity)) return false; key = clanEntity.Has<NetworkId>() ? "c" + clanEntity.Read<NetworkId>() : "e" + clanEntity.Index + ":" + clanEntity.Version; return true; }

        public bool AnyClanMemberHasLegacyClanShare(Entity clanEntity)
        { if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity)) return false; var found = false; EachUser((_, other) => { if (found) return; var otherClan = other.ClanEntity.GetEntityOnServer(); if (otherClan == Entity.Null || !Core.EntityManager.Exists(otherClan) || otherClan != clanEntity) return; if (Core.PlayerSettings.IsClanShareEnabled(other.PlatformId)) found = true; }); return found; }

        public bool IsClanShareOn(User standingOwner) { if (!TryGetClanKey(standingOwner, out var clanKey)) return false; if (Core.PlayerSettings.TryGetClanShareFlag(clanKey, out var flagged)) return flagged; if (!AnyClanMemberHasLegacyClanShare(standingOwner.ClanEntity.GetEntityOnServer())) return false; Core.PlayerSettings.SetClanShareForClan(clanKey, true); return true; }

        public void InvalidateClanPlotCache() { clanPlotCache.Clear(); clanPlotCacheAt.Clear(); }

        List<int> GetCachedClanPlots(string clanKey, Entity clanEntity)
        { if (clanPlotCache.TryGetValue(clanKey, out var cached) && clanPlotCacheAt.TryGetValue(clanKey, out var at) && (DateTime.UtcNow - at).TotalSeconds < 5) return cached; var list = new List<int>(); for (var id = MIN_TERRITORY_ID; id <= MAX_TERRITORY_ID; id++) { var heart = GetCastleHeart(id); if (heart == Entity.Null || !TryGetHeartOwner(heart, out _, out var otherOwner)) continue; var otherClan = otherOwner.ClanEntity.GetEntityOnServer(); if (otherClan != Entity.Null && Core.EntityManager.Exists(otherClan) && otherClan == clanEntity) list.Add(id); } clanPlotCache[clanKey] = list; clanPlotCacheAt[clanKey] = DateTime.UtcNow; return list; }

        public IReadOnlyList<int> GetLogisticsTerritoryIds(int standingTerritoryId)
        { var result = new List<int>(); if (standingTerritoryId < 0) return result; result.Add(standingTerritoryId); var standingHeart = GetCastleHeart(standingTerritoryId); if (!TryGetHeartOwner(standingHeart, out _, out var standingOwner) || !IsClanShareOn(standingOwner)) return result; var clanEntity = standingOwner.ClanEntity.GetEntityOnServer(); if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity) || !TryGetClanKey(standingOwner, out var clanKey)) return result; foreach (var id in GetCachedClanPlots(clanKey, clanEntity)) if (id != standingTerritoryId && !Core.PlayerSettings.IsTerritoryClanShareExcluded(id)) result.Add(id); return result; }

        public IReadOnlyList<int> GetClanLogisticsTerritoryIds(User user)
        { var result = new List<int>(); if (!IsClanShareOn(user) || !TryGetClanKey(user, out var clanKey)) return result; var clanEntity = user.ClanEntity.GetEntityOnServer(); if (clanEntity == Entity.Null || !Core.EntityManager.Exists(clanEntity)) return result; foreach (var id in GetCachedClanPlots(clanKey, clanEntity)) if (!Core.PlayerSettings.IsTerritoryClanShareExcluded(id)) result.Add(id); return result; }

        public IReadOnlyList<int> GetLogisticsTerritoryIdsForCharacter(Entity character)
        { var standing = GetStandingTerritoryId(character); User user = default; var haveUser = false; if (character != Entity.Null && Core.EntityManager.Exists(character) && character.Has<PlayerCharacter>()) { var userEntity = character.Read<PlayerCharacter>().UserEntity; if (userEntity != Entity.Null && Core.EntityManager.Exists(userEntity) && userEntity.Has<User>()) { user = userEntity.Read<User>(); haveUser = true; } } var standingHeart = standing >= 0 ? GetCastleHeart(standing) : Entity.Null; var sameClan = haveUser && standingHeart != Entity.Null && IsSameClanAsHeartOwner(user, standingHeart); if (haveUser && IsClanShareOn(user)) return sameClan && standing >= 0 && Core.PlayerSettings.IsTerritoryClanShareExcluded(standing) && Core.ServerGameManager.IsAllies(standingHeart, character) ? new List<int> { standing } : GetClanLogisticsTerritoryIds(user); if (standing >= 0 && standingHeart != Entity.Null && haveUser && Core.ServerGameManager.IsAllies(standingHeart, character)) return new List<int> { standing }; return new List<int>(); }

        public IReadOnlyList<int> GetServantStashPlotIds(int homePlot)
        { if (homePlot < 0) return new List<int>(); var heart = GetCastleHeart(homePlot); if (heart == Entity.Null) return new List<int> { homePlot }; if (IsHeartRaided(heart)) return new List<int>(); if (!TryGetHeartOwner(heart, out _, out var owner) || !IsClanShareOn(owner) || Core.PlayerSettings.IsTerritoryClanShareExcluded(homePlot)) return new List<int> { homePlot }; var ids = GetClanLogisticsTerritoryIds(owner); return ids == null || ids.Count == 0 ? new List<int> { homePlot } : ids; }

        public bool TryFindOwnedTerritory(ulong platformId, out int territoryId, out User owner)
        { territoryId = -1; owner = default; for (var id = MIN_TERRITORY_ID; id <= MAX_TERRITORY_ID; id++) { var heart = GetCastleHeart(id); if (!TryGetHeartOwner(heart, out _, out var user) || user.PlatformId != platformId) continue; territoryId = id; owner = user; return true; } return false; }

        public static bool IsHeartRaided(Entity heart) =>
            heart != Entity.Null && Core.EntityManager.Exists(heart) && heart.Has<CastleHeart>()
            && heart.Read<CastleHeart>().ActiveEvent >= CastleHeartEvent.Attacked;
}
