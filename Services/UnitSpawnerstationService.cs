using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services;

internal sealed class UnitSpawnerstationService
{
        readonly HeartBoundIndex index;

        static ComponentType[] QueryTypes =>
        [
            ComponentType.ReadOnly(Il2CppType.Of<Team>()),
            ComponentType.ReadOnly(Il2CppType.Of<CastleHeartConnection>()),
            ComponentType.ReadOnly(Il2CppType.Of<UnitSpawnerstation>()),
            ComponentType.ReadOnly(Il2CppType.Of<NameableInteractable>()),
            ComponentType.ReadOnly(Il2CppType.Of<UserOwner>()),
            ComponentType.ReadOnly(Il2CppType.Of<RefinementstationRecipesBuffer>()),
            ComponentType.ReadOnly(Il2CppType.Of<CastleWorkstation>()),
        ];

        public UnitSpawnerstationService() { index = HeartBoundIndex.Scan(includeDisabled: true, QueryTypes); }

        internal void AddUnitSpawnerStation(Entity station) => index.Track(station);
        internal void RemoveUnitSpawnerStation(Entity station) => index.Untrack(station);

        public IEnumerable<Entity> GetAllUnitSpawners(int territoryId) => index.OnTerritory(territoryId);
}
