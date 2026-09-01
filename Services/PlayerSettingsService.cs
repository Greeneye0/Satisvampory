using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Stunlock.Core;

namespace Satisvampory.Services
{
    internal enum CapMode
    {
        Bags = 0,
        Guild = 1
    }

    internal enum AutoFilter
    {
        Around = 0,
        All = 1
    }

    internal enum NotifyMode
    {
        Off = 0,
        Manual = 1,
        On = 2
    }

    internal class PlayerSettingsService
    {
        const int GLOBAL_PLAYER_ID = 0;

        static readonly string CONFIG_PATH = Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
        static readonly string PLAYER_SETTINGS_PATH = Path.Combine(CONFIG_PATH, "playerSettings.json");

        static readonly JsonSerializerOptions prettyJsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true
        };

        public struct PlayerSettings
        {
            public PlayerSettings()
            {
                DontPullLast = true;
                PullReserve = 10;
                ItemReserves = new Dictionary<string, int>();
                ItemReserveNames = new Dictionary<string, string>();
                ItemCaps = new Dictionary<string, int>();
                ItemCapNames = new Dictionary<string, string>();
                ItemGroups = new Dictionary<string, List<string>>();
                ItemGroupNames = new Dictionary<string, Dictionary<string, string>>();
                DeletedGroups = new List<string>();
                OverlaidGroups = new List<string>();
                PlotSalvage = new Dictionary<string, bool>();
                HeartFeed = new Dictionary<string, bool>();
                HeartFuelSeeded = new List<string>();
                HeartFuelOptOut = new List<string>();
                ClanShareByClan = new Dictionary<string, bool>();
                ClanShareExcludedTerritories = new List<string>();
                StarterKitSeeded = new List<string>();
                StarterKitChestSeeded = new List<string>();
                StarterKitChestOptOut = new List<string>();
                AutoScoop = true;
                AutoFilter = "all";
                NotifyMode = "manual";
                Radius = 10f;
                ScoopMode = "bags";
                ScoopExcludes = new List<string>();
                ScoopExcludeNames = new Dictionary<string, string>();
                ScoopCaps = new Dictionary<string, int>();
                ScoopCapNames = new Dictionary<string, string>();
            }

            public bool SortStash { get; set; }
            public bool Pull { get; set; }
            public bool CraftPull { get; set; }
            public bool DontPullLast { get; set; }
            public int PullReserve { get; set; }
            public Dictionary<string, int> ItemReserves { get; set; }
            public Dictionary<string, string> ItemReserveNames { get; set; }
            public Dictionary<string, int> ItemCaps { get; set; }
            public Dictionary<string, string> ItemCapNames { get; set; }
            public Dictionary<string, List<string>> ItemGroups { get; set; }
            public Dictionary<string, Dictionary<string, string>> ItemGroupNames { get; set; }
            public List<string> DeletedGroups { get; set; }
            public List<string> OverlaidGroups { get; set; }
            public bool AutoStashMissions { get; set; }
            public bool Conveyor { get; set; }
            public bool Salvage { get; set; }
            public bool UnitSpawner { get; set; }
            public bool Brazier { get; set; }
            public bool Named { get; set; }
            public bool SilentPull { get; set; }
            public bool SilentStash { get; set; }
            public bool Trash { get; set; }
            // 1.6.1.35: clan-wide RR/.stash. Player default OFF. Server allow default ON.
            public bool RrGlobal { get; set; }
            public bool RrGlobalAllow { get; set; } = true;
            public bool ClanShare { get; set; }
            // Per-plot salvage keyed by territory id. Personal Salvage is unused by ProcessSalvagers.
            public Dictionary<string, bool> PlotSalvage { get; set; }
            // Per-plot heart auto-feed. Missing key = ON by default.
            public Dictionary<string, bool> HeartFeed { get; set; }
            // Persisted heart fuel seed/opt-out keys (heart NetworkId n{net} only; t{plot} ignored).
            public List<string> HeartFuelSeeded { get; set; }
            public List<string> HeartFuelOptOut { get; set; }
            // Clan-wide ClanShare keyed by clan NetworkId ("c{net}"). Missing key = not migrated yet.
            public Dictionary<string, bool> ClanShareByClan { get; set; }
            // Territory ids excluded from clan logistics ("t{id}"). Default not excluded.
            public List<string> ClanShareExcludedTerritories { get; set; }
            // Legacy per-plot starter kit one-shot keyed by t{plot}. Ignored by 1.6.1.31 (false-complete on brick-only).
            public List<string> StarterKitSeeded { get; set; }
            // Per dest-chest NetworkId kit seed/opt-out (n{net} only). Empty after seed = opt-out that chest.
            public List<string> StarterKitChestSeeded { get; set; }
            public List<string> StarterKitChestOptOut { get; set; }
            public bool AutoScoop { get; set; }
            public string AutoFilter { get; set; }
            public string NotifyMode { get; set; }
            public float Radius { get; set; }
            public string ScoopMode { get; set; }
            public List<string> ScoopExcludes { get; set; }
            public Dictionary<string, string> ScoopExcludeNames { get; set; }
            public Dictionary<string, int> ScoopCaps { get; set; }
            public Dictionary<string, string> ScoopCapNames { get; set; }
            public bool AppliedSgAllOn { get; set; }
        }

        PlayerSettings defaultSettings = new();

        Dictionary<ulong, PlayerSettings> playerSettings = [];
        bool saveDirty;
        DateTime lastSaveUtc = DateTime.MinValue;
        const double SaveDebounceSeconds = 1.5;

        public PlayerSettingsService()
        {
            LoadSettings();

            if(!playerSettings.ContainsKey(GLOBAL_PLAYER_ID))
            {
                playerSettings[GLOBAL_PLAYER_ID] = SgAllOn(new PlayerSettings());
                SaveSettings();
            }
            else
            {
                var g = playerSettings[GLOBAL_PLAYER_ID];
                if (!g.AppliedSgAllOn)
                {
                    playerSettings[GLOBAL_PLAYER_ID] = SgAllOn(g);
                    SaveSettings();
                }
            }
            FlushSettings(force: true);
        }

        static PlayerSettings SgAllOn(PlayerSettings s)
        {
            s.SortStash = true;
            s.Pull = true;
            s.CraftPull = true;
            s.AutoStashMissions = true;
            s.Conveyor = true;
            s.Salvage = true;
            s.UnitSpawner = true;
            s.Brazier = true;
            s.Named = true;
            s.Trash = true;
            s.RrGlobalAllow = true;
            s.AppliedSgAllOn = true;
            return s;
        }

        void LoadSettings()
        {
            try
            {
                if (!Directory.Exists(CONFIG_PATH))
                    Directory.CreateDirectory(CONFIG_PATH);

                if (!File.Exists(PLAYER_SETTINGS_PATH))
                {
                    var kindred = Path.Combine(BepInEx.Paths.ConfigPath, "KindredLogistics", "playerSettings.json");
                    if (File.Exists(kindred))
                    {
                        File.Copy(kindred, PLAYER_SETTINGS_PATH, overwrite: false);
                    }
                }

                if (File.Exists(PLAYER_SETTINGS_PATH))
                {
                    var json = File.ReadAllText(PLAYER_SETTINGS_PATH);
                    playerSettings = JsonSerializer.Deserialize<Dictionary<ulong, PlayerSettings>>(json) ?? [];
                }

                MergeGroundScoopSettings();
            }
            catch
            {
                playerSettings ??= [];
            }
        }

        void MergeGroundScoopSettings()
        {
            var scoopPath = Path.Combine(BepInEx.Paths.ConfigPath, "GroundScoop", "playerSettings.json");
            if (!File.Exists(scoopPath))
                return;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(scoopPath));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return;
                foreach (var player in doc.RootElement.EnumerateObject())
                {
                    if (!ulong.TryParse(player.Name, out var id))
                        continue;
                    var s = GetOrCreate(id);
                    var el = player.Value;
                    if (el.TryGetProperty("AutoScoop", out var auto))
                        s.AutoScoop = auto.GetBoolean();
                    if (el.TryGetProperty("AutoFilter", out var filter))
                        s.AutoFilter = filter.GetString();
                    if (el.TryGetProperty("NotifyMode", out var notify))
                        s.NotifyMode = notify.GetString();
                    if (el.TryGetProperty("Radius", out var radius) && radius.TryGetSingle(out var r))
                        s.Radius = r;
                    if (el.TryGetProperty("Mode", out var mode))
                        s.ScoopMode = mode.GetString();
                    CopyStringList(el, "Excludes", s.ScoopExcludes);
                    CopyStringMap(el, "ExcludeNames", s.ScoopExcludeNames);
                    CopyIntMap(el, "Caps", s.ScoopCaps);
                    CopyStringMap(el, "CapNames", s.ScoopCapNames);
                    playerSettings[id] = s;
                }
                saveDirty = true;
            }
            catch
            {
            }
        }

        static void CopyStringList(JsonElement el, string name, List<string> dest)
        {
            if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;
            foreach (var item in arr.EnumerateArray())
            {
                var v = item.GetString();
                if (!string.IsNullOrEmpty(v) && !dest.Contains(v))
                    dest.Add(v);
            }
        }

        static void CopyStringMap(JsonElement el, string name, Dictionary<string, string> dest)
        {
            if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
                return;
            foreach (var p in obj.EnumerateObject())
                dest[p.Name] = p.Value.GetString();
        }

        static void CopyIntMap(JsonElement el, string name, Dictionary<string, int> dest)
        {
            if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
                return;
            foreach (var p in obj.EnumerateObject())
            {
                if (p.Value.TryGetInt32(out var n))
                    dest[p.Name] = n;
            }
        }

        void SaveSettings()
        {
            saveDirty = true;
        }

        public void FlushSettings(bool force = false)
        {
            if (!saveDirty)
                return;
            if (!force && (DateTime.UtcNow - lastSaveUtc).TotalSeconds < SaveDebounceSeconds)
                return;
            try
            {
                if (!Directory.Exists(CONFIG_PATH))
                    Directory.CreateDirectory(CONFIG_PATH);
                var json = JsonSerializer.Serialize(playerSettings, prettyJsonOptions);
                var tmp = PLAYER_SETTINGS_PATH + ".tmp";
                File.WriteAllText(tmp, json);
                File.Copy(tmp, PLAYER_SETTINGS_PATH, overwrite: true);
                File.Delete(tmp);
                saveDirty = false;
                lastSaveUtc = DateTime.UtcNow;
            }
            catch
            {
            }
        }

        PlayerSettings GetOrCreate(ulong platformId)
        {
            if (!playerSettings.TryGetValue(platformId, out var settings))
                settings = new PlayerSettings();
            settings.ScoopExcludes ??= new List<string>();
            settings.ScoopExcludeNames ??= new Dictionary<string, string>();
            settings.ScoopCaps ??= new Dictionary<string, int>();
            settings.ScoopCapNames ??= new Dictionary<string, string>();
            if (string.IsNullOrEmpty(settings.ScoopMode))
                settings.ScoopMode = "bags";
            if (string.IsNullOrEmpty(settings.AutoFilter))
                settings.AutoFilter = "all";
            if (string.IsNullOrEmpty(settings.NotifyMode))
                settings.NotifyMode = "manual";
            if (settings.Radius < 1f)
                settings.Radius = 10f;
            return settings;
        }

        void Put(ulong platformId, PlayerSettings settings)
        {
            playerSettings[platformId] = settings;
            SaveSettings();
        }

        public bool IsAutoEnabled(ulong platformId) => GetOrCreate(platformId).AutoScoop;

        public bool ToggleAuto(ulong platformId)
        {
            var s = GetOrCreate(platformId);
            s.AutoScoop = !s.AutoScoop;
            Put(platformId, s);
            return s.AutoScoop;
        }

        public static bool TryParseAutoFilter(string value, out AutoFilter filter)
        {
            filter = AutoFilter.Around;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (string.Equals(value, "around", StringComparison.OrdinalIgnoreCase))
            {
                filter = AutoFilter.Around;
                return true;
            }
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            {
                filter = AutoFilter.All;
                return true;
            }
            return false;
        }

        public AutoFilter GetAutoFilter(ulong platformId)
        {
            return TryParseAutoFilter(GetOrCreate(platformId).AutoFilter, out var filter)
                ? filter
                : AutoFilter.All;
        }

        public AutoFilter SetAutoFilter(ulong platformId, AutoFilter filter)
        {
            var s = GetOrCreate(platformId);
            s.AutoFilter = filter == AutoFilter.All ? "all" : "around";
            Put(platformId, s);
            return filter;
        }

        public void SetAutoOnWithFilter(ulong platformId, AutoFilter filter)
        {
            var s = GetOrCreate(platformId);
            s.AutoScoop = true;
            s.AutoFilter = filter == AutoFilter.All ? "all" : "around";
            Put(platformId, s);
        }

        public static bool TryParseNotifyMode(string value, out NotifyMode mode)
        {
            mode = NotifyMode.Manual;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                mode = NotifyMode.Off;
                return true;
            }
            if (string.Equals(value, "manual", StringComparison.OrdinalIgnoreCase))
            {
                mode = NotifyMode.Manual;
                return true;
            }
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            {
                mode = NotifyMode.On;
                return true;
            }
            return false;
        }

        public NotifyMode GetNotifyMode(ulong platformId)
        {
            return TryParseNotifyMode(GetOrCreate(platformId).NotifyMode, out var mode)
                ? mode
                : NotifyMode.Manual;
        }

        public NotifyMode SetNotifyMode(ulong platformId, NotifyMode mode)
        {
            var s = GetOrCreate(platformId);
            s.NotifyMode = mode switch
            {
                NotifyMode.Off => "off",
                NotifyMode.On => "on",
                _ => "manual"
            };
            Put(platformId, s);
            return mode;
        }

        public float GetRadius(ulong platformId)
        {
            var r = GetOrCreate(platformId).Radius;
            if (r < 1f) r = 10f;
            if (r > 50f) r = 50f;
            return r;
        }

        public float SetRadius(ulong platformId, float radius)
        {
            if (radius < 1f) radius = 1f;
            if (radius > 50f) radius = 50f;
            var s = GetOrCreate(platformId);
            s.Radius = radius;
            Put(platformId, s);
            return s.Radius;
        }

        public CapMode GetCapMode(ulong platformId)
        {
            return string.Equals(GetOrCreate(platformId).ScoopMode, "guild", StringComparison.OrdinalIgnoreCase)
                ? CapMode.Guild
                : CapMode.Bags;
        }

        public CapMode SetCapMode(ulong platformId, CapMode mode)
        {
            var s = GetOrCreate(platformId);
            s.ScoopMode = mode == CapMode.Guild ? "guild" : "bags";
            Put(platformId, s);
            return mode;
        }

        public bool IsExcluded(ulong platformId, PrefabGUID item)
        {
            return GetOrCreate(platformId).ScoopExcludes.Contains(item.GuidHash.ToString());
        }

        public bool ToggleExclude(ulong platformId, PrefabGUID item, string name)
        {
            var s = GetOrCreate(platformId);
            var key = item.GuidHash.ToString();
            if (s.ScoopExcludes.Contains(key))
            {
                s.ScoopExcludes.Remove(key);
                s.ScoopExcludeNames.Remove(key);
                Put(platformId, s);
                return false;
            }
            s.ScoopExcludes.Add(key);
            s.ScoopExcludeNames[key] = name ?? item.PrefabName();
            Put(platformId, s);
            return true;
        }

        public IReadOnlyList<(PrefabGUID prefab, string name)> ListExcludes(ulong platformId)
        {
            var s = GetOrCreate(platformId);
            var list = new List<(PrefabGUID, string)>();
            foreach (var key in s.ScoopExcludes)
            {
                if (!int.TryParse(key, out var hash)) continue;
                var prefab = new PrefabGUID(hash);
                s.ScoopExcludeNames.TryGetValue(key, out var name);
                if (string.IsNullOrEmpty(name))
                    name = prefab.PrefabName();
                list.Add((prefab, name));
            }
            return list;
        }

        public int GetCap(ulong platformId, PrefabGUID item)
        {
            if (GetOrCreate(platformId).ScoopCaps.TryGetValue(item.GuidHash.ToString(), out var cap))
                return cap;
            return -1;
        }

        public void SetScoopCap(ulong platformId, PrefabGUID item, int cap, string name)
        {
            var s = GetOrCreate(platformId);
            var key = item.GuidHash.ToString();
            if (cap < 0)
            {
                s.ScoopCaps.Remove(key);
                s.ScoopCapNames.Remove(key);
            }
            else
            {
                s.ScoopCaps[key] = cap;
                s.ScoopCapNames[key] = name ?? item.PrefabName();
            }
            Put(platformId, s);
        }

        public void ClearAllScoopCaps(ulong platformId)
        {
            var s = GetOrCreate(platformId);
            s.ScoopCaps.Clear();
            s.ScoopCapNames.Clear();
            Put(platformId, s);
        }

        public IReadOnlyList<(PrefabGUID prefab, string name, int cap)> ListScoopCaps(ulong platformId)
        {
            var s = GetOrCreate(platformId);
            var list = new List<(PrefabGUID, string, int)>();
            foreach (var kv in s.ScoopCaps)
            {
                if (!int.TryParse(kv.Key, out var hash)) continue;
                var prefab = new PrefabGUID(hash);
                s.ScoopCapNames.TryGetValue(kv.Key, out var name);
                if (string.IsNullOrEmpty(name))
                    name = prefab.PrefabName();
                list.Add((prefab, name, kv.Value));
            }
            return list;
        }

        public bool IsSortStashEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.SortStash && playerSettings[GLOBAL_PLAYER_ID].SortStash;
        }

        public bool ToggleSortStash(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.SortStash = !settings.SortStash;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.SortStash;
        }

        public bool IsRrGlobalEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            var playerOn = settings.RrGlobal;
            var allow = true;
            if (playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var g))
                allow = g.RrGlobalAllow;
            return playerOn && allow;
        }

        public bool ToggleRrGlobal(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            if (playerId == GLOBAL_PLAYER_ID)
            {
                settings.RrGlobalAllow = !settings.RrGlobalAllow;
                playerSettings[playerId] = settings;
                SaveSettings();
                return settings.RrGlobalAllow;
            }
            settings.RrGlobal = !settings.RrGlobal;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.RrGlobal;
        }

        public bool IsRrGlobalServerAllowed()
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var g))
                return true;
            return g.RrGlobalAllow;
        }
        
        public bool TogglePull()
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                settings = new PlayerSettings();
            settings.Pull = !settings.Pull;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
            return settings.Pull;
        }

        public bool IsPullEnabled()
        {
            return !playerSettings[GLOBAL_PLAYER_ID].Pull;
        }

        public bool ToggleTrash()
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                settings = new PlayerSettings();
            settings.Trash = !settings.Trash;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
            return settings.Trash;
        }

        public bool IsTrashEnabled()
        {
            return !playerSettings[GLOBAL_PLAYER_ID].Trash;
        }

        public bool IsCraftPullEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.CraftPull && playerSettings[GLOBAL_PLAYER_ID].CraftPull;
        }

        public bool ToggleCraftPull(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.CraftPull = !settings.CraftPull;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.CraftPull;
        }

        public bool IsDontPullLastEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.DontPullLast;
        }

        public int GetPullReserve(ulong playerId, PrefabGUID item = default)
        {
            if (!IsDontPullLastEnabled(playerId)) return 0;
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            if (item.GuidHash != 0 && settings.ItemReserves != null &&
                settings.ItemReserves.TryGetValue(item.GuidHash.ToString(), out var specific))
                return specific; // 0 is a valid override: leave nothing of THIS item
            return settings.PullReserve > 0 ? settings.PullReserve : 10;
        }

        public int SetPullReserve(ulong playerId, int amount)
        {
            if (amount < 0) amount = 0;
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.PullReserve = amount;
            settings.DontPullLast = amount > 0;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.PullReserve;
        }

        public int SetItemReserve(ulong playerId, PrefabGUID item, string itemName, int amount)
        {
            if (amount < 0) amount = 0;
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemReserves ??= new Dictionary<string, int>();
            settings.ItemReserveNames ??= new Dictionary<string, string>();
            var key = item.GuidHash.ToString();
            settings.ItemReserves[key] = amount;
            settings.ItemReserveNames[key] = itemName;
            if (amount > 0) settings.DontPullLast = true;
            playerSettings[playerId] = settings;
            SaveSettings();
            return amount;
        }

        public bool ClearItemReserve(ulong playerId, PrefabGUID item)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings)) return false;
            if (settings.ItemReserves == null) return false;
            var key = item.GuidHash.ToString();
            var removed = settings.ItemReserves.Remove(key);
            settings.ItemReserveNames?.Remove(key);
            playerSettings[playerId] = settings;
            SaveSettings();
            return removed;
        }

        public IEnumerable<(string name, int amount)> ListItemReserves(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                yield break;
            if (settings.ItemReserves == null)
                yield break;
            foreach (var kvp in settings.ItemReserves)
            {
                var name = kvp.Key;
                if (settings.ItemReserveNames != null &&
                    settings.ItemReserveNames.TryGetValue(kvp.Key, out var storedName) &&
                    !string.IsNullOrEmpty(storedName))
                    name = storedName;
                yield return (name, kvp.Value);
            }
        }

        public int GetItemReserveOverrideCount(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemReserves == null)
                return 0;
            return settings.ItemReserves.Count;
        }

        public bool TryGetItemCap(ulong playerId, PrefabGUID item, out int cap)
        {
            cap = 0;
            if (!playerSettings.TryGetValue(playerId, out var settings))
                return false;
            if (settings.ItemCaps == null)
                return false;
            if (item.GuidHash == 0)
                return false;
            return settings.ItemCaps.TryGetValue(item.GuidHash.ToString(), out cap);
        }

        public int SetItemCap(ulong playerId, PrefabGUID item, string itemName, int amount)
        {
            if (amount < 0)
            {
                ClearItemCap(playerId, item);
                return amount;
            }
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemCaps ??= new Dictionary<string, int>();
            settings.ItemCapNames ??= new Dictionary<string, string>();
            var key = item.GuidHash.ToString();
            settings.ItemCaps[key] = amount;
            settings.ItemCapNames[key] = itemName;
            playerSettings[playerId] = settings;
            SaveSettings();
            return amount;
        }

        public bool ClearItemCap(ulong playerId, PrefabGUID item)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings)) return false;
            if (settings.ItemCaps == null) return false;
            var key = item.GuidHash.ToString();
            var removed = settings.ItemCaps.Remove(key);
            settings.ItemCapNames?.Remove(key);
            playerSettings[playerId] = settings;
            SaveSettings();
            return removed;
        }

        public IEnumerable<(string name, int amount)> ListItemCaps(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                yield break;
            if (settings.ItemCaps == null)
                yield break;
            foreach (var kvp in settings.ItemCaps)
            {
                var name = kvp.Key;
                if (settings.ItemCapNames != null &&
                    settings.ItemCapNames.TryGetValue(kvp.Key, out var storedName) &&
                    !string.IsNullOrEmpty(storedName))
                    name = storedName;
                yield return (name, kvp.Value);
            }
        }

        public int GetItemCapOverrideCount(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemCaps == null)
                return 0;
            return settings.ItemCaps.Count;
        }

        public bool ToggleDontPullLast(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.DontPullLast = !settings.DontPullLast;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.DontPullLast;
        }

        public bool IsAutoStashMissionsEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.AutoStashMissions && playerSettings[GLOBAL_PLAYER_ID].AutoStashMissions;
        }

        public bool ToggleAutoStashMissions(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.AutoStashMissions = !settings.AutoStashMissions;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.AutoStashMissions;
        }

        public bool IsConveyorEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.Conveyor && playerSettings[GLOBAL_PLAYER_ID].Conveyor;
        }

        public bool IsSalvageEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.Salvage && playerSettings[GLOBAL_PLAYER_ID].Salvage;
        }

        public bool ToggleSalvage(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.Salvage = !settings.Salvage;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.Salvage;
        }

        public bool IsGlobalSalvageEnabled()
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                return true;
            return settings.Salvage;
        }

        static string PlotSalvageKey(int territoryId) => territoryId.ToString();

        public bool GetPlotSalvageFlag(ulong heartOwnerId, int territoryId)
        {
            if (!playerSettings.TryGetValue(heartOwnerId, out var settings) || settings.PlotSalvage == null)
                return false;
            return settings.PlotSalvage.TryGetValue(PlotSalvageKey(territoryId), out var on) && on;
        }

        public bool TogglePlotSalvage(ulong heartOwnerId, int territoryId)
        {
            if (!playerSettings.TryGetValue(heartOwnerId, out var settings))
                settings = new PlayerSettings();
            settings.PlotSalvage ??= new Dictionary<string, bool>();
            var key = PlotSalvageKey(territoryId);
            var next = !(settings.PlotSalvage.TryGetValue(key, out var on) && on);
            settings.PlotSalvage[key] = next;
            playerSettings[heartOwnerId] = settings;
            SaveSettings();
            return next;
        }

        static string HeartFeedKey(int territoryId) => territoryId.ToString();

        /// <summary>Heart auto-feed is ON by default when the plot has no stored flag.</summary>
        public bool IsHeartFeedEnabled(ulong heartOwnerId, int territoryId)
        {
            if (!playerSettings.TryGetValue(heartOwnerId, out var settings) || settings.HeartFeed == null)
                return true;
            if (!settings.HeartFeed.TryGetValue(HeartFeedKey(territoryId), out var on))
                return true;
            return on;
        }

        public bool ToggleHeartFeed(ulong heartOwnerId, int territoryId)
        {
            if (!playerSettings.TryGetValue(heartOwnerId, out var settings))
                settings = new PlayerSettings();
            settings.HeartFeed ??= new Dictionary<string, bool>();
            var key = HeartFeedKey(territoryId);
            var current = !settings.HeartFeed.TryGetValue(key, out var on) || on;
            var next = !current;
            settings.HeartFeed[key] = next;
            playerSettings[heartOwnerId] = settings;
            SaveSettings();
            return next;
        }

        public bool IsHeartFuelSeeded(string heartKey)
        {
            if (string.IsNullOrEmpty(heartKey))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.HeartFuelSeeded == null)
                return false;
            return settings.HeartFuelSeeded.Contains(heartKey);
        }

        public void MarkHeartFuelSeeded(params string[] heartKeys)
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                settings = new PlayerSettings();
            settings.HeartFuelSeeded ??= new List<string>();
            var changed = false;
            foreach (var key in heartKeys)
            {
                if (string.IsNullOrEmpty(key) || settings.HeartFuelSeeded.Contains(key))
                    continue;
                settings.HeartFuelSeeded.Add(key);
                changed = true;
            }
            if (!changed)
                return;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public bool IsHeartFuelOptOut(string heartKey)
        {
            if (string.IsNullOrEmpty(heartKey))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.HeartFuelOptOut == null)
                return false;
            return settings.HeartFuelOptOut.Contains(heartKey);
        }

        public void SetHeartFuelOptOut(bool optedOut, params string[] heartKeys)
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                settings = new PlayerSettings();
            settings.HeartFuelOptOut ??= new List<string>();
            var changed = false;
            foreach (var key in heartKeys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                if (optedOut)
                {
                    if (settings.HeartFuelOptOut.Contains(key))
                        continue;
                    settings.HeartFuelOptOut.Add(key);
                    changed = true;
                }
                else if (settings.HeartFuelOptOut.Remove(key))
                {
                    changed = true;
                }
            }
            if (!changed)
                return;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public bool IsUnitSpawnerEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.UnitSpawner;
        }

        public bool ToggleUnitSpawner(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.UnitSpawner = !settings.UnitSpawner;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.UnitSpawner;
        }
        
        public bool IsBrazierEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.Brazier;
        }

        public bool IsSolarEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.Named;
        }

        public bool ToggleSolar(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.Named = !settings.Named;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.Named;
        }

        public bool ToggleBrazier(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.Brazier = !settings.Brazier;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.Brazier;
        }

        public bool ToggleSilentPull(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.SilentPull = !settings.SilentPull;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.SilentPull;
        }

        public bool IsSilentPullEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.SilentPull;
        }

        public bool ToggleSilentStash(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.SilentStash = !settings.SilentStash;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.SilentStash;
        }

        public bool IsSilentStashEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = defaultSettings;
            return settings.SilentStash;
        }

        public bool ToggleConveyor(ulong playerId = GLOBAL_PLAYER_ID)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.Conveyor = !settings.Conveyor;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.Conveyor;
        }

        public bool IsClanShareEnabled(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                return false;
            return settings.ClanShare;
        }

        public bool ToggleClanShare(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ClanShare = !settings.ClanShare;
            playerSettings[playerId] = settings;
            SaveSettings();
            return settings.ClanShare;
        }

        PlayerSettings GetGlobalMutable()
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings))
                settings = new PlayerSettings();
            settings.ClanShareByClan ??= new Dictionary<string, bool>();
            settings.ClanShareExcludedTerritories ??= new List<string>();
            settings.StarterKitSeeded ??= new List<string>();
            settings.StarterKitChestSeeded ??= new List<string>();
            settings.StarterKitChestOptOut ??= new List<string>();
            settings.HeartFuelSeeded ??= new List<string>();
            settings.HeartFuelOptOut ??= new List<string>();
            return settings;
        }

        public bool TryGetClanShareFlag(string clanKey, out bool enabled)
        {
            enabled = false;
            if (string.IsNullOrEmpty(clanKey))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.ClanShareByClan == null)
                return false;
            return settings.ClanShareByClan.TryGetValue(clanKey, out enabled);
        }

        public void SetClanShareForClan(string clanKey, bool enabled)
        {
            if (string.IsNullOrEmpty(clanKey))
                return;
            var settings = GetGlobalMutable();
            settings.ClanShareByClan[clanKey] = enabled;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public bool ToggleClanShareForClan(string clanKey, bool current)
        {
            var next = !current;
            SetClanShareForClan(clanKey, next);
            return next;
        }

        static string TerritoryExcludeKey(int territoryId) => "t" + territoryId;

        public bool IsTerritoryClanShareExcluded(int territoryId)
        {
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.ClanShareExcludedTerritories == null)
                return false;
            return settings.ClanShareExcludedTerritories.Contains(TerritoryExcludeKey(territoryId));
        }

        public bool ToggleTerritoryClanShareExclude(int territoryId)
        {
            var settings = GetGlobalMutable();
            var key = TerritoryExcludeKey(territoryId);
            var excluded = settings.ClanShareExcludedTerritories.Contains(key);
            if (excluded)
                settings.ClanShareExcludedTerritories.Remove(key);
            else
                settings.ClanShareExcludedTerritories.Add(key);
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
            return !excluded;
        }

        public bool IsStarterKitSeeded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.StarterKitSeeded == null)
                return false;
            return settings.StarterKitSeeded.Contains(key);
        }

        public void MarkStarterKitSeeded(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = GetGlobalMutable();
            var changed = false;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key) || settings.StarterKitSeeded.Contains(key))
                    continue;
                settings.StarterKitSeeded.Add(key);
                changed = true;
            }
            if (!changed)
                return;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public bool IsStarterKitChestSeeded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.StarterKitChestSeeded == null)
                return false;
            return settings.StarterKitChestSeeded.Contains(key);
        }

        public void MarkStarterKitChestSeeded(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = GetGlobalMutable();
            var changed = false;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key) || settings.StarterKitChestSeeded.Contains(key))
                    continue;
                settings.StarterKitChestSeeded.Add(key);
                changed = true;
            }
            if (!changed)
                return;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public bool IsStarterKitChestOptOut(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!playerSettings.TryGetValue(GLOBAL_PLAYER_ID, out var settings) || settings.StarterKitChestOptOut == null)
                return false;
            return settings.StarterKitChestOptOut.Contains(key);
        }

        public void SetStarterKitChestOptOut(bool optedOut, params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = GetGlobalMutable();
            var changed = false;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                if (optedOut)
                {
                    if (settings.StarterKitChestOptOut.Contains(key))
                        continue;
                    settings.StarterKitChestOptOut.Add(key);
                    changed = true;
                }
                else if (settings.StarterKitChestOptOut.Remove(key))
                {
                    changed = true;
                }
            }
            if (!changed)
                return;
            playerSettings[GLOBAL_PLAYER_ID] = settings;
            SaveSettings();
        }

        public PlayerSettings GetSettings(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                return new PlayerSettings();
            return settings;
        }

        public PlayerSettings GetGlobalSettings()
        {
            return playerSettings[GLOBAL_PLAYER_ID];
        }

        static string NormalizeGroupKey(string name)
        {
            return ItemGroupService.NormalizeName(name);
        }

        public bool HasItemGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = NormalizeGroupKey(name);
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public IEnumerable<(string name, int count)> ListCustomGroups(ulong playerId)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                yield break;
            foreach (var kvp in settings.ItemGroups)
            {
                var count = kvp.Value == null ? 0 : kvp.Value.Count;
                yield return (kvp.Key, count);
            }
        }

        public IEnumerable<(string guid, string name)> ListItemGroupMembers(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                yield break;
            var key = NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    storedKey = existing;
                    break;
                }
            }
            if (storedKey == null)
                yield break;
            var guids = settings.ItemGroups[storedKey];
            if (guids == null)
                yield break;
            Dictionary<string, string> names = null;
            settings.ItemGroupNames?.TryGetValue(storedKey, out names);
            foreach (var guid in guids)
            {
                var itemName = guid;
                if (names != null && names.TryGetValue(guid, out var stored) && !string.IsNullOrEmpty(stored))
                    itemName = stored;
                yield return (guid, itemName);
            }
        }

        public bool CreateItemGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            var key = NormalizeGroupKey(name);
            if (HasItemGroup(playerId, key) || settings.ItemGroups.ContainsKey(key))
                return false;
            settings.ItemGroups[key] = new List<string>();
            settings.ItemGroupNames[key] = new Dictionary<string, string>();
            playerSettings[playerId] = settings;
            SaveSettings();
            return true;
        }

        public bool DeleteItemGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    storedKey = existing;
                    break;
                }
            }
            if (storedKey == null)
                return false;
            settings.ItemGroups.Remove(storedKey);
            settings.ItemGroupNames?.Remove(storedKey);
            playerSettings[playerId] = settings;
            SaveSettings();
            return true;
        }

        public bool AddItemToGroup(ulong playerId, string name, PrefabGUID item, string itemName)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            var key = NormalizeGroupKey(name);
            string storedKey = key;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    storedKey = existing;
                    break;
                }
            }
            if (!settings.ItemGroups.TryGetValue(storedKey, out var list) || list == null)
            {
                list = new List<string>();
                settings.ItemGroups[storedKey] = list;
            }
            if (!settings.ItemGroupNames.TryGetValue(storedKey, out var names) || names == null)
            {
                names = new Dictionary<string, string>();
                settings.ItemGroupNames[storedKey] = names;
            }
            var guid = item.GuidHash.ToString();
            if (!list.Contains(guid))
                list.Add(guid);
            names[guid] = itemName;
            playerSettings[playerId] = settings;
            SaveSettings();
            return true;
        }

        public bool RemoveItemFromGroup(ulong playerId, string name, PrefabGUID item)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    storedKey = existing;
                    break;
                }
            }
            if (storedKey == null)
                return false;
            var guid = item.GuidHash.ToString();
            var removed = settings.ItemGroups[storedKey]?.Remove(guid) == true;
            settings.ItemGroupNames?.GetValueOrDefault(storedKey)?.Remove(guid);
            playerSettings[playerId] = settings;
            SaveSettings();
            return removed;
        }

        public int ApplyGroupAmounts(ulong playerId, IReadOnlyList<(PrefabGUID guid, string name)> items, bool isCap, int amount)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemReserves ??= new Dictionary<string, int>();
            settings.ItemReserveNames ??= new Dictionary<string, string>();
            settings.ItemCaps ??= new Dictionary<string, int>();
            settings.ItemCapNames ??= new Dictionary<string, string>();

            var updated = 0;
            foreach (var (guid, name) in items)
            {
                if (guid.GuidHash == 0)
                    continue;
                var key = guid.GuidHash.ToString();
                if (isCap)
                {
                    if (amount < 0)
                    {
                        settings.ItemCaps.Remove(key);
                        settings.ItemCapNames.Remove(key);
                    }
                    else
                    {
                        settings.ItemCaps[key] = amount;
                        settings.ItemCapNames[key] = name;
                    }
                }
                else
                {
                    if (amount < 0)
                    {
                        settings.ItemReserves.Remove(key);
                        settings.ItemReserveNames.Remove(key);
                    }
                    else
                    {
                        settings.ItemReserves[key] = amount;
                        settings.ItemReserveNames[key] = name;
                        if (amount > 0)
                            settings.DontPullLast = true;
                    }
                }
                updated++;
            }

            playerSettings[playerId] = settings;
            SaveSettings();
            return updated;
        }

        public bool IsDeletedGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.DeletedGroups == null)
                return false;
            var key = NormalizeGroupKey(name);
            foreach (var existing in settings.DeletedGroups)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool HasGroupOverlay(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings) || settings.OverlaidGroups == null)
                return false;
            var key = NormalizeGroupKey(name);
            foreach (var existing in settings.OverlaidGroups)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool ListContainsNormalized(List<string> list, string key)
        {
            if (list == null)
                return false;
            foreach (var existing in list)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static void ListAddNormalized(List<string> list, string key)
        {
            if (!ListContainsNormalized(list, key))
                list.Add(key);
        }

        static bool ListRemoveNormalized(List<string> list, string key)
        {
            if (list == null)
                return false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (NormalizeGroupKey(list[i]).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        static string FindStoredGroupKey(Dictionary<string, List<string>> groups, string key)
        {
            if (groups == null)
                return null;
            foreach (var existing in groups.Keys)
            {
                if (NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            return null;
        }

        public void WriteGroupOverlay(ulong playerId, string name, IEnumerable<(string guid, string itemName)> members)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = NormalizeGroupKey(name);
            var storedKey = FindStoredGroupKey(settings.ItemGroups, key) ?? key;
            var list = new List<string>();
            var names = new Dictionary<string, string>();
            foreach (var (guid, itemName) in members)
            {
                if (string.IsNullOrEmpty(guid) || list.Contains(guid))
                    continue;
                list.Add(guid);
                if (!string.IsNullOrEmpty(itemName))
                    names[guid] = itemName;
            }
            settings.ItemGroups[storedKey] = list;
            settings.ItemGroupNames[storedKey] = names;
            ListRemoveNormalized(settings.DeletedGroups, key);
            ListAddNormalized(settings.OverlaidGroups, key);
            playerSettings[playerId] = settings;
            SaveSettings();
        }

        public bool DeleteBuiltInGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = NormalizeGroupKey(name);
            var storedKey = FindStoredGroupKey(settings.ItemGroups, key);
            if (storedKey != null)
            {
                settings.ItemGroups.Remove(storedKey);
                settings.ItemGroupNames?.Remove(storedKey);
            }
            ListRemoveNormalized(settings.OverlaidGroups, key);
            ListAddNormalized(settings.DeletedGroups, key);
            playerSettings[playerId] = settings;
            SaveSettings();
            return true;
        }

        public bool RestoreBuiltInGroup(ulong playerId, string name)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = NormalizeGroupKey(name);
            var storedKey = FindStoredGroupKey(settings.ItemGroups, key);
            if (storedKey != null)
            {
                settings.ItemGroups.Remove(storedKey);
                settings.ItemGroupNames?.Remove(storedKey);
            }
            var changed = ListRemoveNormalized(settings.OverlaidGroups, key) | ListRemoveNormalized(settings.DeletedGroups, key) | (storedKey != null);
            playerSettings[playerId] = settings;
            SaveSettings();
            return changed;
        }

        public List<string> RestoreAllBuiltInGroups(ulong playerId, IEnumerable<string> builtInNames)
        {
            if (!playerSettings.TryGetValue(playerId, out var settings))
                settings = new PlayerSettings();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var restored = new List<string>();
            foreach (var builtIn in builtInNames)
            {
                var key = NormalizeGroupKey(builtIn);
                var storedKey = FindStoredGroupKey(settings.ItemGroups, key);
                if (storedKey != null)
                {
                    settings.ItemGroups.Remove(storedKey);
                    settings.ItemGroupNames?.Remove(storedKey);
                }
                ListRemoveNormalized(settings.OverlaidGroups, key);
                ListRemoveNormalized(settings.DeletedGroups, key);
                restored.Add(key);
            }
            playerSettings[playerId] = settings;
            SaveSettings();
            return restored;
        }
    }
}

