using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services;

/// <summary>
/// Territory-callback registrar. Demand, fair-share, salvage, spawners, and braziers
/// live in BeltRun / BeltSplit. Inspect/chat lives on BeltInspect.
/// </summary>
internal class ConveyorService
{
    public ConveyorService() { Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Stations); Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Salvagers); Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Spawners); Core.TerritoryService.RegisterTerritoryUpdateCallback(BeltRun.Braziers); }

    public Dictionary<PrefabGUID, int> CountTerritoryItems(int territoryId) => BeltCounts.OfPlot(territoryId);

    public Dictionary<PrefabGUID, int> CountTerritoryItems(IReadOnlyList<int> territoryIds) => BeltCounts.OfPlots(territoryIds);
}
