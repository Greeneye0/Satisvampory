using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Physics;
using Satisvampory.Commands.Converters;
using Stunlock.Core;
using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    internal static class ServerHost
    {
        static MonoBehaviour behaviour;

        public static World Find(string name)
        {
            foreach (var world in World.s_AllWorlds)
            {
                if (world.Name == name)
                    return world;
            }
            return null;
        }

        public static void Start(IEnumerator routine)
        {
            if (behaviour == null)
            {
                var go = new GameObject("Satisvampory");
                behaviour = go.AddComponent<IgnorePhysicsDebugSystem>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
            behaviour.StartCoroutine(routine.WrapToIl2Cpp());
        }

        public static IEnumerator SettingsFlushLoop()
        {
            var wait = new WaitForSeconds(2f);
            while (true)
            {
                yield return wait;
                Core.PlayerSettings.FlushSettings();
            }
        }

        public static void Boot()
        {
            Core.PrefabCollectionSystem = Core.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            Core.ServerGameSettingsSystem = Core.Server.GetExistingSystemManaged<ServerGameSettingsSystem>();
            Core.DebugEventsSystem = Core.Server.GetExistingSystemManaged<DebugEventsSystem>();
            Core.ServerScriptMapper = Core.Server.GetExistingSystemManaged<ProjectM.Scripting.ServerScriptMapper>();

            Core.RefinementStations = new();
            Core.RegionService = new();
            Core.SalvageService = new();
            Core.TerritoryService = new();
            Core.UnitSpawnerstationService = new();
            Core.BrazierService = new();
            Core.ConveyorService = new();
            Core.WorkQueue = new();
            Core.MarkBooted();
            Core.WorkQueue.EnqueueAll();
            RepeatHunts.PublishClientState();
            if (Core.PlayerSettings.IsRepeatHuntEnabled())
                RepeatHunts.CaptureActiveMissions();

            Start(ClanTreasuryLend.Loop());
            Start(DebugPeekService.Loop());
            DropTracker.SeedExisting();
            Start(DropTracker.DiscoverLoop());
            Start(ScoopService.AutoScoopLoop());
            Start(SettingsFlushLoop());

            FoundItemConverter.LoadItemNames();
            ItemGroupService.Initialize();
            FixHiddenGlassBottleRecipe();

            Core.Log.LogInfo("Satisvampory initialized");
            Core.Log.LogInfo($"Satisvampory {MyPluginInfo.PLUGIN_VERSION}: logistics + scoop. Chest labels s#/r#/overflow/salvage/spoils/trash/NS/'' unchanged.");
        }

        public static void FixHiddenGlassBottleRecipe()
        {
            const int advancedFurnace = -222851985;
            const int hiddenBottle = 394757670;
            var visibleBottle = new PrefabGUID(461575192);
            if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(visibleBottle, out _))
                return;

            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<Refinementstation>(), ComponentType.AccessMode.ReadOnly))
                .AddAll(new(Il2CppType.Of<PrefabGUID>(), ComponentType.AccessMode.ReadOnly))
                .AddAll(new(Il2CppType.Of<RefinementstationRecipesBuffer>(), ComponentType.AccessMode.ReadWrite))
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab);

            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            var stations = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (var i = 0; i < stations.Length; i++)
                {
                    var station = stations[i];
                    if (station.Read<PrefabGUID>().GuidHash != advancedFurnace)
                        continue;
                    var recipes = Core.EntityManager.GetBuffer<RefinementstationRecipesBuffer>(station);
                    for (var slot = 0; slot < recipes.Length; slot++)
                    {
                        var row = recipes[slot];
                        if (row.RecipeGuid.GuidHash != hiddenBottle)
                            continue;
                        Core.Log.LogInfo($"Replacing recipe {row.RecipeGuid.LookupName()} on {station.EntityName()} {station.Index}:{station.Version} with {visibleBottle.LookupName()}");
                        row.RecipeGuid = visibleBottle;
                        recipes[slot] = row;
                    }
                }
            }
            finally
            {
                stations.Dispose();
                query.Dispose();
            }
        }
    }
}
