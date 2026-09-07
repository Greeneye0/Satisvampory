using ProjectM;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Il2CppInterop.Runtime;

namespace Satisvampory.Services
{
    internal static class NeedCatalog
    {
        internal sealed class Recipe
        {
            public int Id, Output, Yield;
            public bool Always;
            public Dictionary<int, int> Inputs = new();
            public List<int> Stations = new();
            public List<string> Unlocks = new();
        }
        internal static readonly Dictionary<int, Recipe> Recipes = new();
        internal static readonly Dictionary<int, List<Recipe>> Products = new();
        internal static readonly Dictionary<int, (EquipmentType slot, WeaponType weapon, float level)> Gear = new();
        internal static readonly HashSet<int> Ingredients = new();
        internal static readonly Dictionary<int, List<string>> StationUnlocks = new();
        static readonly Dictionary<int, int> depths = new();
        static object world;
        internal static string Label(int id) => StashRouting.ItemLabel(new PrefabGUID(id));
        internal static Entity Prefab(int id) => id != 0 && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(id), out var ent) ? ent : Entity.Null;
        internal static void Ensure()
        {
            if (ReferenceEquals(world, Core.PrefabCollectionSystem) && Recipes.Count > 0) return;
            world = null; // publish only after the entire catalog succeeds
            Recipes.Clear(); Products.Clear(); Gear.Clear(); Ingredients.Clear(); StationUnlocks.Clear(); depths.Clear();
            var prefabs = new Dictionary<PrefabGUID, Entity>();
            foreach (var ent in GearDebug.Entities(new(Il2CppType.Of<Prefab>(), ComponentType.AccessMode.ReadOnly), true))
                if (ent.Has<PrefabGUID>()) prefabs[ent.Read<PrefabGUID>()] = ent;
            foreach (var kv in prefabs)
            {
                var ent = kv.Value;
                if (!Core.EntityManager.Exists(ent)) continue;
                if (ent.Has<EquippableData>() && GearDebug.Level(kv.Key) is float level)
                {
                    var e = ent.Read<EquippableData>();
                    Gear[kv.Key.GuidHash] = (e.EquipmentType, e.WeaponType, level);
                }
                if (!ent.Has<RecipeData>() || !ent.Has<RecipeOutputBuffer>() || !ent.Has<RecipeRequirementBuffer>()) continue;
                var data = ent.Read<RecipeData>();
                var name = kv.Key.PrefabName();
                if (name.Contains("Repair", StringComparison.OrdinalIgnoreCase) || name.Contains("Salvage", StringComparison.OrdinalIgnoreCase)) continue;
                var outputs = ent.ReadBuffer<RecipeOutputBuffer>();
                if (outputs.Length == 0 || outputs[0].Guid.GuidHash == 0) continue;
                var r = new Recipe { Id = kv.Key.GuidHash, Output = outputs[0].Guid.GuidHash, Yield = Math.Max(1, outputs[0].Amount), Always = data.AlwaysUnlocked };
                foreach (var req in ent.ReadBuffer<RecipeRequirementBuffer>())
                    if (req.Guid.GuidHash != 0 && req.Amount > 0) r.Inputs[req.Guid.GuidHash] = req.Amount;
                if (r.Inputs.Count == 0) continue;
                Ingredients.UnionWith(r.Inputs.Keys);
                Recipes[r.Id] = r;
                if (!Products.TryGetValue(r.Output, out var list)) Products[r.Output] = list = new();
                list.Add(r);
            }
            var techRecipes = new Dictionary<int, List<int>>();
            foreach (var kv in prefabs)
            {
                var ent = kv.Value;
                if (!Core.EntityManager.Exists(ent)) continue;
                void StationRecipe(int id)
                {
                    if (Recipes.TryGetValue(id, out var r) && !r.Stations.Contains(kv.Key.GuidHash)) r.Stations.Add(kv.Key.GuidHash);
                }
                if (ent.Has<WorkstationRecipesBuffer>()) foreach (var row in ent.ReadBuffer<WorkstationRecipesBuffer>()) StationRecipe(row.RecipeGuid.GuidHash);
                if (ent.Has<RefinementstationRecipesBuffer>()) foreach (var row in ent.ReadBuffer<RefinementstationRecipesBuffer>()) StationRecipe(row.RecipeGuid.GuidHash);
                if (ent.Has<TechUnlockRecipeBuffer>())
                {
                    var ids = new List<int>();
                    foreach (var row in ent.ReadBuffer<TechUnlockRecipeBuffer>()) ids.Add(row.Guid.GuidHash);
                    techRecipes[kv.Key.GuidHash] = ids;
                }
            }
            var bossTech = new HashSet<int>();
            foreach (var kv in prefabs)
            {
                var ent = kv.Value;
                if (!Core.EntityManager.Exists(ent) || !ent.Has<VBloodUnlockTechBuffer>()) continue;
                foreach (var tech in ent.ReadBuffer<VBloodUnlockTechBuffer>())
                {
                    bossTech.Add(tech.Guid.GuidHash);
                    var techEnt = Prefab(tech.Guid.GuidHash);
                    if (Core.EntityManager.Exists(techEnt) && techEnt.Has<TechUnlockBlueprintBuffer>())
                        foreach (var blueprint in techEnt.ReadBuffer<TechUnlockBlueprintBuffer>())
                        {
                            if (!StationUnlocks.TryGetValue(blueprint.Guid.GuidHash, out var names)) StationUnlocks[blueprint.Guid.GuidHash] = names = new();
                            names.Add("Defeat " + ent.EntityName());
                        }
                    if (techRecipes.TryGetValue(tech.Guid.GuidHash, out var ids))
                        foreach (var id in ids) if (Recipes.TryGetValue(id, out var r)) r.Unlocks.Add("Defeat " + ent.EntityName());
                }
            }
            foreach (var kv in techRecipes.Where(x => !bossTech.Contains(x.Key)))
                foreach (var id in kv.Value) if (Recipes.TryGetValue(id, out var r)) r.Unlocks.Add("Research " + Prefab(kv.Key).EntityName() + " (random discovery is not guaranteed)");
            world = Core.PrefabCollectionSystem;
        }
        internal static int Depth(int id) => Depth(id, new HashSet<int>());
        static int Depth(int id, HashSet<int> path)
        {
            if (depths.TryGetValue(id, out var found)) return found;
            if (path.Count >= 12 || !path.Add(id)) return 0;
            var depth = 0;
            if (Products.TryGetValue(id, out var recipes))
                depth = 1 + recipes.Min(r => r.Inputs.Keys.Select(i => Depth(i, path)).DefaultIfEmpty(0).Max());
            path.Remove(id);
            return depths[id] = depth;
        }
    }
}
