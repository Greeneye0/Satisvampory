using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services;
class SalvageService
{
    readonly Dictionary<Entity, List<Entity>> salvageStationsByHeart = [];

    public SalvageService()
    {
        Refresh();
    }

    internal void Refresh()
    {
        salvageStationsByHeart.Clear();
        var entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(ComponentType.ReadOnly(Il2CppType.Of<Salvagestation>()))
            .WithOptions(EntityQueryOptions.IncludeDisabled);
        var salvageStationQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
        entityQueryBuilder.Dispose();

        var stationArray = salvageStationQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var station in stationArray)
                AddSalvageStation(station);
        }
        finally
        {
            stationArray.Dispose();
        }
        salvageStationQuery.Dispose();
    }

    internal void AddSalvageStation(Entity stationEntity)
    {
        if (!Core.EntityManager.Exists(stationEntity) || !stationEntity.Has<CastleHeartConnection>())
            return;

        var castleHeartEntity = stationEntity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
        if (castleHeartEntity == Entity.Null)
            return;

        if (!salvageStationsByHeart.TryGetValue(castleHeartEntity, out var list))
        {
            list = [];
            salvageStationsByHeart.Add(castleHeartEntity, list);
        }
        if (!list.Contains(stationEntity))
            list.Add(stationEntity);
    }

    internal void RemoveSalvageStation(Entity stationEntity)
    {
        var castleHeartEntity = stationEntity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();

        if (!salvageStationsByHeart.TryGetValue(castleHeartEntity, out var list)) return;

        list.Remove(stationEntity);
    }

    public IEnumerable<Entity> GetAllSalvageStations(int territoryId)
    {
        var castleHeartEntity = Core.TerritoryService.GetCastleHeart(territoryId);
        if (castleHeartEntity != Entity.Null && salvageStationsByHeart.TryGetValue(castleHeartEntity, out var list))
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var stationEntity = list[i];
                if (!Core.EntityManager.Exists(stationEntity))
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (stationEntity.Has<Disabled>()) continue;
                yield return stationEntity;
            }
            yield break;
        }

        // Heart-key miss: fall back to stations whose own territory matches this plot.
        foreach (var other in salvageStationsByHeart.Values)
        {
            for (var i = other.Count - 1; i >= 0; i--)
            {
                var stationEntity = other[i];
                if (!Core.EntityManager.Exists(stationEntity))
                {
                    other.RemoveAt(i);
                    continue;
                }
                if (stationEntity.Has<Disabled>()) continue;
                if (Core.TerritoryService.GetTerritoryId(stationEntity) != territoryId) continue;
                yield return stationEntity;
            }
        }
    }
}
