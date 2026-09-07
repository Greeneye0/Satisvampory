using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class GearDebug
    {
        static readonly EquipmentType[] Slots = { EquipmentType.Chest, EquipmentType.Gloves,
            EquipmentType.Legs, EquipmentType.Footgear, EquipmentType.Weapon, EquipmentType.MagicSource };

        static List<Entity> Entities(ComponentType type)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp).AddAll(type);
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            NativeArray<Entity> array = default;
            try
            {
                array = query.ToEntityArray(Allocator.Temp);
                var result = new List<Entity>();
                foreach (var entity in array) result.Add(entity);
                return result;
            }
            finally { if (array.IsCreated) array.Dispose(); query.Dispose(); }
        }

        static object Item(PrefabGUID guid, Entity instance)
        {
            Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(guid, out var prefab);
            var ent = Core.EntityManager.Exists(instance) ? instance : prefab;
            float? armor = null, weapon = null, magic = null;
            if (Core.EntityManager.Exists(ent))
            {
                if (ent.Has<ArmorLevel>()) armor = ent.Read<ArmorLevel>().Level;
                if (ent.Has<WeaponLevel>()) weapon = ent.Read<WeaponLevel>().Level;
                if (ent.Has<SpellLevel>()) magic = ent.Read<SpellLevel>().Level;
            }
            return new { guid = guid.GuidHash, name = guid.GuidHash == 0 ? "Empty" : StashRouting.ItemLabel(guid),
                instance = instance.ToString(), armorLevel = armor, weaponLevel = weapon, spellLevel = magic };
        }

        static object Gear(Entity entity, bool servant)
        {
            var rows = new List<object>();
            if (!Core.EntityManager.Exists(entity) || (servant ? !entity.Has<ServantEquipment>() : !entity.Has<Equipment>()))
                return new { available = false, slots = rows };
            foreach (var slot in Slots)
            {
                var guid = servant ? entity.Read<ServantEquipment>().GetEquipmentItemId(slot) : entity.Read<Equipment>().GetEquipmentItemId(slot);
                var item = servant ? entity.Read<ServantEquipment>().GetEquipmentEntity(slot).GetEntityOnServer()
                    : entity.Read<Equipment>().GetEquipmentEntity(slot).GetEntityOnServer();
                rows.Add(new { slot = slot.ToString(), item = Item(guid, item) });
            }
            return new { available = true, slots = rows };
        }

        static object Unlocks(Entity entity)
        {
            var recipes = new List<int>();
            var research = new List<int>();
            if (Core.EntityManager.Exists(entity))
            {
                if (entity.Has<ProgressionBookRecipeElement>())
                    foreach (var row in entity.ReadBuffer<ProgressionBookRecipeElement>()) recipes.Add(row.Recipe.GuidHash);
                if (entity.Has<ResearchBuffer>())
                    foreach (var row in entity.ReadBuffer<ResearchBuffer>()) research.Add(row.ResearchGuid.GuidHash);
            }
            return new { recipes, research, recipesBufferPresent = Core.EntityManager.Exists(entity) && entity.Has<ProgressionBookRecipeElement>(),
                researchBufferPresent = Core.EntityManager.Exists(entity) && entity.Has<ResearchBuffer>() };
        }

        public static string Snapshot(int plot)
        {
            if (plot < 0) return JsonSerializer.Serialize(new { error = "no plot" });
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(plot);
            if (ids == null || ids.Count == 0) return JsonSerializer.Serialize(new { error = "no logistics plots" });
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            var players = new List<object>();
            foreach (var entity in Entities(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly)))
            {
                var user = entity.Read<User>();
                if (!Core.TerritoryService.IsSameClanAsHeartOwner(user, heart)) continue;
                var character = user.LocalCharacter.GetEntityOnServer();
                var bag = new List<object>();
                if (Core.EntityManager.Exists(character) && InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, character, out var inv)
                    && Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buffer))
                    foreach (var row in buffer)
                        if (row.ItemType.GuidHash != 0)
                            bag.Add(new { amount = row.Amount, item = Item(row.ItemType, row.ItemEntity.GetEntityOnServer()) });
                players.Add(new { name = user.CharacterName.ToString(), connected = user.IsConnected,
                    equipment = Gear(character, false), inventory = bag, userUnlocks = Unlocks(entity),
                    characterUnlocks = Unlocks(character), clanUnlocks = Unlocks(user.ClanEntity.GetEntityOnServer()) });
            }
            var servants = new List<object>();
            foreach (var coffin in Entities(new(Il2CppType.Of<ServantCoffinstation>(), ComponentType.AccessMode.ReadOnly)))
            {
                var home = Core.TerritoryService.GetTerritoryId(coffin);
                if (!ids.Contains(home)) continue;
                var data = coffin.Read<ServantCoffinstation>();
                var servant = data.ConnectedServant.GetEntityOnServer();
                servants.Add(new { plot = home, name = data.ServantName.ToString(), state = data.State.ToString(),
                    onMission = Core.EntityManager.Exists(servant) && servant.Has<ServantData>() && servant.Read<ServantData>().IsOnMission,
                    equipment = Gear(servant, true) });
            }
            var stations = new List<object>();
            foreach (var station in Entities(new(Il2CppType.Of<CastleWorkstation>(), ComponentType.AccessMode.ReadOnly)))
            {
                var home = Core.TerritoryService.GetTerritoryId(station);
                if (!ids.Contains(home) || !station.Has<WorkstationRecipesBuffer>()) continue;
                var recipes = new List<object>();
                foreach (var row in station.ReadBuffer<WorkstationRecipesBuffer>())
                {
                    if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(row.RecipeGuid, out var recipe)
                        || !Core.EntityManager.Exists(recipe)) continue;
                    var inputs = new List<object>();
                    var outputs = new List<object>();
                    if (recipe.Has<RecipeRequirementBuffer>())
                        foreach (var req in recipe.ReadBuffer<RecipeRequirementBuffer>())
                            inputs.Add(new { amount = BeltRecipe.PerCraft(req.Amount, BeltRecipe.FloorScale(station)), item = Item(req.Guid, Entity.Null) });
                    if (recipe.Has<RecipeOutputBuffer>())
                        foreach (var output in recipe.ReadBuffer<RecipeOutputBuffer>())
                            outputs.Add(new { amount = output.Amount, item = Item(output.Guid, Entity.Null) });
                    recipes.Add(new { guid = row.RecipeGuid.GuidHash, inputs, outputs });
                }
                stations.Add(new { plot = home, name = station.EntityName(), disabled = station.Has<Disabled>(), recipes });
            }
            return JsonSerializer.Serialize(new { schemaVersion = 1, capturedUtc = DateTime.UtcNow, plot,
                semantics = "Read-only gear evidence. Missing components/levels mean unknown, not zero. Station recipes are candidates, not proof of unlock. Check progression/research and equipped or stored upgrade prerequisites before ranking. Empty or converting coffins are not equipped servants.",
                players, servants, stations });
        }
    }
}
