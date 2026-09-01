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
    internal sealed class CastleHeartMap
    {
        readonly Dictionary<WorldRegionType, List<Entity>> plotsByRegion = new();
        readonly Dictionary<int, Entity> heartByPlot = new();
        readonly HashSet<int> rebuilding = new();
        readonly Dictionary<Entity, int> entityPlot = new();
        readonly Action onMutate;

        public CastleHeartMap(Action onMutate)
        {
            this.onMutate = onMutate;
            ScanPlots();
            ScanHearts();
        }

        void ScanPlots()
        {
            plotsByRegion.Clear();
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<CastleTerritory>(), ComponentType.AccessMode.ReadWrite));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            var rows = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    var plot = rows[i];
                    var region = plot.Read<TerritoryWorldRegion>().Region;
                    if (!plotsByRegion.TryGetValue(region, out var list))
                    {
                        list = new List<Entity>();
                        plotsByRegion[region] = list;
                    }
                    list.Add(plot);
                }
            }
            finally
            {
                rows.Dispose();
                query.Dispose();
            }
        }

        void ScanHearts()
        {
            heartByPlot.Clear();
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<CastleHeart>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            var rows = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (var i = 0; i < rows.Length; i++)
                    Remember(rows[i], notify: false);
            }
            finally
            {
                rows.Dispose();
                query.Dispose();
            }
        }

        public Entity HeartAt(int plot)
        {
            if (!heartByPlot.TryGetValue(plot, out var heart))
                return Entity.Null;
            if (!HeartStillMapsTo(plot, heart))
                return Entity.Null;
            return heart;
        }

        bool HeartStillMapsTo(int plot, Entity heart)
        {
            if (!Core.EntityManager.Exists(heart))
            {
                Forget(plot);
                return false;
            }

            var castle = heart.Read<CastleHeart>();
            var territory = castle.CastleTerritoryEntity;
            if (territory == Entity.Null || !Core.EntityManager.Exists(territory))
            {
                Forget(plot);
                return false;
            }

            var index = territory.Read<CastleTerritory>().CastleTerritoryIndex;
            if (index == plot)
                return true;

            Forget(plot);
            Remember(heart, notify: true);
            return false;
        }

        public void Remember(Entity heart, bool notify = true)
        {
            if (!Core.EntityManager.Exists(heart))
                return;
            var territory = heart.Read<CastleHeart>().CastleTerritoryEntity;
            if (!Core.EntityManager.Exists(territory))
                return;
            heartByPlot[territory.Read<CastleTerritory>().CastleTerritoryIndex] = heart;
            if (notify)
                onMutate?.Invoke();
        }

        public void ForgetHeart(Entity heart)
        {
            if (!Core.EntityManager.Exists(heart))
                return;
            var territory = heart.Read<CastleHeart>().CastleTerritoryEntity;
            if (!Core.EntityManager.Exists(territory))
                return;
            Forget(territory.Read<CastleTerritory>().CastleTerritoryIndex);
        }

        void Forget(int plot)
        {
            heartByPlot.Remove(plot);
            rebuilding.Remove(plot);
            onMutate?.Invoke();
        }

        public bool Rebuilding(int plot) => rebuilding.Contains(plot);

        public void MarkRebuilding(int plot) => rebuilding.Add(plot);

        public void ForgetEntityCache() => entityPlot.Clear();

        public int ResolvePlot(Entity entity)
        {
            if (entityPlot.TryGetValue(entity, out var cached))
                return cached;

            if (entity.Has<CastleHeartConnection>())
            {
                var heart = entity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
                if (Core.EntityManager.Exists(heart) && heart != Entity.Null)
                {
                    var territory = heart.Read<CastleHeart>().CastleTerritoryEntity;
                    if (territory.Has<CastleTerritory>())
                    {
                        var plot = territory.Read<CastleTerritory>().CastleTerritoryIndex;
                        entityPlot[entity] = plot;
                        return plot;
                    }
                }
            }

            return PlotUnderTile(entity);
        }

        public int PlotUnderTile(Entity entity)
        {
            if (entity == Entity.Null || !entity.Has<TilePosition>())
                return -1;
            var region = Core.RegionService.GetRegion(entity);
            if (!plotsByRegion.TryGetValue(region, out var plots))
                return -1;
            var tile = entity.Read<TilePosition>();
            for (var i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                if (!CastleTerritoryExtensions.IsTileInTerritory(Core.EntityManager, tile.Tile, ref plot, out _))
                    continue;
                if (plot.Has<CastleTerritory>())
                    return plot.Read<CastleTerritory>().CastleTerritoryIndex;
            }
            return -1;
        }
    }

}
