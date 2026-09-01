using System;
using System.Collections.Generic;
using Stunlock.Core;

namespace Satisvampory.Services
{
    /// <summary>Satis-only settings (reserves, caps, groups, scoop, ClanShare, heart fuel).</summary>
    internal static class SettingsExtras
    {
        static PlayerSettingsService S => Core.PlayerSettings;

        internal static bool IsRrGlobalEnabled(ulong playerId) =>
            S.Snapshot(playerId, false).RrGlobal && (!S.TryRow(WorldId, out var g) || g.RrGlobalAllow);

        internal static bool ToggleRrGlobal(ulong playerId = WorldId)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            if (playerId == WorldId)
            {
                settings.RrGlobalAllow = !settings.RrGlobalAllow;
                S.Put(playerId, settings);
                S.MarkDirty();
                return settings.RrGlobalAllow;
            }
            settings.RrGlobal = !settings.RrGlobal;
            S.Put(playerId, settings);
            S.MarkDirty();
            return settings.RrGlobal;
        }

        internal static bool IsRrGlobalServerAllowed() => !S.TryRow(0, out var g) || g.RrGlobalAllow;
        
        internal static int GetPullReserve(ulong playerId, PrefabGUID item = default)
        {
            if (!IsDontPullLastEnabled(playerId)) return 0;
            if (!S.TryRow(playerId, out var settings))
                settings = S.Blank;
            if (item.GuidHash != 0 && settings.ItemReserves != null &&
                settings.ItemReserves.TryGetValue(item.GuidHash.ToString(), out var specific))
                return specific; // 0 is a valid override: leave nothing of THIS item
            return settings.PullReserve > 0 ? settings.PullReserve : 10;
        }

        internal static int SetPullReserve(ulong playerId, int amount)
        {
            if (amount < 0) amount = 0;
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.PullReserve = amount;
            settings.DontPullLast = amount > 0;
            S.Put(playerId, settings);
            S.MarkDirty();
            return settings.PullReserve;
        }

        internal static int SetItemReserve(ulong playerId, PrefabGUID item, string itemName, int amount)
        {
            if (amount < 0) amount = 0;
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemReserves ??= new Dictionary<string, int>();
            settings.ItemReserveNames ??= new Dictionary<string, string>();
            var key = item.GuidHash.ToString();
            settings.ItemReserves[key] = amount;
            settings.ItemReserveNames[key] = itemName;
            if (amount > 0) settings.DontPullLast = true;
            S.Put(playerId, settings);
            S.MarkDirty();
            return amount;
        }

        internal static bool ClearItemReserve(ulong playerId, PrefabGUID item)
        {
            if (!S.TryRow(playerId, out var settings)) return false;
            if (settings.ItemReserves == null) return false;
            var key = item.GuidHash.ToString();
            var removed = settings.ItemReserves.Remove(key);
            settings.ItemReserveNames?.Remove(key);
            S.Put(playerId, settings);
            S.MarkDirty();
            return removed;
        }

        internal static IEnumerable<(string name, int amount)> ListItemReserves(ulong playerId)
        {
            if (!S.TryRow(playerId, out var settings))
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

        internal static int GetItemReserveOverrideCount(ulong playerId)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemReserves == null)
                return 0;
            return settings.ItemReserves.Count;
        }

        internal static bool TryGetItemCap(ulong playerId, PrefabGUID item, out int cap)
        {
            cap = 0;
            if (!S.TryRow(playerId, out var settings))
                return false;
            if (settings.ItemCaps == null)
                return false;
            if (item.GuidHash == 0)
                return false;
            return settings.ItemCaps.TryGetValue(item.GuidHash.ToString(), out cap);
        }

        internal static int SetItemCap(ulong playerId, PrefabGUID item, string itemName, int amount)
        {
            if (amount < 0)
            {
                ClearItemCap(playerId, item);
                return amount;
            }
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemCaps ??= new Dictionary<string, int>();
            settings.ItemCapNames ??= new Dictionary<string, string>();
            var key = item.GuidHash.ToString();
            settings.ItemCaps[key] = amount;
            settings.ItemCapNames[key] = itemName;
            S.Put(playerId, settings);
            S.MarkDirty();
            return amount;
        }

        internal static bool ClearItemCap(ulong playerId, PrefabGUID item)
        {
            if (!S.TryRow(playerId, out var settings)) return false;
            if (settings.ItemCaps == null) return false;
            var key = item.GuidHash.ToString();
            var removed = settings.ItemCaps.Remove(key);
            settings.ItemCapNames?.Remove(key);
            S.Put(playerId, settings);
            S.MarkDirty();
            return removed;
        }

        internal static IEnumerable<(string name, int amount)> ListItemCaps(ulong playerId)
        {
            if (!S.TryRow(playerId, out var settings))
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

        internal static int GetItemCapOverrideCount(ulong playerId)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemCaps == null)
                return 0;
            return settings.ItemCaps.Count;
        }

        internal static bool IsConveyorLoopsAllowed() => S.TryRow(0, out var settings) && settings.ConveyorLoops;

        internal static bool ToggleConveyorLoops()
        {
            if (!S.TryRow(0, out var settings))
                settings = new SettingsRow();
            settings.ConveyorLoops = !settings.ConveyorLoops;
            S.Put(0, settings);
            S.MarkDirty();
            return settings.ConveyorLoops;
        }

        internal static bool IsGlobalSalvageEnabled() => !S.TryRow(0, out var settings) || settings.Salvage;

        internal static string S.PlotSalvageKey(int territoryId) => territoryId.ToString();

        internal static bool GetPlotSalvageFlag(ulong heartOwnerId, int territoryId) =>
            S.TryRow(heartOwnerId, out var settings) && settings.PlotSalvage != null
            && settings.PlotSalvage.TryGetValue(S.PlotSalvageKey(territoryId), out var on) && on;

        internal static bool TogglePlotSalvage(ulong heartOwnerId, int territoryId)
        {
            if (!S.Rows.TryGetValue(heartOwnerId, out var settings))
                settings = new SettingsRow();
            settings.PlotSalvage ??= new Dictionary<string, bool>();
            var key = S.PlotSalvageKey(territoryId);
            var next = !(settings.PlotSalvage.TryGetValue(key, out var on) && on);
            settings.PlotSalvage[key] = next;
            S.Rows[heartOwnerId] = settings;
            S.MarkDirty();
            return next;
        }

        internal static string S.HeartFeedKey(int territoryId) => territoryId.ToString();

        /// <summary>Heart auto-feed is ON by default when the plot has no stored flag.</summary>
        internal static bool IsHeartFeedEnabled(ulong heartOwnerId, int territoryId) =>
            !S.TryRow(heartOwnerId, out var settings) || settings.HeartFeed == null
            || !settings.HeartFeed.TryGetValue(S.HeartFeedKey(territoryId), out var on) || on;

        internal static bool ToggleHeartFeed(ulong heartOwnerId, int territoryId)
        {
            if (!S.Rows.TryGetValue(heartOwnerId, out var settings))
                settings = new SettingsRow();
            settings.HeartFeed ??= new Dictionary<string, bool>();
            var key = S.HeartFeedKey(territoryId);
            var current = !settings.HeartFeed.TryGetValue(key, out var on) || on;
            var next = !current;
            settings.HeartFeed[key] = next;
            S.Rows[heartOwnerId] = settings;
            S.MarkDirty();
            return next;
        }

        internal static bool IsHeartFuelSeeded(string heartKey)
        {
            if (string.IsNullOrEmpty(heartKey))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.HeartFuelSeeded == null)
                return false;
            return settings.HeartFuelSeeded.Contains(heartKey);
        }

        internal static void MarkHeartFuelSeeded(params string[] heartKeys)
        {
            if (!S.TryRow(0, out var settings))
                settings = new SettingsRow();
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
            S.Put(0, settings);
            S.MarkDirty();
        }

        internal static bool IsHeartFuelOptOut(string heartKey)
        {
            if (string.IsNullOrEmpty(heartKey))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.HeartFuelOptOut == null)
                return false;
            return settings.HeartFuelOptOut.Contains(heartKey);
        }

        internal static void SetHeartFuelOptOut(bool optedOut, params string[] heartKeys)
        {
            if (!S.TryRow(0, out var settings))
                settings = new SettingsRow();
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
            S.Put(0, settings);
            S.MarkDirty();
        }

        internal static bool IsAutoEnabled(ulong platformId) => S.GetOrCreate(platformId).AutoScoop;

        internal static bool ToggleAuto(ulong platformId)
        {
            var s = S.GetOrCreate(platformId);
            s.AutoScoop = !s.AutoScoop;
            S.Put(platformId, s);
            return s.AutoScoop;
        }

        internal static static bool TryParseAutoFilter(string value, out AutoFilter filter)
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

        internal static AutoFilter GetAutoFilter(ulong platformId)
        {
            return TryParseAutoFilter(S.GetOrCreate(platformId).AutoFilter, out var filter)
                ? filter
                : AutoFilter.All;
        }

        internal static AutoFilter SetAutoFilter(ulong platformId, AutoFilter filter)
        {
            var s = S.GetOrCreate(platformId);
            s.AutoFilter = filter == AutoFilter.All ? "all" : "around";
            S.Put(platformId, s);
            return filter;
        }

        internal static void SetAutoOnWithFilter(ulong platformId, AutoFilter filter)
        {
            var s = S.GetOrCreate(platformId);
            s.AutoScoop = true;
            s.AutoFilter = filter == AutoFilter.All ? "all" : "around";
            S.Put(platformId, s);
        }

        internal static static bool TryParseNotifyMode(string value, out NotifyMode mode)
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

        internal static NotifyMode GetNotifyMode(ulong platformId)
        {
            return TryParseNotifyMode(S.GetOrCreate(platformId).NotifyMode, out var mode)
                ? mode
                : NotifyMode.Manual;
        }

        internal static NotifyMode SetNotifyMode(ulong platformId, NotifyMode mode)
        {
            var s = S.GetOrCreate(platformId);
            s.NotifyMode = mode switch
            {
                NotifyMode.Off => "off",
                NotifyMode.On => "on",
                _ => "manual"
            };
            S.Put(platformId, s);
            return mode;
        }

        internal static float GetRadius(ulong platformId)
        {
            var r = S.GetOrCreate(platformId).Radius;
            if (r < 1f) r = 10f;
            if (r > 50f) r = 50f;
            return r;
        }

        internal static float SetRadius(ulong platformId, float radius)
        {
            if (radius < 1f) radius = 1f;
            if (radius > 50f) radius = 50f;
            var s = S.GetOrCreate(platformId);
            s.Radius = radius;
            S.Put(platformId, s);
            return s.Radius;
        }

        internal static CapMode GetCapMode(ulong platformId)
        {
            return string.Equals(S.GetOrCreate(platformId).ScoopMode, "guild", StringComparison.OrdinalIgnoreCase)
                ? CapMode.Guild
                : CapMode.Bags;
        }

        internal static CapMode SetCapMode(ulong platformId, CapMode mode)
        {
            var s = S.GetOrCreate(platformId);
            s.ScoopMode = mode == CapMode.Guild ? "guild" : "bags";
            S.Put(platformId, s);
            return mode;
        }

        internal static bool IsExcluded(ulong platformId, PrefabGUID item)
        {
            return S.GetOrCreate(platformId).ScoopExcludes.Contains(item.GuidHash.ToString());
        }

        internal static bool ToggleExclude(ulong platformId, PrefabGUID item, string name)
        {
            var s = S.GetOrCreate(platformId);
            var key = item.GuidHash.ToString();
            if (s.ScoopExcludes.Contains(key))
            {
                s.ScoopExcludes.Remove(key);
                s.ScoopExcludeNames.Remove(key);
                S.Put(platformId, s);
                return false;
            }
            s.ScoopExcludes.Add(key);
            s.ScoopExcludeNames[key] = name ?? item.PrefabName();
            S.Put(platformId, s);
            return true;
        }

        internal static IReadOnlyList<(PrefabGUID prefab, string name)> ListExcludes(ulong platformId)
        {
            var s = S.GetOrCreate(platformId);
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

        internal static int GetCap(ulong platformId, PrefabGUID item)
        {
            if (S.GetOrCreate(platformId).ScoopCaps.TryGetValue(item.GuidHash.ToString(), out var cap))
                return cap;
            return -1;
        }

        internal static void SetScoopCap(ulong platformId, PrefabGUID item, int cap, string name)
        {
            var s = S.GetOrCreate(platformId);
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
            S.Put(platformId, s);
        }

        internal static void ClearAllScoopCaps(ulong platformId)
        {
            var s = S.GetOrCreate(platformId);
            s.ScoopCaps.Clear();
            s.ScoopCapNames.Clear();
            S.Put(platformId, s);
        }

        internal static IReadOnlyList<(PrefabGUID prefab, string name, int cap)> ListScoopCaps(ulong platformId)
        {
            var s = S.GetOrCreate(platformId);
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

        internal static bool IsClanShareEnabled(ulong playerId)
        {
            if (!S.TryRow(playerId, out var settings))
                return false;
            return settings.ClanShare;
        }

        internal static bool ToggleClanShare(ulong playerId)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ClanShare = !settings.ClanShare;
            S.Put(playerId, settings);
            S.MarkDirty();
            return settings.ClanShare;
        }

        internal static bool TryGetClanShareFlag(string clanKey, out bool enabled)
        {
            enabled = false;
            if (string.IsNullOrEmpty(clanKey))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.ClanShareByClan == null)
                return false;
            return settings.ClanShareByClan.TryGetValue(clanKey, out enabled);
        }

        internal static void SetClanShareForClan(string clanKey, bool enabled)
        {
            if (string.IsNullOrEmpty(clanKey))
                return;
            var settings = S.GetGlobalMutable();
            settings.ClanShareByClan[clanKey] = enabled;
            S.Put(0, settings);
            S.MarkDirty();
            if (Core.HasInitialized && Core.TerritoryService != null)
                Core.TerritoryService.InvalidateClanPlotCache();
        }

        internal static bool ToggleClanShareForClan(string clanKey, bool current)
        {
            var next = !current;
            SetClanShareForClan(clanKey, next);
            return next;
        }

        internal static string S.TerritoryExcludeKey(int territoryId) => "t" + territoryId;

        internal static bool IsTerritoryClanShareExcluded(int territoryId)
        {
            if (!S.TryRow(WorldId, out var settings) || settings.ClanShareExcludedTerritories == null)
                return false;
            return settings.ClanShareExcludedTerritories.Contains(S.TerritoryExcludeKey(territoryId));
        }

        internal static bool ToggleTerritoryClanShareExclude(int territoryId)
        {
            var settings = S.GetGlobalMutable();
            var key = S.TerritoryExcludeKey(territoryId);
            var excluded = settings.ClanShareExcludedTerritories.Contains(key);
            if (excluded)
                settings.ClanShareExcludedTerritories.Remove(key);
            else
                settings.ClanShareExcludedTerritories.Add(key);
            S.Put(0, settings);
            S.MarkDirty();
            if (Core.HasInitialized && Core.TerritoryService != null)
                Core.TerritoryService.InvalidateClanPlotCache();
            return !excluded;
        }

        internal static bool IsStarterKitSeeded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.StarterKitSeeded == null)
                return false;
            return settings.StarterKitSeeded.Contains(key);
        }

        internal static void MarkStarterKitSeeded(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = S.GetGlobalMutable();
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
            S.Put(0, settings);
            S.MarkDirty();
        }

        internal static bool IsStarterKitChestSeeded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.StarterKitChestSeeded == null)
                return false;
            return settings.StarterKitChestSeeded.Contains(key);
        }

        internal static void MarkStarterKitChestSeeded(params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = S.GetGlobalMutable();
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
            S.Put(0, settings);
            S.MarkDirty();
        }

        internal static bool IsStarterKitChestOptOut(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (!S.TryRow(WorldId, out var settings) || settings.StarterKitChestOptOut == null)
                return false;
            return settings.StarterKitChestOptOut.Contains(key);
        }

        internal static void SetStarterKitChestOptOut(bool optedOut, params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;
            var settings = S.GetGlobalMutable();
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
            S.Put(0, settings);
            S.MarkDirty();
        }

        internal static string S.NormalizeGroupKey(string name)
        {
            return ItemGroupService.NormalizeName(name);
        }

        internal static bool S.HasItemGroup(ulong playerId, string name)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = S.NormalizeGroupKey(name);
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static IEnumerable<(string name, int count)> ListCustomGroups(ulong playerId)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                yield break;
            foreach (var kvp in settings.ItemGroups)
            {
                var count = kvp.Value == null ? 0 : kvp.Value.Count;
                yield return (kvp.Key, count);
            }
        }

        internal static IEnumerable<(string guid, string name)> ListItemGroupMembers(ulong playerId, string name)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                yield break;
            var key = S.NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
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

        internal static bool CreateItemGroup(ulong playerId, string name)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            var key = S.NormalizeGroupKey(name);
            if (S.HasItemGroup(playerId, key) || settings.ItemGroups.ContainsKey(key))
                return false;
            settings.ItemGroups[key] = new List<string>();
            settings.ItemGroupNames[key] = new Dictionary<string, string>();
            S.Put(playerId, settings);
            S.MarkDirty();
            return true;
        }

        internal static bool DeleteItemGroup(ulong playerId, string name)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = S.NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    storedKey = existing;
                    break;
                }
            }
            if (storedKey == null)
                return false;
            settings.ItemGroups.Remove(storedKey);
            settings.ItemGroupNames?.Remove(storedKey);
            S.Put(playerId, settings);
            S.MarkDirty();
            return true;
        }

        internal static bool AddItemToGroup(ulong playerId, string name, PrefabGUID item, string itemName)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            var key = S.NormalizeGroupKey(name);
            string storedKey = key;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
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
            S.Put(playerId, settings);
            S.MarkDirty();
            return true;
        }

        internal static bool RemoveItemFromGroup(ulong playerId, string name, PrefabGUID item)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.ItemGroups == null)
                return false;
            var key = S.NormalizeGroupKey(name);
            string storedKey = null;
            foreach (var existing in settings.ItemGroups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
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
            S.Put(playerId, settings);
            S.MarkDirty();
            return removed;
        }

        internal static int ApplyGroupAmounts(ulong playerId, IReadOnlyList<(PrefabGUID guid, string name)> items, bool isCap, int amount)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
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

            S.Put(playerId, settings);
            S.MarkDirty();
            return updated;
        }

        internal static bool IsDeletedGroup(ulong playerId, string name)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.DeletedGroups == null)
                return false;
            var key = S.NormalizeGroupKey(name);
            foreach (var existing in settings.DeletedGroups)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool HasGroupOverlay(ulong playerId, string name)
        {
            if (!S.Rows.TryGetValue(playerId, out var settings) || settings.OverlaidGroups == null)
                return false;
            var key = S.NormalizeGroupKey(name);
            foreach (var existing in settings.OverlaidGroups)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool S.ListContainsNormalized(List<string> list, string key)
        {
            if (list == null)
                return false;
            foreach (var existing in list)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static void S.ListAddNormalized(List<string> list, string key)
        {
            if (!S.ListContainsNormalized(list, key))
                list.Add(key);
        }

        internal static bool S.ListRemoveNormalized(List<string> list, string key)
        {
            if (list == null)
                return false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (S.NormalizeGroupKey(list[i]).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        internal static string S.FindStoredGroupKey(Dictionary<string, List<string>> groups, string key)
        {
            if (groups == null)
                return null;
            foreach (var existing in groups.Keys)
            {
                if (S.NormalizeGroupKey(existing).Equals(key, System.StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
            return null;
        }

        internal static void WriteGroupOverlay(ulong playerId, string name, IEnumerable<(string guid, string itemName)> members)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = S.NormalizeGroupKey(name);
            var storedKey = S.FindStoredGroupKey(settings.ItemGroups, key) ?? key;
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
            S.ListRemoveNormalized(settings.DeletedGroups, key);
            S.ListAddNormalized(settings.OverlaidGroups, key);
            S.Put(playerId, settings);
            S.MarkDirty();
        }

        internal static bool DeleteBuiltInGroup(ulong playerId, string name)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = S.NormalizeGroupKey(name);
            var storedKey = S.FindStoredGroupKey(settings.ItemGroups, key);
            if (storedKey != null)
            {
                settings.ItemGroups.Remove(storedKey);
                settings.ItemGroupNames?.Remove(storedKey);
            }
            S.ListRemoveNormalized(settings.OverlaidGroups, key);
            S.ListAddNormalized(settings.DeletedGroups, key);
            S.Put(playerId, settings);
            S.MarkDirty();
            return true;
        }

        internal static bool RestoreBuiltInGroup(ulong playerId, string name)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var key = S.NormalizeGroupKey(name);
            var storedKey = S.FindStoredGroupKey(settings.ItemGroups, key);
            if (storedKey != null)
            {
                settings.ItemGroups.Remove(storedKey);
                settings.ItemGroupNames?.Remove(storedKey);
            }
            var changed = S.ListRemoveNormalized(settings.OverlaidGroups, key) | S.ListRemoveNormalized(settings.DeletedGroups, key) | (storedKey != null);
            S.Put(playerId, settings);
            S.MarkDirty();
            return changed;
        }

        internal static List<string> RestoreAllBuiltInGroups(ulong playerId, IEnumerable<string> builtInNames)
        {
            if (!S.TryRow(playerId, out var settings))
                settings = new SettingsRow();
            settings.ItemGroups ??= new Dictionary<string, List<string>>();
            settings.ItemGroupNames ??= new Dictionary<string, Dictionary<string, string>>();
            settings.OverlaidGroups ??= new List<string>();
            settings.DeletedGroups ??= new List<string>();

            var restored = new List<string>();
            foreach (var builtIn in builtInNames)
            {
                var key = S.NormalizeGroupKey(builtIn);
                var storedKey = S.FindStoredGroupKey(settings.ItemGroups, key);
                if (storedKey != null)
                {
                    settings.ItemGroups.Remove(storedKey);
                    settings.ItemGroupNames?.Remove(storedKey);
                }
                S.ListRemoveNormalized(settings.OverlaidGroups, key);
                S.ListRemoveNormalized(settings.DeletedGroups, key);
                restored.Add(key);
            }
            S.Put(playerId, settings);
            S.MarkDirty();
            return restored;
        }
    }
}
