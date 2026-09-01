using HookDOTS.API.Attributes;
using Il2CppInterop.Runtime;
using ProjectM.CastleBuilding.Rebuilding;

namespace Satisvampory.Patches;

public static class CastleRebuildPatches
{
    static EntityQuery transferQuery;

    [EcsSystemUpdatePrefix(typeof(CastleRebuildRegistryServerEventSystem))]
    public static void OnRegistryPrefix()
    {
        if (!Core.HasInitialized) return;
        if (transferQuery == default)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<CastleRebuildTransferEvent>(), ComponentType.AccessMode.ReadOnly));
            transferQuery = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
        }
        var rows = transferQuery.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < rows.Length; i++)
                Core.TerritoryService.MarkTerritoryRebuilding(rows[i].Read<CastleRebuildTransferEvent>().SourceTerritory.ZoneIndex);
        }
        finally { rows.Dispose(); }
    }
}
