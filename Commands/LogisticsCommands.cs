using Satisvampory;
using System.Linq;
using Satisvampory.Commands.Converters;
using Satisvampory.Services;

namespace Satisvampory.Commands
{
    [CommandGroup(name: "satisvampory", shortHand: "s")]
    public static class LogisticsCommands
    {

        internal static void ReplyOnOff(ChatCommandContext ctx, string label, bool on)
            => ctx.Reply($"{label} is {(on ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");

        internal static string OnOff(bool on) => on ? "<color=green>On</color>" : "<color=red>Off</color>";
        static string Combined(bool server, bool player) => server ? OnOff(player) : "<color=red>Server Off</color>";

        internal static bool HandleAmbiguousItem(ChatCommandContext ctx, FoundItem item, PendingItemCommand command, int amount = 0, string groupName = null)
        {
            if (!item.Ambiguous) return false;
            PendingItemChoiceService.AttachCommand(ctx.Event.User.PlatformId, command, amount, groupName);
            PendingItemChoiceService.ReplyNumberedList(ctx);
            return true;
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
                    CastleCommands.ShowItemReserve(ctx, FoundItemOrGroup.FromItem(item));
                    break;
                case PendingItemCommand.ReserveSet:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.SetItemReserveCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
                    break;
                case PendingItemCommand.ReserveClear:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.ClearItemReserveCmd(ctx, item);
                    break;
                case PendingItemCommand.CapShow:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.ShowItemCap(ctx, FoundItemOrGroup.FromItem(item));
                    break;
                case PendingItemCommand.CapSet:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.SetItemCapCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
                    break;
                case PendingItemCommand.CapClear:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.ClearItemCapCmd(ctx, item);
                    break;
                case PendingItemCommand.GroupAdd:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.ApplyResolvedGroupItems(ctx, pending.GroupName, "add", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
                    break;
                case PendingItemCommand.GroupRemove:
                    ctx.Reply($"Using {name}.");
                    CastleCommands.ApplyResolvedGroupItems(ctx, pending.GroupName, "remove", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
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
                    CastleCommands.ConvTroubleshoot(ctx, item);
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
    

        [Command(name: "verifyqueue", shortHand: "vq", usage: ".s vq", description: "Reports the logistics work queue depth and whether your territory is queued.", adminOnly: true)]
        public static void VerifyQueue(ChatCommandContext ctx)
        {
            var plot = Core.TerritoryService.GetTerritoryId(ctx.Event.SenderCharacterEntity);
            var q = Core.WorkQueue;
            var here = plot >= 0 && q.IsQueued(plot);
            ctx.Reply($"Queue {q.QueueDepth} deep. Standing plot {(plot < 0 ? "none" : plot.ToString())} is {(here ? "<color=green>queued</color>" : "<color=red>idle</color>")}.");
        }


        [Command(name: "settings", shortHand: "s", usage: ".s s", description: "Displays current settings.")]
        public static void DisplaySettings(ChatCommandContext ctx)
        {
            var steam = ctx.Event.User.PlatformId;

            var settings = Core.PlayerSettings.GetSettings(steam);
            var globalSettings = Core.PlayerSettings.GetGlobalSettings();
            var leftoverLine = $"DontPullLast: {(settings.DontPullLast ? "<color=green>On</color>" : "<color=red>Off</color>")}";
            var capsLine = "";
            var salvageLine = globalSettings.Salvage
                ? "Salvage: stand on a claimed plot to view/toggle (.s sal)"
                : "Salvage: <color=red>Server Off</color>";
            if (ItemGroupService.TryGetStandingCastleSettingsOwner(ctx, out var castleId, out var castleName, replyIfMissing: false))
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
                capsLine += $"RR global: {(Core.PlayerSettings.IsRrGlobalEnabled(steam) ? "<color=green>On</color>" : "<color=red>Off</color>")} (OFF by default; only allows RR/stash off-plot. On-plot dest is ClanShare. .s rrglobal)\n";
                capsLine += $"ClanShare exclude (this plot, {castleName}): {(excluded ? "<color=red>Excluded / local-only</color>" : "<color=green>Included</color>")} (owner-only .s cse)\n";
                var plotSalvage = Core.PlayerSettings.GetPlotSalvageFlag(castleId, territoryId);
                salvageLine = !globalSettings.Salvage
                    ? $"Salvage (this plot, {castleName}): {(plotSalvage ? "<color=green>On</color>" : "<color=red>Off</color>")} | <color=red>Server Off</color>"
                    : $"Salvage (this plot, {castleName}): {(plotSalvage ? "<color=green>On</color>" : "<color=red>Off</color>")}";
                var heartFeed = Core.PlayerSettings.IsHeartFeedEnabled(castleId, territoryId);
                salvageLine += $"\nHeartFeed (this plot, {castleName}): {(heartFeed ? "<color=green>On</color>" : "<color=red>Off</color>")} (ON by default; .s hf)";
            }
            ctx.Reply(string.Join("\n",
                "Satisvampory Settings:",
                "SortStash: " + Combined(globalSettings.SortStash, settings.SortStash),
                "Pull (server): " + (globalSettings.Pull ? "<color=green>Server On</color>" : "<color=red>Server Off</color>"),
                "CraftPull: " + Combined(globalSettings.CraftPull, settings.CraftPull),
                leftoverLine,
                capsLine.TrimEnd(),
                "AutoStashMissions: " + Combined(globalSettings.AutoStashMissions, settings.AutoStashMissions),
                "Conveyor: " + Combined(globalSettings.Conveyor, settings.Conveyor),
                "ConveyorLoops: " + OnOff(globalSettings.ConveyorLoops) + " (admin .sg convloop; default OFF)",
                ".s conv <item> troubleshoot conveyor for a crafted item (station, line, why not moving).",
                salvageLine,
                "UnitSpawner: " + Combined(globalSettings.UnitSpawner, settings.UnitSpawner),
                "Brazier: " + Combined(globalSettings.Brazier, settings.Brazier) + " | Named: " + (globalSettings.Named ? "<color=green>Server On</color>" : "<color=red>Server Off</color>"),
                "Silent pull " + OnOff(settings.SilentPull) + " | stash " + OnOff(settings.SilentStash)
            ));
        }
    
    }

    [CommandGroup(name: "satisvampory", shortHand: "s")]
    public static class PlayerToggles
    {
        [Command(name: "sortstash", shortHand: "ss", usage: ".s ss", description: "Toggles autostashing on double clicking sort button for player.")]
        public static void SortStash(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "SortStash", Core.PlayerSettings.ToggleSortStash(ctx.Event.User.PlatformId));

        [Command(name: "craftpull", shortHand: "cr", usage: ".s cr", description: "Toggles right-clicking on recipes for missing ingredients.")]
        public static void CraftPull(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "CraftPull", Core.PlayerSettings.ToggleCraftPull(ctx.Event.User.PlatformId));

        [Command(name: "dontpulllast", shortHand: "dpl", usage: ".s dpl", description: "Toggles the ability to not pull the last item from a container for Logistics commands.")]
        public static void DontPullLast(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "DontPullLast", Core.PlayerSettings.ToggleDontPullLast(ctx.Event.User.PlatformId));

        [Command(name: "autostashmissions", shortHand: "asm", usage: ".s asm", description: "Toggles autostashing for servant missions.")]
        public static void AutoStashMissions(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "AutoStash for missions", Core.PlayerSettings.ToggleAutoStashMissions(ctx.Event.User.PlatformId));

        [Command(name: "conveyor", shortHand: "co", usage: ".s co", description: "Toggles the ability of sender/receiver's to move items around.")]
        public static void Conveyor(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleConveyor(ctx.Event.User.PlatformId);
            if (on)
                Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Conveyor", on);
        }

        [Command(name: "unitspawner", shortHand: "us", usage: ".s sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.")]
        public static void UnitSpawner(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleUnitSpawner(ctx.Event.User.PlatformId);
            if (on)
                Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Spawner", on);
        }

        [Command(name: "brazier", shortHand: "bz", usage: ".s bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.")]
        public static void Brazier(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleBrazier(ctx.Event.User.PlatformId);
            if (on)
                Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Brazier", on);
        }

        [Command(name: "silentpull", shortHand: "sp", description: "Toggles the ability to not send messages when pulling about where they came from.")]
        public static void SilentPull(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "SilentPull", Core.PlayerSettings.ToggleSilentPull(ctx.Event.User.PlatformId));

        [Command(name: "silentstash", shortHand: "ssh", description: "Toggles the ability to not send messages when stashing items about where they go.")]
        public static void SilentStash(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "SilentStash", Core.PlayerSettings.ToggleSilentStash(ctx.Event.User.PlatformId));
    }

    [CommandGroup(name: "satisvamporyglobal", shortHand: "sg")]
    public static class LogisticsGlobal
    {

        [Command(name: "sortstash", shortHand: "ss", usage: ".sg ss", description: "Toggles autostashing on double clicking sort button for player.", adminOnly: true)]
        public static void GlobalSortStash(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global SortStash", Core.PlayerSettings.ToggleSortStash());

        [Command(name: "rrglobal", shortHand: "rrg", usage: ".sg rrglobal", description: "Server allow for off-plot RR/stash. Default ON (player .s rrglobal still defaults OFF). Does not gate on-plot ClanShare dest.", adminOnly: true)]
        public static void ToggleGlobalRrGlobal(ChatCommandContext ctx)
        {
            var allow = Core.PlayerSettings.ToggleRrGlobal();
            ctx.Reply($"Server RR global allow is {(allow ? "<color=green>enabled</color>" : "<color=red>disabled</color>")} (player .s rrglobal still defaults off). Off-plot only; on-plot dest is ClanShare.");
        }

        [Command(name: "pull", shortHand: "p", usage: ".sg p", description: "Toggles the ability to pull items from containers.", adminOnly: true)]
        public static void GlobalPull(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global Pull", Core.PlayerSettings.TogglePull());

        [Command(name: "craftpull", shortHand: "cr", usage: ".sg cr", description: "Toggles right-clicking on recipes for missing ingredients.", adminOnly: true)]
        public static void GlobalCraftPull(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "CraftPull", Core.PlayerSettings.ToggleCraftPull());

        [Command(name: "autostashmissions", shortHand: "asm", usage: ".sg asm", description: "Toggles autostashing for servant missions.", adminOnly: true)]
        public static void GlobalAutoStashMissions(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global AutoStash for missions", Core.PlayerSettings.ToggleAutoStashMissions());

        [Command(name: "conveyor", shortHand: "co", usage: ".sg co", description: "Toggles the ability of sender/receiver's to move items around.", adminOnly: true)]
        public static void ToggleConveyor(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleConveyor();
            if (on) Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Global Conveyor", on);
        }

        [Command(name: "convloop", shortHand: "cloop", usage: ".sg convloop", description: "Allow s# chest → r# chest loops (dest is also s# on the same group). Default OFF.", adminOnly: true)]
        public static void ToggleConveyorLoops(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleConveyorLoops();
            if (on) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Conveyor chest loops are {(on ? "<color=green>ON</color>" : "<color=red>OFF</color>")} (default OFF). OFF: s# chests fill r# chests unless that dest is also s# on the same group (cycle). ON: s#r# buffers may trade.");
        }

        [Command(name: "salvage", shortHand: "sal", usage: ".sg sal", description: "Toggles the ability to salvage items from a chest named 'salvage'.", adminOnly: true)]
        public static void ToggleSalvage(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleSalvage();
            if (on) Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Global Salvage", on);
        }

        [Command(name: "unitspawner", shortHand: "us", usage: ".sg sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.", adminOnly: true)]
        public static void GlobalUnitSpawner(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleUnitSpawner();
            if (on) Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Global Spawner", on);
        }

        [Command(name: "brazier", shortHand: "bz", usage: ".sg bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.", adminOnly: true)]
        public static void GlobalBrazier(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleBrazier();
            if (on) Core.WorkQueue?.EnqueueAll();
            LogisticsCommands.ReplyOnOff(ctx, "Global Brazier", on);
        }

        [Command(name: "named", shortHand:"nam", usage: ".sg nam", description: "Toggles the ability allow night/proximity controlled braziers.", adminOnly: true)]
        public static void GlobalNamed(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global Named", Core.PlayerSettings.ToggleSolar());

        [Command(name: "trash", usage: ".sg trash", description:"Toggles the ability to allowed trashes to delete contents.", adminOnly: true )]
        public static void GlobalTrash(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global Trash", Core.PlayerSettings.ToggleTrash());

        [Command(name: "settings", shortHand: "s", usage: ".sg s", description: "Displays current settings.", adminOnly: true)]
        public static void ShowGlobal(ChatCommandContext ctx)
        {
            var g = Core.PlayerSettings.GetGlobalSettings();
            ctx.Reply(string.Join("\n",
                "Satisvampory server flags:",
                "SortStash " + LogisticsCommands.OnOff(g.SortStash),
                "Pull " + LogisticsCommands.OnOff(g.Pull),
                "CraftPull " + LogisticsCommands.OnOff(g.CraftPull),
                "AutoStashMissions " + LogisticsCommands.OnOff(g.AutoStashMissions),
                "Conveyor " + LogisticsCommands.OnOff(g.Conveyor),
                "ConveyorLoops " + LogisticsCommands.OnOff(g.ConveyorLoops) + " (default OFF; .sg convloop)",
                "Salvage " + LogisticsCommands.OnOff(g.Salvage),
                "UnitSpawner " + LogisticsCommands.OnOff(g.UnitSpawner),
                "Brazier " + LogisticsCommands.OnOff(g.Brazier),
                "Named " + LogisticsCommands.OnOff(g.Named),
                "Trash " + LogisticsCommands.OnOff(g.Trash)
            ));
        }
    }

    public static class AdditionalCommands
    {
        [Command(name: "stash", description: "Stashes all items in your inventory.")]
        public static void StashInventory(ChatCommandContext ctx)
            => Core.Stash.StashCharacterInventory(ctx.Event.SenderCharacterEntity);

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

        [Command(name: "finditem", shortHand: "fi", description: "Finds the item in chests. Shows the plot you are standing on. ClanShare ON: groups by plot and heart level.")]
        public static void FindItem(ChatCommandContext ctx, FoundItem item)
        {
            if (LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.FindItem))
                return;
            Core.Stash.ReportWhereItemIsLocated(ctx.Event.SenderCharacterEntity, item.prefab);
        }

        [Command(name: "findchest", shortHand: "fc", description: "Finds chests by name. Shows the plot you are standing on. ClanShare ON: groups by plot and heart level.")]
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
