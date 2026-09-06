using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Scripting;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Dest/source naming. NS = no-share (never source or dest).
    /// Trailing '' (two apostrophes) is skip-quotes, treated like NS everywhere.
    /// Does not decide HOW MUCH to move - only WHERE an already-decided amount goes,
    /// plus same-plot self-sort of surplus above leftover.
    /// Name match: '+' AND clauses; spaces OR within a clause (group / ItemCategory / name).
    /// No '+': type-word AND fallback when a token is weapon/armor/material.
    /// Overflow/spoils/salvage/trash are dest-class, never item names. Overflow never
    /// outranks exact/category/named custom. Never drain s#/r#. Never self-sort INTO overflow.
    /// Built-in dest words (blood, stone, bone, jewel, …) match dest-group membership
    /// only — not ItemCategory flags or a substring of the item name (Blood Jewel is
    /// jewels, not blood; Miststone is not stone).
    /// Spelling fold: fiber/fibre, sulfur/sulphur, armor/armour, etc.
    /// </summary>
    internal static class StashRouting
    {
        static readonly Regex NoShareToken = new(@"\bns\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex SendRx = new(BeltTokens.Sender, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ReceiveRx = new(BeltTokens.Receiver, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public const string LabelSkipNs = "skip-NS";
        public const string LabelSkipQuotes = "skip-quotes";
        public const string SkipSuffix = "''";
        public const string LabelGeneric = "generic";
        public const string LabelNameMatch = "name-match";
        public const string LabelCategory = "category";
        public const string LabelSender = "s#";
        public const string LabelCustomLast = "custom-last";
        public const string LabelOverflow = "overflow";

        // Verified ProjectM.ItemCategory flags (ProjectM.Shared). No Material; materials→Mineral.
        static readonly HashSet<string> AndCategoryTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "weapon", "armor", "armour", "material", "mineral"
        };

        static readonly Dictionary<string, ItemCategory> CategoryByToken = new(StringComparer.OrdinalIgnoreCase)
        {
            ["weapon"] = ItemCategory.Weapon,
            ["armor"] = ItemCategory.Armor,
            ["armour"] = ItemCategory.Armor,
            ["gem"] = ItemCategory.Gem,
            ["flower"] = ItemCategory.Flower,
            ["lumber"] = ItemCategory.Lumber,
            ["stone"] = ItemCategory.Stone,
            ["bloodessence"] = ItemCategory.BloodEssence,
            ["silver"] = ItemCategory.Silver,
            ["knowledge"] = ItemCategory.Knowledge,
            ["blood"] = ItemCategory.Blood,
            ["relic"] = ItemCategory.Relic,
            ["coin"] = ItemCategory.Coin,
            ["consumable"] = ItemCategory.Consumable,
            ["herb"] = ItemCategory.Herb,
            ["bag"] = ItemCategory.Bag,
            ["saddle"] = ItemCategory.Saddle,
            ["fish"] = ItemCategory.Fish,
            ["jewel"] = ItemCategory.Jewel,
            ["alchemy"] = ItemCategory.Alchemy,
            ["scroll"] = ItemCategory.Knowledge,
            ["scrolls"] = ItemCategory.Knowledge,
            ["paper"] = ItemCategory.Knowledge,
            ["book"] = ItemCategory.Knowledge,
            ["books"] = ItemCategory.Knowledge,
            ["tailoring"] = ItemCategory.Tailoring,
            ["mineral"] = ItemCategory.Mineral,
            ["material"] = ItemCategory.Mineral,
            ["woodworking"] = ItemCategory.Woodworking,
            ["magic"] = ItemCategory.Magic,
            ["bloodpotion"] = ItemCategory.BloodPotion,
            ["soulshard"] = ItemCategory.Soulshard,
            ["soulshards"] = ItemCategory.Soulshard,
            ["shard"] = ItemCategory.Soulshard,
            ["shards"] = ItemCategory.Soulshard,
            ["bags"] = ItemCategory.Bag,
            ["saddles"] = ItemCategory.Saddle,
            ["relics"] = ItemCategory.Relic,
        };

        public static string RawName(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return "";
            // 1.0.108: trailing '+' signs are a priority boost, not part of the name.
            // 1.0.110: "--word" tokens are exclusions, not part of the name.
            return StripExclusions(StripTrailingPlus(stash.Read<NameableInteractable>().Name.ToString() ?? ""));
        }

        /// <summary>
        /// "--word" tokens on a plate: never deposit an item that word matches (alias, exact item,
        /// group word, category word, or name fragment - equipment included). Returns the words
        /// (lowercase, without the dashes); <paramref name="clean"/> is the plate without them.
        /// "Weapons --copper --iron+" -> clean "Weapons", exclusions [copper, iron].
        /// </summary>
        public static List<string> ParseExclusions(string plate, out string clean)
        {
            var words = new List<string>();
            clean = plate ?? "";
            if (string.IsNullOrWhiteSpace(plate) || plate.IndexOf("--", StringComparison.Ordinal) < 0)
                return words;
            var keep = new List<string>();
            foreach (var raw in plate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.StartsWith("--", StringComparison.Ordinal))
                {
                    var w = raw.Substring(2).Trim('+', '-', '_', '/', '|', '.', ':', ',').ToLowerInvariant();
                    if (w.Length > 0)
                        words.Add(w);
                    continue;
                }
                keep.Add(raw);
            }
            clean = string.Join(" ", keep);
            return words;
        }

        public static string StripExclusions(string plate)
        {
            ParseExclusions(plate, out var clean);
            return clean;
        }

        public static List<string> ExclusionsOf(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return new List<string>();
            return ParseExclusions(StripTrailingPlus(stash.Read<NameableInteractable>().Name.ToString() ?? ""), out _);
        }

        /// <summary>
        /// Broad match for an exclusion word: admin/essence alias, exact item name, built-in or
        /// custom group word, ItemCategory word, or a 3+ letter name fragment. Unlike dest
        /// matching this applies the fragment rule to equipment too, so "--copper" catches
        /// Copper Sword.
        /// </summary>
        public static bool ExclusionMatches(string word, PrefabGUID item, ulong ownerId)
        {
            if (string.IsNullOrEmpty(word) || item.GuidHash == 0)
                return false;
            if (ItemGroupService.TryExactItemAlias(ownerId, word, out var aliasHash))
                return aliasHash == item.GuidHash;
            var itemName = Normalize(ItemLabel(item));
            if (!string.IsNullOrEmpty(itemName) && itemName == Normalize(word))
                return true;
            TryGetItemCategory(item, out var cat);
            if (TokenMatchesItem(word, item, itemName, cat, ownerId, allowCategory: true, out _))
                return true;
            if (word.Length >= 3 && !IsAllDigits(word) && !string.IsNullOrEmpty(itemName))
            {
                foreach (var v in TokenVariants(word))
                {
                    if (v.Length >= 3 && itemName.IndexOf(v, StringComparison.Ordinal) >= 0)
                        return true;
                    foreach (var it in itemName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (VariantsOverlap(v, it))
                            return true;
                    }
                }
            }
            return false;
        }

        public static string ExcludedBy(Entity stash, PrefabGUID item, ulong ownerId)
        {
            foreach (var w in ExclusionsOf(stash))
            {
                if (ExclusionMatches(w, item, ownerId))
                    return w;
            }
            return null;
        }

        /// <summary>Count of '+' at the end of the plate (whitespace ignored). "Stone Brick R1S1++" = 2.</summary>
        public static int TrailingPlus(string plate)
        {
            if (string.IsNullOrEmpty(plate))
                return 0;
            var i = plate.Length - 1;
            var n = 0;
            while (i >= 0)
            {
                var c = plate[i];
                if (c == '+') { n++; i--; continue; }
                if (char.IsWhiteSpace(c)) { i--; continue; }
                break;
            }
            return n;
        }

        public static string StripTrailingPlus(string plate)
        {
            if (string.IsNullOrEmpty(plate))
                return plate ?? "";
            var n = TrailingPlus(plate);
            if (n == 0)
                return plate;
            return plate.TrimEnd().TrimEnd('+').TrimEnd();
        }

        /// <summary>Priority boost on the plate: number of trailing '+'.</summary>
        public static int PriorityOf(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return 0;
            return TrailingPlus(stash.Read<NameableInteractable>().Name.ToString() ?? "");
        }

        /// <summary>
        /// Dest quality name: nameplate if set, else prefab/EntityName (never entity.ToString()).
        /// Blank plate on dest-quality furniture (Jewel Storage) still matches that dest.
        /// Blank Small Chest / cabinet / bureau stay generic so covering can park there.
        /// </summary>
        public static string DestName(Entity stash)
        {
            var raw = RawName(stash);
            if (!string.IsNullOrWhiteSpace(raw))
                return raw;
            try
            {
                if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<PrefabGUID>())
                    return "";
                var n = stash.Read<PrefabGUID>().PrefabName();
                if (string.IsNullOrEmpty(n))
                    return "";
                if (n.IndexOf("GUID Not Found", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "";
                return n;
            }
            catch
            {
                return "";
            }
        }

        static readonly Regex SkipQuotesRx = new(@"^\s*'\s*'|'\s*'\s*$", RegexOptions.Compiled);

        public static bool IsSkipQuotesName(string name)
        {
            // Empty name is not skip. Two apostrophes at the START or END of the plate, with
            // optional space between them: "Lock Box''", "''Lock Box", "' ' Lock Box" (1.0.107).
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return SkipQuotesRx.IsMatch(StripTrailingPlus(name));
        }

        public static string SkipLabel(string name)
        {
            return IsSkipQuotesName(name) ? LabelSkipQuotes : LabelSkipNs;
        }

        public static bool IsNoShareName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (IsSkipQuotesName(name))
                return true;
            return NoShareToken.IsMatch(name);
        }

        public static bool IsNoShare(Entity stash) => IsNoShareName(RawName(stash));

        public static bool IsGenericName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            var t = name.Trim();
            if (t.Equals("Chest", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Container", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Empty", StringComparison.OrdinalIgnoreCase)
                || t.Equals("General", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Misc", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Miscellaneous", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Everything", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Everything Else", StringComparison.OrdinalIgnoreCase)
                || t.Equals("All", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Other", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Others", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Extra", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Dump", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Stuff", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        public static bool IsOverflowName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("overflow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsSpecialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var n = name.ToLowerInvariant();
            return n.Contains("salvage") || n.Contains("spoils") || n.Contains("brazier")
                || n.Contains("spawner") || n.Contains("trash");
        }

        public static bool IsSenderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return SendRx.IsMatch(name.ToLowerInvariant());
        }

        public static bool IsReceiverName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return ReceiveRx.IsMatch(name.ToLowerInvariant());
        }

        public static bool IsConveyorName(string name) => IsSenderName(name) || IsReceiverName(name);

        public static bool IsTreasury(Entity stash) => ClanTreasuryShare.IsTreasuryLinked(stash);

        public static string ItemLabel(PrefabGUID item)
        {
            try
            {
                var n = item.PrefabName();
                if (!string.IsNullOrEmpty(n) && n.IndexOf("GUID Not Found", StringComparison.OrdinalIgnoreCase) < 0)
                    return CleanItemName(n);
            }
            catch { }
            if (ItemGroupService.IsGreaterBloodEssence(item))
                return "Greater Blood Essence";
            return item.GuidHash.ToString();
        }

        static string CleanItemName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return "";
            var space = n.LastIndexOf(' ');
            if (space > 0)
            {
                var tail = n.Substring(space + 1).TrimStart('-');
                if (int.TryParse(tail, out _))
                    return n.Substring(0, space);
            }
            return n;
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Trim().ToLowerInvariant();
        }

        static readonly Dictionary<string, string[]> SpellingFold = BuildSpellingFold();

        static Dictionary<string, string[]> BuildSpellingFold()
        {
            string[][] groups =
            {
                new[] { "fiber", "fibre", "fibers", "fibres" },
                new[] { "sulfur", "sulphur", "sulfurs", "sulphurs" },
                new[] { "armor", "armour", "armors", "armours" },
                new[] { "gray", "grey", "grays", "greys" },
                new[] { "plow", "plough", "plows", "ploughs" },
                new[] { "defense", "defence", "defenses", "defences" },
                new[] { "aluminum", "aluminium" },
                new[] { "mold", "mould", "molds", "moulds" },
                new[] { "traveler", "traveller", "travelers", "travellers" },
                new[] { "artifact", "artefact", "artifacts", "artefacts" },
                new[] { "woolen", "woollen" },
                new[] { "jewelry", "jewellery" },
            };
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var g in groups)
            {
                foreach (var w in g)
                    map[w] = g;
            }
            return map;
        }

        static bool IsDestClassToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;
            var x = token.Trim().ToLowerInvariant();
            return x == "overflow" || x == "spoils" || x == "spoil"
                || x == "salvage" || x == "trash" || x == "storage";
        }

        public static bool IsOverflowDestName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var n = name.ToLowerInvariant();
            return n.Contains("overflow") || n.Contains("spoils") || n.Contains("salvage") || n.Contains("trash");
        }

        /// <summary>
        /// Exact/category named sink for plot self-sort: do not drain this item via self-sort.
        /// Overflow names never count as a named sink. Named sinks still feed matching stations.
        /// </summary>
        public static bool NamedSinkShouldStay(Entity stash, PrefabGUID item, ulong ownerId)
        {
            var name = DestName(stash);
            if (string.IsNullOrWhiteSpace(name) || IsOverflowDestName(name) || IsNoShareName(RawName(stash)))
                return false;
            if (item.GuidHash == 0)
                return false;
            if (ExactItemNameMatch(name, item, out _, ownerId))
                return true;
            if (CategoryMatch(name, item, ownerId, out _))
                return true;
            return false;
        }

        static List<string> TokenVariants(string token)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var t = string.IsNullOrEmpty(token) ? "" : token.Trim().ToLowerInvariant();
            if (t.Length == 0)
                return new List<string>();
            set.Add(t);
            if (SpellingFold.TryGetValue(t, out var fold))
            {
                foreach (var w in fold)
                    set.Add(w);
            }
            if (t.Length >= 4 && t.EndsWith("ies"))
            {
                var y = t.Substring(0, t.Length - 3) + "y";
                if (y.Length >= 3)
                    set.Add(y);
            }
            // Simple English plural. Do not stem glass/brass (ss). Stem length >= 3.
            if (t.Length >= 4 && t.EndsWith("s") && !t.EndsWith("ss"))
            {
                var stem = t.Substring(0, t.Length - 1);
                if (stem.Length >= 3)
                    set.Add(stem);
            }
            if (!t.EndsWith("s"))
            {
                set.Add(t + "s");
                if (t.Length >= 3 && t.EndsWith("y"))
                {
                    var prev = t[t.Length - 2];
                    if (prev != 'a' && prev != 'e' && prev != 'i' && prev != 'o' && prev != 'u')
                        set.Add(t.Substring(0, t.Length - 1) + "ies");
                }
            }
            return new List<string>(set);
        }

        static bool VariantsOverlap(string a, string b)
        {
            var av = TokenVariants(a);
            var bv = TokenVariants(b);
            foreach (var x in av)
            {
                foreach (var y in bv)
                {
                    if (x == y)
                        return true;
                }
            }
            return false;
        }

        static string StripConveyorFromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";
            var t = token.Trim().ToLowerInvariant();
            t = SendRx.Replace(t, "");
            t = ReceiveRx.Replace(t, "");
            return t.Trim(' ', '-', '_', '/', '|', '.', ':');
        }

        static string RemainingNameText(string chestName)
        {
            if (string.IsNullOrWhiteSpace(chestName))
                return "";
            var parts = new List<string>();
            foreach (var raw in chestName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                var leftover = StripConveyorFromToken(raw);
                if (leftover.Length == 0)
                    continue;
                if (leftover == "overflow" || leftover == "spoils" || leftover == "spoil"
                    || leftover == "salvage" || leftover == "trash" || leftover == "storage")
                    continue;
                parts.Add(leftover);
            }
            return string.Join(" ", parts);
        }

        static List<string> RemainingNameTokens(string chestName)
        {
            var remaining = RemainingNameText(chestName);
            if (remaining.Length == 0)
                return new List<string>();
            return new List<string>(remaining.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        static List<List<string>> ParseAndOrClauses(string remaining)
        {
            var clauses = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(remaining))
                return clauses;
            foreach (var andPart in remaining.Split('+'))
            {
                var names = new List<string>();
                foreach (var raw in andPart.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var tok = raw.Trim().Trim('-', '_', '/', '|', '.', ':');
                    if (tok.Length == 0)
                        continue;
                    names.Add(tok);
                }
                if (names.Count > 0)
                    clauses.Add(names);
            }
            return clauses;
        }

        static bool TryGetItemCategory(PrefabGUID item, out ItemCategory cat)
        {
            cat = ItemCategory.NONE;
            if (item.GuidHash == 0)
                return false;
            try
            {
                if (Core.PrefabCollectionSystem == null)
                    return false;
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
                    return false;
                if (prefab == Entity.Null || !prefab.Has<ItemData>())
                    return false;
                cat = prefab.Read<ItemData>().ItemCategory;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TokenIsAndCategory(string token)
        {
            foreach (var v in TokenVariants(token))
            {
                if (AndCategoryTokens.Contains(v))
                    return true;
            }
            return false;
        }

        static bool IsSmallMaterialsTokens(List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return false;
            var hasSmall = false;
            var hasMat = false;
            foreach (var token in tokens)
            {
                foreach (var v in TokenVariants(token))
                {
                    if (v == "small")
                        hasSmall = true;
                    if (v == "material" || v == "mineral")
                        hasMat = true;
                }
            }
            return hasSmall && hasMat;
        }

        /// <summary>Match tier for CategoryMatch specificity: 4 custom group, 3 built-in group / essence alias, 2 ItemCategory word, 1 partial item name.</summary>
        public const int TierCustomGroup = 4;
        public const int TierGroup = 3;
        public const int TierCategory = 2;
        public const int TierPartial = 1;
        public const int TierStep = 100000;

        static bool TokenMatchesItem(string token, PrefabGUID item, string itemName, ItemCategory itemCat, ulong ownerId, bool allowCategory)
            => TokenMatchesItem(token, item, itemName, itemCat, ownerId, allowCategory, out _);

        static bool TokenMatchesItem(string token, PrefabGUID item, string itemName, ItemCategory itemCat, ulong ownerId, bool allowCategory, out int tier)
            => TokenMatchesItem(token, item, itemName, itemCat, ownerId, allowCategory, false, out tier);

        /// <param name="typeWordFlagFallback">AND mode only: a type word (weapon / armor / material) that is a
        /// built-in group the item is NOT in still matches on the game's own ItemCategory flag. "Shattered Weapons"
        /// = shattered group AND Weapon flag, even though shards left the weapons group in 1.0.107. A lone
        /// "Weapons" plate never gets this fallback, so it still does not take shards.</param>
        static bool TokenMatchesItem(string token, PrefabGUID item, string itemName, ItemCategory itemCat, ulong ownerId, bool allowCategory, bool typeWordFlagFallback, out int tier)
        {
            tier = 0;
            if (string.IsNullOrEmpty(token))
                return false;
            if (IsDestClassToken(token))
                return false;
            if (ItemGroupService.TryExactItemAlias(ownerId, token, out var essenceHash))
            {
                tier = TierGroup;
                return item.GuidHash == essenceHash;
            }
            var variants = TokenVariants(token);

            // 1.0.102: custom groups first and above built-ins. Exact normalized name only
            // (plural / spelling fold), never a substring.
            // 1.0.126: island-wide under ClanShare - a chest named for a clan mate's custom group
            // matches from any castle on the island (standing owner first).
            foreach (var groupOwner in Core.TerritoryService.GetIslandOwnerIds(ownerId))
            {
                foreach (var (name, _) in Core.PlayerSettings.ListCustomGroups(groupOwner))
                {
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var gNorm = ItemGroupService.NormalizeName(name);
                    if (gNorm.Length < 3)
                        continue;
                    if (!VariantsOverlap(token, gNorm))
                        continue;
                    foreach (var m in ItemGroupService.ResolveMembers(groupOwner, name))
                    {
                        if (m.GuidHash == item.GuidHash)
                        {
                            tier = TierCustomGroup;
                            return true;
                        }
                    }
                    // The token IS this custom group's name and the item is not in it: no fallback
                    // to a built-in or partial-name match on the same word.
                    return false;
                }
            }

            var tokenIsDestGroup = false;
            foreach (var v in variants)
            {
                if (!ItemGroupService.TryGetBuiltInCanonical(v, out var canonical))
                    continue;
                tokenIsDestGroup = true;
                if (ItemGroupService.IsDeletedBuiltIn(ownerId, canonical))
                    continue;
                foreach (var m in ItemGroupService.ResolveMembers(ownerId, canonical))
                {
                    if (m.GuidHash == item.GuidHash)
                    {
                        tier = TierGroup;
                        return true;
                    }
                }
                // "Wood Stone Bone" should take planks, not only raw logs.
                if (canonical == ItemGroupService.GroupWood)
                {
                    foreach (var m in ItemGroupService.ResolveMembers(ownerId, ItemGroupService.GroupPlanks))
                    {
                        if (m.GuidHash == item.GuidHash)
                        {
                            tier = TierGroup;
                            return true;
                        }
                    }
                }
            }
            // "Blood" is the blood dest group, not a substring of "Blood Jewel".
            // "Stone" is stone dests, not Miststone. Membership already failed.
            if (tokenIsDestGroup)
            {
                // 1.0.124: AND-mode type word falls back to the ItemCategory flag.
                if (typeWordFlagFallback && itemCat != ItemCategory.NONE && TokenIsAndCategory(token))
                {
                    foreach (var v in variants)
                    {
                        if (CategoryByToken.TryGetValue(v, out var f) && f != ItemCategory.NONE && (itemCat & f) != 0)
                        {
                            tier = TierCategory;
                            return true;
                        }
                    }
                }
                return false;
            }

            if (allowCategory && itemCat != ItemCategory.NONE)
            {
                foreach (var v in variants)
                {
                    // "Wood" chests: logs (Lumber) and planks (Woodworking). CategoryByToken
                    // has lumber/woodworking but not the word players actually write.
                    if (v == "wood" || v == "woods" || v == "wooden")
                    {
                        if ((itemCat & ItemCategory.Lumber) != 0 || (itemCat & ItemCategory.Woodworking) != 0)
                        {
                            tier = TierCategory;
                            return true;
                        }
                    }
                    if (!CategoryByToken.TryGetValue(v, out var flag))
                        continue;
                    if (flag != ItemCategory.NONE && (itemCat & flag) != 0)
                    {
                        tier = TierCategory;
                        return true;
                    }
                }
            }

            // 1.0.104: equipment never matches by a fragment of its name. Materials are named
            // for what they are ("Copper Ingot"); equipment is named for what it is made of
            // ("Copper Sword", legendary "Merciless Iron Crossbow"), so "Copper Iron" must not
            // take weapons. Equipment still matches exact name, group word, category word, or
            // a custom group.
            if (IsEquipmentCategory(itemCat))
                return false;

            var itemTokens = string.IsNullOrEmpty(itemName)
                ? Array.Empty<string>()
                : itemName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var v in variants)
            {
                // 1.0.101: a bare number or a 1-2 letter token is not a partial item name.
                // "Alch 1" must not match every "... Tier 1 Shattered" weapon via the "1".
                if (v.Length < 3 || IsAllDigits(v))
                    continue;
                if (!string.IsNullOrEmpty(itemName) && itemName.IndexOf(v, StringComparison.Ordinal) >= 0)
                {
                    tier = TierPartial;
                    return true;
                }
                foreach (var it in itemTokens)
                {
                    if (VariantsOverlap(v, it))
                    {
                        tier = TierPartial;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool IsEquipmentCategory(ItemCategory cat)
            => (cat & (ItemCategory.Weapon | ItemCategory.Armor | ItemCategory.Magic)) != 0;

        static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    return false;
            }
            return true;
        }

        public static bool ExactItemNameMatch(string chestName, PrefabGUID item, out int specificity)
            => ExactItemNameMatch(chestName, item, out specificity, 0);

        /// <param name="ownerId">Castle owner whose castle aliases apply (0 = server aliases only).</param>
        public static bool ExactItemNameMatch(string chestName, PrefabGUID item, out int specificity, ulong ownerId)
        {
            specificity = 0;
            if (string.IsNullOrWhiteSpace(chestName) || IsGenericName(chestName))
                return false;
            var itemName = Normalize(ItemLabel(item));
            if (itemName.Length < 3)
                return false;
            var remaining = RemainingNameText(chestName);
            if (remaining.Length == 0)
                return false;
            // '+' names are AND expressions, not exact item titles.
            if (remaining.IndexOf('+') >= 0)
                return false;
            var tokens = RemainingNameTokens(chestName);
            if (tokens.Count == 0)
                return false;
            if (ItemGroupService.TryExactItemAlias(ownerId, remaining, out var essenceHash)
                || (tokens.Count == 1 && ItemGroupService.TryExactItemAlias(ownerId, tokens[0], out essenceHash)))
            {
                if (item.GuidHash == essenceHash)
                {
                    specificity = 22;
                    return true;
                }
                return false;
            }
            if (remaining.IndexOf(itemName, StringComparison.Ordinal) >= 0)
            {
                specificity = itemName.Length;
                return true;
            }
            var compactItem = itemName.Replace(" ", "");
            var compactChest = remaining.Replace(" ", "");
            if (compactItem.Length >= 3 && compactChest.IndexOf(compactItem, StringComparison.Ordinal) >= 0)
            {
                specificity = compactItem.Length;
                return true;
            }
            var itemTokens = itemName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            // "Blood" is Blood Essence, not Greater/Primal. Rank above Alchemy category.
            if (tokens.Count == 1 && itemTokens.Length == 2
                && itemTokens[0] == "blood" && itemTokens[1] == "essence"
                && VariantsOverlap(tokens[0], "blood"))
            {
                specificity = itemName.Length + 20;
                return true;
            }
            if (tokens.Count == itemTokens.Length)
            {
                var all = true;
                var spec = 0;
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (!VariantsOverlap(tokens[i], itemTokens[i]))
                    {
                        all = false;
                        break;
                    }
                    spec += itemTokens[i].Length;
                }
                if (all)
                {
                    specificity = spec > 0 ? spec : itemName.Length;
                    return true;
                }
            }
            // Spelling-fold exact: every item token overlaps some remaining chest token
            // (Sulfur & Misc vs Sulphur; Gem Dust vs gemdust). Extra chest tokens ok.
            if (itemTokens.Length > 0 && tokens.Count > 0)
            {
                var allHit = true;
                foreach (var it in itemTokens)
                {
                    var ok = false;
                    foreach (var tok in tokens)
                    {
                        if (IsDestClassToken(tok))
                            continue;
                        if (!VariantsOverlap(tok, it))
                            continue;
                        ok = true;
                        break;
                    }
                    if (!ok)
                    {
                        allHit = false;
                        break;
                    }
                }
                if (allHit)
                {
                    specificity = itemName.Length;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Name match after stripping whole s#/r# tokens and extra overflow/spoils/salvage/trash words.
        /// '+' splits AND-clauses. Spaces split OR names inside a clause (plurals fold).
        /// Item matches if EVERY '+' clause matches; a clause matches if ANY space-separated name
        /// matches (dest group, ItemCategory token, or item-name/plural).
        /// No '+': type-word AND fallback when a token is weapon/armor/material.
        /// If '+' is present, '+' is the only AND; do not also AND on type-word across the string.
        /// Specificity: matching clause/token count, then matched length. Exact still ranks above this.
        /// </summary>
        public static bool CategoryMatch(string chestName, PrefabGUID item, ulong ownerPlatformId, out int specificity)
            => CategoryMatch(chestName, item, ownerPlatformId, out specificity, null);

        /// <param name="matched">When non-null, receives every matching token with its tier (for `.s why`).</param>
        public static bool CategoryMatch(string chestName, PrefabGUID item, ulong ownerPlatformId, out int specificity, List<(string token, int tier)> matched)
        {
            specificity = 0;
            if (string.IsNullOrWhiteSpace(chestName) || IsGenericName(chestName))
                return false;
            if (ExactItemNameMatch(chestName, item, out _, ownerPlatformId))
                return false;

            var remaining = RemainingNameText(chestName);
            if (remaining.Length == 0)
                return false;

            var itemName = Normalize(ItemLabel(item));
            TryGetItemCategory(item, out var cat);

            var flat = RemainingNameTokens(chestName);
            if (IsSmallMaterialsTokens(flat))
            {
                // 1.6.1.44: prefab "Small Material Storage" must not steal all minerals from Ore S2.
                // Require 'small' in the item name AND Mineral AND not Weapon (spec locked 2026-08-31).
                if ((cat & ItemCategory.Weapon) != 0)
                    return false;
                if ((cat & ItemCategory.Mineral) == 0)
                    return false;
                if (string.IsNullOrEmpty(itemName) || itemName.IndexOf("small", StringComparison.Ordinal) < 0)
                    return false;
                var smallLen = 0;
                foreach (var token in flat)
                    smallLen += token.Length;
                specificity = flat.Count * 1000 + smallLen;
                return true;
            }

            var hasPlus = remaining.IndexOf('+') >= 0;
            var clauses = ParseAndOrClauses(remaining);
            if (clauses.Count == 0)
                return false;

            // 1.0.96: a group word (built-in dest group, custom group, essence alias) outranks an
            // ItemCategory word, which outranks a partial item-name hit. Tier is the best matching
            // token's tier; token count and length break ties inside a tier.
            var bestTier = 0;
            if (hasPlus)
            {
                // '+' is the only AND. Each clause is OR of space-separated names.
                var matchedLen = 0;
                foreach (var clause in clauses)
                {
                    var clauseHit = false;
                    var clauseLen = 0;
                    foreach (var token in clause)
                    {
                        if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true, out var tier))
                            continue;
                        clauseHit = true;
                        clauseLen += token.Length;
                        matched?.Add((token, tier));
                        if (tier > bestTier)
                            bestTier = tier;
                    }
                    if (!clauseHit)
                        return false;
                    matchedLen += clauseLen;
                }
                specificity = bestTier * TierStep + clauses.Count * 1000 + matchedLen;
                return true;
            }

            // No '+': flatten to the single space-separated clause.
            var tokens = clauses[0];

            var andMode = false;
            foreach (var token in tokens)
            {
                if (TokenIsAndCategory(token))
                {
                    andMode = true;
                    break;
                }
            }

            if (andMode)
            {
                var totalLen = 0;
                foreach (var token in tokens)
                {
                    // 1.0.125: the flag fallback needs a second word ("Shattered Weapons"); a lone "Weapons" stays group-only.
                    if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true, typeWordFlagFallback: tokens.Count > 1, out var tier))
                        return false;
                    totalLen += token.Length;
                    matched?.Add((token, tier));
                    if (tier > bestTier)
                        bestTier = tier;
                }
                specificity = bestTier * TierStep + tokens.Count * 1000 + totalLen;
                return true;
            }

            var matchedCount = 0;
            var matchedLenOr = 0;
            foreach (var token in tokens)
            {
                if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true, out var tier))
                    continue;
                matchedCount++;
                matchedLenOr += token.Length;
                matched?.Add((token, tier));
                if (tier > bestTier)
                    bestTier = tier;
            }
            if (matchedCount == 0)
                return false;
            specificity = bestTier * TierStep + matchedCount * 1000 + matchedLenOr;
            return true;
        }

        static string TierWord(int tier) => tier switch
        {
            TierCustomGroup => "custom group",
            TierGroup => "group word",
            TierCategory => "item category",
            TierPartial => "partial name",
            _ => "match"
        };

        static string DescribeMatches(List<(string token, int tier)> matched)
        {
            if (matched == null || matched.Count == 0)
                return "";
            var parts = new List<string>();
            foreach (var (token, tier) in matched)
                parts.Add($"'{token}' {TierWord(tier)}");
            return string.Join(" + ", parts);
        }

        /// <summary>
        /// `.s why`: RankDeposit plus a one-line human reason for the class it landed in.
        /// </summary>
        public static string ExplainRank(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem, int standingPlot, out DepositRank rank)
        {
            rank = RankDeposit(stash, item, ownerId, hasItem, standingPlot);
            var plate = RawName(stash);
            var destName = DestName(stash);
            var matchName = RankMatchName(plate, destName);
            if (rank.Class == ClassRestricted)
            {
                AcceptsItem(stash, item, out var why);
                var core = RankDepositCore(stash, item, ownerId, hasItem, standingPlot);
                return $"{why} (name would be c{core.Class} {core.Label})";
            }
            if (rank.Class == ClassExcluded)
            {
                var word = ExcludedBy(stash, item, ownerId);
                return $"excluded: plate has --{word} and it matches this item";
            }
            if (rank.Class < 0)
            {
                var core = RankDepositCore(stash, item, ownerId, hasItem, standingPlot);
                var baseWhy = ExplainCore(core, plate, destName, matchName, item, ownerId);
                return $"priority +{-rank.Class} (trailing '+'), base c{core.Class} {baseWhy}";
            }
            return ExplainCore(rank, plate, destName, matchName, item, ownerId);
        }

        static string ExplainCore(DepositRank rank, string plate, string destName, string matchName, PrefabGUID item, ulong ownerId)
        {
            switch (rank.Class)
            {
                case 99:
                    return IsSkipQuotesName(plate) ? "skip: trailing '' on the name" : "skip: NS on the name";
                case 90:
                    return "special chest (salvage/spoils/brazier/spawner/trash), not a dump dest";
                case 5:
                    return "overflow: last resort only";
            }
            var sender = IsSenderName(plate);
            var exact = item.GuidHash != 0 && ExactItemNameMatch(matchName, item, out _, ownerId);
            var matched = new List<(string token, int tier)>();
            var category = !exact && item.GuidHash != 0 && CategoryMatch(matchName, item, ownerId, out _, matched);
            var unnamed = IsUnnamedDest(plate, destName);
            var detail = exact ? "exact item name" : category ? DescribeMatches(matched) : unnamed ? "generic plate" : "no name match";
            switch (rank.Class)
            {
                case 0:
                    return $"s# sender: {detail}" + (!exact && !category ? " (seeded: holds the item)" : "");
                case 1:
                    return "name-match: exact item name";
                case 2:
                    return "category: " + detail;
                case 3:
                    return unnamed ? "generic plate, already holds the item" : "custom name, no match, but already holds the item";
                case 4:
                    return sender ? "s# but empty of this item and no name match (seed it to make it class 0)" : "generic plate, empty of this item";
                case 6:
                    return "custom name, no match, empty — not a dest";
            }
            return detail;
        }

        public struct DepositRank : IComparable<DepositRank>
        {
            // Incoming dest: 0 s# (seeded or name-matches), 1 exact, 2 category, 3 seeded (generic or custom, no name match), 4 empty generic, 5 overflow last-resort, 6 empty-custom, 90 special, 99 NS
            public int Class;
            public int Spec;
            public bool Seeded;
            public bool Treasury;
            public bool Local;
            public string Label;

            public int CompareTo(DepositRank other)
            {
                var c = Class.CompareTo(other.Class);
                if (c != 0) return c;
                c = other.Spec.CompareTo(Spec);
                if (c != 0) return c;
                c = other.Seeded.CompareTo(Seeded);
                if (c != 0) return c;
                c = other.Treasury.CompareTo(Treasury);
                if (c != 0) return c;
                return other.Local.CompareTo(Local);
            }

            public bool IsDepositUsable => Class <= 5;
        }

        public static bool IsUnnamedOrGeneric(string name)
        {
            if (IsOverflowDestName(name))
                return false;
            if (IsGenericName(name))
                return true;
            if (IsFurnitureChestName(name))
                return true;
            return string.IsNullOrEmpty(RemainingNameText(name));
        }

        /// <summary>
        /// Prefab/nameplate that is only chest size words ("Small Chest") is generic.
        /// </summary>
        static bool IsFurnitureChestName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || IsOverflowDestName(name))
                return false;
            var tokens = RemainingNameTokens(name);
            if (tokens.Count == 0)
                return true;
            var hasChestWord = false;
            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                if (t == "chest" || t == "container" || t == "stash" || t == "inventory")
                {
                    hasChestWord = true;
                    continue;
                }
                if (t == "storage" || t == "small" || t == "large" || t == "medium" || t == "tiny"
                    || t == "big" || t == "the" || t == "of" || t == "a" || t == "an" || t == "and")
                    continue;
                return false;
            }
            return hasChestWord;
        }

        /// <summary>
        /// Rank unnamed from the nameplate. Blank plate is still unnamed for unmatched
        /// items so covering can park on unlabeled treasury. Dest-quality prefab names
        /// (Jewel Storage) still category-match via RankMatchName.
        /// </summary>
        internal static bool IsUnnamedDest(string plate, string destName)
        {
            if (IsOverflowDestName(plate) || IsOverflowDestName(destName))
                return false;
            if (string.IsNullOrWhiteSpace(plate))
                return true;
            return IsUnnamedOrGeneric(plate) || IsUnnamedOrGeneric(destName);
        }

        internal static string SelfTestDest()
        {
            var blankJewel = IsUnnamedDest("", "Jewel Storage");
            var blankEmpty = IsUnnamedDest("", "");
            var blankFurniture = IsUnnamedDest("", "Small Chest");
            var namedLeather = IsUnnamedDest("Leather", "Leather");
            var overflow = IsUnnamedDest("", "overflow");
            var namedJewel = IsUnnamedDest("jewels", "Jewel Storage");
            var blankMatch = RankMatchName("", "Jewel Storage");
            var blankChestMatch = RankMatchName("", "Small Chest");
            var blankBureauMatch = RankMatchName("", "Bureau");
            var namedMatch = RankMatchName("jewels", "Jewel Storage");
            var matchOk = blankMatch == "Jewel Storage" && blankChestMatch.Length == 0
                && blankBureauMatch.Length == 0 && namedMatch == "Jewel Storage";
            var emptyGeneric = IsGenericName("Empty") && IsUnnamedDest("Empty", "Jewel Storage");
            var blankClass = RankClassUnmatched("", "Jewel Storage", false);
            var emptyClass = RankClassUnmatched("Empty", "Jewel Storage", false);
            var leatherClass = RankClassUnmatched("Leather", "Leather", false);
            var classOk = blankClass == 4 && emptyClass == 4 && leatherClass == 6;
            var generalClass = RankClassUnmatched("General", "Jewel Storage", false);
            var elseClass = RankClassUnmatched("Everything Else", "Jewel Storage", false);
            var catchAll = IsGenericName("General") && IsGenericName("Everything Else") && generalClass == 4 && elseClass == 4;
            var passBlank = SourcePassFromName("", false);
            var passLeather = SourcePassFromName("Leather", false);
            var passBelt = SourcePassFromName("Planks R0S0", true);
            var passOre = SourcePassFromName("Ore & Ingots", false);
            var passOverflow = SourcePassFromName("Spoils Overflow", false);
            var passOk = passBlank == 0 && passLeather == 1 && passBelt == 2 && passOre == 1 && passOverflow == 0;
            var beltNotDest = IsConveyorName("Planks R0S0") && !IsConveyorName("Ore & Ingots") && !IsConveyorName("General");
            var ok = blankJewel && blankEmpty && blankFurniture && !namedLeather && !overflow && !namedJewel && matchOk && emptyGeneric && classOk && catchAll && passOk && beltNotDest;
            return "{\"blankJewel\":" + (blankJewel ? "true" : "false")
                + ",\"blankEmpty\":" + (blankEmpty ? "true" : "false")
                + ",\"blankFurniture\":" + (blankFurniture ? "true" : "false")
                + ",\"namedLeather\":" + (namedLeather ? "true" : "false")
                + ",\"overflow\":" + (overflow ? "true" : "false")
                + ",\"namedJewel\":" + (namedJewel ? "true" : "false")
                + ",\"blankMatchJewel\":" + (blankMatch == "Jewel Storage" ? "true" : "false")
                + ",\"blankChestGeneric\":" + (blankChestMatch.Length == 0 ? "true" : "false")
                + ",\"blankBureauGeneric\":" + (blankBureauMatch.Length == 0 ? "true" : "false")
                + ",\"namedMatchJewel\":" + (namedMatch == "Jewel Storage" ? "true" : "false")
                + ",\"emptyGeneric\":" + (emptyGeneric ? "true" : "false")
                + ",\"blankClass\":" + blankClass
                + ",\"emptyClass\":" + emptyClass
                + ",\"leatherClass\":" + leatherClass
                + ",\"generalClass\":" + generalClass
                + ",\"elseClass\":" + elseClass
                + ",\"passBlank\":" + passBlank
                + ",\"passLeather\":" + passLeather
                + ",\"passBelt\":" + passBelt
                + ",\"passOre\":" + passOre
                + ",\"passOverflow\":" + passOverflow
                + ",\"beltNotDest\":" + (beltNotDest ? "true" : "false")
                + ",\"ok\":" + (ok ? "true" : "false") + "}";
        }

        /// <summary>
        /// Dest class when exact/category miss. Blank plate stays class 3 unnamed even
        /// when the prefab is Jewel Storage; jewels already hit category via RankMatchName.
        /// </summary>
        internal static int RankClassUnmatched(string plate, string destName, bool hasItem)
        {
            if (IsOverflowDestName(plate) || IsOverflowDestName(destName))
                return 5;
            // 1.0.99: a chest that already holds the item (3) beats an empty generic plate (4).
            if (IsUnnamedDest(plate, destName))
                return hasItem ? 3 : 4;
            return hasItem ? 3 : 6;
        }

        /// <summary>
        /// Exact/category matching name. Typed plates always count. Blank plate inherits
        /// dest-quality prefab names (Jewel Storage, Woodworking Storage) so those boxes
        /// are jewel/plank dests without renaming. Small Chest / cabinet / bureau stay
        /// generic.
        /// </summary>
        internal static string RankMatchName(string plate, string destName)
        {
            if (!string.IsNullOrWhiteSpace(plate))
                return destName ?? "";
            if (string.IsNullOrWhiteSpace(destName) || PrefabNameIsGenericDest(destName))
                return "";
            return destName;
        }

        /// <summary>
        /// Unlabeled furniture that is only a box (Small Chest, Cabinet, Bureau, Small
        /// Storage). A dest word in the prefab name (Jewel Storage) is not generic.
        /// </summary>
        static bool PrefabNameIsGenericDest(string destName)
        {
            if (IsGenericName(destName) || IsOverflowDestName(destName) || IsFurnitureChestName(destName))
                return true;
            var tokens = RemainingNameTokens(destName);
            if (tokens.Count == 0)
                return true;
            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                if (t == "chest" || t == "container" || t == "stash" || t == "inventory"
                    || t == "storage" || t == "cabinet" || t == "bureau" || t == "wardrobe"
                    || t == "cupboard" || t == "locker" || t == "crate" || t == "trunk"
                    || t == "small" || t == "large" || t == "medium" || t == "tiny"
                    || t == "big" || t == "the" || t == "of" || t == "a" || t == "an" || t == "and")
                    continue;
                return false;
            }
            return true;
        }

        // Priority '+' encodes the base class inside Spec so boosted chests still order by
        // their underlying match quality. Base Spec never reaches 1e7 (exact bonus is 1e6).
        const int PriorityClassStep = 10000000;

        /// <summary>
        /// 1.0.108: a plate ending in one or more '+' outranks everything, s# included, for any
        /// item it matches or already holds (base class 0-3). More '+' = higher. Class becomes
        /// -N. A '+' on an empty generic / unmatched / overflow chest does nothing.
        /// </summary>
        public const int ClassExcluded = 98;
        public const string LabelExcluded = "excluded";
        public const int ClassRestricted = 97;
        public const string LabelRestricted = "restricted";

        /// <summary>
        /// 1.0.119: does the furniture itself accept this item? Vanilla restricted storage
        /// (Consumables, Jewel Storage, Coin Storage, ...) rejects other categories at TryAddItem,
        /// so ranking such a chest first only makes the stash skip it silently.
        /// </summary>
        public static bool AcceptsItem(Entity stash, PrefabGUID item, out string reason)
        {
            reason = null;
            if (stash == Entity.Null || item.GuidHash == 0 || !Core.EntityManager.Exists(stash))
                return true;
            try
            {
                var sgm = Core.ServerGameManager;
                if (!sgm.TryGetBuffer<InventoryInstanceElement>(stash, out var instances) || instances.Length == 0)
                    return true;
                ItemData data = default;
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
                    data = prefab.Read<ItemData>();
                var soulshard = data.ItemCategory == ItemCategory.Soulshard;
                string why = null;
                foreach (var inst in instances)
                {
                    if (inst.RestrictedType != PrefabGUID.Empty && inst.RestrictedType != data.ItemTypeGUID)
                    {
                        why = "container only takes " + ItemLabel(inst.RestrictedType);
                        continue;
                    }
                    if (inst.RestrictedCategory != 0 && (inst.RestrictedCategory & (long)data.ItemCategory) == 0)
                    {
                        why = "container restricted to " + ((ItemCategory)inst.RestrictedCategory).ToString().ToLowerInvariant();
                        continue;
                    }
                    if (soulshard && inst.RestrictedCategory == 0)
                    {
                        why = "soul shards need a restricted container";
                        continue;
                    }
                    return true;
                }
                reason = why ?? "container rejects this item";
                return false;
            }
            catch
            {
                return true;
            }
        }

        public static DepositRank RankDeposit(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem, int standingPlot = -1)
        {
            var rank = RankDepositCore(stash, item, ownerId, hasItem, standingPlot);
            // 1.0.119: furniture that cannot hold the item is never a dest, whatever its name.
            if (rank.Class < 90 && !AcceptsItem(stash, item, out _))
            {
                rank.Class = ClassRestricted;
                rank.Spec = 0;
                rank.Label = LabelRestricted;
                return rank;
            }
            // 1.0.110: "--word" on the plate: never a dest for items that word matches.
            if (rank.Class < 90 && ExcludedBy(stash, item, ownerId) != null)
            {
                rank.Class = ClassExcluded;
                rank.Spec = 0;
                rank.Label = LabelExcluded;
                return rank;
            }
            var boost = PriorityOf(stash);
            if (boost <= 0 || rank.Class < 0 || rank.Class > 3)
                return rank;
            rank.Spec = (9 - rank.Class) * PriorityClassStep + rank.Spec;
            rank.Class = -boost;
            rank.Label = rank.Label + "+" + boost;
            return rank;
        }

        static DepositRank RankDepositCore(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem, int standingPlot = -1)
        {
            var plate = RawName(stash);
            var destName = DestName(stash);
            var name = destName;
            var matchName = RankMatchName(plate, destName);
            var local = standingPlot >= 0 && Core.TerritoryService.GetTerritoryId(stash) == standingPlot;
            var rank = new DepositRank { Class = 99, Label = SkipLabel(plate), Seeded = hasItem, Treasury = IsTreasury(stash), Local = local };
            if (IsNoShareName(plate))
                return rank;

            var overflowDest = IsOverflowDestName(name) || IsOverflowDestName(plate);
            // Special identity is the nameplate. Blank plate does not inherit a prefab "salvage" token.
            if ((IsSpecialName(plate) || (!string.IsNullOrWhiteSpace(plate) && IsSpecialName(name))) && !overflowDest)
            {
                rank.Class = 90;
                rank.Label = LabelCustomLast;
                return rank;
            }

            var specExact = 0;
            var specCat = 0;
            var exact = !overflowDest && item.GuidHash != 0 && ExactItemNameMatch(matchName, item, out specExact, ownerId);
            var category = !overflowDest && !exact && item.GuidHash != 0 && CategoryMatch(matchName, item, ownerId, out specCat);
            var unnamed = !overflowDest && IsUnnamedDest(plate, name);

            // 1.0.99: s# is class 0 when it already holds the item (seeded) OR the plate name
            // matches (exact / category). "Alch S5" holding Grave Dust takes more Grave Dust;
            // an empty "Blood Essence S5R5" still beats "Blood". Overflow names never class 0.
            // s# lives on the nameplate only - never a prefab name.
            if (!overflowDest && IsSenderName(plate) && (hasItem || exact || category))
            {
                rank.Class = 0;
                rank.Spec = exact ? specExact + 10 * TierStep : (category ? specCat + 10000 : (hasItem ? 1 : 0));
                rank.Label = LabelSender;
                return rank;
            }
            if (exact)
            {
                rank.Class = 1;
                rank.Spec = specExact;
                rank.Label = LabelNameMatch;
                return rank;
            }
            if (category)
            {
                rank.Class = 2;
                rank.Spec = specCat;
                rank.Label = LabelCategory;
                return rank;
            }
            var unmatched = RankClassUnmatched(plate, destName, hasItem);
            rank.Class = unmatched;
            if (unmatched == 3)
            {
                // Seeded: generic seeded slightly prefers over custom seeded, then treasury.
                rank.Spec = (unnamed ? 2 : 0) + (rank.Treasury ? 1 : 0);
                rank.Label = unnamed ? LabelGeneric : LabelCustomLast;
                return rank;
            }
            if (unmatched == 4)
            {
                rank.Spec = rank.Treasury ? 1 : 0;
                rank.Label = LabelGeneric;
                return rank;
            }
            if (unmatched == 5)
            {
                rank.Label = LabelOverflow;
                return rank;
            }
            rank.Label = LabelCustomLast;
            return rank;
        }

        public struct SortRank : IComparable<SortRank>
        {
            // Same-plot self-sort dest quality: 0 exact, 1 category, 3 generic/custom source (overflow is NEVER a dest)
            public int Class;
            public int Spec;
            public bool Seeded;
            public bool UsableDest;
            public bool UsableSource;
            public string Label;

            public int CompareTo(SortRank other)
            {
                var c = Class.CompareTo(other.Class);
                if (c != 0) return c;
                c = other.Spec.CompareTo(Spec);
                if (c != 0) return c;
                return other.Seeded.CompareTo(Seeded);
            }

            public bool StrictlyBetterDestThan(SortRank source)
            {
                if (!UsableDest)
                    return false;
                if (Class < source.Class)
                    return true;
                if (Class == source.Class && Spec > source.Spec)
                    return true;
                return false;
            }
        }

        public static SortRank RankSort(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem)
        {
            var plate = RawName(stash);
            var destName = DestName(stash);
            var name = destName;
            var matchName = RankMatchName(plate, destName);
            var r = new SortRank { Class = 9, Label = SkipLabel(plate), Seeded = hasItem };
            if (IsNoShareName(plate))
                return r;
            // Never drain s#/r#. Overflow/spoils are last-resort RR dests, not self-sort dests.
            if (IsConveyorName(plate) || IsSpecialName(plate) || IsOverflowDestName(plate))
            {
                r.Class = 8;
                r.UsableSource = false;
                r.UsableDest = false;
                r.Label = IsOverflowDestName(plate) ? LabelOverflow : (IsConveyorName(plate) ? LabelSender : LabelCustomLast);
                return r;
            }
            if (item.GuidHash != 0 && ExactItemNameMatch(matchName, item, out var specExact, ownerId))
            {
                r.Class = 0;
                r.Spec = specExact;
                r.UsableDest = true;
                r.UsableSource = true;
                r.Label = LabelNameMatch;
                return r;
            }
            if (item.GuidHash != 0 && CategoryMatch(matchName, item, ownerId, out var specCat))
            {
                r.Class = 1;
                r.Spec = specCat;
                r.UsableDest = true;
                r.UsableSource = true;
                r.Label = LabelCategory;
                return r;
            }
            r.Class = 3;
            r.UsableDest = false;
            r.UsableSource = true;
            r.Label = (string.IsNullOrWhiteSpace(plate) || IsGenericName(plate)) ? LabelGeneric : LabelCustomLast;
            return r;
        }

        /// <summary>
        /// lend/.pull source: -1 NS, 0 unnamed/generic/overflow, 1 named (even on treasury floor),
        /// 2 s#/r# last-resort. Named treasury dests are not pass 0.
        /// </summary>
        internal static int SourcePassFromName(string plate, bool conveyor)
        {
            if (IsNoShareName(plate))
                return -1;
            if (conveyor)
                return 2;
            if (string.IsNullOrWhiteSpace(plate) || IsGenericName(plate) || IsUnnamedOrGeneric(plate)
                || IsOverflowDestName(plate))
                return 0;
            return 1;
        }

        /// <summary>
        /// `.pull` / craft-pull source order: the inverse of dest ranking for this item, so the
        /// chests the item would be stashed INTO are drained LAST (overflow, empty generic, seeded
        /// generic/custom, category, exact, then s#). Never castle hearts (heart fuel is not a
        /// store), never stations, never NS / skip-quotes.
        /// </summary>
        public static List<Entity> OrderPullSources(IEnumerable<Entity> stashes, PrefabGUID item, ulong ownerId)
        {
            var ranked = new List<(DepositRank rank, int order, Entity stash)>();
            var order = 0;
            foreach (var stash in stashes)
            {
                if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                    continue;
                if (stash.Has<CastleHeart>() || stash.Has<Refinementstation>())
                    continue;
                if (!TryGetExternalInventory(stash, out var inv))
                    continue;
                var has = InventoryHasItem(inv, item);
                var rank = RankDeposit(stash, item, ownerId, has);
                if (rank.Class == 99)
                    continue;
                ranked.Add((rank, order++, stash));
            }
            // Worst dest first: higher class, then lower spec, then original scan order.
            ranked.Sort((a, b) =>
            {
                var c = b.rank.Class.CompareTo(a.rank.Class);
                if (c != 0) return c;
                c = a.rank.Spec.CompareTo(b.rank.Spec);
                if (c != 0) return c;
                return a.order.CompareTo(b.order);
            });
            var result = new List<Entity>(ranked.Count);
            foreach (var row in ranked)
                result.Add(row.stash);
            return result;
        }

        public static int SourcePass(Entity stash)
        {
            var name = RawName(stash);
            return SourcePassFromName(name, IsConveyorName(name));
        }

        public static bool TryGetExternalInventory(Entity stash, out Entity inventory)
        {
            inventory = Entity.Null;
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                return false;
            var sgm = Core.ServerGameManager;
            if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                return false;
            foreach (var attachedBuffer in buffer)
            {
                var inv = attachedBuffer.Entity;
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (!inv.Has<PrefabGUID>())
                    continue;
                if (!inv.Read<PrefabGUID>().Equals(StashService.ChestBagGuid))
                    continue;
                inventory = inv;
                return true;
            }
            return false;
        }

        public static bool InventoryHasItem(Entity inventory, PrefabGUID item)
        {
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory) || item.GuidHash == 0)
                return false;
            return Core.ServerGameManager.GetInventoryItemCount(inventory, item) > 0;
        }

        public static List<(Entity stash, Entity inventory)> OrderDepositDests(
            List<(Entity stash, Entity inventory)> candidates, PrefabGUID item, ulong ownerId, int standingPlot = -1)
        {
            var ranked = new List<(DepositRank rank, Entity stash, Entity inventory)>();
            foreach (var (stash, inventory) in candidates)
            {
                if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                    continue;
                if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                    continue;
                var has = InventoryHasItem(inventory, item);
                var rank = RankDeposit(stash, item, ownerId, has, standingPlot);
                if (!rank.IsDepositUsable)
                    continue;
                ranked.Add((rank, stash, inventory));
            }
            ranked.Sort((a, b) => a.rank.CompareTo(b.rank));
            OrderTwinsEmptiestFirst(ranked, item);
            var result = new List<(Entity stash, Entity inventory)>(ranked.Count);
            foreach (var row in ranked)
                result.Add((row.stash, row.inventory));
            return result;
        }

        /// <summary>
        /// 1.0.112: twins = chests with the same clean name AND the same '+' count (>0) that tie
        /// on rank, anywhere on the island. Within a twin run, the chest holding the LEAST of the
        /// item goes first, so successive deposits balance across the twins.
        /// </summary>
        static void OrderTwinsEmptiestFirst(List<(DepositRank rank, Entity stash, Entity inventory)> ranked, PrefabGUID item)
        {
            var sgm = Core.ServerGameManager;
            var i = 0;
            while (i < ranked.Count)
            {
                var j = i + 1;
                var boost = PriorityOf(ranked[i].stash);
                var name = TwinKey(ranked[i].stash);
                if (boost > 0 && name.Length > 0)
                {
                    while (j < ranked.Count
                        && ranked[j].rank.CompareTo(ranked[i].rank) == 0
                        && PriorityOf(ranked[j].stash) == boost
                        && TwinKey(ranked[j].stash) == name)
                        j++;
                }
                if (j - i > 1)
                {
                    var run = ranked.GetRange(i, j - i);
                    run.Sort((a, b) => sgm.GetInventoryItemCount(a.inventory, item).CompareTo(sgm.GetInventoryItemCount(b.inventory, item)));
                    for (var k = 0; k < run.Count; k++)
                        ranked[i + k] = run[k];
                }
                i = j;
            }
        }

        static string TwinKey(Entity stash)
        {
            var n = RawName(stash);
            return string.IsNullOrWhiteSpace(n) ? "" : Normalize(n);
        }

        /// <summary>
        /// The run of twins at the top of an already-ordered dest list (same name, same '+' > 0,
        /// same rank as the first entry). Empty unless there are at least two.
        /// </summary>
        public static List<(Entity stash, Entity inventory)> TopTwins(List<(Entity stash, Entity inventory)> ordered, PrefabGUID item, ulong ownerId)
        {
            var twins = new List<(Entity stash, Entity inventory)>();
            if (ordered == null || ordered.Count < 2)
                return twins;
            var first = ordered[0];
            var boost = PriorityOf(first.stash);
            var name = TwinKey(first.stash);
            if (boost <= 0 || name.Length == 0)
                return twins;
            var firstRank = RankDeposit(first.stash, item, ownerId, InventoryHasItem(first.inventory, item));
            twins.Add(first);
            for (var i = 1; i < ordered.Count; i++)
            {
                var d = ordered[i];
                if (PriorityOf(d.stash) != boost || TwinKey(d.stash) != name)
                    break;
                var r = RankDeposit(d.stash, item, ownerId, InventoryHasItem(d.inventory, item));
                if (r.CompareTo(firstRank) != 0)
                    break;
                twins.Add(d);
            }
            if (twins.Count < 2)
                twins.Clear();
            return twins;
        }

        /// <summary>
        /// Split <paramref name="amount"/> across twins so their counts of the item end as even as
        /// possible: fill the lowest up toward the others first, then share the rest evenly.
        /// Returns (dest, give) in the order to place them.
        /// </summary>
        public static List<((Entity stash, Entity inventory) dest, int give)> SplitAcrossTwins(List<(Entity stash, Entity inventory)> twins, PrefabGUID item, int amount)
        {
            var plan = new List<((Entity stash, Entity inventory), int)>();
            if (twins == null || twins.Count < 2 || amount <= 0)
                return plan;
            var sgm = Core.ServerGameManager;
            var counts = new int[twins.Count];
            var total = amount;
            for (var i = 0; i < twins.Count; i++)
            {
                counts[i] = sgm.GetInventoryItemCount(twins[i].inventory, item);
                total += counts[i];
            }
            var target = (total + twins.Count - 1) / twins.Count;
            var give = new int[twins.Count];
            var left = amount;
            // Pass 1: lift each twin up to the target, emptiest first.
            var order = new List<int>();
            for (var i = 0; i < twins.Count; i++) order.Add(i);
            order.Sort((a, b) => counts[a].CompareTo(counts[b]));
            foreach (var i in order)
            {
                if (left <= 0) break;
                var need = target - counts[i];
                if (need <= 0) continue;
                var g = need < left ? need : left;
                give[i] += g;
                left -= g;
            }
            // Pass 2: any rounding remainder to the emptiest.
            foreach (var i in order)
            {
                if (left <= 0) break;
                give[i] += 1;
                left -= 1;
            }
            foreach (var i in order)
            {
                if (give[i] > 0)
                    plan.Add((twins[i], give[i]));
            }
            return plan;
        }

        static Dictionary<int, Dictionary<Entity, Entity>> plotInvStash;
        static DateTime plotInvStashAt;

        internal static void InvalidatePlotStashMap()
        {
            plotInvStash = null;
            plotInvStashAt = DateTime.MinValue;
        }

        static Dictionary<Entity, Entity> InvToStash(int plot)
        {
            var now = DateTime.UtcNow;
            if (plotInvStash == null || (now - plotInvStashAt).TotalSeconds >= 0.25)
            {
                plotInvStash = new Dictionary<int, Dictionary<Entity, Entity>>();
                plotInvStashAt = now;
            }
            if (plotInvStash.TryGetValue(plot, out var cached))
                return cached;
            var map = new Dictionary<Entity, Entity>();
            foreach (var stash in Core.Stash.ChestsOnPlot(plot))
            {
                if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                    continue;
                if (!TryGetExternalInventory(stash, out var inv))
                    continue;
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                map[inv] = stash;
            }
            plotInvStash[plot] = map;
            return map;
        }

        public static List<Entity> OrderDepositInventories(int plot, List<Entity> destInvs, PrefabGUID item, int maxClass = 5)
        {
            var result = new List<Entity>();
            if (destInvs == null || destInvs.Count == 0)
                return result;
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var ownerId);
            var stashOf = InvToStash(plot);
            var ranked = new List<(DepositRank rank, Entity inv)>();
            foreach (var inv in destInvs)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                Entity stash;
                if (!stashOf.TryGetValue(inv, out stash))
                    stash = Entity.Null;
                if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                {
                    // Heart-fuel / attached invs are real dests with no chest nameplate.
                    var orphan = new DepositRank { Class = 3, Label = LabelGeneric, Local = true };
                    if (orphan.IsDepositUsable && orphan.Class <= maxClass)
                        ranked.Add((orphan, inv));
                    continue;
                }
                var name = RawName(stash);
                if (IsConveyorName(name))
                    continue;
                if (IsNoShareName(name) && loggedNs.Add(plot + ":" + name))
                    LogDestPick(SkipLabel(name), plot, item, name, "deposit-filter");
                var has = InventoryHasItem(inv, item);
                var rank = RankDeposit(stash, item, ownerId, has, plot);
                if (!rank.IsDepositUsable || rank.Class > maxClass)
                    continue;
                ranked.Add((rank, inv));
            }
            ranked.Sort((a, b) => a.rank.CompareTo(b.rank));
            foreach (var row in ranked)
                result.Add(row.inv);
            return result;
        }

        public static List<int> SenderGroups(string name)
        {
            var groups = new List<int>();
            if (string.IsNullOrWhiteSpace(name))
                return groups;
            foreach (Match match in SendRx.Matches(name.ToLowerInvariant()))
            {
                if (!int.TryParse(match.Groups[1].Value, out var g))
                    continue;
                if (!groups.Contains(g))
                    groups.Add(g);
            }
            return groups;
        }

        public static List<int> ReceiverGroups(string name)
        {
            var groups = new List<int>();
            if (string.IsNullOrWhiteSpace(name))
                return groups;
            foreach (Match match in ReceiveRx.Matches(name.ToLowerInvariant()))
            {
                if (!int.TryParse(match.Groups[1].Value, out var g))
                    continue;
                if (!groups.Contains(g))
                    groups.Add(g);
            }
            return groups;
        }

        /// <summary>
        /// Belt line identity: sorted sender groups + sorted receiver groups ("s2|r2").
        /// Empty when the plate has no s#/r# token. Two chests with the same signature are
        /// "on the same line" (1.0.111): rank decides direction between them, no seed needed.
        /// </summary>
        public static string LineSignature(string plate)
        {
            var s = SenderGroups(plate);
            var r = ReceiverGroups(plate);
            if (s.Count == 0 && r.Count == 0)
                return "";
            s.Sort();
            r.Sort();
            return "s" + string.Join(",", s) + "|r" + string.Join(",", r);
        }

        public static bool SameLine(Entity a, Entity b)
        {
            if (a == Entity.Null || b == Entity.Null || a == b)
                return false;
            var sa = LineSignature(RawName(a));
            return sa.Length > 0 && sa == LineSignature(RawName(b));
        }

        public static List<string> PredictBeltReceivers(Entity destStash)
        {
            var names = new List<string>();
            if (destStash == Entity.Null || !Core.EntityManager.Exists(destStash))
                return names;
            var destName = RawName(destStash);
            var groups = SenderGroups(destName);
            if (groups.Count == 0)
                return names;
            var destPlot = Core.TerritoryService.GetTerritoryId(destStash);
            IReadOnlyList<int> logisticsIds = Array.Empty<int>();
            try
            {
                logisticsIds = Core.TerritoryService.GetLogisticsTerritoryIds(destPlot);
            }
            catch { }
            if (logisticsIds == null || logisticsIds.Count == 0)
                logisticsIds = new[] { destPlot };

            var seen = new HashSet<Entity>();
            foreach (var lid in logisticsIds)
            {
                foreach (var (group, receiver) in Core.Stash.ReceiveChests(lid))
                {
                    if (!groups.Contains(group))
                        continue;
                    if (receiver.Equals(destStash))
                        continue;
                    if (!seen.Add(receiver))
                        continue;
                    var n = RawName(receiver);
                    if (!string.IsNullOrWhiteSpace(n))
                        names.Add(n);
                }
            }
            return names;
        }

        /// <summary>
        /// Receiver names for chat/log, or "r# with no receiver found". Null if dest is not a sender.
        /// Prediction only — does not move items.
        /// </summary>
        public static string FormatBeltTo(Entity destStash)
        {
            var destName = RawName(destStash);
            if (!IsSenderName(destName))
                return null;
            var groups = SenderGroups(destName);
            var receivers = PredictBeltReceivers(destStash);
            if (receivers.Count == 0)
            {
                if (groups.Count == 0)
                    return "r# with no receiver found";
                var labels = new List<string>(groups.Count);
                foreach (var g in groups)
                    labels.Add("r" + g);
                return string.Join("/", labels) + " with no receiver found";
            }
            return string.Join(", ", receivers);
        }

        public static Entity PredictNextBeltDest(Entity destStash, PrefabGUID item)
        {
            var destName = RawName(destStash);
            if (!IsSenderName(destName))
                return Entity.Null;
            var groups = SenderGroups(destName);
            if (groups.Count == 0)
                return Entity.Null;
            var destPlot = Core.TerritoryService.GetTerritoryId(destStash);
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(destPlot, out var ownerId);

            IReadOnlyList<int> logisticsIds = Array.Empty<int>();
            try
            {
                logisticsIds = Core.TerritoryService.GetLogisticsTerritoryIds(destPlot);
            }
            catch { }
            if (logisticsIds == null || logisticsIds.Count == 0)
                logisticsIds = new[] { destPlot };

            var candidates = new List<(Entity stash, Entity inventory)>();
            var seen = new HashSet<Entity>();
            foreach (var lid in logisticsIds)
            {
                foreach (var (group, receiver) in Core.Stash.ReceiveChests(lid))
                {
                    if (!groups.Contains(group))
                        continue;
                    if (receiver.Equals(destStash))
                        continue;
                    if (!seen.Add(receiver))
                        continue;
                    var n = RawName(receiver);
                    if (IsNoShareName(n))
                        continue;
                    if (!TryGetExternalInventory(receiver, out var inv))
                        continue;
                    candidates.Add((receiver, inv));
                }
            }
            if (candidates.Count == 0)
                return Entity.Null;

            var ordered = OrderDepositDests(candidates, item, ownerId, destPlot);
            if (ordered.Count == 0)
                return Entity.Null;
            var winner = ordered[0];

            TryGetExternalInventory(destStash, out var destInv);
            var destHas = InventoryHasItem(destInv, item);
            var destRank = RankDeposit(destStash, item, ownerId, destHas);
            var winHas = InventoryHasItem(winner.inventory, item);
            var winRank = RankDeposit(winner.stash, item, ownerId, winHas);
            // Exact/category named sink stays. Dual s#/r# (Gem Dust S1R1S6) must not fan.
            if (NamedSinkShouldStay(destStash, item, ownerId))
                return Entity.Null;
            // Overflow is last-resort RR dest, never a conveyor next hop from a named/usable dest.
            if (IsOverflowDestName(RawName(winner.stash)))
                return Entity.Null;
            if (destRank.CompareTo(winRank) <= 0)
                return Entity.Null;
            return winner.stash;
        }

        public static string FormatBeltChat(Entity destStash)
        {
            return FormatBeltChat(destStash, default);
        }

        public static string FormatBeltChat(Entity destStash, PrefabGUID item)
        {
            var next = PredictNextBeltDest(destStash, item);
            if (next == Entity.Null)
                return "";
            if (IsOverflowDestName(RawName(next)))
                return "";
            var n = RawName(next);
            if (string.IsNullOrWhiteSpace(n))
                return "";
            return "; will be belted to " + n;
        }

        static readonly HashSet<string> loggedNs = new();
        static readonly HashSet<string> loggedPicks = new();

        public static void LogDestPick(string label, int plot, PrefabGUID item, string chestName, string via, string beltTo = null)
        {
            var key = via + ":" + plot + ":" + item.GuidHash + ":" + label;
            if (!loggedPicks.Add(key))
                return;
            var belt = string.IsNullOrEmpty(beltTo) ? "" : $" belt-to={beltTo}";
            Core.Log.LogInfo($"[Satisvampory] dest-pick {label} via={via} plot={plot} item={ItemLabel(item)} chest={chestName}{belt}");
        }

        public static void LogDestPickAlways(string label, int plot, PrefabGUID item, string chestName, string via, string beltTo = null)
        {
            var belt = string.IsNullOrEmpty(beltTo) ? "" : $" belt-to={beltTo}";
            Core.Log.LogInfo($"[Satisvampory] dest-pick {label} via={via} plot={plot} item={ItemLabel(item)} chest={chestName}{belt}");
        }

        public static void LogBeltTo(Entity destStash, PrefabGUID item, int plot, string via)
        {
            var name = RawName(destStash);
            if (!IsSenderName(name))
                return;
            var next = PredictNextBeltDest(destStash, item);
            var belt = next == Entity.Null ? "stays" : RawName(next);
            var key = via + ":belt:" + plot + ":" + item.GuidHash + ":" + name;
            if (!loggedPicks.Add(key))
                return;
            Core.Log.LogInfo($"[Satisvampory] dest-pick {LabelSender} via={via} plot={plot} item={ItemLabel(item)} chest={name} belt-to={belt}");
        }
    }
}
