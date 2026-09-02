using Satisvampory.Services;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text;
using VampireCommandFramework;

namespace Satisvampory.Commands.Converters;

internal static class ItemCatalog
{
    internal static ItemResolveStatus TryResolve(string input, out FoundItem item, out List<(PrefabGUID prefab, string name)> candidates)
    {
        item = default;
        candidates = new List<(PrefabGUID prefab, string name)>();

        var normalizedInput = NormalizeName(input);

        if (TryGet(input, out var exact))
        {
            item = exact;
            return ItemResolveStatus.Unique;
        }

        List<PrefabGUID> searchResults = [];
        foreach (var kvp in itemNamesToPrefabs)
        {
            if (kvp.Key.Contains(normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                searchResults.Add(kvp.Value);
            }
        }

        if (TryResolveUniqueOrExact(searchResults, normalizedInput, out var resolved))
        {
            item = resolved;
            return ItemResolveStatus.Unique;
        }

        if (searchResults.Count > 1)
            return AmbiguousFrom(searchResults, out item, out candidates);

        // Try a double search splitting the input
        for (var i = 3; i < normalizedInput.Length; ++i)
        {
            var inputOne = NormalizeName(normalizedInput[..i]);
            var inputTwo = NormalizeName(normalizedInput[i..]);
            if (inputOne.Length == 0 || inputTwo.Length == 0) continue;

            foreach (var kvp in itemNamesToPrefabs)
            {
                if (kvp.Key.Contains(inputOne, StringComparison.OrdinalIgnoreCase) &&
                    kvp.Key.Contains(inputTwo, StringComparison.OrdinalIgnoreCase))
                {
                    searchResults.Add(kvp.Value);
                }
            }
        }

        if (TryResolveUniqueOrExact(searchResults, normalizedInput, out resolved))
        {
            item = resolved;
            return ItemResolveStatus.Unique;
        }

        if (searchResults.Count == 0)
        {
            // Try a triple search splitting the input
            foreach (var kvp in itemNamesToPrefabs)
            {
                for (var i = 3; i < normalizedInput.Length - 3; ++i)
                {
                    var inputOne = NormalizeName(normalizedInput[..i]);
                    if (inputOne.Length == 0 || !kvp.Key.Contains(inputOne, StringComparison.OrdinalIgnoreCase)) continue;

                    for (var j = i + 3; j < normalizedInput.Length; j++)
                    {
                        var inputTwo = NormalizeName(normalizedInput[i..j]);
                        var inputThree = NormalizeName(normalizedInput[j..]);
                        if (inputTwo.Length == 0 || inputThree.Length == 0) continue;

                        if (kvp.Key.Contains(inputTwo, StringComparison.OrdinalIgnoreCase) &&
                            kvp.Key.Contains(inputThree, StringComparison.OrdinalIgnoreCase))
                        {
                            searchResults.Add(kvp.Value);
                        }
                    }
                }
            }
        }

        if (TryResolveUniqueOrExact(searchResults, normalizedInput, out resolved))
        {
            item = resolved;
            return ItemResolveStatus.Unique;
        }

        if (searchResults.Count > 1)
            return AmbiguousFrom(searchResults, out item, out candidates);

        return ItemResolveStatus.None;
    }

    static ItemResolveStatus AmbiguousFrom(List<PrefabGUID> searchResults, out FoundItem item, out List<(PrefabGUID prefab, string name)> candidates)
    {
        item = default;
        candidates = new List<(PrefabGUID prefab, string name)>();
        var listed = new HashSet<PrefabGUID>();
        foreach (var prefab in searchResults)
        {
            if (!listed.Add(prefab)) continue;
            var name = prefab.PrefabName();
            if (string.IsNullOrEmpty(name)) continue;
            candidates.Add((prefab, name));
        }
        return ItemResolveStatus.Ambiguous;
    }

    internal static List<PrefabGUID> PrefabsFromCandidates(List<(PrefabGUID prefab, string name)> candidates)
    {
        var list = new List<PrefabGUID>();
        if (candidates == null) return list;
        foreach (var (prefab, _) in candidates)
            list.Add(prefab);
        return list;
    }

    static Dictionary<string, PrefabGUID> itemNamesToPrefabs = new Dictionary<string, PrefabGUID>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<int> omittedHashes =
    [
        -625033436, 1217578824, 409678749, 2029158532, -1199259626, 930747930
    ];

    internal static void Load()
    {
        var catalog = Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary;
        foreach (var pair in catalog)
        {
            if (!IsLiveItemPrefab(pair.Key, pair.Value))
                continue;
            var label = pair.Value.PrefabName();
            if (string.IsNullOrEmpty(label))
                continue;
            itemNamesToPrefabs[NormalizeName(label)] = pair.Value;
        }
        RegisterBloodEssenceAliases();
    }

    static bool IsLiveItemPrefab(string spawnName, PrefabGUID prefab)
    {
        if (omittedHashes.Contains(prefab.GuidHash))
            return false;
        if (!spawnName.StartsWith("Item_"))
            return false;
        return !spawnName.EndsWith("_Base")
            && !spawnName.EndsWith("_Trader_Template")
            && !spawnName.EndsWith("_Debug");
    }

    public static void RegisterExactAlias(string alias, PrefabGUID prefab)
    {
        if (string.IsNullOrWhiteSpace(alias) || prefab.GuidHash == 0)
            return;
        itemNamesToPrefabs[NormalizeName(alias)] = prefab;
    }

    static void RegisterBloodEssenceAliases()
    {
        RegisterEssenceAlias("Greater Blood Essence", "GBE", "greater blood");
        RegisterEssenceAlias("Primal Blood Essence", "PBE", "primal blood");
        RegisterEssenceAlias("Ancestral Blood Essence", "ABE", "ancestral blood");
    }

    static void RegisterEssenceAlias(string locName, string shortAlias, string midAlias)
    {
        if (!itemNamesToPrefabs.TryGetValue(NormalizeName(locName), out var prefab))
            return;
        var loc = NormalizeName(prefab.PrefabName());
        if (!loc.Equals(NormalizeName(locName), StringComparison.OrdinalIgnoreCase))
            return;
        itemNamesToPrefabs[NormalizeName(shortAlias)] = prefab;
        if (!string.IsNullOrEmpty(midAlias))
            itemNamesToPrefabs[NormalizeName(midAlias)] = prefab;
    }


    static string NormalizeName(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var ch in s)
        {
            var c = ch switch
            {
                '\u0027' or '\u2018' or '\u2019' or '\u201B' or '\u0060' or '\u00B4' or '\u02BC' or '\u2032' => '\'',
                '\u2010' or '\u2011' or '\u2013' or '\u2014' => '-',
                _ => ch
            };

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    internal static bool TryGetExact(string input, out FoundItem item) => TryGet(input, out item);

    internal static string Normalize(string s) => NormalizeName(s);

    internal static IReadOnlyDictionary<string, PrefabGUID> ExactNames => itemNamesToPrefabs;

    static bool TryGet(string input, out FoundItem item)
    {
        if (itemNamesToPrefabs.TryGetValue(NormalizeName(input), out var prefab))
        {
            item = new FoundItem(prefab);
            return true;
        }

        item = new FoundItem(new(0));
        return false;
    }

    static bool TryResolveUniqueOrExact(List<PrefabGUID> searchResults, string normalizedInput, out FoundItem item)
    {
        if (searchResults.Count == 1)
        {
            item = new FoundItem(searchResults[0]);
            return true;
        }

        if (searchResults.Count > 1)
        {
            PrefabGUID exact = default;
            var exactCount = 0;
            var seen = new HashSet<PrefabGUID>();
            foreach (var prefab in searchResults)
            {
                if (!seen.Add(prefab)) continue;
                var name = NormalizeName(prefab.PrefabName());
                if (name.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
                {
                    exact = prefab;
                    exactCount++;
                }
            }

            if (exactCount == 1)
            {
                item = new FoundItem(exact);
                return true;
            }
        }

        item = default;
        return false;
    }

    internal static Exception MultipleResultsError(ICommandContext ctx, List<PrefabGUID> searchResults, int lengthOfFail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Multiple results be more specific");
        var listed = new HashSet<PrefabGUID>();
        foreach (var prefab in searchResults)
        {
            if (!listed.Add(prefab)) continue;
            var name = prefab.PrefabName();
            if (string.IsNullOrEmpty(name)) continue;
            if (sb.Length + name.Length + lengthOfFail >= Core.MaxChatReply)
            {
                sb.AppendLine("...");
                return ctx.Error(sb.ToString());
            }

            sb.AppendLine(name);
        }
        return ctx.Error(sb.ToString());
    }
}
