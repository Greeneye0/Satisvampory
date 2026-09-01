using System;
using System.Collections.Generic;
using Satisvampory.Commands.Converters;
using Satisvampory.Services;
using Stunlock.Core;
using VampireCommandFramework;

namespace Satisvampory.Commands;

[CommandGroup(name: "satisvampory", shortHand: "s")]
internal static class ScoopCommands
{
    [Command(name: "auto", usage: ".s auto", description: "Toggle auto-scoop. Default auto all. Keeps current filter.")]
    public static void Auto(ChatCommandContext ctx)
    {
        var on = Core.PlayerSettings.ToggleAuto(ctx.Event.User.PlatformId);
        var filterName = Core.PlayerSettings.GetAutoFilter(ctx.Event.User.PlatformId) == AutoFilter.All ? "all" : "around";
        ctx.Reply(on
            ? $"Auto-scoop <color=green>ON</color> (filter <color=white>{filterName}</color>)."
            : $"Auto-scoop <color=yellow>OFF</color> (filter <color=white>{filterName}</color>).");
    }

    [Command(name: "auto", usage: ".s auto <around|all>", description: "Turn auto-scoop ON and set the auto filter.")]
    public static void AutoSet(ChatCommandContext ctx, string filter)
    {
        if (!PlayerSettingsService.TryParseAutoFilter(filter, out var parsed))
        {
            ctx.Reply("Use <color=white>.s auto around</color> or <color=white>.s auto all</color>.");
            return;
        }
        Core.PlayerSettings.SetAutoOnWithFilter(ctx.Event.User.PlatformId, parsed);
        if (parsed == AutoFilter.Around)
            ctx.Reply("Auto-scoop <color=green>ON</color>, filter <color=white>around</color>: only piles that spawned while you were in radius.");
        else
            ctx.Reply("Auto-scoop <color=green>ON</color>, filter <color=white>all</color>: any non-player pile in radius.");
    }

    [Command(name: "filter", usage: ".s filter", description: "Show your auto-scoop filter.")]
    public static void FilterShow(ChatCommandContext ctx)
    {
        var id = ctx.Event.User.PlatformId;
        var on = Core.PlayerSettings.IsAutoEnabled(id);
        var autoState = on ? "<color=green>ON</color>" : "<color=yellow>OFF</color>";
        if (Core.PlayerSettings.GetAutoFilter(id) == AutoFilter.Around)
            ctx.Reply($"Auto filter <color=white>around</color> (auto {autoState}): only piles that spawned while you were in radius.");
        else
            ctx.Reply($"Auto filter <color=white>all</color> (auto {autoState}): any non-player pile in radius.");
    }

    [Command(name: "filter", usage: ".s filter <around|all>", description: "Set auto-scoop filter without toggling auto.")]
    public static void FilterSet(ChatCommandContext ctx, string filter)
    {
        if (!PlayerSettingsService.TryParseAutoFilter(filter, out var parsed))
        {
            ctx.Reply("Use <color=white>.s filter around</color> or <color=white>.s filter all</color>.");
            return;
        }
        Core.PlayerSettings.SetAutoFilter(ctx.Event.User.PlatformId, parsed);
        FilterShow(ctx);
    }

    [Command(name: "notify", usage: ".s notify", description: "Show scoop pickup chat mode.")]
    public static void NotifyShow(ChatCommandContext ctx)
    {
        var mode = Core.PlayerSettings.GetNotifyMode(ctx.Event.User.PlatformId);
        if (mode == NotifyMode.Off)
            ctx.Reply("Notify <color=white>off</color>: no pickup chat. <color=white>.s last</color> still reprints.");
        else if (mode == NotifyMode.On)
            ctx.Reply("Notify <color=white>on</color>: one line per drain on <color=white>.s</color> and auto.");
        else
            ctx.Reply("Notify <color=white>manual</color>: one line on <color=white>.s</color> only. Auto is silent.");
    }

    [Command(name: "notify", usage: ".s notify <off|manual|on>", description: "Set scoop pickup chat: off, manual (.s only), or on (auto too).")]
    public static void NotifySet(ChatCommandContext ctx, string mode)
    {
        if (!PlayerSettingsService.TryParseNotifyMode(mode, out var parsed))
        {
            ctx.Reply("Use <color=white>.s notify off</color>, <color=white>manual</color>, or <color=white>on</color>.");
            return;
        }
        Core.PlayerSettings.SetNotifyMode(ctx.Event.User.PlatformId, parsed);
        NotifyShow(ctx);
    }

    [Command(name: "last", usage: ".s last", description: "Reprint your last scoop line.")]
    public static void Last(ChatCommandContext ctx)
    {
        if (ScoopReport.TryGetLast(ctx.Event.User.PlatformId, out var summary))
            ctx.Reply(summary);
        else
            ctx.Reply("No scoop yet.");
    }

    [Command(name: "radius", usage: ".s radius", description: "Show your scoop radius.")]
    public static void RadiusShow(ChatCommandContext ctx)
    {
        ctx.Reply($"Scoop radius: <color=white>{Core.PlayerSettings.GetRadius(ctx.Event.User.PlatformId):0.#}</color> (1-50).");
    }

    [Command(name: "radius", usage: ".s radius <n>", description: "Set your scoop radius (1-50).")]
    public static void RadiusSet(ChatCommandContext ctx, float n)
    {
        var r = Core.PlayerSettings.SetRadius(ctx.Event.User.PlatformId, n);
        ctx.Reply($"Scoop radius set to <color=white>{r:0.#}</color>.");
    }

    [Command(name: "exclude", usage: ".s exclude", description: "List items auto-scoop will skip.")]
    public static void ExcludeList(ChatCommandContext ctx)
    {
        var list = Core.PlayerSettings.ListExcludes(ctx.Event.User.PlatformId);
        if (list.Count == 0)
        {
            ctx.Reply("No scoop excludes. Soul shards and death bags are always skipped.");
            return;
        }
        ctx.Reply($"Scoop excludes ({list.Count}):");
        foreach (var (_, name) in list)
            ctx.Reply($"  <color=green>{name}</color>");
    }

    [Command(name: "exclude", usage: ".s exclude <item|group>", description: "Toggle scoop-exclude for an item or group.")]
    public static void ExcludeToggle(ChatCommandContext ctx, FoundItemOrGroup target)
    {
        if (!target.IsGroup && LogisticsCommands.HandleAmbiguousItem(ctx, target.Item, PendingItemCommand.ExcludeToggle))
            return;
        if (target.IsGroup)
        {
            var members = ItemGroupService.ResolveMembers(ctx.Event.User.PlatformId, target.GroupName);
            if (members.Count == 0)
            {
                ctx.Reply($"Group <color=yellow>{target.GroupName}</color> has no members.");
                return;
            }
            var added = 0;
            var removed = 0;
            foreach (var m in members)
            {
                if (Core.PlayerSettings.ToggleExclude(ctx.Event.User.PlatformId, m.Prefab, m.Name))
                    added++;
                else
                    removed++;
            }
            ctx.Reply($"Group <color=yellow>{target.GroupName}</color>: excluded +{added} / unexcluded {removed}.");
            return;
        }
        DoExcludeToggle(ctx, target.Item);
    }

    internal static void DoExcludeToggle(ChatCommandContext ctx, FoundItem item)
    {
        if (item.prefab.GuidHash == 0) return;
        var name = item.prefab.PrefabName();
        var now = Core.PlayerSettings.ToggleExclude(ctx.Event.User.PlatformId, item.prefab, name);
        ctx.Reply(now
            ? $"Excluded <color=green>{name}</color> from scoop."
            : $"No longer excluding <color=green>{name}</color> from scoop.");
    }

    [Command(name: "bagcap", usage: ".s bagcap", description: "List your scoop bag/guild caps (not castle production caps).")]
    public static void BagCapList(ChatCommandContext ctx)
    {
        var id = ctx.Event.User.PlatformId;
        var mode = Core.PlayerSettings.GetCapMode(id);
        var caps = Core.PlayerSettings.ListScoopCaps(id);
        ctx.Reply($"Scoop cap mode: <color=white>{(mode == CapMode.Guild ? "guild" : "bags")}</color> (your Steam id only).");
        if (caps.Count == 0)
        {
            ctx.Reply("No scoop caps. Missing or -1 = unlimited. 0 = scoop none of that item. Castle conveyor caps are <color=white>.s cap</color>.");
            return;
        }
        foreach (var (_, name, cap) in caps)
            ctx.Reply($"  <color=green>{name}</color> = <color=white>{cap}</color>");
    }

    [Command(name: "bagcap", usage: ".s bagcap <item|group>", description: "Show scoop cap and current count.")]
    public static void BagCapShow(ChatCommandContext ctx, FoundItemOrGroup target)
    {
        if (!target.IsGroup && LogisticsCommands.HandleAmbiguousItem(ctx, target.Item, PendingItemCommand.BagCapShow))
            return;
        if (target.IsGroup)
        {
            var members = ItemGroupService.ResolveMembers(ctx.Event.User.PlatformId, target.GroupName);
            if (members.Count == 0)
            {
                ctx.Reply($"Group <color=yellow>{target.GroupName}</color> has no members.");
                return;
            }
            ctx.Reply($"Scoop caps for <color=yellow>{target.GroupName}</color>:");
            foreach (var m in members)
                DoBagCapShowNamed(ctx, m.Prefab, m.Name);
            return;
        }
        DoBagCapShow(ctx, target.Item);
    }

    [Command(name: "bagcap", usage: ".s bagcap <item|group> <n>", description: "Set a scoop cap. 0 = scoop none. -1 = unlimited.")]
    public static void BagCapSet(ChatCommandContext ctx, FoundItemOrGroup target, int n)
    {
        if (!target.IsGroup && LogisticsCommands.HandleAmbiguousItem(ctx, target.Item, PendingItemCommand.BagCapSet, n))
            return;
        if (target.IsGroup)
        {
            var members = ItemGroupService.ResolveMembers(ctx.Event.User.PlatformId, target.GroupName);
            if (members.Count == 0)
            {
                ctx.Reply($"Group <color=yellow>{target.GroupName}</color> has no members.");
                return;
            }
            foreach (var m in members)
                Core.PlayerSettings.SetScoopCap(ctx.Event.User.PlatformId, m.Prefab, n, m.Name);
            ctx.Reply($"Set scoop cap <color=white>{n}</color> on {members.Count} item(s) in <color=yellow>{target.GroupName}</color>.");
            return;
        }
        DoBagCapSet(ctx, target.Item, n);
    }

    internal static void DoBagCapShow(ChatCommandContext ctx, FoundItem item)
    {
        if (item.prefab.GuidHash == 0) return;
        DoBagCapShowNamed(ctx, item.prefab, item.prefab.PrefabName());
    }

    static void DoBagCapShowNamed(ChatCommandContext ctx, PrefabGUID prefab, string name)
    {
        var id = ctx.Event.User.PlatformId;
        var cap = Core.PlayerSettings.GetCap(id, prefab);
        var mode = Core.PlayerSettings.GetCapMode(id);
        var count = InventoryCountService.CountForCap(ctx.Event.SenderCharacterEntity, ctx.Event.User, prefab, mode);
        var capText = cap < 0 ? "unlimited" : cap.ToString();
        ctx.Reply($"  <color=green>{name}</color> scoop-cap=<color=white>{capText}</color> have=<color=white>{count}</color> ({mode})");
    }

    internal static void DoBagCapSet(ChatCommandContext ctx, FoundItem item, int n)
    {
        if (item.prefab.GuidHash == 0) return;
        var name = item.prefab.PrefabName();
        Core.PlayerSettings.SetScoopCap(ctx.Event.User.PlatformId, item.prefab, n, name);
        if (n < 0)
            ctx.Reply($"Cleared scoop cap on <color=green>{name}</color> (unlimited).");
        else
            ctx.Reply($"Scoop cap <color=green>{name}</color> = <color=white>{n}</color>.");
    }

    [Command(name: "bagcapclear", usage: ".s bagcapclear", description: "Clear all of your scoop bag caps.")]
    public static void BagCapClear(ChatCommandContext ctx)
    {
        Core.PlayerSettings.ClearAllScoopCaps(ctx.Event.User.PlatformId);
        ctx.Reply("Cleared all your scoop bag caps. Castle production caps are unchanged.");
    }

    [Command(name: "mode", usage: ".s mode bags|guild", description: "Scoop cap counting: your bags, or bags + clan stashes.")]
    public static void ModeSet(ChatCommandContext ctx, string mode)
    {
        if (string.Equals(mode, "bags", StringComparison.OrdinalIgnoreCase))
        {
            Core.PlayerSettings.SetCapMode(ctx.Event.User.PlatformId, CapMode.Bags);
            ctx.Reply("Scoop cap mode <color=white>bags</color>: count only your inventory.");
            return;
        }
        if (string.Equals(mode, "guild", StringComparison.OrdinalIgnoreCase))
        {
            Core.PlayerSettings.SetCapMode(ctx.Event.User.PlatformId, CapMode.Guild);
            ctx.Reply("Scoop cap mode <color=white>guild</color>: count your bags plus ClanShare stashes. This is YOUR cap, not a shared clan cap.");
            return;
        }
        ctx.Reply("Use <color=white>.s mode bags</color> or <color=white>.s mode guild</color>.");
    }
}

[CommandGroup(name: "logistics", shortHand: "l")]
internal static class LegacyPrefixAlias
{
    [Command(name: "help", shortHand: "h", usage: ".l help", description: "Same as .s help")]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply("Use <color=white>.s help</color>.");
        CastleCommands.ShowHelp(ctx);
    }
}

internal static class ScoopRootCommands
{
    [Command(name: "scoop", usage: ".scoop", description: "Scoop nearby world drops into your bags.")]
    public static void ScoopNow(ChatCommandContext ctx) => RunScoop(ctx);

    [Command(name: "s", usage: ".s", description: "Scoop nearby world drops into your bags.")]
    public static void SNow(ChatCommandContext ctx) => RunScoop(ctx);

    [Command(name: "sc", usage: ".sc", description: "Scoop nearby world drops into your bags.")]
    public static void ScNow(ChatCommandContext ctx) => RunScoop(ctx);

    [Command(name: "s", usage: ".s <number>", description: "Pick a numbered item from the last ambiguous search.")]
    public static void SPick(ChatCommandContext ctx, int number) => LogisticsCommands.ReplayPendingPick(ctx, number);

    [Command(name: "scoop", usage: ".scoop <number>", description: "Pick a numbered item from the last ambiguous search.")]
    public static void ScoopPick(ChatCommandContext ctx, int number) => LogisticsCommands.ReplayPendingPick(ctx, number);

    static void RunScoop(ChatCommandContext ctx)
    {
        if (!Core.HasInitialized)
        {
            ctx.Reply("Satisvampory is still starting.");
            return;
        }
        var result = ScoopService.ScoopNow(ctx.Event.SenderCharacterEntity, ctx.Event.User);
        if (result.Busy)
        {
            ctx.Reply("Scoop is already running for you.");
            return;
        }
        if (string.IsNullOrEmpty(result.Summary))
        {
            ctx.Reply("Nothing to scoop.");
            return;
        }
        var notify = Core.PlayerSettings.GetNotifyMode(ctx.Event.User.PlatformId);
        if (notify != NotifyMode.Off)
            ctx.Reply(result.Summary);
        else
            ctx.Reply("Scooped. <color=white>.s last</color> reprints the line.");
    }
}
