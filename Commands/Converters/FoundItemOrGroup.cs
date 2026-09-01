using Satisvampory;
using Satisvampory.Services;
using Stunlock.Core;
using VampireCommandFramework;

namespace Satisvampory.Commands.Converters;

public record struct FoundItemOrGroup
{
    public bool IsGroup;
    public string GroupName;
    public FoundItem Item;

    public static FoundItemOrGroup FromItem(FoundItem item) => new()
    {
        IsGroup = false,
        GroupName = null,
        Item = item
    };

    public static FoundItemOrGroup FromGroup(string name) => new()
    {
        IsGroup = true,
        GroupName = name,
        Item = default
    };
}

class FoundItemOrGroupConverter : CommandArgumentConverter<FoundItemOrGroup>
{
    public override FoundItemOrGroup Parse(ICommandContext ctx, string input)
    {
        // Exact item name always wins over a group name ("Cotton" is the item).
        if (FoundItemConverter.TryGetExact(input, out var exact))
            return FoundItemOrGroup.FromItem(exact);

        var normalized = FoundItemConverter.Normalize(input);

        if (ItemGroupService.TryGetBuiltInCanonical(normalized, out var builtIn))
            return FoundItemOrGroup.FromGroup(builtIn);

        if (ctx is ChatCommandContext chat &&
            ItemGroupService.TryGetStandingCastleSettingsOwner(chat, out var ownerId, out _, replyIfMissing: false) &&
            Core.PlayerSettings.HasItemGroup(ownerId, normalized))
        {
            return FoundItemOrGroup.FromGroup(ItemGroupService.NormalizeName(normalized));
        }

        // Fuzzy item match as a last resort. Built-in group names like "ore" never reach here.
        var item = new FoundItemConverter().Parse(ctx, input);
        return FoundItemOrGroup.FromItem(item);
    }
}
