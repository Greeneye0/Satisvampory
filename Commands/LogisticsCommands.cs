using Satisvampory;
using System;
using System.Collections.Generic;
using System.Linq;
using Satisvampory.Commands.Converters;
using Stunlock.Core;
using Satisvampory.Services;
using ProjectM;
using ProjectM.Network;
using Steamworks;
using Unity.Entities;
using VampireCommandFramework;

namespace Satisvampory.Commands
{
    [CommandGroup(name: "satisvampory", shortHand: "s")]
    public static class LogisticsCommands
    {

        static bool TryGetStandingCastleSettingsOwner(ChatCommandContext ctx, out ulong ownerPlatformId, out string ownerName, bool replyIfMissing = true)
        {
            return ItemGroupService.TryGetStandingCastleSettingsOwner(ctx, out ownerPlatformId, out ownerName, replyIfMissing);
        }

        static bool TryGetCastleSettingsOwner(ChatCommandContext ctx, bool forSet, out ulong ownerPlatformId, out string ownerName, bool replyIfMissing = true)
        {
            return ItemGroupService.TryGetCastleSettingsOwner(ctx, forSet, out ownerPlatformId, out ownerName, replyIfMissing);
        }

        internal static bool HandleAmbiguousItem(ChatCommandContext ctx, FoundItem item, PendingItemCommand command, int amount = 0, string groupName = null)
        {
            if (!item.Ambiguous)
                return false;
            PendingItemChoiceService.AttachCommand(ctx.Event.User.PlatformId, command, amount, groupName);
            PendingItemChoiceService.ReplyNumberedList(ctx);
            return true;
        }

        static bool HandleAmbiguousTarget(ChatCommandContext ctx, FoundItemOrGroup target, PendingItemCommand command, int amount = 0, string groupName = null)
        {
            if (target.IsGroup || !target.Item.Ambiguous)
                return false;
            return HandleAmbiguousItem(ctx, target.Item, command, amount, groupName);
        }

        internal static void ReplayPendingPick(ChatCommandContext ctx, int number)
        {
            if (!PendingItemChoiceService.TryPick(ctx.Event.User.PlatformId, number, out var pending, out var picked, out var error))
            {
                ctx.Reply(error);
                return;
            }

            var item = new FoundItem(picked.Prefab);
            var name = picked.Name;
            if (string.IsNullOrEmpty(name))
                name = item.prefab.PrefabName();

            switch (pending.Command)
            {
                case PendingItemCommand.Pull:
                    ctx.Reply($"Using {name}. Pulling {pending.Amount}.");
                    AdditionalCommands.PullItem(ctx, item, pending.Amount);
                    break;
                case PendingItemCommand.ReserveShow:
                    ctx.Reply($"Using {name}.");
                    ShowItemReserve(ctx, FoundItemOrGroup.FromItem(item));
                    break;
                case PendingItemCommand.ReserveSet:
                    ctx.Reply($"Using {name}.");
                    SetItemReserveCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
                    break;
                case PendingItemCommand.ReserveClear:
                    ctx.Reply($"Using {name}.");
                    ClearItemReserveCmd(ctx, item);
                    break;
                case PendingItemCommand.CapShow:
                    ctx.Reply($"Using {name}.");
                    ShowItemCap(ctx, FoundItemOrGroup.FromItem(item));
                    break;
                case PendingItemCommand.CapSet:
                    ctx.Reply($"Using {name}.");
                    SetItemCapCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
                    break;
                case PendingItemCommand.CapClear:
                    ctx.Reply($"Using {name}.");
                    ClearItemCapCmd(ctx, item);
                    break;
                case PendingItemCommand.GroupAdd:
                    ctx.Reply($"Using {name}.");
                    ApplyResolvedGroupItems(ctx, pending.GroupName, "add", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
                    break;
                case PendingItemCommand.GroupRemove:
                    ctx.Reply($"Using {name}.");
                    ApplyResolvedGroupItems(ctx, pending.GroupName, "remove", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
                    break;
                case PendingItemCommand.FindItem:
                    ctx.Reply($"Using {name}.");
                    AdditionalCommands.FindItem(ctx, item);
                    break;
                case PendingItemCommand.AdminStash:
                    ctx.Reply($"Using {name}.");
                    AdditionalCommands.AdminStash(ctx, item, pending.Amount);
                    break;
                case PendingItemCommand.Conv:
                    ctx.Reply($"Using {name}.");
                    ConvTroubleshoot(ctx, item);
                    break;
                case PendingItemCommand.ExcludeToggle:
                    ScoopCommands.DoExcludeToggle(ctx, item);
                    break;
                case PendingItemCommand.BagCapShow:
                    ScoopCommands.DoBagCapShow(ctx, item);
                    break;
                case PendingItemCommand.BagCapSet:
                    ScoopCommands.DoBagCapSet(ctx, item, pending.Amount);
                    break;
                default:
                    ctx.Reply($"Using {name}.");
                    break;
            }
        }

        [Command(name: "help", shortHand: "h", usage: ".s help", description: "Satisvampory commands. Scopes: you, plot, castle, clan, server.")]
        public static void ShowHelp(ChatCommandContext ctx)
        {
            ctx.Reply("<color=yellow>Satisvampory</color>  prefix <color=white>.s</color>  admin <color=white>.sg</color>  (full list: COMMANDS.md)");
            ctx.Reply("<color=white>YOU</color> scoop: <color=white>.s</color> / <color=white>.scoop</color>  <color=white>.s auto</color>  <color=white>.s radius</color>  <color=white>.s exclude</color>  <color=white>.s bagcap</color>  <color=white>.s mode bags|guild</color>  <color=white>.s notify</color>");
            ctx.Reply("<color=white>YOU</color> stash/pull (dest is plot, or clan if .s cs on): <color=white>.stash</color>  <color=white>.pull plank 50</color>  <color=white>.fi</color>  <color=white>.fc</color>  <color=white>.s ss</color>  <color=white>.s cr</color>  <color=white>.s co</color>  <color=white>.s rrglobal</color>");
            ctx.Reply("<color=white>CASTLE</color> (stand on the plot, stored on heart owner): <color=white>.s reserve</color>  <color=white>.s reserve plank 50</color>  <color=white>.s cap</color>  <color=white>.s group</color>. Not leftover. .pull ignores reserve; craft-pull/conveyors honor it.");
            ctx.Reply("<color=white>PLOT</color> (feet): <color=white>.s sal</color> salvage  <color=white>.s hf</color> heart feed  <color=white>.s cse</color> exclude this plot (owner only)");
            ctx.Reply("<color=white>CLAN</color>: <color=white>.s cs</color> all clan plots as one. <color=white>SERVER</color> admin: <color=white>.sg s</color>  <color=white>.sg sal</color>  (need adminauth). Player toggles still start off except scoop auto and .s dpl.");
            ctx.Reply("Example: <color=white>.s bagcap cotton 200</color> then <color=white>.s</color> — your bags. <color=white>.s cap cotton 200</color> — castle conveyors.");
            ctx.Reply("Example: <color=white>.s reserve plank 50</color> then <color=white>.pull plank 200</color> — pull ignores reserve. Name chests <color=white>s1</color>/<color=white>r1</color> then <color=white>.s co</color>.");
            ctx.Reply("Ambiguous names: numbered list, then <color=white>.s 2</color> or <color=white>.s pick 2</color>. <color=white>.s settings</color>  <color=white>.s conv plank</color>  <color=white>.s need</color>");
        }

        [Command(name: "pick", shortHand: "p", usage: ".s pick <number>", description: "Pick a numbered item from the last ambiguous name search.")]
        public static void PickPending(ChatCommandContext ctx, int number)
        {
            ReplayPendingPick(ctx, number);
        }

        [Command(name: "clanshare", shortHand: "cs", usage: ".s clanshare", description: "Toggle clan-wide sharing of pull/stash/find/caps/conveyors and vanilla treasury for ALL clan members and ALL clan plots. OFF by default. Items will move between clan castles.")]
        public static void ToggleClanShareCmd(ChatCommandContext ctx)
        {
            var ownerUser = ctx.Event.User;
            var ownerName = ownerUser.CharacterName.ToString();
            if (string.IsNullOrWhiteSpace(ownerName))
                ownerName = ownerUser.PlatformId.ToString();
            if (!Core.TerritoryService.TryGetClanKey(ownerUser, out var clanKey))
            {
                ctx.Reply("ClanShare is clan-wide. Join or create a clan first.");
                return;
            }
            var current = Core.TerritoryService.IsClanShareOn(ownerUser);
            var on = Core.PlayerSettings.ToggleClanShareForClan(clanKey, current);
            ctx.Reply($"ClanShare is now {(on ? "<color=green>ON</color>" : "<color=red>OFF</color>")} for the whole clan (all members, all castles, including {ownerName}'s).");
            ctx.Reply("OFF by default. When ON, pull, stash, finditem, craft-pull, cap counts, conveyors, and vanilla treasury floors use all clan plots except owner-excluded ones (.s cse). Works from the wild. New plots are included automatically. Items will move between clan castles.");
        }

        [Command(name: "csexclude", shortHand: "cse", usage: ".s cse", description: "Owner-only: exclude or include THIS plot from clan-wide ClanShare. Standing on an excluded plot is local-only.")]
        public static void ToggleClanShareExcludeCmd(ChatCommandContext ctx)
        {
            var territoryId = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
            if (territoryId < 0)
            {
                ctx.Reply("You must stand on a claimed castle plot you own to exclude it from ClanShare.");
                return;
            }
            if (!Core.TerritoryService.TryGetTerritoryOwnerPlatformId(territoryId, out var ownerId))
            {
                ctx.Reply("You must stand on a claimed castle plot you own to exclude it from ClanShare.");
                return;
            }
            if (ctx.Event.User.PlatformId != ownerId)
            {
                ctx.Reply("You must own this plot to exclude it from ClanShare. Clanmates and allies cannot exclude someone else's castle.");
                return;
            }
            var excluded = Core.PlayerSettings.ToggleTerritoryClanShareExclude(territoryId);
            if (excluded)
                ctx.Reply("This plot is now <color=red>EXCLUDED</color> from ClanShare. Other clan plots will not pull/stash/lend from it. Standing here is local-only (this castle alone).");
            else
                ctx.Reply("This plot is now <color=green>INCLUDED</color> in ClanShare (clan-wide CS still has to be ON).");
        }

        [Command(name: "guildshare", shortHand: "gs", usage: ".s guildshare", description: "Same as .s clanshare. OFF by default.")]
        public static void ToggleGuildShareCmd(ChatCommandContext ctx)
        {
            ToggleClanShareCmd(ctx);
        }

        [Command(name: "finditem", shortHand: "fi", usage: ".s fi <item>", description: "Finds the specified item in containers.")]
        public static void FindItemAlias(ChatCommandContext ctx, FoundItem item)
        {
            AdditionalCommands.FindItem(ctx, item);
        }

        [Command(name: "sortstash", shortHand: "ss", usage: ".s ss", description: "Toggles autostashing on double clicking sort button for player.")]
        public static void TogglePlayerAutoStash(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var autoStash = Core.PlayerSettings.ToggleSortStash(SteamID);
            ctx.Reply($"SortStash is {(autoStash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "rrglobal", shortHand: "rrg", usage: ".s rrglobal", description: "Toggle RR/stash off-plot. OFF by default. Does not change on-plot dest: ClanShare still dumps to all clan plots as one.")]
        public static void ToggleRrGlobal(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;
            var on = Core.PlayerSettings.ToggleRrGlobal(SteamID);
            var allow = Core.PlayerSettings.IsRrGlobalServerAllowed();
            ctx.Reply($"RR global is {(on ? "<color=green>enabled</color>" : "<color=red>disabled</color>")} (default off). Off-plot RR/stash: {(on && allow ? "<color=green>allowed</color>" : "<color=red>blocked</color>")}. On-plot dest is ClanShare (all clan plots as one), not this flag.");
            if (on && !allow)
                ctx.Reply("Server RR global is off (.sg rrglobal). Off-plot RR blocked.");
        }
       
        [Command(name: "craftpull", shortHand: "cr", usage: ".s cr", description: "Toggles right-clicking on recipes for missing ingredients.")]
        public static void TogglePlayerAutoPull(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var autoPull = Core.PlayerSettings.ToggleCraftPull(SteamID);
            ctx.Reply($"CraftPull is {(autoPull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "dontpulllast", shortHand: "dpl", usage: ".s dpl", description: "Toggles the ability to not pull the last item from a container for Logistics commands.")]
        public static void ToggleDontPullLast(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var dontPullLast = Core.PlayerSettings.ToggleDontPullLast(SteamID);
            ctx.Reply($"DontPullLast is {(dontPullLast ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "reserve", shortHand: "rsv", usage: ".s reserve [amount]", description: "Show or set the default reserve. 0 disables reserve.")]
        public static void SetReserve(ChatCommandContext ctx, int? amount = null)
        {
            if (!TryGetCastleSettingsOwner(ctx, forSet: amount != null, out var SteamID, out var ownerName))
                return;

            if (amount == null)
            {
                var current = Core.PlayerSettings.GetPullReserve(SteamID);
                ctx.Reply($"Castle reserves ({ownerName}):");
                ctx.Reply($"Default reserve is <color=white>{current}</color> (0 disables reserve).");
                var any = false;
                foreach (var (name, itemAmount) in Core.PlayerSettings.ListItemReserves(SteamID))
                {
                    if (!any)
                    {
                        ctx.Reply("Item reserves:");
                        any = true;
                    }
                    ctx.Reply($"  <color=green>{name}</color>: leave <color=white>{itemAmount}</color>");
                }
                if (!any)
                    ctx.Reply("No per-item reserves set. Use .s reserve <item> <amount> to add one.");
                return;
            }

            var reserve = Core.PlayerSettings.SetPullReserve(SteamID, amount.Value);
            ctx.Reply($"Set default reserve to <color=white>{reserve}</color> on {ownerName}'s castle. DontPullLast is {(reserve > 0 ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "reserve", shortHand: "rsv", usage: ".s reserve <item|group>", description: "Show reserve for a specific item or material group.")]
        public static void ShowItemReserve(ChatCommandContext ctx, FoundItemOrGroup target)
        {
            if (HandleAmbiguousTarget(ctx, target, PendingItemCommand.ReserveShow))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: false, out var SteamID, out var ownerName))
                return;
            if (target.IsGroup)
            {
                ReplyGroupAmounts(ctx, SteamID, ownerName, target.GroupName, isCap: false);
                return;
            }
            var name = target.Item.prefab.PrefabName();
            var effective = Core.PlayerSettings.GetPullReserve(SteamID, target.Item.prefab);
            var stores = PullService.CountAlliedStores(ctx.Event.SenderCharacterEntity, target.Item.prefab);
            var takeable = PullService.CountAlliedTakeable(ctx.Event.SenderCharacterEntity, target.Item.prefab, SteamID);
            ctx.Reply($"Reserve for <color=green>{name}</color> is <color=white>{effective}</color> on {ownerName}'s castle. Stores: <color=white>{stores}</color> (takeable <color=white>{takeable}</color>). Clear this override with <color=white>.s rsvc {name}</color> or <color=white>.s reserve \"{name}\" -1</color>.");
        }

        [Command(name: "reserve", shortHand: "rsv", usage: ".s reserve <item|group> <amount>", description: "Set reserve for an item or every item in a material group. Use -1 to clear the override.")]
        public static void SetItemReserveCmd(ChatCommandContext ctx, FoundItemOrGroup target, int amount)
        {
            if (HandleAmbiguousTarget(ctx, target, PendingItemCommand.ReserveSet, amount))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: true, out var SteamID, out var ownerName))
                return;
            if (target.IsGroup)
            {
                ApplyGroupAmount(ctx, SteamID, ownerName, target.GroupName, amount, isCap: false);
                return;
            }
            var name = target.Item.prefab.PrefabName();
            if (amount < 0)
            {
                var cleared = Core.PlayerSettings.ClearItemReserve(SteamID, target.Item.prefab);
                if (cleared)
                    ctx.Reply($"Cleared reserve override for <color=green>{name}</color> on {ownerName}'s castle; now uses the default.");
                else
                    ctx.Reply($"No reserve override for <color=green>{name}</color> on {ownerName}'s castle.");
                return;
            }

            var reserve = Core.PlayerSettings.SetItemReserve(SteamID, target.Item.prefab, name, amount);
            ctx.Reply($"Set {name} reserve to <color=white>{reserve}</color> on {ownerName}'s castle.");
        }

        [Command(name: "reserveclear", shortHand: "rsvc", usage: ".s reserveclear <item>", description: "Clear a per-item reserve override so it uses the default.")]
        public static void ClearItemReserveCmd(ChatCommandContext ctx, FoundItem item)
        {
            if (HandleAmbiguousItem(ctx, item, PendingItemCommand.ReserveClear))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: true, out var SteamID, out var ownerName))
                return;
            var name = item.prefab.PrefabName();
            var cleared = Core.PlayerSettings.ClearItemReserve(SteamID, item.prefab);
            if (cleared)
                ctx.Reply($"Cleared reserve override for <color=green>{name}</color> on {ownerName}'s castle; now uses the default.");
            else
                ctx.Reply($"No reserve override for <color=green>{name}</color> on {ownerName}'s castle.");
        }

        [Command(name: "cap", shortHand: "c", usage: ".s cap", description: "List production caps. Conveyors stop feeding an item once the plot has this many.")]
        public static void ListCaps(ChatCommandContext ctx)
        {
            if (!TryGetCastleSettingsOwner(ctx, forSet: false, out var SteamID, out var ownerName))
                return;
            var any = false;
            foreach (var (name, amount) in Core.PlayerSettings.ListItemCaps(SteamID))
            {
                if (!any)
                {
                    ctx.Reply($"Castle production caps ({ownerName}):");
                    any = true;
                }
                ctx.Reply($"  <color=green>{name}</color>: stop at <color=white>{amount}</color>");
            }
            if (!any)
                ctx.Reply($"No production caps set on {ownerName}'s castle. Use .s cap <item> <amount> to add one. 0 = make none, -1 = unlimited.");
        }

        [Command(name: "cap", shortHand: "c", usage: ".s cap <item|group>", description: "Show the production cap for a specific item or material group.")]
        public static void ShowItemCap(ChatCommandContext ctx, FoundItemOrGroup target)
        {
            if (HandleAmbiguousTarget(ctx, target, PendingItemCommand.CapShow))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: false, out var SteamID, out var ownerName))
                return;
            if (target.IsGroup)
            {
                ReplyGroupAmounts(ctx, SteamID, ownerName, target.GroupName, isCap: true);
                return;
            }
            var name = target.Item.prefab.PrefabName();
            if (!Core.PlayerSettings.TryGetItemCap(SteamID, target.Item.prefab, out var cap))
                ctx.Reply($"No production cap for <color=green>{name}</color> on {ownerName}'s castle (unlimited). Set with <color=white>.s cap \"{name}\" <amount></color>.");
            else
                ctx.Reply($"Production cap for <color=green>{name}</color> is <color=white>{cap}</color> on {ownerName}'s castle. Clear with <color=white>.s capc {name}</color> or <color=white>.s cap \"{name}\" -1</color>.");

            var ids = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(ctx.Event.SenderCharacterEntity);
            if (ids.Count > 0 && Core.ConveyorService != null)
            {
                var have = 0;
                foreach (var id in ids)
                {
                    var counts = Core.ConveyorService.CountTerritoryItems(id);
                    counts.TryGetValue(target.Item.prefab, out var c);
                    have += c;
                }
                var scope = ids.Count > 1 ? "Clan plots currently have" : "This territory currently has";
                ctx.Reply($"{scope} <color=white>{have}</color>.");
            }
        }

        [Command(name: "cap", shortHand: "c", usage: ".s cap <item|group> <amount>", description: "Set a production cap for an item or every item in a material group. 0 = make none. -1 = unlimited.")]
        public static void SetItemCapCmd(ChatCommandContext ctx, FoundItemOrGroup target, int amount)
        {
            if (HandleAmbiguousTarget(ctx, target, PendingItemCommand.CapSet, amount))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: true, out var SteamID, out var ownerName))
                return;
            if (target.IsGroup)
            {
                ApplyGroupAmount(ctx, SteamID, ownerName, target.GroupName, amount, isCap: true);
                return;
            }
            var name = target.Item.prefab.PrefabName();
            if (amount < 0)
            {
                var cleared = Core.PlayerSettings.ClearItemCap(SteamID, target.Item.prefab);
                if (cleared)
                    ctx.Reply($"Cleared production cap for <color=green>{name}</color> on {ownerName}'s castle; now unlimited.");
                else
                    ctx.Reply($"No production cap for <color=green>{name}</color> on {ownerName}'s castle.");
                return;
            }

            var cap = Core.PlayerSettings.SetItemCap(SteamID, target.Item.prefab, name, amount);
            ctx.Reply($"Set {name} production cap to <color=white>{cap}</color> on {ownerName}'s castle.");
        }

        [Command(name: "capclear", shortHand: "capc", usage: ".s capclear <item>", description: "Clear a production cap so the item is unlimited.")]
        public static void ClearItemCapCmd(ChatCommandContext ctx, FoundItem item)
        {
            if (HandleAmbiguousItem(ctx, item, PendingItemCommand.CapClear))
                return;
            if (!TryGetCastleSettingsOwner(ctx, forSet: true, out var SteamID, out var ownerName))
                return;
            var name = item.prefab.PrefabName();
            var cleared = Core.PlayerSettings.ClearItemCap(SteamID, item.prefab);
            if (cleared)
                ctx.Reply($"Cleared production cap for <color=green>{name}</color> on {ownerName}'s castle; now unlimited.");
            else
                ctx.Reply($"No production cap for <color=green>{name}</color> on {ownerName}'s castle.");
        }

        [Command(name: "group", usage: ".s group", description: "List built-in and custom material groups on this castle.")]
        public static void ListGroups(ChatCommandContext ctx)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var SteamID, out var ownerName))
                return;
            ctx.Reply($"Material groups on {ownerName}'s castle:");
            foreach (var builtIn in ItemGroupService.BuiltInNames)
            {
                if (ItemGroupService.IsDeletedBuiltIn(SteamID, builtIn))
                    continue;
                var members = ItemGroupService.ResolveMembers(SteamID, builtIn);
                var edited = ItemGroupService.HasCastleOverlay(SteamID, builtIn) ? ", edited" : "";
                ctx.Reply($"  <color=green>{builtIn}</color> (built-in{edited}, {members.Count} items)");
            }
            var anyCustom = false;
            foreach (var (name, count) in Core.PlayerSettings.ListCustomGroups(SteamID))
            {
                if (ItemGroupService.IsBuiltInName(name))
                    continue;
                if (!anyCustom)
                {
                    ctx.Reply("Custom groups:");
                    anyCustom = true;
                }
                var members = ItemGroupService.ResolveMembers(SteamID, name);
                ctx.Reply($"  <color=green>{name}</color> ({members.Count} items)");
            }
            if (!anyCustom)
                ctx.Reply("No custom groups. Use .s group create <name> then .s group <name> add <item>.");
        }

        [Command(name: "group", usage: ".s group <name>", description: "List members of a built-in or custom material group.")]
        public static void ShowGroup(ChatCommandContext ctx, string name)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var SteamID, out var ownerName))
                return;
            if (name.Trim().Equals("restore", StringComparison.OrdinalIgnoreCase))
            {
                var restored = Core.PlayerSettings.RestoreAllBuiltInGroups(SteamID, ItemGroupService.BuiltInNames);
                ctx.Reply($"Restored default groups on {ownerName}'s castle: {string.Join(", ", restored)}. Custom groups were left as-is.");
                return;
            }
            if (!ItemGroupService.TryResolveGroup(SteamID, name, out var resolved, out var isBuiltIn))
            {
                if (ItemGroupService.IsDeletedBuiltIn(SteamID, name))
                    ctx.Reply($"Group <color=green>{ItemGroupService.NormalizeName(name)}</color> is gone on {ownerName}'s castle. Use .s group restore {ItemGroupService.NormalizeName(name)} to bring it back.");
                else
                    ctx.Reply($"No group named <color=green>{name}</color> on {ownerName}'s castle.");
                return;
            }
            var members = ItemGroupService.ResolveMembers(SteamID, resolved);
            var kind = isBuiltIn
                ? (ItemGroupService.HasCastleOverlay(SteamID, resolved) ? "edited built-in" : "built-in")
                : "custom";
            ctx.Reply($"{kind} group <color=green>{resolved}</color> on {ownerName}'s castle ({members.Count} items):");
            if (members.Count == 0)
            {
                ctx.Reply("  (empty)");
                return;
            }
            ctx.Reply("  " + ItemGroupService.FormatMemberNames(members.Select(m => m.Name), 400));
        }

        [Command(name: "group", usage: ".s group create|delete|restore <name>", description: "Create a custom group, delete a group, or restore a built-in default.")]
        public static void CreateOrDeleteGroup(ChatCommandContext ctx, string action, string name)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var SteamID, out var ownerName))
                return;
            action = action.Trim().ToLowerInvariant();
            var normalized = ItemGroupService.NormalizeName(name);
            if (action is "create" or "addgroup" or "new")
            {
                if (ItemGroupService.IsBuiltInName(normalized))
                {
                    ctx.Reply($"<color=green>{normalized}</color> is a built-in group name and cannot be created.");
                    return;
                }
                if (ItemGroupService.IsExactItemName(normalized))
                {
                    ctx.Reply($"<color=green>{normalized}</color> is an item name. Use a different group name.");
                    return;
                }
                if (Core.PlayerSettings.HasItemGroup(SteamID, normalized))
                {
                    ctx.Reply($"Custom group <color=green>{normalized}</color> already exists on {ownerName}'s castle.");
                    return;
                }
                Core.PlayerSettings.CreateItemGroup(SteamID, normalized);
                ctx.Reply($"Created custom group <color=green>{normalized}</color> on {ownerName}'s castle. Add items with .s group {normalized} add <item>.");
                return;
            }
            if (action is "delete" or "remove" or "del")
            {
                if (ItemGroupService.IsBuiltInName(normalized))
                {
                    ItemGroupService.TryGetBuiltInCanonical(normalized, out normalized);
                    if (ItemGroupService.IsDeletedBuiltIn(SteamID, normalized))
                    {
                        ctx.Reply($"Group <color=green>{normalized}</color> is already gone on {ownerName}'s castle.");
                        return;
                    }
                    Core.PlayerSettings.DeleteBuiltInGroup(SteamID, normalized);
                    ctx.Reply($"Deleted built-in group <color=green>{normalized}</color> on {ownerName}'s castle. Restore with .s group restore {normalized}.");
                    return;
                }
                if (!Core.PlayerSettings.DeleteItemGroup(SteamID, normalized))
                {
                    ctx.Reply($"No custom group named <color=green>{normalized}</color> on {ownerName}'s castle.");
                    return;
                }
                ctx.Reply($"Deleted custom group <color=green>{normalized}</color> on {ownerName}'s castle.");
                return;
            }
            if (action is "restore")
            {
                if (!ItemGroupService.IsBuiltInName(normalized))
                {
                    ctx.Reply($"<color=green>{normalized}</color> is not a default group. Only built-in groups can be restored.");
                    return;
                }
                ItemGroupService.TryGetBuiltInCanonical(normalized, out normalized);
                Core.PlayerSettings.RestoreBuiltInGroup(SteamID, normalized);
                var members = ItemGroupService.ResolveMembers(SteamID, normalized);
                ctx.Reply($"Restored default group <color=green>{normalized}</color> on {ownerName}'s castle ({members.Count} items).");
                return;
            }
            ctx.Reply("Usage: .s group create <name>, .s group delete <name>, or .s group restore [name].");
        }

        [Command(name: "group", usage: ".s group <name> add|remove <item> [<item> ...]", description: "Add or remove one or more items on a group. Quote names with spaces. First edit of a built-in copies the default list.")]
        public static void ModifyGroup(ChatCommandContext ctx, string name, string action,
            string item1,
            string item2 = null,
            string item3 = null,
            string item4 = null,
            string item5 = null,
            string item6 = null,
            string item7 = null,
            string item8 = null,
            string item9 = null,
            string item10 = null,
            string item11 = null,
            string item12 = null,
            string item13 = null,
            string item14 = null,
            string item15 = null,
            string item16 = null)
        {
            action = action.Trim().ToLowerInvariant();
            if (action is not ("add" or "remove"))
            {
                ctx.Reply("Usage: .s group <name> add <item> [<item> ...] or .s group <name> remove <item> [<item> ...]. Quote names with spaces.");
                return;
            }

            var tokens = new List<string>();
            var raw = ctx.Event.Message;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var rawTokens = ItemGroupService.ParseItemTokens(raw);
                var start = 0;
                if (rawTokens.Count > 0 && rawTokens[0].StartsWith(".", StringComparison.Ordinal))
                    start = 1;
                if (start < rawTokens.Count && rawTokens[start].Equals("l", StringComparison.OrdinalIgnoreCase))
                    start++;
                else if (start < rawTokens.Count && rawTokens[start].Equals("logistics", StringComparison.OrdinalIgnoreCase))
                    start++;
                if (start < rawTokens.Count && rawTokens[start].Equals("group", StringComparison.OrdinalIgnoreCase))
                    start++;
                if (start < rawTokens.Count && rawTokens[start].Equals(name, StringComparison.OrdinalIgnoreCase))
                    start++;
                if (start < rawTokens.Count && rawTokens[start].Equals(action, StringComparison.OrdinalIgnoreCase))
                    start++;
                for (var i = start; i < rawTokens.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rawTokens[i]))
                        tokens.Add(rawTokens[i]);
                }
            }
            if (tokens.Count == 0)
            {
                foreach (var token in new[] { item1, item2, item3, item4, item5, item6, item7, item8, item9, item10, item11, item12, item13, item14, item15, item16 })
                {
                    if (!string.IsNullOrWhiteSpace(token))
                        tokens.Add(token);
                }
            }
            if (tokens.Count == 0)
            {
                ctx.Reply("Usage: .s group <name> add <item> [<item> ...] or .s group <name> remove <item> [<item> ...]. Quote names with spaces.");
                return;
            }

            ApplyGroupItemTokens(ctx, name, action, tokens);
        }

        internal static void ApplyGroupItemTokens(ChatCommandContext ctx, string name, string action, List<string> tokens)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var SteamID, out var ownerName))
                return;
            if (!TryPrepareGroupEdit(ctx, SteamID, ownerName, name, out var normalized))
                return;

            var uniqueItems = new List<(PrefabGUID prefab, string name)>();
            var seen = new HashSet<int>();
            string ambiguousToken = null;
            List<(PrefabGUID prefab, string name)> ambiguousCandidates = null;
            var missing = new List<string>();
            var extraAmbiguous = new List<string>();

            foreach (var token in tokens)
            {
                var status = FoundItemConverter.TryResolve(token, out var found, out var candidates);
                if (status == ItemResolveStatus.None)
                {
                    missing.Add(token);
                    continue;
                }
                if (status == ItemResolveStatus.Ambiguous)
                {
                    if (ambiguousToken == null)
                    {
                        ambiguousToken = token;
                        ambiguousCandidates = candidates;
                    }
                    else
                        extraAmbiguous.Add(token);
                    continue;
                }
                if (!seen.Add(found.prefab.GuidHash))
                    continue;
                var itemName = found.prefab.PrefabName();
                if (string.IsNullOrEmpty(itemName))
                    itemName = token;
                uniqueItems.Add((found.prefab, itemName));
            }

            ApplyResolvedGroupItems(ctx, normalized, action, uniqueItems, ownerAlreadyChecked: true, steamId: SteamID, ownerName: ownerName);

            if (missing.Count > 0)
                ctx.Reply("No items found matching: " + string.Join(", ", missing));
            if (extraAmbiguous.Count > 0)
                ctx.Reply("Also ambiguous (be more specific): " + string.Join(", ", extraAmbiguous));
            if (ambiguousToken != null)
            {
                PendingItemChoiceService.BeginAmbiguous(ctx.Event.User.PlatformId, ambiguousCandidates);
                var pendingKind = action is "remove" ? PendingItemCommand.GroupRemove : PendingItemCommand.GroupAdd;
                PendingItemChoiceService.AttachCommand(ctx.Event.User.PlatformId, pendingKind, groupName: normalized);
                ctx.Reply($"Multiple matches for <color=green>{ambiguousToken}</color>. Unique matches were applied.");
                PendingItemChoiceService.ReplyNumberedList(ctx);
            }
        }

        static bool TryPrepareGroupEdit(ChatCommandContext ctx, ulong steamId, string ownerName, string name, out string normalized)
        {
            normalized = ItemGroupService.NormalizeName(name);
            if (ItemGroupService.IsDeletedBuiltIn(steamId, normalized))
            {
                ItemGroupService.TryGetBuiltInCanonical(normalized, out normalized);
                ctx.Reply($"Group <color=green>{normalized}</color> is gone on {ownerName}'s castle. Use .s group restore {normalized} to bring it back.");
                return false;
            }
            var isBuiltIn = ItemGroupService.IsBuiltInName(normalized);
            if (!isBuiltIn && !Core.PlayerSettings.HasItemGroup(steamId, normalized))
            {
                ctx.Reply($"No group named <color=green>{normalized}</color> on {ownerName}'s castle. Create one with .s group create {normalized}.");
                return false;
            }
            if (isBuiltIn)
            {
                ItemGroupService.TryGetBuiltInCanonical(normalized, out normalized);
                ItemGroupService.EnsureCastleOverlay(steamId, normalized);
            }
            return true;
        }

        internal static void ApplyResolvedGroupItems(ChatCommandContext ctx, string name, string action,
            List<(PrefabGUID prefab, string name)> items,
            bool ownerAlreadyChecked = false, ulong steamId = 0, string ownerName = null)
        {
            if (!ownerAlreadyChecked)
            {
                if (!TryGetStandingCastleSettingsOwner(ctx, out steamId, out ownerName))
                    return;
            }
            if (!TryPrepareGroupEdit(ctx, steamId, ownerName, name, out var normalized))
                return;

            action = action.Trim().ToLowerInvariant();
            var changed = new List<string>();
            var skipped = new List<string>();
            var members = ItemGroupService.ResolveMembers(steamId, normalized);
            var present = new HashSet<int>(members.Select(m => m.GuidHash));

            foreach (var (prefab, itemName) in items)
            {
                if (prefab.GuidHash == 0)
                    continue;
                if (action is "add")
                {
                    if (present.Contains(prefab.GuidHash))
                    {
                        skipped.Add(itemName);
                        continue;
                    }
                    Core.PlayerSettings.AddItemToGroup(steamId, normalized, prefab, itemName);
                    present.Add(prefab.GuidHash);
                    changed.Add(itemName);
                }
                else if (action is "remove")
                {
                    if (!present.Contains(prefab.GuidHash))
                    {
                        skipped.Add(itemName);
                        continue;
                    }
                    Core.PlayerSettings.RemoveItemFromGroup(steamId, normalized, prefab);
                    present.Remove(prefab.GuidHash);
                    changed.Add(itemName);
                }
            }

            if (changed.Count > 0)
            {
                var verb = action is "remove" ? "Removed" : "Added";
                var prep = action is "remove" ? "from" : "to";
                var noun = changed.Count == 1 ? "item" : "items";
                ctx.Reply($"{verb} {changed.Count} {noun} {prep} {normalized} on {ownerName}'s castle: {ItemGroupService.FormatMemberNames(changed)}");
            }
            else if (skipped.Count > 0 && items.Count > 0)
            {
                if (action is "add")
                    ctx.Reply($"Already in {normalized} on {ownerName}'s castle: {ItemGroupService.FormatMemberNames(skipped)}");
                else
                    ctx.Reply($"Not in {normalized} on {ownerName}'s castle: {ItemGroupService.FormatMemberNames(skipped)}");
            }
        }

        static void ApplyGroupAmount(ChatCommandContext ctx, ulong steamId, string ownerName, string groupName, int amount, bool isCap)
        {
            if (!ItemGroupService.TryResolveGroup(steamId, groupName, out groupName, out _))
            {
                ctx.Reply($"Group <color=green>{groupName}</color> is gone on {ownerName}'s castle.");
                return;
            }
            var members = ItemGroupService.ResolveMembers(steamId, groupName);
            if (members.Count == 0)
            {
                ctx.Reply($"Group <color=green>{groupName}</color> on {ownerName}'s castle has no items.");
                return;
            }
            var pairs = members.Select(m => (m.Prefab, m.Name)).ToList();
            var updated = Core.PlayerSettings.ApplyGroupAmounts(steamId, pairs, isCap, amount);
            var listed = ItemGroupService.FormatMemberNames(members.Select(m => m.Name));
            if (isCap)
            {
                if (amount < 0)
                    ctx.Reply($"Cleared cap on {ownerName}'s castle for {groupName} ({updated} items): {listed}");
                else
                    ctx.Reply($"Set cap {amount} on {ownerName}'s castle for {groupName} ({updated} items): {listed}");
            }
            else
            {
                if (amount < 0)
                    ctx.Reply($"Cleared reserve override on {ownerName}'s castle for {groupName} ({updated} items): {listed}");
                else
                    ctx.Reply($"Set reserve {amount} on {ownerName}'s castle for {groupName} ({updated} items): {listed}");
            }
        }

        static void ReplyGroupAmounts(ChatCommandContext ctx, ulong steamId, string ownerName, string groupName, bool isCap)
        {
            if (!ItemGroupService.TryResolveGroup(steamId, groupName, out groupName, out _))
            {
                ctx.Reply($"Group <color=green>{groupName}</color> is gone on {ownerName}'s castle.");
                return;
            }
            var members = ItemGroupService.ResolveMembers(steamId, groupName);
            if (members.Count == 0)
            {
                ctx.Reply($"Group <color=green>{groupName}</color> on {ownerName}'s castle has no items.");
                return;
            }
            var label = isCap ? "caps" : "reserves";
            ctx.Reply($"{label} for {groupName} on {ownerName}'s castle ({members.Count} items):");
            foreach (var member in members)
            {
                if (isCap)
                {
                    if (Core.PlayerSettings.TryGetItemCap(steamId, member.Prefab, out var cap))
                        ctx.Reply($"  <color=green>{member.Name}</color>: stop at <color=white>{cap}</color>");
                    else
                        ctx.Reply($"  <color=green>{member.Name}</color>: unlimited");
                }
                else
                {
                    var leftover = Core.PlayerSettings.GetPullReserve(steamId, member.Prefab);
                    var stores = PullService.CountAlliedStores(ctx.Event.SenderCharacterEntity, member.Prefab);
                    var takeable = PullService.CountAlliedTakeable(ctx.Event.SenderCharacterEntity, member.Prefab, steamId);
                    ctx.Reply($"  <color=green>{member.Name}</color>: leave <color=white>{leftover}</color>. Stores: <color=white>{stores}</color> (takeable <color=white>{takeable}</color>)");
                }
            }
        }

        [Command(name: "autostashmissions", shortHand: "asm", usage: ".s asm", description: "Toggles autostashing for servant missions.")]
        public static void ToggleServantAutoStash(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var autoStashMissions = Core.PlayerSettings.ToggleAutoStashMissions(SteamID);
            ctx.Reply($"AutoStash for missions is {(autoStashMissions ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "conveyor", shortHand: "co", usage: ".s co", description: "Toggles the ability of sender/receiver's to move items around.")]
        public static void ToggleConveyor(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var conveyor = Core.PlayerSettings.ToggleConveyor(SteamID);
            if (conveyor) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Conveyor is {(conveyor ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "conv", usage: ".s conv <item>", description: "Troubleshoot conveyor for a crafted item (station, line, why not moving).")]
        public static void ConvTroubleshoot(ChatCommandContext ctx, FoundItem item)
        {
            if (HandleAmbiguousItem(ctx, item, PendingItemCommand.Conv))
                return;

            var standing = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            if (standing < 0)
            {
                ctx.Reply("You must stand on a castle plot to troubleshoot conveyors.");
                return;
            }

            if (Core.ConveyorService == null)
            {
                ctx.Reply("Satisvampory is not ready.");
                return;
            }

            foreach (var line in Core.ConveyorService.DescribeProductConveyor(standing, item.prefab))
                ctx.Reply(line);
        }

        [Command(name: "need", usage: ".s need", description: "Top 10 items receiving stations want. Higher tier first, then lowest stock after reserve.")]
        public static void ConveyorNeed(ChatCommandContext ctx)
        {
            var standing = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            if (standing < 0)
            {
                ctx.Reply("You must stand on a castle plot to list conveyor need.");
                return;
            }

            if (Core.ConveyorService == null)
            {
                ctx.Reply("Satisvampory is not ready.");
                return;
            }

            foreach (var line in Core.ConveyorService.DescribeConveyorNeed(standing))
                ctx.Reply(line);
        }

        static bool IsHeartOwnerOrClanmate(User player, User owner)
        {
            if (player.PlatformId == owner.PlatformId)
                return true;
            var playerClan = player.ClanEntity.GetEntityOnServer();
            var ownerClan = owner.ClanEntity.GetEntityOnServer();
            if (playerClan == Entity.Null || ownerClan == Entity.Null)
                return false;
            return playerClan.Equals(ownerClan);
        }

        [Command(name: "salvage", shortHand: "sal", usage: ".s sal", description: "Toggles salvage for the plot you are standing on. Clanmates of the heart owner can toggle it. Feeds a chest named 'salvage' into the Devourer.")]
        public static void ToggleSalvage(ChatCommandContext ctx)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var ownerPlatformId, out var ownerName, replyIfMissing: false))
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle salvage for that plot.");
                return;
            }

            var territoryId = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
            var castleHeartEntity = Core.TerritoryService.GetCastleHeart(territoryId);
            if (castleHeartEntity == Entity.Null || !castleHeartEntity.Has<UserOwner>())
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle salvage for that plot.");
                return;
            }

            var heartUserEntity = castleHeartEntity.Read<UserOwner>().Owner.GetEntityOnServer();
            if (heartUserEntity == Entity.Null || !heartUserEntity.Has<User>())
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle salvage for that plot.");
                return;
            }

            var ownerUser = heartUserEntity.Read<User>();
            if (!IsHeartOwnerOrClanmate(ctx.Event.User, ownerUser))
            {
                ctx.Reply("This is not your clan's plot.");
                return;
            }

            var salvage = Core.PlayerSettings.TogglePlotSalvage(ownerPlatformId, territoryId);
            var globalOn = Core.PlayerSettings.IsGlobalSalvageEnabled();
            if (!globalOn)
                Core.Log.LogInfo($"Salvage plot toggle t={territoryId} owner={ownerPlatformId} plot={salvage} but GLOBAL Salvage is off; ProcessSalvagers will not run.");
            if (salvage)
                Core.WorkQueue?.Enqueue(territoryId);
            var plotState = salvage ? "<color=green>enabled</color>" : "<color=red>disabled</color>";
            if (!globalOn)
                ctx.Reply($"Salvage on this plot ({ownerName}) is {plotState}, but global salvage is <color=red>Server Off</color>. An admin must enable it with .sg sal.");
            else
                ctx.Reply($"Salvage on this plot ({ownerName}) is {plotState}.");
        }

        [Command(name: "unitspawner", shortHand: "us", usage: ".s sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.")]
        public static void ToggleUnitSpawner(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var spawner = Core.PlayerSettings.ToggleUnitSpawner(SteamID);
            if (spawner) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Spawner is {(spawner ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "brazier", shortHand: "bz", usage: ".s bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.")]
        public static void ToggleBrazier(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var brazier = Core.PlayerSettings.ToggleBrazier(SteamID);
            if (brazier) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Brazier is {(brazier ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "heartfeed", shortHand: "hf", usage: ".s heartfeed", description: "Toggle castle-heart Blood Essence auto-feed on the plot you are standing on. ON by default.")]
        public static void ToggleHeartFeed(ChatCommandContext ctx)
        {
            if (!TryGetStandingCastleSettingsOwner(ctx, out var ownerPlatformId, out var ownerName, replyIfMissing: false))
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle heart auto-feed for that plot.");
                return;
            }

            var territoryId = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
            var castleHeartEntity = Core.TerritoryService.GetCastleHeart(territoryId);
            if (castleHeartEntity == Entity.Null || !castleHeartEntity.Has<UserOwner>())
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle heart auto-feed for that plot.");
                return;
            }

            var heartUserEntity = castleHeartEntity.Read<UserOwner>().Owner.GetEntityOnServer();
            if (heartUserEntity == Entity.Null || !heartUserEntity.Has<User>())
            {
                ctx.Reply("You must stand on a claimed castle plot to toggle heart auto-feed for that plot.");
                return;
            }

            var ownerUser = heartUserEntity.Read<User>();
            if (!IsHeartOwnerOrClanmate(ctx.Event.User, ownerUser))
            {
                ctx.Reply("This is not your clan's plot.");
                return;
            }

            var on = Core.PlayerSettings.ToggleHeartFeed(ownerPlatformId, territoryId);
            ctx.Reply($"Heart auto-feed on this plot ({ownerName}) is {(on ? "<color=green>ON</color>" : "<color=red>OFF</color>")} (ON by default; fills the heart fuel slots from clan treasury/unnamed, reserve honored).");
        }

        [Command(name: "silentpull", shortHand: "sp", description: "Toggles the ability to not send messages when pulling about where they came from.")]
        public static void ToggleSilentCraftPull(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var silentCraftPull = Core.PlayerSettings.ToggleSilentPull(SteamID);
            ctx.Reply($"SilentPull is {(silentCraftPull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "silentstash", shortHand: "ssh", description: "Toggles the ability to not send messages when stashing items about where they go.")]
        public static void ToggleSilentStash(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var silentStash = Core.PlayerSettings.ToggleSilentStash(SteamID);
            ctx.Reply($"SilentStash is {(silentStash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "peek", usage: ".s peek [plot]", description: "Write a live plot dump to BepInEx/config/Satisvampory/debug/res.json.", adminOnly: true)]
        public static void Peek(ChatCommandContext ctx, int plot = -1)
        {
            if (!Core.HasInitialized)
            {
                ctx.Reply("Satisvampory is not initialized yet.");
                return;
            }
            if (plot < 0)
                plot = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            try
            {
                DebugPeekService.PeekNow(plot);
                ctx.Reply($"Wrote debug dump for plot {plot} to BepInEx/config/Satisvampory/debug/res.json");
            }
            catch (Exception e)
            {
                ctx.Reply("peek failed: " + e.Message);
            }
        }

        [Command(name: "verifyqueue", shortHand: "vq", usage: ".s vq", description: "Reports the logistics work queue depth and whether your territory is queued.", adminOnly: true)]
        public static void VerifyQueue(ChatCommandContext ctx)
        {
            var territoryId = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
            var depth = Core.WorkQueue.QueueDepth;
            var queuedHere = territoryId >= 0 && Core.WorkQueue.IsQueued(territoryId);
            ctx.Reply($"Work queue depth: {depth} | Your territory: {(territoryId < 0 ? "none" : territoryId.ToString())} | " +
                      $"Queued: {(queuedHere ? "<color=green>yes</color>" : "<color=red>no</color>")}");
        }


        [Command(name: "catalog", shortHand: "ic", usage: ".s catalog", description: "Dump the live PrefabCollection item catalog to BepInEx\\Log\\item-catalog.csv without a restart.")]
        public static void DumpCatalog(ChatCommandContext ctx)
        {
            var n = ItemGroupService.DumpItemCatalog();
            ctx.Reply($"Item catalog dumped {n} items to BepInEx\\Log\\item-catalog.csv (and Satisvampory-src if present). Dest groups are already live in memory.");
            var missing = ItemGroupService.MissingRequestedNames;
            if (missing != null && missing.Count > 0)
                ctx.Reply($"Unresolved dest names ({missing.Count}): {ItemGroupService.FormatMemberNames(missing, 280)}");
            else
                ctx.Reply("Unresolved dest names: none (FakeItem_/Any* recipe placeholders omitted).");
        }

        [Command(name: "settings", shortHand: "s", usage: ".s s", description: "Displays current settings.")]
        public static void DisplaySettings(ChatCommandContext ctx)
        {
            var SteamID = ctx.Event.User.PlatformId;

            var settings = Core.PlayerSettings.GetSettings(SteamID);
            var globalSettings = Core.PlayerSettings.GetGlobalSettings();
            var leftoverLine = $"DontPullLast: {(settings.DontPullLast ? "<color=green>On</color>" : "<color=red>Off</color>")}";
            var capsLine = "";
            var salvageLine = globalSettings.Salvage
                ? "Salvage: stand on a claimed plot to view/toggle (.s sal)"
                : "Salvage: <color=red>Server Off</color>";
            if (TryGetStandingCastleSettingsOwner(ctx, out var castleId, out var castleName, replyIfMissing: false))
            {
                var castleReserve = Core.PlayerSettings.GetPullReserve(castleId);
                var overrideCount = Core.PlayerSettings.GetItemReserveOverrideCount(castleId);
                leftoverLine += $" | Castle reserves ({castleName}): <color=green>leave {castleReserve}</color>" +
                    (overrideCount > 0 ? $", {overrideCount} item override{(overrideCount == 1 ? "" : "s")}" : "");
                var capCount = Core.PlayerSettings.GetItemCapOverrideCount(castleId);
                capsLine = $"Castle caps ({castleName}): {(capCount > 0 ? $"<color=green>{capCount} item cap{(capCount == 1 ? "" : "s")}</color>" : "<color=white>none</color>")}\n";
                var territoryId = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
                var castleHeartEntity = Core.TerritoryService.GetCastleHeart(territoryId);
                var clanShareOn = false;
                if (castleHeartEntity != Entity.Null && castleHeartEntity.Has<UserOwner>())
                {
                    var heartUserEntity = castleHeartEntity.Read<UserOwner>().Owner.GetEntityOnServer();
                    if (heartUserEntity != Entity.Null && heartUserEntity.Has<User>())
                        clanShareOn = Core.TerritoryService.IsClanShareOn(heartUserEntity.Read<User>());
                }
                var excluded = Core.PlayerSettings.IsTerritoryClanShareExcluded(territoryId);
                capsLine += $"ClanShare (clan-wide): {(clanShareOn ? "<color=green>On</color>" : "<color=red>Off</color>")} (OFF by default; all members, all castles. .s cs)\n";
                capsLine += $"RR global: {(Core.PlayerSettings.IsRrGlobalEnabled(SteamID) ? "<color=green>On</color>" : "<color=red>Off</color>")} (OFF by default; only allows RR/stash off-plot. On-plot dest is ClanShare. .s rrglobal)\n";
                capsLine += $"ClanShare exclude (this plot, {castleName}): {(excluded ? "<color=red>Excluded / local-only</color>" : "<color=green>Included</color>")} (owner-only .s cse)\n";
                var plotSalvage = Core.PlayerSettings.GetPlotSalvageFlag(castleId, territoryId);
                salvageLine = !globalSettings.Salvage
                    ? $"Salvage (this plot, {castleName}): {(plotSalvage ? "<color=green>On</color>" : "<color=red>Off</color>")} | <color=red>Server Off</color>"
                    : $"Salvage (this plot, {castleName}): {(plotSalvage ? "<color=green>On</color>" : "<color=red>Off</color>")}";
                var heartFeed = Core.PlayerSettings.IsHeartFeedEnabled(castleId, territoryId);
                salvageLine += $"\nHeartFeed (this plot, {castleName}): {(heartFeed ? "<color=green>On</color>" : "<color=red>Off</color>")} (ON by default; .s hf)";
            }
            ctx.Reply("Satisvampory Settings:\n" +
                      $"SortStash: {(globalSettings.SortStash ? (settings.SortStash ? "<color=green>On</color>" : "<color=red>Off</color>") : ("<color=red>Server Off</color>"))}\n" +
                      $"Pull (Global) : {(globalSettings.Pull ? "<color=green>Server On</color>" : "<color=red>Server Off</color>")}\n" +
                      $"CraftPull: {(globalSettings.CraftPull ? (settings.CraftPull ? "<color=green>On</color>" : "<color=red>Off</color>") : "<color=red>Server Off</color>")}\n" +
                      leftoverLine + "\n" +
                      capsLine +
                      $"AutoStashMissions: {(globalSettings.AutoStashMissions ? (settings.AutoStashMissions ? "<color=green>On</color>" : "<color=red>Off</color>") : "<color=red>Server Off</color>")}\n" +
                      $"Conveyor: {(globalSettings.Conveyor ? (settings.Conveyor ? "<color=green>On</color>" : "<color=red>Off</color>") : "<color=red>Server Off</color>")}\n" +
                      ".s conv <item> troubleshoot conveyor for a crafted item (station, line, why not moving).\n" +
                      salvageLine + "\n" +
                      $"UnitSpawner: {(globalSettings.UnitSpawner ? (settings.UnitSpawner ? "<color=green>On</color>" : "<color=red>Off</color>") : "<color=red>Server Off</color>")}\n" +
                      $"Brazier: {(globalSettings.Brazier ? (settings.Brazier ? "<color=green>On</color>" : "<color=red>Off</color>") : "<color=red>Server Off</color>")}" + $" | Named: {(globalSettings.Named ? "<color=green>Server On</color>" : "<color=red>Server Off</color>")}\n" +
                      $"Silent (Pull: {(settings.SilentPull ? "<color=green>On</color>" : "<color=red>Off</color>")}" + $" | Stash: { (settings.SilentStash ? "<color=green>On</color>" : "<color=red>Off</color>")})"
                      );
        }

    }

    [CommandGroup(name: "satisvamporyglobal", shortHand: "sg")]
    public static class LogisticsGlobal
    {

        [Command(name: "sortstash", shortHand: "ss", usage: ".sg ss", description: "Toggles autostashing on double clicking sort button for player.", adminOnly: true)]
        public static void TogglePlayerAutoStash(ChatCommandContext ctx)
        {
            var autoStash = Core.PlayerSettings.ToggleSortStash();
            ctx.Reply($"Global SortStash is {(autoStash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "rrglobal", shortHand: "rrg", usage: ".sg rrglobal", description: "Server allow for off-plot RR/stash. Default ON (player .s rrglobal still defaults OFF). Does not gate on-plot ClanShare dest.", adminOnly: true)]
        public static void ToggleGlobalRrGlobal(ChatCommandContext ctx)
        {
            var allow = Core.PlayerSettings.ToggleRrGlobal();
            ctx.Reply($"Server RR global allow is {(allow ? "<color=green>enabled</color>" : "<color=red>disabled</color>")} (player .s rrglobal still defaults off). Off-plot only; on-plot dest is ClanShare.");
        }

        [Command(name: "pull", shortHand: "p", usage: ".sg p", description: "Toggles the ability to pull items from containers.", adminOnly: true)]
        public static void TogglePlayerPull(ChatCommandContext ctx)
        {
            var pull = Core.PlayerSettings.TogglePull();
            ctx.Reply($"Global Pull is {(pull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "craftpull", shortHand: "cr", usage: ".sg cr", description: "Toggles right-clicking on recipes for missing ingredients.", adminOnly: true)]
        public static void TogglePlayerAutoPull(ChatCommandContext ctx)
        {
            var autoPull = Core.PlayerSettings.ToggleCraftPull();
            ctx.Reply($"CraftPull is {(autoPull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "autostashmissions", shortHand: "asm", usage: ".sg asm", description: "Toggles autostashing for servant missions.", adminOnly: true)]
        public static void ToggleServantAutoStash(ChatCommandContext ctx)
        {
            var autoStashMissions = Core.PlayerSettings.ToggleAutoStashMissions();
            ctx.Reply($"Global AutoStash for missions is {(autoStashMissions ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "conveyor", shortHand: "co", usage: ".sg co", description: "Toggles the ability of sender/receiver's to move items around.", adminOnly: true)]
        public static void ToggleConveyor(ChatCommandContext ctx)
        {
            var conveyor = Core.PlayerSettings.ToggleConveyor();
            if (conveyor) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Global Conveyor is {(conveyor ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "salvage", shortHand: "sal", usage: ".sg sal", description: "Toggles the ability to salvage items from a chest named 'salvage'.", adminOnly: true)]
        public static void ToggleSalvage(ChatCommandContext ctx)
        {
            var salvage = Core.PlayerSettings.ToggleSalvage();
            if (salvage) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Global Salvage is {(salvage ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "unitspawner", shortHand: "us", usage: ".sg sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.", adminOnly: true)]
        public static void ToggleUnitSpawner(ChatCommandContext ctx)
        {
            var spawner = Core.PlayerSettings.ToggleUnitSpawner();
            if (spawner) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Global Spawner is {(spawner ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "brazier", shortHand: "bz", usage: ".sg bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.", adminOnly: true)]
        public static void ToggleBrazier(ChatCommandContext ctx)
        {
            var brazier = Core.PlayerSettings.ToggleBrazier();
            if (brazier) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Global Brazier is {(brazier ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "named", shortHand:"nam", usage: ".sg nam", description: "Toggles the ability allow night/proximity controlled braziers.", adminOnly: true)]
        public static void ToggleSolar(ChatCommandContext ctx)
        {
            var solar = Core.PlayerSettings.ToggleSolar();
            ctx.Reply($"Global Named is {(solar ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "trash", usage: ".sg trash", description:"Toggles the ability to allowed trashes to delete contents.", adminOnly: true )]
        public static void ToggleTrash(ChatCommandContext ctx)
        {
            var trash = Core.PlayerSettings.ToggleTrash();
            ctx.Reply($"Global Trash is {(trash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");
        }

        [Command(name: "settings", shortHand: "s", usage: ".sg s", description: "Displays current settings.", adminOnly: true)]
        public static void DisplaySettings(ChatCommandContext ctx)
        {
            var settings = Core.PlayerSettings.GetGlobalSettings();
            ctx.Reply("Satisvampory Global settings:\n" +
                      $"SortStash: {(settings.SortStash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Pull: {(settings.Pull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"CraftPull: {(settings.CraftPull ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"AutoStashMissions: {(settings.AutoStashMissions ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Conveyor: {(settings.Conveyor ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Salvage: {(settings.Salvage ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"UnitSpawner: {(settings.UnitSpawner ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Brazier: {(settings.Brazier ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Named: {(settings.Named ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}\n" +
                      $"Trash: {(settings.Trash ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}"

                      );
        }
    }

    public static class AdditionalCommands
    {
        [Command(name: "stash", description: "Stashes all items in your inventory.")]
        public static void StashInventory(ChatCommandContext ctx)
        {
            Core.Stash.StashCharacterInventory(ctx.Event.SenderCharacterEntity);
        }

        [Command(name: "l", usage: ".s <number>", description: "Pick a numbered item from the last ambiguous name search.")]
        public static void PickItemByNumber(ChatCommandContext ctx, int number)
        {
            LogisticsCommands.ReplayPendingPick(ctx, number);
        }

        [Command(name: "pull", description: "Pulls specified item from containers.")]
        public static void PullItem(ChatCommandContext ctx, FoundItem item, int quantity = 1)
        {
            if (LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.Pull, quantity))
                return;
            PullService.PullItem(ctx.Event.SenderCharacterEntity, item.prefab, quantity);
        }

        [Command(name: "finditem", shortHand: "fi", description: "Finds the specified item in containers")]
        public static void FindItem(ChatCommandContext ctx, FoundItem item)
        {
            if (LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.FindItem))
                return;
            Core.Stash.ReportWhereItemIsLocated(ctx.Event.SenderCharacterEntity, item.prefab);
        }

        [Command(name: "findchest", shortHand: "fc", description: "Finds the specified chest by name")]
        public static void FindChest(ChatCommandContext ctx, string name)
        {
            Core.Stash.ReportWhereChestIsLocated(ctx.Event.SenderCharacterEntity, name);
        }

        [Command(name: "emptytrash", description: "Empties all items in your trash containers.", adminOnly: true)]
        public static void EmptyTrash(ChatCommandContext ctx)
        {
            Core.Trash.EmptyTrash(ctx.Event.SenderCharacterEntity);
        }

        [Command(name: "adminstash", description: "Spawns in items to stash to the current territory.", adminOnly: true)]
        public static void AdminStash(ChatCommandContext ctx, FoundItem item, int quantity = 1)
        {
            if (LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.AdminStash, quantity))
                return;
            Core.Stash.AdminStash(ctx.Event.SenderCharacterEntity, item.prefab, quantity);
        }
    }
}
