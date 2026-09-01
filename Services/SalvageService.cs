using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services;

internal sealed class SalvageService
{
        readonly HeartBoundIndex index;

        public SalvageService() { index = HeartBoundIndex.Scan(includeDisabled: true, ComponentType.ReadOnly(Il2CppType.Of<Salvagestation>())); }

        internal void Refresh() =>
            index.Rebuild(includeDisabled: true, ComponentType.ReadOnly(Il2CppType.Of<Salvagestation>()));

        internal void AddSalvageStation(Entity station) => index.Track(station);
        internal void RemoveSalvageStation(Entity station) => index.Untrack(station);

        public IEnumerable<Entity> GetAllSalvageStations(int territoryId) => index.OnTerritory(territoryId);
}
