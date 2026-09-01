using Satisvampory;
using Satisvampory.Commands.Converters;
using Satisvampory.Services;
using Steamworks;
using VampireCommandFramework;
using System.Collections.Generic;
using System.Linq;
using Stunlock.Core;

namespace Satisvampory.Commands;
    [CommandGroup(name: "satisvampory", shortHand: "s")]
    public static class LogisticsCommands
    {

        internal static void ReplyOnOff(ChatCommandContext ctx, string label, bool on)
            => ctx.Reply($"{label} is {(on ? "<color=green>enabled</color>" : "<color=red>disabled</color>")}.");

        internal static void FlipWake(ChatCommandContext ctx, string label, bool on) { if (on) Core.WorkQueue?.EnqueueAll(); ReplyOnOff(ctx, label, on); }

        internal static string OnOff(bool on) => on ? "<color=green>On</color>" : "<color=red>Off</color>";
        internal static string CombinedFlag(bool server, bool player) => server ? OnOff(player) : "<color=red>Server Off</color>";

        internal static bool HandleAmbiguousItem(ChatCommandContext ctx, FoundItem item, PendingItemCommand command, int amount = 0, string groupName = null) { if (!item.Ambiguous) return false; PendingItemChoiceService.AttachCommand(ctx.Event.User.PlatformId, command, amount, groupName); PendingItemChoiceService.ReplyNumberedList(ctx); return true; }

        internal static void ReplayPendingPick(ChatCommandContext ctx, int number)
        {
            if (ClanThroneServants.TryPickNumber(ctx.Event.SenderCharacterEntity, ctx.Event.User.PlatformId, number, out var throneReply))
            { ctx.Reply(throneReply); return; }
            if (!PendingItemChoiceService.TryPick(ctx.Event.User.PlatformId, number, out var pending, out var picked, out var error))
            { ctx.Reply(error); return; }
            var item = new FoundItem(picked.Prefab);
            var name = string.IsNullOrEmpty(picked.Name) ? item.prefab.PrefabName() : picked.Name;
            var cmd = pending.Command;
            if (cmd == PendingItemCommand.Pull) { ctx.Reply($"Using {name}. Pulling {pending.Amount}."); InventoryCommands.PullItem(ctx, item, pending.Amount); return; }
            ctx.Reply($"Using {name}.");
            if (cmd == PendingItemCommand.ReserveShow) CastleCommands.ShowItemReserve(ctx, FoundItemOrGroup.FromItem(item));
            else if (cmd == PendingItemCommand.ReserveSet) CastleCommands.SetItemReserveCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
            else if (cmd == PendingItemCommand.ReserveClear) CastleCommands.ClearItemReserveCmd(ctx, item);
            else if (cmd == PendingItemCommand.CapShow) CastleCommands.ShowItemCap(ctx, FoundItemOrGroup.FromItem(item));
            else if (cmd == PendingItemCommand.CapSet) CastleCommands.SetItemCapCmd(ctx, FoundItemOrGroup.FromItem(item), pending.Amount);
            else if (cmd == PendingItemCommand.CapClear) CastleCommands.ClearItemCapCmd(ctx, item);
            else if (cmd == PendingItemCommand.GroupAdd) CastleCommands.ApplyResolvedGroupItems(ctx, pending.GroupName, "add", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
            else if (cmd == PendingItemCommand.GroupRemove) CastleCommands.ApplyResolvedGroupItems(ctx, pending.GroupName, "remove", new List<(PrefabGUID prefab, string name)> { (item.prefab, name) });
            else if (cmd == PendingItemCommand.FindItem) InventoryCommands.LocateItem(ctx, item);
            else if (cmd == PendingItemCommand.AdminStash) InventoryCommands.GiveIntoPlot(ctx, item, pending.Amount);
            else if (cmd == PendingItemCommand.Conv) CastleCommands.ConvTroubleshoot(ctx, item);
            else if (cmd == PendingItemCommand.ExcludeToggle) ScoopCommands.DoExcludeToggle(ctx, item);
            else if (cmd == PendingItemCommand.BagCapShow) ScoopCommands.DoBagCapShow(ctx, item);
            else if (cmd == PendingItemCommand.BagCapSet) ScoopCommands.DoBagCapSet(ctx, item, pending.Amount);
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
        public static void DisplaySettings(ChatCommandContext ctx) => ctx.Reply(string.Join("\n", CastleCommands.SettingsLines(ctx)));
    
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
        public static void Conveyor(ChatCommandContext ctx) =>
            LogisticsCommands.FlipWake(ctx, "Conveyor", Core.PlayerSettings.ToggleConveyor(ctx.Event.User.PlatformId));

        [Command(name: "unitspawner", shortHand: "us", usage: ".s sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.")]
        public static void UnitSpawner(ChatCommandContext ctx) =>
            LogisticsCommands.FlipWake(ctx, "Spawner", Core.PlayerSettings.ToggleUnitSpawner(ctx.Event.User.PlatformId));

        [Command(name: "brazier", shortHand: "bz", usage: ".s bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.")]
        public static void Brazier(ChatCommandContext ctx) =>
            LogisticsCommands.FlipWake(ctx, "Brazier", Core.PlayerSettings.ToggleBrazier(ctx.Event.User.PlatformId));

        [Command(name: "silentpull", shortHand: "sp", description: "Hide dest-chest names when pulling.")]
        public static void SilentPull(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "SilentPull", Core.PlayerSettings.ToggleSilentPull(ctx.Event.User.PlatformId));

        [Command(name: "silentstash", shortHand: "ssh", description: "Hide dest-chest names when stashing.")]
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
        public static void GlobalConveyor(ChatCommandContext ctx) => LogisticsCommands.FlipWake(ctx, "Global Conveyor", Core.PlayerSettings.ToggleConveyor());

        [Command(name: "convloop", shortHand: "cloop", usage: ".sg convloop", description: "Allow s# chest → r# chest loops (dest is also s# on the same group). Default OFF.", adminOnly: true)]
        public static void ToggleConveyorLoops(ChatCommandContext ctx)
        {
            var on = Core.PlayerSettings.ToggleConveyorLoops();
            if (on) Core.WorkQueue?.EnqueueAll();
            ctx.Reply($"Conveyor chest loops are {(on ? "<color=green>ON</color>" : "<color=red>OFF</color>")} (default OFF). OFF: s# chests fill r# chests unless that dest is also s# on the same group (cycle). ON: s#r# buffers may trade.");
        }

        [Command(name: "salvage", shortHand: "sal", usage: ".sg sal", description: "Toggles the ability to salvage items from a chest named 'salvage'.", adminOnly: true)]
        public static void GlobalSalvage(ChatCommandContext ctx) => LogisticsCommands.FlipWake(ctx, "Global Salvage", Core.PlayerSettings.ToggleSalvage());

        [Command(name: "unitspawner", shortHand: "us", usage: ".sg sp", description: "Toggles the ability to fill unit stations from a chest named 'spawner'.", adminOnly: true)]
        public static void GlobalUnitSpawner(ChatCommandContext ctx) => LogisticsCommands.FlipWake(ctx, "Global Spawner", Core.PlayerSettings.ToggleUnitSpawner());

        [Command(name: "brazier", shortHand: "bz", usage: ".sg bz", description: "Toggles the ability to fill braziers from a chest named 'brazier'.", adminOnly: true)]
        public static void GlobalBrazier(ChatCommandContext ctx) => LogisticsCommands.FlipWake(ctx, "Global Brazier", Core.PlayerSettings.ToggleBrazier());

        [Command(name: "named", shortHand:"nam", usage: ".sg nam", description: "Toggles the ability allow night/proximity controlled braziers.", adminOnly: true)]
        public static void GlobalNamed(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global Named", Core.PlayerSettings.ToggleSolar());

        [Command(name: "trash", usage: ".sg trash", description:"Toggles the ability to allowed trashes to delete contents.", adminOnly: true )]
        public static void GlobalTrash(ChatCommandContext ctx)
            => LogisticsCommands.ReplyOnOff(ctx, "Global Trash", Core.PlayerSettings.ToggleTrash());

        [Command(name: "settings", shortHand: "s", usage: ".sg s", description: "Displays current settings.", adminOnly: true)]
        public static void ShowGlobal(ChatCommandContext ctx) { var g = Core.PlayerSettings.GetGlobalSettings(); ctx.Reply("Satisvampory server flags:\nSortStash " + LogisticsCommands.OnOff(g.SortStash) + "\nPull " + LogisticsCommands.OnOff(g.Pull) + "\nCraftPull " + LogisticsCommands.OnOff(g.CraftPull) + "\nAutoStashMissions " + LogisticsCommands.OnOff(g.AutoStashMissions) + "\nConveyor " + LogisticsCommands.OnOff(g.Conveyor) + "\nConveyorLoops " + LogisticsCommands.OnOff(g.ConveyorLoops) + " (default OFF; .sg convloop)\nSalvage " + LogisticsCommands.OnOff(g.Salvage) + "\nUnitSpawner " + LogisticsCommands.OnOff(g.UnitSpawner) + "\nBrazier " + LogisticsCommands.OnOff(g.Brazier) + "\nNamed " + LogisticsCommands.OnOff(g.Named) + "\nTrash " + LogisticsCommands.OnOff(g.Trash)); }
    }

    public static class InventoryCommands
    {
        [Command(name: "stash", description: "Dump inventory into dest chests (ClanShare island if .s cs on).")]
        public static void StashNow(ChatCommandContext ctx)
            => Core.Stash.StashCharacterInventory(ctx.Event.SenderCharacterEntity);

        [Command(name: "l", usage: ".s <number>", description: "Pick a numbered item from the last ambiguous name search.")]
        public static void PickItemByNumber(ChatCommandContext ctx, int number) =>
            LogisticsCommands.ReplayPendingPick(ctx, number);

        [Command(name: "pull", description: "Pull an item from dest chests on this plot or clan island.")]
        public static void PullItem(ChatCommandContext ctx, FoundItem item, int quantity = 1) { if (!LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.Pull, quantity)) PlayerWithdraw.Pull(ctx.Event.SenderCharacterEntity, item.prefab, quantity); }

        [Command(name: "finditem", shortHand: "fi", description: "Finds the item in chests. Shows the plot you are standing on. ClanShare ON: groups by plot and heart level.")]
        public static void LocateItem(ChatCommandContext ctx, FoundItem item) {
            if (!LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.FindItem))
                Core.Stash.ShowItems(ctx.Event.SenderCharacterEntity, item.prefab);
        }

        [Command(name: "findchest", shortHand: "fc", description: "Finds chests by name. Shows the plot you are standing on. ClanShare ON: groups by plot and heart level.")]
        public static void LocateChest(ChatCommandContext ctx, string name) =>
            Core.Stash.ShowChests(ctx.Event.SenderCharacterEntity, name);

        [Command(name: "emptytrash", description: "Empty every trash chest on this plot.", adminOnly: true)]
        public static void AshTrash(ChatCommandContext ctx) =>
            Core.Trash.AshAll(ctx.Event.SenderCharacterEntity);

        [Command(name: "adminstash", description: "Admin: spawn items into the standing plot dests.", adminOnly: true)]
        public static void GiveIntoPlot(ChatCommandContext ctx, FoundItem item, int quantity = 1) {
            if (!LogisticsCommands.HandleAmbiguousItem(ctx, item, PendingItemCommand.AdminStash, quantity))
                AdminGive.IntoPlot(ctx.Event.SenderCharacterEntity, item.prefab, quantity);
        }
    }
