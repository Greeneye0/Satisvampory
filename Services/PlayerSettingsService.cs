using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System;
using Stunlock.Core;

namespace Satisvampory.Services;
    internal enum CapMode { Bags = 0, Guild = 1 }
    internal enum AutoFilter { Around = 0, All = 1 }
    internal enum NotifyMode { Off = 0, Manual = 1, On = 2 }

    /// <summary>Owns playerSettings.json and every flag/reserve/cap/clan/group row.</summary>
    internal class PlayerSettingsService
    {
        public bool IsSortStashEnabled(ulong playerId) => ReadFlag(playerId, static s => s.SortStash, requireGlobal: true);

        public bool ToggleSortStash(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.SortStash, static (s, v) => { s.SortStash = v; return s; });

        public bool TogglePull() => FlipFlag(WorldId, static s => s.Pull, static (s, v) => { s.Pull = v; return s; });

        public bool IsPullEnabled() => !ReadFlag(WorldId, static s => s.Pull);

        public bool ToggleTrash() => FlipFlag(WorldId, static s => s.Trash, static (s, v) => { s.Trash = v; return s; });

        public bool IsTrashEnabled() => !ReadFlag(WorldId, static s => s.Trash);

        public bool IsCraftPullEnabled(ulong playerId) => ReadFlag(playerId, static s => s.CraftPull, requireGlobal: true);

        public bool ToggleCraftPull(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.CraftPull, static (s, v) => { s.CraftPull = v; return s; });

        public bool IsDontPullLastEnabled(ulong playerId) => ReadFlag(playerId, static s => s.DontPullLast);

        public bool ToggleDontPullLast(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.DontPullLast, static (s, v) => { s.DontPullLast = v; return s; });

        public bool IsAutoStashMissionsEnabled(ulong playerId) => ReadFlag(playerId, static s => s.AutoStashMissions, requireGlobal: true);

        public bool ToggleAutoStashMissions(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.AutoStashMissions, static (s, v) => { s.AutoStashMissions = v; return s; });

        public bool IsRepeatHuntEnabled() => TryRow(WorldId, out var world) && world.RepeatHunt;

        public bool ToggleRepeatHunt()
        {
            var on = FlipFlag(WorldId, static s => s.RepeatHunt, static (s, v) => { s.RepeatHunt = v; return s; });
            if (on)
                RepeatHunts.CaptureActiveMissions();
            RepeatHunts.PublishClientState();
            return on;
        }

        public int GetRepeatHuntMaxSuccess()
        {
            if (!TryRow(WorldId, out var world) || world.RepeatHuntMaxSuccess <= 0)
                return 99;
            if (world.RepeatHuntMaxSuccess > 100)
                return 100;
            return world.RepeatHuntMaxSuccess;
        }

        public int SetRepeatHuntMaxSuccess(int percent)
        {
            if (percent < 1)
                percent = 1;
            if (percent > 100)
                percent = 100;
            var row = Snapshot(WorldId, true);
            row.RepeatHuntMaxSuccess = percent;
            playerSettings[WorldId] = row;
            MarkDirty();
            RepeatHunts.PublishClientState();
            return percent;
        }

        public bool TryWorldOffPlots(out List<string> off)
        {
            off = null;
            if (!TryRow(WorldId, out var world))
                return false;
            off = world.RepeatHuntOffPlots;
            return true;
        }

        public bool IsRepeatHuntPlotOn(int plot)
        {
            if (!IsRepeatHuntEnabled())
                return false;
            if (!TryRow(WorldId, out var world) || world.RepeatHuntOffPlots == null)
                return true;
            return !world.RepeatHuntOffPlots.Contains("t" + plot);
        }

        public void SetRepeatHuntPlot(int plot, bool on)
        {
            var row = Snapshot(WorldId, true);
            var list = row.RepeatHuntOffPlots != null ? new List<string>(row.RepeatHuntOffPlots) : new List<string>();
            var key = "t" + plot;
            if (on)
                list.Remove(key);
            else if (!list.Contains(key))
                list.Add(key);
            row.RepeatHuntOffPlots = list;
            playerSettings[WorldId] = row;
            MarkDirty();
            if (on)
                RepeatHunts.CaptureActiveMissions(plot);
            RepeatHunts.PublishClientState();
        }

        public bool IsConveyorEnabled(ulong playerId) => ReadFlag(playerId, static s => s.Conveyor, requireGlobal: true);

        public bool IsSalvageEnabled(ulong playerId) => ReadFlag(playerId, static s => s.Salvage, requireGlobal: true);

        public bool ToggleSalvage(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.Salvage, static (s, v) => { s.Salvage = v; return s; });

        public bool IsUnitSpawnerEnabled(ulong playerId) => ReadFlag(playerId, static s => s.UnitSpawner);

        public bool ToggleUnitSpawner(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.UnitSpawner, static (s, v) => { s.UnitSpawner = v; return s; });

        public bool IsBrazierEnabled(ulong playerId) => ReadFlag(playerId, static s => s.Brazier);

        public bool IsSolarEnabled(ulong playerId) => ReadFlag(playerId, static s => s.Named);

        public bool ToggleSolar(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.Named, static (s, v) => { s.Named = v; return s; });

        public bool ToggleBrazier(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.Brazier, static (s, v) => { s.Brazier = v; return s; });

        public bool ToggleSilentPull(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.SilentPull, static (s, v) => { s.SilentPull = v; return s; });

        public bool IsSilentPullEnabled(ulong playerId) => ReadFlag(playerId, static s => s.SilentPull);

        public bool ToggleSilentStash(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.SilentStash, static (s, v) => { s.SilentStash = v; return s; });

        public bool IsSilentStashEnabled(ulong playerId) => ReadFlag(playerId, static s => s.SilentStash);

        public bool ToggleConveyor(ulong playerId = WorldId) => FlipFlag(playerId, static s => s.Conveyor, static (s, v) => { s.Conveyor = v; return s; });

        internal const int WorldId = 0;

        static readonly string ConfigDir = Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
        static readonly string SettingsFile = Path.Combine(ConfigDir, "playerSettings.json");

        static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, IncludeFields = true };

        SettingsRow defaultSettings = new();

        Dictionary<ulong, SettingsRow> playerSettings = [];
        bool saveDirty;
        DateTime lastSaveUtc = DateTime.MinValue;
        const double SaveDebounceSeconds = 1.5;

        public PlayerSettingsService() { Hydrate(); if (!TryRow(WorldId, out var world)) Put(WorldId, SgAllOn(new SettingsRow())); else if (!world.AppliedSgAllOn) Put(WorldId, SgAllOn(world)); FlushSettings(force: true); }

        static SettingsRow SgAllOn(SettingsRow s) { s.SortStash = s.Pull = s.CraftPull = s.AutoStashMissions = s.Conveyor = s.Salvage = s.UnitSpawner = s.Brazier = s.Named = s.Trash = s.RrGlobalAllow = s.AppliedSgAllOn = true; return s; }

        void Hydrate()
        {
            try { if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir); if (File.Exists(SettingsFile)) playerSettings = JsonSerializer.Deserialize<Dictionary<ulong, SettingsRow>>(File.ReadAllText(SettingsFile)) ?? []; SettingsExtras.ImportGroundScoop(this); }
            catch { playerSettings ??= []; }
        }

        internal void MarkDirty() { saveDirty = true; ClanTreasuryLend.BumpSettings(); }

        internal bool TryRow(ulong id, out SettingsRow settings) => playerSettings.TryGetValue(id, out settings);

        public void FlushSettings(bool force = false)
        {
            if (!saveDirty) return;
            if (!force && (DateTime.UtcNow - lastSaveUtc).TotalSeconds < SaveDebounceSeconds) return;
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                var tmp = SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(playerSettings, JsonOpts));
                File.Copy(tmp, SettingsFile, overwrite: true);
                File.Delete(tmp);
                saveDirty = false;
                lastSaveUtc = DateTime.UtcNow;
            }
            catch { }
        }

        internal SettingsRow GetOrCreate(ulong platformId)
        {
            if (!playerSettings.TryGetValue(platformId, out var settings)) settings = new SettingsRow();
            settings.ScoopExcludes ??= new List<string>();
            settings.ScoopExcludeNames ??= new Dictionary<string, string>();
            settings.ScoopCaps ??= new Dictionary<string, int>();
            settings.ScoopCapNames ??= new Dictionary<string, string>();
            if (string.IsNullOrEmpty(settings.ScoopMode)) settings.ScoopMode = "bags";
            if (string.IsNullOrEmpty(settings.AutoFilter)) settings.AutoFilter = "all";
            if (string.IsNullOrEmpty(settings.NotifyMode)) settings.NotifyMode = "manual";
            if (settings.Radius < 1f) settings.Radius = 10f;
            return settings;
        }

        internal void Put(ulong platformId, SettingsRow settings) { playerSettings[platformId] = settings; MarkDirty(); }

        internal SettingsRow Snapshot(ulong id, bool create) =>
            playerSettings.TryGetValue(id, out var row) ? row : create ? new SettingsRow() : defaultSettings;

        bool ReadFlag(ulong id, Func<SettingsRow, bool> read, bool requireGlobal = false) =>
            requireGlobal ? read(Snapshot(id, false)) && read(Snapshot(WorldId, false)) : read(Snapshot(id, false));

        bool FlipFlag(ulong id, Func<SettingsRow, bool> read, Func<SettingsRow, bool, SettingsRow> write) { var row = Snapshot(id, true); var next = !read(row); playerSettings[id] = write(row, next); MarkDirty(); return next; }

        internal SettingsRow GetGlobalMutable()
        {
            if (!TryRow(0, out var settings)) settings = new SettingsRow();
            settings.ClanShareByClan ??= new Dictionary<string, bool>();
            settings.ClanShareExcludedTerritories ??= new List<string>();
            settings.StarterKitSeeded ??= new List<string>();
            settings.StarterKitChestSeeded ??= new List<string>();
            settings.StarterKitChestOptOut ??= new List<string>();
            settings.HeartFuelSeeded ??= new List<string>();
            settings.HeartFuelOptOut ??= new List<string>();
            return settings;
        }

        public SettingsRow GetSettings(ulong playerId) => TryRow(playerId, out var settings) ? settings : new SettingsRow();
        public SettingsRow GetGlobalSettings() => playerSettings[WorldId];



        internal Dictionary<ulong, SettingsRow> Rows => playerSettings;
        internal SettingsRow Blank => defaultSettings;
        public bool IsRrGlobalEnabled(ulong playerId) => SettingsExtras.IsRrGlobalEnabled(playerId);
        public bool ToggleRrGlobal(ulong playerId = WorldId) => SettingsExtras.ToggleRrGlobal(playerId);
        public bool IsRrGlobalServerAllowed() => SettingsExtras.IsRrGlobalServerAllowed();
        public int GetPullReserve(ulong playerId, PrefabGUID item = default) => SettingsExtras.GetPullReserve(playerId, item);
        public int SetPullReserve(ulong playerId, int amount) => SettingsExtras.SetPullReserve(playerId, amount);
        public int SetItemReserve(ulong playerId, PrefabGUID item, string itemName, int amount) => SettingsExtras.SetItemReserve(playerId, item, itemName, amount);
        public bool ClearItemReserve(ulong playerId, PrefabGUID item) => SettingsExtras.ClearItemReserve(playerId, item);
        public IEnumerable<(string name, int amount)> ListItemReserves(ulong playerId) => SettingsExtras.ListItemReserves(playerId);
        public int GetItemReserveOverrideCount(ulong playerId) => SettingsExtras.GetItemReserveOverrideCount(playerId);
        public bool TryGetItemCap(ulong playerId, PrefabGUID item, out int cap) => SettingsExtras.TryGetItemCap(playerId, item, out cap);
        public int SetItemCap(ulong playerId, PrefabGUID item, string itemName, int amount) => SettingsExtras.SetItemCap(playerId, item, itemName, amount);
        public bool ClearItemCap(ulong playerId, PrefabGUID item) => SettingsExtras.ClearItemCap(playerId, item);
        public IEnumerable<(string name, int amount)> ListItemCaps(ulong playerId) => SettingsExtras.ListItemCaps(playerId);
        public int GetItemCapOverrideCount(ulong playerId) => SettingsExtras.GetItemCapOverrideCount(playerId);
        public bool IsConveyorLoopsAllowed() => SettingsExtras.IsConveyorLoopsAllowed();
        public bool ToggleConveyorLoops() => SettingsExtras.ToggleConveyorLoops();
        public bool IsGlobalSalvageEnabled() => SettingsExtras.IsGlobalSalvageEnabled();
        public bool GetPlotSalvageFlag(ulong heartOwnerId, int territoryId) => SettingsExtras.GetPlotSalvageFlag(heartOwnerId, territoryId);
        public bool TogglePlotSalvage(ulong heartOwnerId, int territoryId) => SettingsExtras.TogglePlotSalvage(heartOwnerId, territoryId);
        public bool IsHeartFeedEnabled(ulong heartOwnerId, int territoryId) => SettingsExtras.IsHeartFeedEnabled(heartOwnerId, territoryId);
        public bool ToggleHeartFeed(ulong heartOwnerId, int territoryId) => SettingsExtras.ToggleHeartFeed(heartOwnerId, territoryId);
        public bool IsHeartFuelSeeded(string heartKey) => SettingsExtras.IsHeartFuelSeeded(heartKey);
        public void MarkHeartFuelSeeded(params string[] heartKeys) => SettingsExtras.MarkHeartFuelSeeded(heartKeys);
        public bool IsHeartFuelOptOut(string heartKey) => SettingsExtras.IsHeartFuelOptOut(heartKey);
        public void SetHeartFuelOptOut(bool optedOut, params string[] heartKeys) => SettingsExtras.SetHeartFuelOptOut(optedOut, heartKeys);
        public bool IsAutoEnabled(ulong platformId) => SettingsExtras.IsAutoEnabled(platformId);
        public bool ToggleAuto(ulong platformId) => SettingsExtras.ToggleAuto(platformId);
        public static bool TryParseAutoFilter(string value, out AutoFilter filter) => SettingsExtras.TryParseAutoFilter(value, out filter);
        public AutoFilter GetAutoFilter(ulong platformId) => SettingsExtras.GetAutoFilter(platformId);
        public AutoFilter SetAutoFilter(ulong platformId, AutoFilter filter) => SettingsExtras.SetAutoFilter(platformId, filter);
        public void SetAutoOnWithFilter(ulong platformId, AutoFilter filter) => SettingsExtras.SetAutoOnWithFilter(platformId, filter);
        public static bool TryParseNotifyMode(string value, out NotifyMode mode) => SettingsExtras.TryParseNotifyMode(value, out mode);
        public NotifyMode GetNotifyMode(ulong platformId) => SettingsExtras.GetNotifyMode(platformId);
        public NotifyMode SetNotifyMode(ulong platformId, NotifyMode mode) => SettingsExtras.SetNotifyMode(platformId, mode);
        public float GetRadius(ulong platformId) => SettingsExtras.GetRadius(platformId);
        public float SetRadius(ulong platformId, float radius) => SettingsExtras.SetRadius(platformId, radius);
        public CapMode GetCapMode(ulong platformId) => SettingsExtras.GetCapMode(platformId);
        public CapMode SetCapMode(ulong platformId, CapMode mode) => SettingsExtras.SetCapMode(platformId, mode);
        public bool IsExcluded(ulong platformId, PrefabGUID item) => SettingsExtras.IsExcluded(platformId, item);
        public bool ToggleExclude(ulong platformId, PrefabGUID item, string name) => SettingsExtras.ToggleExclude(platformId, item, name);
        public IReadOnlyList<(PrefabGUID prefab, string name)> ListExcludes(ulong platformId) => SettingsExtras.ListExcludes(platformId);
        public int GetCap(ulong platformId, PrefabGUID item) => SettingsExtras.GetCap(platformId, item);
        public void SetScoopCap(ulong platformId, PrefabGUID item, int cap, string name) => SettingsExtras.SetScoopCap(platformId, item, cap, name);
        public void ClearAllScoopCaps(ulong platformId) => SettingsExtras.ClearAllScoopCaps(platformId);
        public IReadOnlyList<(PrefabGUID prefab, string name, int cap)> ListScoopCaps(ulong platformId) => SettingsExtras.ListScoopCaps(platformId);
        public bool IsClanShareEnabled(ulong playerId) => SettingsExtras.IsClanShareEnabled(playerId);
        public bool ToggleClanShare(ulong playerId) => SettingsExtras.ToggleClanShare(playerId);
        public bool TryGetClanShareFlag(string clanKey, out bool enabled) => SettingsExtras.TryGetClanShareFlag(clanKey, out enabled);
        public void SetClanShareForClan(string clanKey, bool enabled) => SettingsExtras.SetClanShareForClan(clanKey, enabled);
        public bool ToggleClanShareForClan(string clanKey, bool current) => SettingsExtras.ToggleClanShareForClan(clanKey, current);
        public bool IsTerritoryClanShareExcluded(int territoryId) => SettingsExtras.IsTerritoryClanShareExcluded(territoryId);
        public bool ToggleTerritoryClanShareExclude(int territoryId) => SettingsExtras.ToggleTerritoryClanShareExclude(territoryId);
        public bool IsStarterKitSeeded(string key) => SettingsExtras.IsStarterKitSeeded(key);
        public void MarkStarterKitSeeded(params string[] keys) => SettingsExtras.MarkStarterKitSeeded(keys);
        public bool IsStarterKitChestSeeded(string key) => SettingsExtras.IsStarterKitChestSeeded(key);
        public void MarkStarterKitChestSeeded(params string[] keys) => SettingsExtras.MarkStarterKitChestSeeded(keys);
        public bool IsStarterKitChestOptOut(string key) => SettingsExtras.IsStarterKitChestOptOut(key);
        public void SetStarterKitChestOptOut(bool optedOut, params string[] keys) => SettingsExtras.SetStarterKitChestOptOut(optedOut, keys);
        public bool HasItemGroup(ulong playerId, string name) => SettingsExtras.HasItemGroup(playerId, name);
        public IEnumerable<(string name, int count)> ListCustomGroups(ulong playerId) => SettingsExtras.ListCustomGroups(playerId);
        public IEnumerable<(string guid, string name)> ListItemGroupMembers(ulong playerId, string name) => SettingsExtras.ListItemGroupMembers(playerId, name);
        public bool CreateItemGroup(ulong playerId, string name) => SettingsExtras.CreateItemGroup(playerId, name);
        public bool DeleteItemGroup(ulong playerId, string name) => SettingsExtras.DeleteItemGroup(playerId, name);
        public bool AddItemToGroup(ulong playerId, string name, PrefabGUID item, string itemName) => SettingsExtras.AddItemToGroup(playerId, name, item, itemName);
        public bool RemoveItemFromGroup(ulong playerId, string name, PrefabGUID item) => SettingsExtras.RemoveItemFromGroup(playerId, name, item);
        public int ApplyGroupAmounts(ulong playerId, IReadOnlyList<(PrefabGUID guid, string name)> items, bool isCap, int amount) => SettingsExtras.ApplyGroupAmounts(playerId, items, isCap, amount);
        public bool IsDeletedGroup(ulong playerId, string name) => SettingsExtras.IsDeletedGroup(playerId, name);
        public bool HasGroupOverlay(ulong playerId, string name) => SettingsExtras.HasGroupOverlay(playerId, name);
        public void WriteGroupOverlay(ulong playerId, string name, IEnumerable<(string guid, string itemName)> members) => SettingsExtras.WriteGroupOverlay(playerId, name, members);
        public bool DeleteBuiltInGroup(ulong playerId, string name) => SettingsExtras.DeleteBuiltInGroup(playerId, name);
        public bool RestoreBuiltInGroup(ulong playerId, string name) => SettingsExtras.RestoreBuiltInGroup(playerId, name);
        public List<string> RestoreAllBuiltInGroups(ulong playerId, IEnumerable<string> builtInNames) => SettingsExtras.RestoreAllBuiltInGroups(playerId, builtInNames);
    }
