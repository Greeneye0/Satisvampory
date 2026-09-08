using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text;
using VampireCommandFramework;
using Satisvampory.Services;

namespace Satisvampory.Commands.Converters;

public record struct FoundItem(PrefabGUID prefab, bool Ambiguous = false, bool Corrected = false);

public enum ItemResolveStatus { Unique, None, Ambiguous }

class FoundItemConverter : CommandArgumentConverter<FoundItem>
{
    public override FoundItem Parse(ICommandContext ctx, string input)
    {
        // 1.0.116: a castle alias on the standing castle wins before the catalogue.
        if (ctx is ChatCommandContext cc && TryCastleAliasFor(cc, input, out var aliased))
            return aliased;
        var status = TryResolve(input, out var result, out var candidates);
        if (status == ItemResolveStatus.Unique)
        {
            if (result.Corrected && ctx is ChatCommandContext corrected) corrected.Reply($"Using {result.prefab.PrefabName()} (corrected item name).");
            return result;
        }

        if (status == ItemResolveStatus.Ambiguous)
        {
            if (ctx is ChatCommandContext chat) { PendingItemChoiceService.BeginAmbiguous(chat.Event.User.PlatformId, candidates); return new FoundItem(default, true); }
            throw MultipleResultsError(ctx, PrefabsFromCandidates(candidates), 60 + "\n...".Length);
        }

        throw ctx.Error($"No items found matching: {input}. Quote names with spaces (\"Iron Ore\"), try part of the name with .fi, or use an alias (.s alias).");
    }

    static bool TryCastleAliasFor(ChatCommandContext ctx, string input, out FoundItem item)
    {
        item = default;
        try
        {
            var character = ctx.Event.SenderCharacterEntity;
            var plot = Core.TerritoryService.GetStandingTerritoryId(character);
            if (plot < 0)
                return false;
            if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var owner) || owner == 0)
                return false;
            var hash = 0;
            var hit = false;
            foreach (var o in Core.TerritoryService.GetIslandOwnerIds(owner))
            {
                if (Core.PlayerSettings.TryCastleAlias(o, Normalize(input), out hash) && hash != 0) { hit = true; break; }
            }
            if (!hit)
                return false;
            item = new FoundItem(new PrefabGUID(hash));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static ItemResolveStatus TryResolve(string input, out FoundItem item, out List<(PrefabGUID prefab, string name)> candidates)
        => ItemCatalog.TryResolve(input, out item, out candidates);

    public static void LoadItemNames() => ItemCatalog.Load();
    public static bool TryGetExact(string input, out FoundItem item) => ItemCatalog.TryGetExact(input, out item);
    public static string Normalize(string s) => ItemCatalog.Normalize(s);
    public static IReadOnlyDictionary<string, PrefabGUID> ExactItemNames => ItemCatalog.ExactNames;
    public static void RegisterExactAlias(string alias, PrefabGUID prefab) => ItemCatalog.RegisterExactAlias(alias, prefab);
    public static void UnregisterExactAlias(string alias) => ItemCatalog.UnregisterExactAlias(alias);

    static Exception MultipleResultsError(ICommandContext ctx, List<PrefabGUID> searchResults, int lengthOfFail)
        => ItemCatalog.MultipleResultsError(ctx, searchResults, lengthOfFail);

    static List<PrefabGUID> PrefabsFromCandidates(List<(PrefabGUID prefab, string name)> candidates)
        => ItemCatalog.PrefabsFromCandidates(candidates);
}
