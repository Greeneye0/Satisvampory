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
    /// 1.6.1.38 dest/source naming. NS = no-share (never source or dest).
    /// Trailing '' (two apostrophes) is skip-quotes, treated like NS everywhere.
    /// Does not decide HOW MUCH to move - only WHERE an already-decided amount goes,
    /// plus same-plot self-sort of surplus above leftover.
    /// Name match: '+' AND clauses; spaces OR within a clause (group / ItemCategory / name).
    /// No '+': 1.6.1.35 type-word AND fallback when a token is weapon/armor/material.
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
        static readonly Regex SenderRegex = new(Const.SENDER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ReceiverRegex = new(Const.RECEIVER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            return stash.Read<NameableInteractable>().Name.ToString() ?? "";
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

        public static bool IsSkipQuotesName(string name)
        {
            // Empty name is not skip. Only a trailing two-apostrophe suffix.
            if (string.IsNullOrEmpty(name))
                return false;
            return name.EndsWith(SkipSuffix, StringComparison.Ordinal);
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
            return SenderRegex.IsMatch(name.ToLowerInvariant());
        }

        public static bool IsReceiverName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return ReceiverRegex.IsMatch(name.ToLowerInvariant());
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
            if (ExactItemNameMatch(name, item, out _))
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
            t = SenderRegex.Replace(t, "");
            t = ReceiverRegex.Replace(t, "");
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

        static bool TokenMatchesItem(string token, PrefabGUID item, string itemName, ItemCategory itemCat, ulong ownerId, bool allowCategory)
        {
            if (string.IsNullOrEmpty(token))
                return false;
            if (IsDestClassToken(token))
                return false;
            if (ItemGroupService.IsGbeAliasText(token))
                return ItemGroupService.IsGreaterBloodEssence(item);
            var variants = TokenVariants(token);

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
                        return true;
                }
                // "Wood Stone Bone" should take planks, not only raw logs.
                if (canonical == ItemGroupService.GroupWood)
                {
                    foreach (var m in ItemGroupService.ResolveMembers(ownerId, ItemGroupService.GroupPlanks))
                    {
                        if (m.GuidHash == item.GuidHash)
                            return true;
                    }
                }
            }
            // "Blood" is the blood dest group, not a substring of "Blood Jewel".
            // "Stone" is stone dests, not Miststone. Membership already failed.
            if (tokenIsDestGroup)
                return false;

            foreach (var (name, _) in Core.PlayerSettings.ListCustomGroups(ownerId))
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                var gNorm = ItemGroupService.NormalizeName(name);
                if (gNorm.Length < 3)
                    continue;
                if (!VariantsOverlap(token, gNorm))
                    continue;
                foreach (var m in ItemGroupService.ResolveMembers(ownerId, name))
                {
                    if (m.GuidHash == item.GuidHash)
                        return true;
                }
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
                            return true;
                    }
                    if (!CategoryByToken.TryGetValue(v, out var flag))
                        continue;
                    if (flag != ItemCategory.NONE && (itemCat & flag) != 0)
                        return true;
                }
            }

            var itemTokens = string.IsNullOrEmpty(itemName)
                ? Array.Empty<string>()
                : itemName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var v in variants)
            {
                if (v.Length >= 3 && !string.IsNullOrEmpty(itemName) && itemName.IndexOf(v, StringComparison.Ordinal) >= 0)
                    return true;
                foreach (var it in itemTokens)
                {
                    if (VariantsOverlap(v, it))
                        return true;
                }
            }
            return false;
        }

        public static bool ExactItemNameMatch(string chestName, PrefabGUID item, out int specificity)
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
            if (ItemGroupService.IsGbeAliasText(remaining) || (tokens.Count == 1 && ItemGroupService.IsGbeAliasText(tokens[0])))
            {
                if (ItemGroupService.IsGreaterBloodEssence(item))
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
        {
            specificity = 0;
            if (string.IsNullOrWhiteSpace(chestName) || IsGenericName(chestName))
                return false;
            if (ExactItemNameMatch(chestName, item, out _))
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
                        if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true))
                            continue;
                        clauseHit = true;
                        clauseLen += token.Length;
                    }
                    if (!clauseHit)
                        return false;
                    matchedLen += clauseLen;
                }
                specificity = clauses.Count * 1000 + matchedLen;
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
                    if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true))
                        return false;
                    totalLen += token.Length;
                }
                specificity = tokens.Count * 1000 + totalLen;
                return true;
            }

            var matched = 0;
            var matchedLenOr = 0;
            foreach (var token in tokens)
            {
                if (!TokenMatchesItem(token, item, itemName, cat, ownerPlatformId, allowCategory: true))
                    continue;
                matched++;
                matchedLenOr += token.Length;
            }
            if (matched == 0)
                return false;
            specificity = matched * 1000 + matchedLenOr;
            return true;
        }

        public struct DepositRank : IComparable<DepositRank>
        {
            // Incoming dest: 0 seeded s# (unnamed or name-matches), 1 exact, 2 category, 3 unnamed/generic, 4 custom-seeded, 5 overflow last-resort, 6 empty-custom, 90 special, 99 NS
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
            var classOk = blankClass == 3 && emptyClass == 3 && leatherClass == 6;
            var generalClass = RankClassUnmatched("General", "Jewel Storage", false);
            var elseClass = RankClassUnmatched("Everything Else", "Jewel Storage", false);
            var catchAll = IsGenericName("General") && IsGenericName("Everything Else") && generalClass == 3 && elseClass == 3;
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
            if (IsUnnamedDest(plate, destName))
                return 3;
            return hasItem ? 4 : 6;
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

        public static DepositRank RankDeposit(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem, int standingPlot = -1)
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
            var exact = !overflowDest && item.GuidHash != 0 && ExactItemNameMatch(matchName, item, out specExact);
            var category = !overflowDest && !exact && item.GuidHash != 0 && CategoryMatch(matchName, item, ownerId, out specCat);
            var unnamed = !overflowDest && IsUnnamedDest(plate, name);

            // 1.6.1.38: seeded s# (has this item) first only if unnamed/generic OR name matches.
            // Overflow names never seed as class 0. Named s# must not take unmatched items.
            // Empty s# / unnamed treasury do not beat exact item-name.
            // s# lives on the nameplate only - never treat a vanilla prefab name as a sender.
            if (!overflowDest && IsSenderName(plate) && hasItem && (unnamed || exact || category))
            {
                rank.Class = 0;
                rank.Spec = exact ? specExact + 20000 : (category ? specCat + 10000 : 0);
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
            if (item.GuidHash != 0 && ExactItemNameMatch(matchName, item, out var specExact))
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
                if (!inv.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
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
            var result = new List<(Entity stash, Entity inventory)>(ranked.Count);
            foreach (var row in ranked)
                result.Add((row.stash, row.inventory));
            return result;
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
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
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
            foreach (Match match in SenderRegex.Matches(name.ToLowerInvariant()))
            {
                if (!int.TryParse(match.Groups[1].Value, out var g))
                    continue;
                if (!groups.Contains(g))
                    groups.Add(g);
            }
            return groups;
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
                foreach (var (group, receiver) in Core.Stash.GetAllReceivingStashes(lid))
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
                foreach (var (group, receiver) in Core.Stash.GetAllReceivingStashes(lid))
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
