
namespace Satisvampory.Services
{
    /// <summary>
    /// Territory-callback registrar. Demand, fair-share, salvage, spawners, and braziers
    /// live in BeltRun / BeltSplit so this file is not a Kindred ProcessConveyors fork.
    /// </summary>
    internal partial class ConveyorService
    {
        internal static readonly PrefabGUID Item_Building_Siege_Golem_T02 = BeltSplit.SiegeGolemT02;

        public ConveyorService()
        {
            Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Stations);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Salvagers);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Spawners);
            Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Braziers);
        }

        public Dictionary<PrefabGUID, int> CountTerritoryItems(int territoryId) => BeltCounts.OfPlot(territoryId);

        public Dictionary<PrefabGUID, int> CountTerritoryItems(IReadOnlyList<int> territoryIds) => BeltCounts.OfPlots(territoryIds);
    }
}
