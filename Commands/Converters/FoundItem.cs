using System;
using Satisvampory.Services;
using System.Text;

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
        => ItemCatalog.TryResolve(input, out item, out candidates);

    public static void LoadItemNames() => ItemCatalog.Load();
    public static bool TryGetExact(string input, out FoundItem item) => ItemCatalog.TryGetExact(input, out item);
    public static string Normalize(string s) => ItemCatalog.Normalize(s);
    public static IReadOnlyDictionary<string, PrefabGUID> ExactItemNames => ItemCatalog.ExactNames;
    public static void RegisterExactAlias(string alias, PrefabGUID prefab) => ItemCatalog.RegisterExactAlias(alias, prefab);

    static Exception MultipleResultsError(ICommandContext ctx, List<PrefabGUID> searchResults, int lengthOfFail)
        => ItemCatalog.MultipleResultsError(ctx, searchResults, lengthOfFail);

    static List<PrefabGUID> PrefabsFromCandidates(List<(PrefabGUID prefab, string name)> candidates)
        => ItemCatalog.PrefabsFromCandidates(candidates);
}
