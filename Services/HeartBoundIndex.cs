using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Plot-keyed live entity lists (stations, braziers, spawners). Replaces the copied
    /// per-service "query world, bucket by CastleHeartConnection" Kindred registries.
    /// </summary>
    internal sealed class HeartBoundIndex
    {
        readonly Dictionary<Entity, List<Entity>> buckets = new();
        readonly bool skipDisabled;

        public HeartBoundIndex(bool skipDisabledWhenListing = true)
        {
            this.skipDisabled = skipDisabledWhenListing;
        }

        public static HeartBoundIndex Scan(bool includeDisabled, params ComponentType[] required)
        {
            var index = new HeartBoundIndex();
            var builder = new EntityQueryBuilder(Allocator.Temp);
            if (includeDisabled)
                builder.WithOptions(EntityQueryOptions.IncludeDisabled);
            for (var i = 0; i < required.Length; i++)
                builder.AddAll(required[i]);
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            NativeArray<Entity> found = default;
            try
            {
                found = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < found.Length; i++)
                    index.Track(found[i]);
            }
            finally
            {
                if (found.IsCreated)
                    found.Dispose();
                query.Dispose();
            }
            return index;
        }

        static Entity HeartOf(Entity station)
        {
            if (station == Entity.Null || !Core.EntityManager.Exists(station) || !station.Has<CastleHeartConnection>())
                return Entity.Null;
            return station.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
        }

        public void Track(Entity station)
        {
            if (station == Entity.Null || !Core.EntityManager.Exists(station))
                return;
            var heart = HeartOf(station);
            if (heart == Entity.Null)
                return;
            if (!buckets.TryGetValue(heart, out var list))
            {
                list = new List<Entity>();
                buckets[heart] = list;
            }
            if (!list.Contains(station))
                list.Add(station);
        }

        public void Untrack(Entity station)
        {
            var heart = HeartOf(station);
            if (heart == Entity.Null || !buckets.TryGetValue(heart, out var list))
                return;
            list.Remove(station);
        }

        public void Rebuild(bool includeDisabled, params ComponentType[] required)
        {
            buckets.Clear();
            var rebuilt = Scan(includeDisabled, required);
            foreach (var kv in rebuilt.buckets)
                buckets[kv.Key] = kv.Value;
        }

        public IEnumerable<Entity> OnTerritory(int territoryId)
        {
            var heart = Core.TerritoryService.GetCastleHeart(territoryId);
            if (heart == Entity.Null)
                yield break;
            if (buckets.TryGetValue(heart, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (e == Entity.Null || !Core.EntityManager.Exists(e))
                    {
                        list.RemoveAt(i);
                        continue;
                    }
                    if (skipDisabled && e.Has<Disabled>())
                        continue;
                    yield return e;
                }
                yield break;
            }

            foreach (var bucket in buckets.Values)
            {
                for (var i = bucket.Count - 1; i >= 0; i--)
                {
                    var e = bucket[i];
                    if (e == Entity.Null || !Core.EntityManager.Exists(e))
                    {
                        bucket.RemoveAt(i);
                        continue;
                    }
                    if (skipDisabled && e.Has<Disabled>())
                        continue;
                    if (Core.TerritoryService.GetTerritoryId(e) != territoryId)
                        continue;
                    yield return e;
                }
            }
        }

        public IEnumerable<int> OccupiedTerritoryIds()
        {
            foreach (var heart in new List<Entity>(buckets.Keys))
            {
                if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>())
                    continue;
                var territory = heart.Read<CastleHeart>().CastleTerritoryEntity;
                if (territory == Entity.Null || !Core.EntityManager.Exists(territory) || !territory.Has<CastleTerritory>())
                    continue;
                yield return territory.Read<CastleTerritory>().CastleTerritoryIndex;
            }
        }
    }
}
