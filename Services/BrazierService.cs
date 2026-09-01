using Il2CppInterop.Runtime;
using System.Collections;
using Unity.Transforms;
using UnityEngine;

namespace Satisvampory.Services
{
    internal sealed class BrazierService
    {
        readonly HeartBoundIndex index;
        readonly Dictionary<int, HashSet<Entity>> proxTouched = new();
        const float TickSeconds = 2.5f;
        const float ProxRange = 20f;
        readonly ProxLights lights;

        public BrazierService()
        {
            index = HeartBoundIndex.Scan(includeDisabled: true, ComponentType.ReadOnly(Il2CppType.Of<Bonfire>()));
            for (var plot = TerritoryService.MIN_TERRITORY_ID; plot <= TerritoryService.MAX_TERRITORY_ID; plot++)
                proxTouched[plot] = new HashSet<Entity>();
            lights = new ProxLights(index, proxTouched);
        }

        internal void AddBrazier(Entity bonfire) => index.Track(bonfire);
        internal void RemoveBrazier(Entity bonfire) => index.Untrack(bonfire);
        public IEnumerable<Entity> GetAllBraziers(int territoryId) => index.OnTerritory(territoryId);
    }
}
