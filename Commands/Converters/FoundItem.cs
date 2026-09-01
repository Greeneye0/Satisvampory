using Satisvampory.Services;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text;
using VampireCommandFramework;

namespace Satisvampory.Commands.Converters;

public record struct FoundItem(PrefabGUID prefab, bool Ambiguous = false);

public enum ItemResolveStatus
{
    Unique,
    None,
    Ambiguous
}

class FoundItemConverter : CommandArgumentConverter<FoundItem>
{
    public override FoundItem Parse(ICommandContext ctx, string input)
    {
        var status = TryResolve(input, out var result, out var candidates);
        if (status == ItemResolveStatus.Unique)
            return result;

        if (status == ItemResolveStatus.Ambiguous)
        {
            if (ctx is ChatCommandContext chat)
            {
                PendingItemChoiceService.BeginAmbiguous(chat.Event.User.PlatformId, candidates);
                return new FoundItem(default, true);
            }

            throw MultipleResultsError(ctx, PrefabsFromCandidates(candidates), 60 + "\n...".Length);
        }

        throw ctx.Error($"No items found matching: {input}");
    }

    public static ItemResolveStatus TryResolve(string input, out FoundItem item, out List<(PrefabGUID prefab, string name)> candidates)
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

    static List<PrefabGUID> PrefabsFromCandidates(List<(PrefabGUID prefab, string name)> candidates)
    {
        var list = new List<PrefabGUID>();
        if (candidates == null) return list;
        foreach (var (prefab, _) in candidates)
            list.Add(prefab);
        return list;
    }

    static Dictionary<string, PrefabGUID> itemNamesToPrefabs = new Dictionary<string, PrefabGUID>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<PrefabGUID> skipItems = [
        new PrefabGUID(-625033436), // Chest TransmogTest
        new PrefabGUID(1217578824), // Legs TransmogTest
        new PrefabGUID(409678749),  // Item_Headgear_GeneralHelmet 
        new PrefabGUID(2029158532), // Item_Dummy_Rat
        new PrefabGUID(-1199259626),// Item_Ingredient_Scales
        new PrefabGUID(930747930),  // Item_Dummy_Silkworm
        ];

    public static void LoadItemNames()
    {
        foreach (var (name, prefab) in Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary)
        {
            if(skipItems.Contains(prefab)) continue;
            if (name.StartsWith("Item_") && !name.EndsWith("_Base") && !name.EndsWith("_Trader_Template") && !name.EndsWith("_Debug"))
            {
                var prefabName = prefab.PrefabName();
                /*if(itemNamesToPrefabs.TryGetValue(prefabName, out var otherPrefab))
                {
                    Core.Log.LogWarning($"Duplicate item name found: {prefabName} {prefab} {otherPrefab}");
                }//*/
                if (string.IsNullOrEmpty(prefabName)) continue;
                itemNamesToPrefabs[NormalizeName(prefabName)] = prefab;
            }
        }
        RegisterGreaterBloodEssenceAlias();
    }

    public static void RegisterExactAlias(string alias, PrefabGUID prefab)
    {
        if (string.IsNullOrWhiteSpace(alias) || prefab.GuidHash == 0)
            return;
        itemNamesToPrefabs[NormalizeName(alias)] = prefab;
    }

    static void RegisterGreaterBloodEssenceAlias()
    {
        if (!itemNamesToPrefabs.TryGetValue(NormalizeName("Greater Blood Essence"), out var prefab))
            return;
        var loc = NormalizeName(prefab.PrefabName());
        if (!loc.Equals(NormalizeName("Greater Blood Essence"), StringComparison.OrdinalIgnoreCase))
            return;
        itemNamesToPrefabs[NormalizeName("GBE")] = prefab;
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

    public static bool TryGetExact(string input, out FoundItem item) => TryGet(input, out item);

    public static string Normalize(string s) => NormalizeName(s);

    public static IReadOnlyDictionary<string, PrefabGUID> ExactItemNames => itemNamesToPrefabs;

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

    static Exception MultipleResultsError(ICommandContext ctx, List<PrefabGUID> searchResults, int lengthOfFail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Multiple results be more specific");
        var listed = new HashSet<PrefabGUID>();
        foreach (var prefab in searchResults)
        {
            if (!listed.Add(prefab)) continue;
            var name = prefab.PrefabName();
            if (string.IsNullOrEmpty(name)) continue;
            if (sb.Length + name.Length + lengthOfFail >= Core.MAX_REPLY_LENGTH)
            {
                sb.AppendLine("...");
                return ctx.Error(sb.ToString());
            }

            sb.AppendLine(name);
        }
        return ctx.Error(sb.ToString());
    }
}
