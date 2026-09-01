using Il2CppInterop.Runtime;
using ProjectM.Terrain;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Satisvampory.Services;

internal class RegionService
{
    readonly record struct Hull(WorldRegionType Kind, Aabb Box, float2[] Ring);
    readonly List<Hull> hulls = [];

    public RegionService() => Rescan();

    public void Rescan()
    {
        hulls.Clear();
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(ComponentType.ReadWrite(Il2CppType.Of<WorldRegionPolygon>()));
        var query = Core.EntityManager.CreateEntityQuery(ref builder);
        builder.Dispose();
        var rows = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++) { var polygon = rows[i].Read<WorldRegionPolygon>(); var verts = Core.EntityManager.GetBuffer<WorldRegionPolygonVertex>(rows[i]); var ring = new float2[verts.Length]; for (var v = 0; v < verts.Length; v++) ring[v] = verts[v].VertexPos; hulls.Add(new Hull(polygon.WorldRegion, polygon.PolygonBounds, ring)); }
        }
        finally { rows.Dispose(); query.Dispose(); }
    }

    public WorldRegionType GetRegion(Entity entity) => GetRegion(entity.Read<Translation>().Value);

    public WorldRegionType GetRegion(float3 pos) { for (var i = 0; i < hulls.Count; i++) if (hulls[i].Box.Contains(pos) && OddCrossings(hulls[i].Ring, pos.xz)) return hulls[i].Kind; return WorldRegionType.None; }

    static bool OddCrossings(float2[] ring, float2 point)
    {
        var hits = 0;
        var n = ring.Length;
        if (n == 0) return false;
        for (int i = 0, prev = n - 1; i < n; prev = i++) { var a = ring[i]; var b = ring[prev]; if ((a.y > point.y) == (b.y > point.y)) continue; if (point.x < a.x + (point.y - a.y) / (b.y - a.y) * (b.x - a.x)) hits++; }
        return (hits & 1) == 1;
    }
}
