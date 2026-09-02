using BepInEx;
using System;
using System.IO;
using Satisvampory;
using System.Collections.Generic;
using System.Linq;
using Satisvampory.Commands.Converters;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using VampireCommandFramework;

namespace Satisvampory.Services
{
    internal static class ItemGroupService
    {
        public const string GroupOre = "ore";
        public const string GroupFlowers = "flowers";
        public const string GroupSeeds = "seeds";
        public const string GroupMushrooms = "mushrooms";
        public const string GroupTailoring = "tailoring";
        public const string GroupHides = "hides";
        public const string GroupWood = "wood";
        public const string GroupGems = "gems";
        public const string GroupAlchemy = "alchemy";
        public const string GroupBlood = "blood";
        public const string GroupBones = "bones";
        public const string GroupIngots = "ingots";
        public const string GroupPlanks = "planks";
        public const string GroupStone = "stone";
        public const string GroupCoins = "coins";
        public const string GroupFish = "fish";
        public const string GroupKnowledge = "knowledge";
        public const string GroupMinerals = "minerals";
        public const string GroupConsumables = "consumables";
        public const string GroupWeapons = "weapons";
        public const string GroupArmor = "armor";
        public const string GroupJewels = "jewels";
        public const string GroupMagic = "magic";
        public const string GroupSoulshards = "soulshards";
        public const string GroupBags = "bags";
        public const string GroupSaddles = "saddles";
        public const string GroupRelics = "relics";

        public readonly record struct GroupMember(PrefabGUID Prefab, string Name, int GuidHash);

        // Aliases so chest names (Ingots, Scrolls, Leather, Herbs, ...) hit the canonical group.
        // herbs -> flowers: chest names use both; plants already live in flowers.
        static readonly Dictionary<string, string> builtInCanonical = new(StringComparer.OrdinalIgnoreCase)
        {
            [GroupOre] = GroupOre,
            [GroupFlowers] = GroupFlowers,
            ["herb"] = GroupFlowers,
            ["herbs"] = GroupFlowers,
            [GroupSeeds] = GroupSeeds,
            ["seed"] = GroupSeeds,
            [GroupMushrooms] = GroupMushrooms,
            [GroupTailoring] = GroupTailoring,
            ["thread"] = GroupTailoring,
            ["threads"] = GroupTailoring,
            [GroupHides] = GroupHides,
            ["hide"] = GroupHides,
            ["leather"] = GroupHides,
            ["leathers"] = GroupHides,
            [GroupWood] = GroupWood,
            [GroupGems] = GroupGems,
            ["gem"] = GroupGems,
            [GroupAlchemy] = GroupAlchemy,
            [GroupBlood] = GroupBlood,
            [GroupBones] = GroupBones,
            ["bone"] = GroupBones,
            [GroupIngots] = GroupIngots,
            ["ingot"] = GroupIngots,
            [GroupPlanks] = GroupPlanks,
            ["plank"] = GroupPlanks,
            [GroupStone] = GroupStone,
            ["stones"] = GroupStone,
            [GroupCoins] = GroupCoins,
            ["coin"] = GroupCoins,
            [GroupFish] = GroupFish,
            [GroupKnowledge] = GroupKnowledge,
            ["scroll"] = GroupKnowledge,
            ["scrolls"] = GroupKnowledge,
            ["paper"] = GroupKnowledge,
            ["book"] = GroupKnowledge,
            ["books"] = GroupKnowledge,
            [GroupMinerals] = GroupMinerals,
            ["mineral"] = GroupMinerals,
            ["material"] = GroupMinerals,
            ["materials"] = GroupMinerals,
            ["tech"] = GroupMinerals,
            [GroupConsumables] = GroupConsumables,
            ["consumable"] = GroupConsumables,
            ["potion"] = GroupConsumables,
            ["potions"] = GroupConsumables,
            [GroupWeapons] = GroupWeapons,
            ["weapon"] = GroupWeapons,
            [GroupArmor] = GroupArmor,
            ["armour"] = GroupArmor,
            [GroupJewels] = GroupJewels,
            ["jewel"] = GroupJewels,
            [GroupMagic] = GroupMagic,
            [GroupSoulshards] = GroupSoulshards,
            ["soulshard"] = GroupSoulshards,
            ["shard"] = GroupSoulshards,
            ["shards"] = GroupSoulshards,
            [GroupBags] = GroupBags,
            ["bag"] = GroupBags,
            [GroupSaddles] = GroupSaddles,
            ["saddle"] = GroupSaddles,
            [GroupRelics] = GroupRelics,
            ["relic"] = GroupRelics,
        };

        // Dest groups rebuilt at startup from PrefabCollection ItemData.
        // Flags first, then name/prefab overlay rules. No frozen GUID table. CSV is debug-only.
        // Exact dest / find / pull aliases (BE, GBE, PBE, GSS, …). `blood` dest stays regular Blood Essence.
        // FakeItem_ / Any * recipe placeholders are not stash dests and are omitted from unresolved chat.
        static readonly Dictionary<string, List<GroupMember>> builtInMembers = new(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> missingRequested = new();
        static readonly Dictionary<int, string> destGroupByHash = new();
        static readonly List<string> catalogRows = new();
        static readonly Dictionary<string, int> exactAliasToHash = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> builtInAliasKeys = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, string> catalogAliasByHash = new();

        public static int BloodEssenceHash { get; private set; }
        public static int GreaterBloodEssenceHash { get; private set; }
        public static int PrimalBloodEssenceHash { get; private set; }
        public static int AncestralBloodEssenceHash { get; private set; }

        public static IReadOnlyList<string> MissingRequestedNames => missingRequested;
        public static IEnumerable<string> BuiltInNames => new[]
        {
            GroupOre, GroupFlowers, GroupSeeds, GroupMushrooms, GroupTailoring, GroupHides, GroupWood, GroupPlanks,
            GroupGems, GroupAlchemy, GroupBlood, GroupBones, GroupIngots, GroupStone, GroupCoins,
            GroupFish, GroupKnowledge, GroupMinerals, GroupConsumables, GroupWeapons, GroupArmor,
            GroupJewels, GroupMagic, GroupSoulshards, GroupBags, GroupSaddles, GroupRelics
        };

        public static bool IsGreaterBloodEssence(PrefabGUID item)
        {
            return item.GuidHash != 0 && GreaterBloodEssenceHash != 0 && item.GuidHash == GreaterBloodEssenceHash;
        }

        public static bool TryExactEssenceAlias(string s, out int hash) => TryExactItemAlias(s, out hash);

        public static bool TryExactItemAlias(string s, out int hash)
        {
            hash = 0;
            if (string.IsNullOrWhiteSpace(s))
                return false;
            return exactAliasToHash.TryGetValue(FoundItemConverter.Normalize(s), out hash) && hash != 0;
        }

        public static bool IsBuiltInAlias(string alias)
        {
            return !string.IsNullOrWhiteSpace(alias) && builtInAliasKeys.Contains(FoundItemConverter.Normalize(alias));
        }

        public static bool IsReservedAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return true;
            var t = FoundItemConverter.Normalize(alias);
            if (t.Length < 2 || t.Length > 16)
                return true;
            if (t.IndexOf(' ') >= 0)
                return true;
            if (TryGetBuiltInCanonical(t, out _))
                return true;
            if (t is "overflow" or "spoils" or "spoil" or "salvage" or "trash" or "storage" or "ns")
                return true;
            if (t.Length >= 2 && (t[0] == 's' || t[0] == 'r') && t.Skip(1).All(char.IsDigit))
                return true;
            return false;
        }

        public static IReadOnlyList<(string Alias, string Name, bool BuiltIn)> ListExactAliases()
        {
            var rows = new List<(string, string, bool)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string alias, int hash, bool builtIn)
            {
                if (string.IsNullOrWhiteSpace(alias) || hash == 0 || !seen.Add(alias))
                    return;
                var name = catalogAliasByHash.TryGetValue(hash, out var n) ? n : alias;
                if (FoundItemConverter.TryGetExact(alias, out var found) && found.prefab.GuidHash == hash)
                    name = found.prefab.PrefabName() ?? name;
                rows.Add((alias, name, builtIn));
            }

            foreach (var key in builtInAliasKeys)
            {
                if (exactAliasToHash.TryGetValue(key, out var hash))
                    Add(key, hash, true);
            }

            if (Core.PlayerSettings.TryItemAliases(out var admin, out var names) && admin != null)
            {
                foreach (var kv in admin)
                {
                    var label = names != null && names.TryGetValue(kv.Key, out var n) ? n : kv.Key;
                    if (!seen.Contains(kv.Key))
                        rows.Add((kv.Key, label, false));
                }
            }

            rows.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        public static bool TryGetDestGroup(int guidHash, out string group)
        {
            return destGroupByHash.TryGetValue(guidHash, out group) && !string.IsNullOrEmpty(group);
        }

        /// <summary>
        /// Ingredients / machine fuel (ore, fish, blood, planks, …). Not finished
        /// weapons, armor, jewels, or relics — those bloated covering to 200+ GUIDs.
        /// </summary>
        public static bool IsMachineSpendItem(int guidHash)
        {
            if (guidHash == 0 || !TryGetDestGroup(guidHash, out var group))
                return false;
            return group != GroupWeapons && group != GroupArmor && group != GroupJewels
                && group != GroupMagic && group != GroupBags && group != GroupSaddles
                && group != GroupRelics && group != GroupSoulshards;
        }

        public static bool IsFishOrFeedPlaceholder(string prefab, string loc)
        {
            return IsRecipePlaceholder(prefab, loc);
        }

        public static bool IsGbeAliasText(string s)
        {
            return TryExactEssenceAlias(s, out var hash) && hash == GreaterBloodEssenceHash;
        }

        public static string CatalogAliases(int guidHash)
        {
            if (guidHash == 0)
                return "";
            if (catalogAliasByHash.TryGetValue(guidHash, out var shortName))
                return shortName;
            return "";
        }

        public static void Initialize()
        {
            builtInMembers.Clear();
            missingRequested.Clear();
            destGroupByHash.Clear();
            catalogRows.Clear();
            BloodEssenceHash = 0;
            GreaterBloodEssenceHash = 0;
            PrimalBloodEssenceHash = 0;
            AncestralBloodEssenceHash = 0;
            exactAliasToHash.Clear();
            builtInAliasKeys.Clear();
            catalogAliasByHash.Clear();

            foreach (var n in BuiltInNames)
                builtInMembers[n] = new List<GroupMember>();

            ScanPrefabCollection();
            ResolveNamedItem("Blood Essence", "BE", v => BloodEssenceHash = v);
            ResolveNamedItem("Greater Blood Essence", "GBE", v => GreaterBloodEssenceHash = v, "greater blood");
            ResolveNamedItem("Primal Blood Essence", "PBE", v => PrimalBloodEssenceHash = v, "primal blood");
            ResolveNamedItem("Ancestral Blood Essence", "ABE", v => AncestralBloodEssenceHash = v, "ancestral blood");
            ResolveNamedItem("Greater Stygian Shard", "GSS", null, "greater stygian");
            ResolveNamedItem("Greater Stygian Shards", "GSS", null, "greater stygian");
            ResolveNamedItem("Siege Golem Stone", "SGS", null);
            ResolveNamedItem("Dark Silver Ingot", "DSI", null, "dark silver");
            ResolveNamedItem("Onyx Tear", "OT", null);
            ApplyAdminAliases();

            foreach (var kv in builtInMembers)
            {
                var members = string.Join(", ", kv.Value.Select(m => m.Name + " (" + m.GuidHash + ")"));
                Core.Log.LogInfo("Item group '" + kv.Key + "' (" + kv.Value.Count + "): " + members);
            }

            DumpItemCatalog();
            if (missingRequested.Count > 0)
                Core.Log.LogWarning("Item groups unresolved " + missingRequested.Count + " ItemData names: " + string.Join(", ", missingRequested));
            Core.Log.LogInfo("Item aliases BE=" + BloodEssenceHash
                + " GBE=" + GreaterBloodEssenceHash
                + " PBE=" + PrimalBloodEssenceHash
                + " ABE=" + AncestralBloodEssenceHash
                + " (dest works without item-catalog.csv)");
        }

        static void ResolveNamedItem(string locName, string alias, Action<int> setHash, params string[] extraAliases)
        {
            if (!TryFindItemPrefab(locName, alias, out var prefab))
            {
                Core.Log.LogWarning(alias + " alias: " + locName + " not found in PrefabCollection by name");
                if (string.Equals(alias, "ABE", StringComparison.OrdinalIgnoreCase))
                    LogEssenceItemCandidates();
                return;
            }
            setHash?.Invoke(prefab.GuidHash);
            BindAlias(alias, prefab, builtIn: true);
            BindAlias(locName, prefab, builtIn: true);
            if (extraAliases != null)
            {
                for (var i = 0; i < extraAliases.Length; i++)
                    BindAlias(extraAliases[i], prefab, builtIn: true);
            }
        }

        static bool TryFindItemPrefab(string locName, string alias, out PrefabGUID prefab)
        {
            prefab = default;
            if (FoundItemConverter.TryGetExact(locName, out var exact) && exact.prefab.GuidHash != 0)
            {
                var loc = FoundItemConverter.Normalize(exact.prefab.PrefabName() ?? "");
                if (loc.Equals(locName, StringComparison.OrdinalIgnoreCase))
                {
                    prefab = exact.prefab;
                    return true;
                }
            }
            if (FoundItemConverter.ExactItemNames != null)
            {
                foreach (var kvp in FoundItemConverter.ExactItemNames)
                {
                    if (!kvp.Key.Equals(locName, StringComparison.OrdinalIgnoreCase) || kvp.Value.GuidHash == 0)
                        continue;
                    prefab = kvp.Value;
                    return true;
                }
            }

            var want = FoundItemConverter.Normalize(locName);
            if (TryMatchSpawnable(want, alias, out prefab))
                return true;
            if (TryMatchItemDataPrefabs(want, alias, out prefab))
                return true;
            return false;
        }

        static bool AliasSpawnMatch(string want, string alias, string spawn, string loc)
        {
            var sn = FoundItemConverter.Normalize(spawn ?? "");
            var ln = FoundItemConverter.Normalize(loc ?? "");
            if (ln.Equals(want, StringComparison.OrdinalIgnoreCase)
                || sn.Equals(want, StringComparison.OrdinalIgnoreCase))
                return true;
            var s = (spawn ?? "").Replace(" ", "").ToLowerInvariant();
            var a = (alias ?? "").ToLowerInvariant();
            if (a == "abe")
            {
                if (s.IndexOf("bloodessence", StringComparison.Ordinal) >= 0
                    && s.IndexOf("ancestral", StringComparison.Ordinal) >= 0)
                    return true;
                if (s.IndexOf("item_bloodessence_t04", StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        static bool TryMatchSpawnable(string want, string alias, out PrefabGUID prefab)
        {
            prefab = default;
            if (Core.PrefabCollectionSystem == null)
                return false;
            try
            {
                foreach (var kvp in Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary)
                {
                    if (kvp.Value.GuidHash == 0)
                        continue;
                    var spawn = kvp.Key ?? "";
                    var loc = "";
                    try { loc = kvp.Value.PrefabName() ?? ""; } catch { loc = ""; }
                    if (!AliasSpawnMatch(want, alias, spawn, loc))
                        continue;
                    if (!PrefabHasItemData(kvp.Value))
                        continue;
                    prefab = kvp.Value;
                    Core.Log.LogInfo(alias + " alias matched spawn " + spawn + " guid=" + prefab.GuidHash + " loc=" + loc);
                    return true;
                }
            }
            catch (Exception e)
            {
                Core.Log.LogWarning(alias + " alias spawnable scan: " + e.Message);
            }
            return false;
        }

        static bool TryMatchItemDataPrefabs(string want, string alias, out PrefabGUID prefab)
        {
            prefab = default;
            if (Core.EntityManager == default)
                return false;
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ItemData>(), ComponentType.AccessMode.ReadOnly))
                .AddAll(new(Il2CppType.Of<PrefabGUID>(), ComponentType.AccessMode.ReadOnly))
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities);
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            NativeArray<Entity> ents = default;
            try
            {
                ents = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < ents.Length; i++)
                {
                    var ent = ents[i];
                    if (ent == Entity.Null || !ent.Has<PrefabGUID>())
                        continue;
                    var guid = ent.Read<PrefabGUID>();
                    if (guid.GuidHash == 0)
                        continue;
                    var spawn = "";
                    try
                    {
                        Core.PrefabCollectionSystem._PrefabLookupMap.TryGetName(guid, out spawn);
                    }
                    catch { spawn = ""; }
                    var loc = "";
                    try { loc = guid.PrefabName() ?? ""; } catch { loc = ""; }
                    if (!AliasSpawnMatch(want, alias, spawn, loc))
                        continue;
                    prefab = guid;
                    Core.Log.LogInfo(alias + " alias matched ItemData " + spawn + " guid=" + guid.GuidHash + " loc=" + loc);
                    return true;
                }
            }
            catch (Exception e)
            {
                Core.Log.LogWarning(alias + " alias ItemData scan: " + e.Message);
            }
            finally
            {
                if (ents.IsCreated)
                    ents.Dispose();
                query.Dispose();
            }
            return false;
        }

        static bool PrefabHasItemData(PrefabGUID prefab)
        {
            try
            {
                return Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(prefab, out var ent)
                    && ent != Entity.Null && ent.Has<ItemData>();
            }
            catch
            {
                return false;
            }
        }

        static void LogEssenceItemCandidates()
        {
            if (Core.PrefabCollectionSystem == null)
                return;
            var n = 0;
            try
            {
                foreach (var kvp in Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary)
                {
                    var spawn = kvp.Key ?? "";
                    if (spawn.IndexOf("Essence", StringComparison.OrdinalIgnoreCase) < 0
                        && spawn.IndexOf("Ancestral", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (!PrefabHasItemData(kvp.Value))
                        continue;
                    var loc = "";
                    try { loc = kvp.Value.PrefabName() ?? ""; } catch { }
                    Core.Log.LogInfo("ABE candidate spawn=" + spawn + " guid=" + kvp.Value.GuidHash + " loc=" + loc);
                    n++;
                    if (n >= 40)
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log.LogWarning("ABE candidate scan: " + e.Message);
            }
        }

        static void BindAlias(string alias, PrefabGUID prefab, bool builtIn)
        {
            if (string.IsNullOrWhiteSpace(alias) || prefab.GuidHash == 0)
                return;
            var key = FoundItemConverter.Normalize(alias);
            if (string.IsNullOrEmpty(key))
                return;
            FoundItemConverter.RegisterExactAlias(alias, prefab);
            exactAliasToHash[key] = prefab.GuidHash;
            if (builtIn)
                builtInAliasKeys.Add(key);
            if (key.Length <= 4 && key.IndexOf(' ') < 0)
                catalogAliasByHash[prefab.GuidHash] = alias.ToUpperInvariant();
        }

        public static void ApplyAdminAliases()
        {
            if (!Core.PlayerSettings.TryItemAliases(out var map, out _) || map == null)
                return;
            foreach (var kv in map)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == 0)
                    continue;
                if (builtInAliasKeys.Contains(FoundItemConverter.Normalize(kv.Key)))
                    continue;
                var prefab = new PrefabGUID(kv.Value);
                FoundItemConverter.RegisterExactAlias(kv.Key, prefab);
                exactAliasToHash[FoundItemConverter.Normalize(kv.Key)] = kv.Value;
            }
        }

        public static string BindAdminAlias(string alias, PrefabGUID prefab, string displayName)
        {
            var key = FoundItemConverter.Normalize(alias);
            if (IsReservedAlias(key))
                return "Alias must be 2–16 letters/digits with no spaces, and cannot be a dest word (blood, stone, salvage, s1, …).";
            if (builtInAliasKeys.Contains(key))
                return "Cannot overwrite built-in alias <color=white>" + key + "</color>.";
            if (prefab.GuidHash == 0)
                return "Unknown item.";
            var err = Core.PlayerSettings.SetItemAlias(key, prefab.GuidHash, displayName);
            if (!string.IsNullOrEmpty(err))
                return err;
            FoundItemConverter.RegisterExactAlias(key, prefab);
            exactAliasToHash[key] = prefab.GuidHash;
            return null;
        }

        public static string UnbindAdminAlias(string alias)
        {
            var key = FoundItemConverter.Normalize(alias);
            if (builtInAliasKeys.Contains(key))
                return "Cannot delete built-in alias <color=white>" + key + "</color>.";
            if (!Core.PlayerSettings.RemoveItemAlias(key))
                return "No admin alias <color=white>" + key + "</color>.";
            FoundItemConverter.UnregisterExactAlias(key);
            exactAliasToHash.Remove(key);
            return null;
        }

        static void ScanPrefabCollection()
        {
            var seen = new HashSet<int>();
            if (Core.PrefabCollectionSystem == null)
            {
                Core.Log.LogWarning("Item groups: PrefabCollectionSystem is null; dest catalog empty this boot");
                return;
            }

            try
            {
                foreach (var kvp in Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary)
                    ConsiderPrefab(kvp.Key, kvp.Value, seen);
            }
            catch (Exception e)
            {
                Core.Log.LogWarning("Item groups spawnable scan failed: " + e.Message);
            }
        }

        static void ConsiderPrefab(string prefabName, PrefabGUID prefab, HashSet<int> seen)
        {
            if (prefab.GuidHash == 0)
                return;
            if (!seen.Add(prefab.GuidHash))
                return;

            Entity ent = Entity.Null;
            ItemCategory cat = ItemCategory.NONE;
            try
            {
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(prefab, out ent)
                    && ent != Entity.Null && ent.Has<ItemData>())
                    cat = ent.Read<ItemData>().ItemCategory;
                else
                    return;
            }
            catch
            {
                return;
            }

            var loc = "";
            try { loc = prefab.PrefabName() ?? ""; } catch { loc = ""; }
            if (string.IsNullOrEmpty(loc) || loc.IndexOf("GUID Not Found", StringComparison.OrdinalIgnoreCase) >= 0)
                loc = prefabName ?? "";

            if (ShouldSkipPrefab(prefabName, loc))
                return;

            var group = AssignDestGroup(prefabName ?? "", loc, cat);
            var catName = "";
            var flags = 0L;
            try
            {
                flags = (long)cat;
                catName = cat.ToString();
            }
            catch { }

            if (!string.IsNullOrEmpty(group))
            {
                destGroupByHash[prefab.GuidHash] = group;
                AddMember(group, prefab, loc);
                foreach (var extra in ExtraDestGroups(prefabName ?? "", loc, group))
                    AddMember(extra, prefab, loc);
            }
            else if (!IsRecipePlaceholder(prefabName, loc))
            {
                missingRequested.Add((string.IsNullOrEmpty(loc) ? prefabName : loc) + " [" + (prefabName ?? "") + " " + prefab.GuidHash + "]");
            }

            catalogRows.Add(string.Join(",",
                Csv(prefabName ?? ""),
                Csv(loc),
                prefab.GuidHash.ToString(),
                Csv(catName),
                flags.ToString(),
                Csv(group ?? ""),
                Csv(CatalogAliases(prefab.GuidHash))));
        }

        static bool ShouldSkipPrefab(string prefab, string loc)
        {
            var p = prefab ?? "";
            if (p.Length == 0)
                return true;
            if (p.EndsWith("_Base", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("_Trader_Template", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("_Debug", StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.StartsWith("Item_Any", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("Item_Dummy", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("Item_EquipBuff", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("Item_Base_", StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.IndexOf("MapZone", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("CastleKey", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("CastleUpkeep", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("PaintingFrame", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("Transmog", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (p.IndexOf("_Kit_", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("_Passive_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (p.StartsWith("Item_Building_", StringComparison.OrdinalIgnoreCase))
            {
                var pl = p.ToLowerInvariant();
                if (pl.Contains("explosive") || pl.Contains("emp") || pl.Contains("plants") || pl.Contains("sapling"))
                    return false;
                return true;
            }
            return false;
        }

        static void AddMember(string group, PrefabGUID prefab, string loc)
        {
            if (string.IsNullOrEmpty(group))
                return;
            if (!builtInMembers.TryGetValue(group, out var list))
            {
                list = new List<GroupMember>();
                builtInMembers[group] = list;
            }
            if (!list.Any(m => m.GuidHash == prefab.GuidHash))
                list.Add(new GroupMember(prefab, loc, prefab.GuidHash));
        }

        static IEnumerable<string> ExtraDestGroups(string prefabName, string locName, string primary)
        {
            var pl = (prefabName ?? "").ToLowerInvariant();
            var nl = (locName ?? "").ToLowerInvariant();
            // Clay Mold: alchemy primary, minerals fallback so Materials chests also take it.
            if (pl.Contains("claymold") || nl.Contains("clay mold"))
            {
                if (primary != GroupMinerals)
                    yield return GroupMinerals;
            }
            // Copper Wires: minerals primary (Materials), also ingots so Ingots chests match.
            if (pl.Contains("copperwires") || nl.Contains("copper wire"))
            {
                if (primary != GroupIngots)
                    yield return GroupIngots;
            }
            // Seeds dest to a Seeds chest, but existing Herbs / Mushrooms / Cotton plates still match.
            if (primary == GroupSeeds)
            {
                if (nl.Contains("shroom") || nl.Contains("clarion") || nl.Contains("spore")
                    || pl.Contains("shroom") || pl.Contains("clarion"))
                    yield return GroupMushrooms;
                else if (nl.Contains("cotton") || pl.Contains("cotton"))
                    yield return GroupTailoring;
                else
                    yield return GroupFlowers;
            }
        }

        static bool IsPlantSeed(string prefab, string loc)
        {
            var pl = (prefab ?? "").ToLowerInvariant();
            var nl = (loc ?? "").ToLowerInvariant();
            if (pl.StartsWith("item_building_sapling") || nl.Contains("sapling"))
                return false;
            if (pl.StartsWith("item_building_plants_") && pl.Contains("seed"))
                return true;
            return nl.EndsWith(" seed") || nl.EndsWith(" seeds");
        }

        static bool IsRecipePlaceholder(string prefab, string loc)
        {
            var p = prefab ?? "";
            var n = loc ?? "";
            if (p.StartsWith("FakeItem_", StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.StartsWith("Item_Any", StringComparison.OrdinalIgnoreCase))
                return true;
            var nl = n.Trim();
            if (nl.StartsWith("Any ", StringComparison.OrdinalIgnoreCase) || nl.Equals("Any", StringComparison.OrdinalIgnoreCase))
                return true;
            if (nl.Equals("Feed Prisoner", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        static bool ContainsFold(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle))
                return false;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsHeartAlchemyName(string loc, string prefab)
        {
            var p = prefab ?? "";
            var n = loc ?? "";
            if (ContainsFold(p, "Item_Consumable_Heart"))
                return true;
            if (ContainsFold(n, "Bleeding Heart"))
                return false;
            return ContainsFold(n, "Tainted Heart")
                || ContainsFold(n, "Unsullied Heart")
                || ContainsFold(n, "Exquisite Heart")
                || ContainsFold(n, "Pristine Heart")
                || ContainsFold(n, "Defiled Heart");
        }

        static bool IsAlchemyReagentName(string loc, string prefab)
        {
            string[] names =
            {
                "grave dust", "gravedust",
                "scourgestone", "scourge stone",
                "spectral dust", "spectraldust",
                "witchdust", "witch dust",
                "vampiric dust", "vampiricdust",
                "demon fragment", "demonfragment",
                "mutant grease", "mutantgrease",
                "onyx tear", "onyxtear",
            };
            var blob = (loc ?? "") + " " + (prefab ?? "");
            foreach (var n in names)
            {
                if (ContainsFold(blob, n))
                    return true;
            }
            return false;
        }

        static bool IsExplosiveOrEmp(string loc, string prefab)
        {
            var blob = (loc ?? "") + " " + (prefab ?? "");
            if (ContainsFold(blob, "explosive box") || ContainsFold(blob, "explosivebox") || ContainsFold(blob, "explosives"))
                return true;
            if (ContainsFold(prefab ?? "", "Item_Building_EMP"))
                return true;
            var n = FoundItemConverter.Normalize(loc ?? "");
            return n.Equals("emp", StringComparison.OrdinalIgnoreCase)
                || n.Equals("minor explosive box", StringComparison.OrdinalIgnoreCase)
                || n.Equals("major explosive box", StringComparison.OrdinalIgnoreCase);
        }

        static string AssignDestGroup(string prefabName, string locName, ItemCategory cat)
        {
            var p = prefabName ?? "";
            var n = locName ?? "";
            var pl = p.ToLowerInvariant();
            var nl = n.ToLowerInvariant();

            if ((cat & ItemCategory.Weapon) != 0 || pl.StartsWith("item_weapon_"))
                return GroupWeapons;

            // 1.6.1.41 leftover stashables: name/prefab rules, not hash 0.
            if (pl.StartsWith("item_magicsource_soulshard") || nl.StartsWith("soul shard")
                || (cat & ItemCategory.Soulshard) != 0)
                return GroupSoulshards;
            if (pl.StartsWith("item_newbag_") || (cat & ItemCategory.Bag) != 0)
                return GroupBags;
            if (pl.StartsWith("item_saddle_") || (cat & ItemCategory.Saddle) != 0)
                return GroupSaddles;
            if ((cat & ItemCategory.Relic) != 0 || pl.Contains("_relic"))
                return GroupRelics;
            if (pl.Equals("item_ingredient_crystal") || nl.Equals("crystal"))
                return ((cat & ItemCategory.Mineral) != 0) ? GroupMinerals : GroupGems;
            if (pl.Contains("copperwires") || nl.Contains("copper wire"))
                return GroupMinerals;
            if (pl.Equals("item_ingredient_coal") || nl.Equals("coal"))
                return GroupMinerals;
            if (pl.Equals("item_ingredient_chitin") || pl.Equals("item_ingredient_scales")
                || nl.Equals("chitin") || nl.Equals("scales"))
                return GroupMinerals;
            if (pl.Contains("claymold") || nl.Contains("clay mold"))
                return GroupAlchemy;
            if (pl.Contains("ironbody") || nl.Equals("iron body"))
                return GroupIngots;
            if (pl.Equals("item_ingredient_document") || nl.Equals("document"))
                return GroupKnowledge;
            if (pl.Contains("barreldisguise") || nl.Contains("barrel disguise"))
                return GroupConsumables;

            if ((cat & ItemCategory.BloodEssence) != 0 || nl.Contains("blood essence") || pl.Contains("bloodessence"))
                return GroupBlood;
            if ((cat & ItemCategory.Coin) != 0 || nl.Contains("coin") || pl.Contains("coin"))
                return GroupCoins;
            if ((cat & ItemCategory.Knowledge) != 0)
                return GroupKnowledge;
            if (nl.Contains("gem dust") || nl.Contains("gemdust") || pl.Contains("gemdust"))
                return GroupGems;
            if ((cat & ItemCategory.Gem) != 0)
                return GroupGems;
            if (nl.Contains("radium") || pl.Contains("radium"))
                return GroupIngots;

            if (pl.Contains("nethershard") || nl.Contains("stygian shard"))
                return GroupAlchemy;
            if (IsHeartAlchemyName(n, p))
                return GroupAlchemy;
            if (IsAlchemyReagentName(n, p))
                return GroupAlchemy;
            if (IsExplosiveOrEmp(n, p))
                return GroupAlchemy;

            if ((cat & ItemCategory.Armor) != 0)
                return GroupArmor;
            if ((cat & ItemCategory.Jewel) != 0)
                return GroupJewels;
            if ((cat & ItemCategory.Magic) != 0)
                return GroupMagic;

            if ((cat & ItemCategory.Fish) != 0 || pl.Contains("item_ingredient_fish") || pl.Contains("_fish_"))
            {
                if (nl.Contains("fish bone") || nl.Contains("fishbone") || pl.Contains("fishbone"))
                    return GroupBones;
                if (nl.Contains("fish oil") || pl.Contains("fishoil"))
                    return GroupMinerals;
                return GroupFish;
            }

            if (IsPlantSeed(p, n))
                return GroupSeeds;

            if (nl.Contains("shroom") || nl.Contains("mushroom") || nl.Contains("clarion")
                || pl.Contains("shroom") || pl.Contains("hellsclarion") || pl.Contains("clarion"))
                return GroupMushrooms;

            if (nl.Contains("hide") || nl.Contains("leather") || pl.Contains("hide") || pl.Contains("leather"))
                return GroupHides;

            if (nl.Contains("bone") || pl.Contains("bone"))
                return GroupBones;

            if (nl.EndsWith(" ore") || nl.Equals("ore") || p.Contains("Ore")
                || nl.Equals("quartz") || pl.Contains("quartz")
                || (nl.Equals("clay") || (nl.Contains("clay") && !nl.Contains("mold"))))
                return GroupOre;

            if (nl.Contains("ingot") || pl.Contains("ingot") || nl.Contains("iron bar") || pl.Contains("ironbar")
                || nl.Contains("emberglass") || pl.Contains("emberglass"))
                return GroupIngots;

            if ((cat & ItemCategory.Woodworking) != 0
                || nl.Contains("plank") || pl.Contains("plank")
                || nl.Contains("sawdust") || pl.Contains("sawdust")
                || nl.Contains("sculptured wood") || pl.Contains("sculpturedwood"))
                return GroupPlanks;
            if ((cat & ItemCategory.Lumber) != 0 || nl.Equals("lumber") || pl.Contains("item_ingredient_lumber")
                || nl.Contains("wood") || nl.Contains("oak") || pl.Contains("item_ingredient_wood") || pl.Contains("sapling"))
                return GroupWood;

            if ((cat & ItemCategory.Stone) != 0 || nl.Equals("stone") || nl.Contains("stone brick")
                || nl.Contains("stone dust") || pl.Contains("stonebrick") || pl.Contains("stonedust")
                || pl.Contains("ingredient_stone"))
                return GroupStone;
 

            if ((cat & ItemCategory.Tailoring) != 0)
                return GroupTailoring;

            if (nl.Contains("cotton") || nl.Contains("thread"))
                return GroupTailoring;
            if ((cat & ItemCategory.Herb) != 0 || (cat & ItemCategory.Flower) != 0)
                return GroupFlowers;
            if ((cat & ItemCategory.Alchemy) != 0)
                return GroupAlchemy;
            if ((cat & ItemCategory.BloodPotion) != 0)
                return GroupConsumables;
            if ((cat & ItemCategory.Blood) != 0)
                return GroupBlood;
            if ((cat & ItemCategory.Consumable) != 0)
                return GroupConsumables;
            if ((cat & ItemCategory.Mineral) != 0)
                return GroupMinerals;
            return null;
        }

        public static int DumpItemCatalog()
        {
            try
            {
                var rows = new List<string>();
                rows.Add("prefab_name,localized_name,guid_hash,itemcategory,itemcategory_flags,dest_group,aliases");
                rows.AddRange(catalogRows);
                var text = string.Join("\n", rows) + "\n";
                var logDir = Path.Combine(Paths.BepInExRootPath, "Log");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, "item-catalog.csv"), text);
                var srcDir = Path.Combine(Paths.BepInExRootPath, "Satisvampory-src");
                if (Directory.Exists(srcDir))
                    File.WriteAllText(Path.Combine(srcDir, "item-catalog.csv"), text);
                Core.Log.LogInfo("Item catalog dumped " + catalogRows.Count + " ItemData prefabs");
                return catalogRows.Count;
            }
            catch (Exception e)
            {
                Core.Log.LogWarning("Item catalog dump failed: " + e.Message);
                return 0;
            }
        }

        static string Csv(string s)
        {
            if (s == null) s = "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public static string NormalizeName(string name)
        {
            return FoundItemConverter.Normalize(name);
        }

        public static bool IsBuiltInName(string name)
        {
            return builtInCanonical.ContainsKey(NormalizeName(name));
        }

        public static bool TryGetBuiltInCanonical(string name, out string canonical)
        {
            return builtInCanonical.TryGetValue(NormalizeName(name), out canonical);
        }

        public static bool IsExactItemName(string name)
        {
            return FoundItemConverter.TryGetExact(name, out _);
        }

        public static IReadOnlyList<GroupMember> GetBuiltInMembers(string name)
        {
            if (builtInMembers.TryGetValue(NormalizeName(name), out var list))
                return list;
            return Array.Empty<GroupMember>();
        }

        public static int MaxStack(PrefabGUID item)
        {
            try
            {
                if (Core.GameDataSystem != null
                    && Core.GameDataSystem.ItemHashLookupMap.TryGetValue(item, out var data)
                    && data.MaxAmount > 0)
                    return data.MaxAmount;
            }
            catch { }
            try
            {
                if (Core.PrefabCollectionSystem != null
                    && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var ent)
                    && ent != Entity.Null && ent.Has<ItemData>() && Core.EntityManager.Exists(ent))
                {
                    var max = ent.Read<ItemData>().MaxAmount;
                    if (max > 0)
                        return max;
                }
            }
            catch { }
            return 1;
        }

        public static bool IsHardcodedMember(string groupName, int guidHash)
        {
            if (!TryGetBuiltInCanonical(groupName, out var canonical))
                return false;
            return builtInMembers.TryGetValue(canonical, out var list) && list.Any(m => m.GuidHash == guidHash);
        }

        public static bool IsDeletedBuiltIn(ulong ownerPlatformId, string name)
        {
            return TryGetBuiltInCanonical(name, out var canonical) &&
                   Core.PlayerSettings.IsDeletedGroup(ownerPlatformId, canonical);
        }

        public static bool HasCastleOverlay(ulong ownerPlatformId, string name)
        {
            return TryGetBuiltInCanonical(name, out var canonical) &&
                   Core.PlayerSettings.HasGroupOverlay(ownerPlatformId, canonical);
        }

        public static void EnsureCastleOverlay(ulong ownerPlatformId, string name)
        {
            if (!TryGetBuiltInCanonical(name, out var canonical))
                return;
            if (Core.PlayerSettings.IsDeletedGroup(ownerPlatformId, canonical))
                return;
            if (Core.PlayerSettings.HasGroupOverlay(ownerPlatformId, canonical))
                return;

            var members = ResolveMembers(ownerPlatformId, canonical);
            Core.PlayerSettings.WriteGroupOverlay(ownerPlatformId, canonical, members.Select(m => (m.GuidHash.ToString(), m.Name)));
        }

        public static bool TryGetStandingCastleSettingsOwner(ChatCommandContext ctx, out ulong ownerPlatformId, out string ownerName, bool replyIfMissing = true)
        {
            ownerPlatformId = 0;
            ownerName = null;

            var territoryId = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            if (territoryId < 0)
            {
                if (replyIfMissing)
                    ctx.Reply("You must stand on a castle plot to view or set its reserves and production caps.");
                return false;
            }

            var plotHeart = Core.TerritoryService.GetCastleHeart(territoryId);
            if (plotHeart == Entity.Null || !Core.EntityManager.Exists(plotHeart) || !plotHeart.Has<UserOwner>())
            {
                if (replyIfMissing)
                    ctx.Reply("You must stand on a castle plot to view or set its reserves and production caps.");
                return false;
            }

            var userOwner = plotHeart.Read<UserOwner>();
            var userEntity = userOwner.Owner.GetEntityOnServer();
            if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
            {
                if (replyIfMissing)
                    ctx.Reply("You must stand on a castle plot to view or set its reserves and production caps.");
                return false;
            }

            var user = userEntity.Read<User>();
            ownerPlatformId = user.PlatformId;
            ownerName = user.CharacterName.ToString();
            if (string.IsNullOrWhiteSpace(ownerName))
                ownerName = ownerPlatformId.ToString();
            return true;
        }

        /// <summary>
        /// CS OFF: must stand on a castle (existing).
        /// CS ON SHOW: works off-plot using the player's leftover settings.
        /// CS ON SET: if not standing, the player's own castle if they own a plot;
        /// else tell them to stand.
        /// </summary>
        public static bool TryGetCastleSettingsOwner(ChatCommandContext ctx, bool forSet, out ulong ownerPlatformId, out string ownerName, bool replyIfMissing = true)
        {
            ownerPlatformId = 0;
            ownerName = null;
            var territoryId = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            if (territoryId >= 0)
                return TryGetStandingCastleSettingsOwner(ctx, out ownerPlatformId, out ownerName, replyIfMissing);

            var player = ctx.Event.User;
            if (!Core.TerritoryService.IsClanShareOn(player))
            {
                if (replyIfMissing)
                    ctx.Reply("You must stand on a castle plot to view or set its reserves and production caps.");
                return false;
            }

            if (Core.TerritoryService.TryFindOwnedTerritory(player.PlatformId, out _, out var owner))
            {
                ownerPlatformId = owner.PlatformId;
                ownerName = owner.CharacterName.ToString();
                if (string.IsNullOrWhiteSpace(ownerName))
                    ownerName = ownerPlatformId.ToString();
                return true;
            }

            if (forSet)
            {
                if (replyIfMissing)
                    ctx.Reply("Stand on a castle to set reserve/cap, or own a castle plot.");
                return false;
            }

            ownerPlatformId = player.PlatformId;
            ownerName = player.CharacterName.ToString();
            if (string.IsNullOrWhiteSpace(ownerName))
                ownerName = ownerPlatformId.ToString();
            return true;
        }

        public static bool TryResolveGroup(ulong ownerPlatformId, string name, out string resolvedName, out bool isBuiltIn)
        {
            resolvedName = NormalizeName(name);
            isBuiltIn = TryGetBuiltInCanonical(resolvedName, out var canonical);
            if (isBuiltIn)
            {
                resolvedName = canonical;
                if (Core.PlayerSettings.IsDeletedGroup(ownerPlatformId, canonical))
                    return false;
                return true;
            }

            return Core.PlayerSettings.HasItemGroup(ownerPlatformId, resolvedName);
        }

        public static List<GroupMember> ResolveMembers(ulong ownerPlatformId, string name)
        {
            var result = new List<GroupMember>();
            var seen = new HashSet<int>();

            var key = NormalizeName(name);
            if (TryGetBuiltInCanonical(key, out var canonical))
            {
                key = canonical;
                if (Core.PlayerSettings.IsDeletedGroup(ownerPlatformId, canonical))
                    return result;

                if (Core.PlayerSettings.HasGroupOverlay(ownerPlatformId, canonical))
                {
                    foreach (var extra in Core.PlayerSettings.ListItemGroupMembers(ownerPlatformId, canonical))
                    {
                        if (!int.TryParse(extra.guid, out var hash))
                            continue;
                        if (!seen.Add(hash))
                            continue;
                        var prefab = new PrefabGUID(hash);
                        var itemName = extra.name;
                        if (string.IsNullOrEmpty(itemName))
                            itemName = prefab.PrefabName() ?? extra.guid;
                        result.Add(new GroupMember(prefab, itemName, hash));
                    }
                    return result;
                }

                foreach (var member in GetBuiltInMembers(canonical))
                {
                    if (seen.Add(member.GuidHash))
                        result.Add(member);
                }
            }

            foreach (var extra in Core.PlayerSettings.ListItemGroupMembers(ownerPlatformId, key))
            {
                if (!int.TryParse(extra.guid, out var hash))
                    continue;
                if (!seen.Add(hash))
                    continue;
                var prefab = new PrefabGUID(hash);
                var itemName = extra.name;
                if (string.IsNullOrEmpty(itemName))
                    itemName = prefab.PrefabName() ?? extra.guid;
                result.Add(new GroupMember(prefab, itemName, hash));
            }

            return result;
        }

        public static List<string> ParseItemTokens(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
                return tokens;

            var i = 0;
            while (i < input.Length)
            {
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                    i++;
                if (i >= input.Length)
                    break;

                if (input[i] == '"' || input[i] == '\'')
                {
                    var quote = input[i++];
                    var start = i;
                    while (i < input.Length && input[i] != quote)
                        i++;
                    tokens.Add(input.Substring(start, i - start));
                    if (i < input.Length)
                        i++;
                }
                else
                {
                    var start = i;
                    while (i < input.Length && !char.IsWhiteSpace(input[i]))
                        i++;
                    tokens.Add(input.Substring(start, i - start));
                }
            }

            return tokens;
        }

        public static string FormatMemberNames(IEnumerable<string> names, int maxLen = 180)
        {
            var list = string.Join(", ", names);
            if (list.Length <= maxLen)
                return list;
            var cut = list.LastIndexOf(',', maxLen);
            if (cut <= 0)
                cut = maxLen;
            return list.Substring(0, cut).TrimEnd(',', ' ') + ", ...";
        }
    }
}
