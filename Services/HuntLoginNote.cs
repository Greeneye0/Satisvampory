using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// On login: servants by plot/status, then hunt haul since last logout or 72h
    /// (smallest stacks first so the largest count is the last chat line).
    /// </summary>
    internal static class HuntLoginNote
    {
        const int MaxAgeHours = 72;
        const int MaxChat = 500;
        static readonly TimeSpan MaxAge = TimeSpan.FromHours(MaxAgeHours);
        static readonly string PathFile = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "hunt-haul.json");
        static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        static readonly HashSet<ulong> seen = new();
        static readonly Dictionary<ulong, DateTime> logoutAt = new();
        static readonly List<HaulRow> haul = new();
        static bool loaded;
        static bool seeded;

        struct HaulRow
        {
            public DateTime Utc;
            public int Plot;
            public List<(string name, int amount)> Items;
        }

        class FileDto
        {
            public Dictionary<string, string> Logout { get; set; }
            public List<HaulDto> Haul { get; set; }
        }

        class HaulDto
        {
            public string Utc { get; set; }
            public int Plot { get; set; }
            public List<ItemDto> Items { get; set; }
        }

        class ItemDto
        {
            public string N { get; set; }
            public int A { get; set; }
        }

        public static IEnumerator Loop()
        {
            Load();
            yield return new WaitForSeconds(6f);
            SeedConnected();
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                try { Tick(); }
                catch (Exception e) { Core.LogException(e, "HuntLoginNote"); }
            }
        }

        public static void FlushLogouts()
        {
            try
            {
                if (!Core.HasInitialized)
                    return;
                Core.TerritoryService.EachUser((_, user) =>
                {
                    if (user.IsConnected)
                        logoutAt[user.PlatformId] = DateTime.UtcNow;
                });
                Save();
            }
            catch { }
        }

        public static void RecordHaul(int plot, Dictionary<PrefabGUID, int> loot)
        {
            if (loot == null || loot.Count == 0)
                return;
            Load();
            var items = new List<(string name, int amount)>(loot.Count);
            foreach (var kv in loot)
            {
                if (kv.Value <= 0 || kv.Key.GuidHash == 0)
                    continue;
                items.Add((StashRouting.ItemLabel(kv.Key), kv.Value));
            }
            if (items.Count == 0)
                return;
            haul.Add(new HaulRow { Utc = DateTime.UtcNow, Plot = plot, Items = items });
            Prune();
            Save();
        }

        static void SeedConnected()
        {
            seen.Clear();
            Core.TerritoryService.EachUser((_, user) =>
            {
                if (user.IsConnected)
                    seen.Add(user.PlatformId);
            });
            seeded = true;
        }

        static void Tick()
        {
            if (!Core.HasInitialized)
                return;
            Load();
            var now = new HashSet<ulong>();
            var joining = new List<User>();
            Core.TerritoryService.EachUser((_, user) =>
            {
                if (!user.IsConnected)
                    return;
                now.Add(user.PlatformId);
                if (seeded && !seen.Contains(user.PlatformId))
                    joining.Add(user);
            });
            foreach (var id in seen)
            {
                if (!now.Contains(id))
                    logoutAt[id] = DateTime.UtcNow;
            }
            if (seen.Count != now.Count)
                Save();
            else
            {
                foreach (var id in now)
                {
                    if (!seen.Contains(id))
                    {
                        Save();
                        break;
                    }
                }
            }
            seen.Clear();
            foreach (var id in now)
                seen.Add(id);
            for (var i = 0; i < joining.Count; i++)
            {
                var user = joining[i];
                Core.StartCoroutine(NoteSoon(user.PlatformId));
            }
        }

        static IEnumerator NoteSoon(ulong steamId)
        {
            yield return new WaitForSeconds(3f);
            try { SendNote(steamId); }
            catch (Exception e) { Core.LogException(e, "HuntLoginNote.Send"); }
        }

        static void SendNote(ulong steamId)
        {
            User user = default;
            var found = false;
            Core.TerritoryService.EachUser((_, u) =>
            {
                if (found || !u.IsConnected || u.PlatformId != steamId)
                    return;
                user = u;
                found = true;
            });
            if (!found)
                return;

            var plots = PlotsFor(user);
            var since = Since(steamId, out var hours, out var fromLogout);
            var haulLines = HaulLines(plots, since, hours, fromLogout);
            var servantLines = new List<string>();
            for (var p = 0; p < plots.Count; p++)
                ServantLines(plots[p], servantLines);
            if (servantLines.Count > 0)
                servantLines.Insert(0, "Servants");
            if (haulLines.Count == 0 && servantLines.Count == 0)
                return;

            // Vanilla client chat history is a short ring; oldest lines fall off the top.
            // Servants first, then haul, with smallest stacks first so the largest count
            // is the last (still-visible) line. Server plugins cannot lengthen that ring.
            for (var i = 0; i < servantLines.Count; i++)
                Tell(user, servantLines[i]);
            for (var i = 0; i < haulLines.Count; i++)
                Tell(user, haulLines[i]);
            DestDebugLog.Note("throne", plots.Count > 0 ? plots[0] : -1, steamId,
                "login servants=" + servantLines.Count + " haul=" + haulLines.Count
                + " hours=" + hours.ToString("0.0", CultureInfo.InvariantCulture));
        }

        static List<int> PlotsFor(User user)
        {
            var plots = new List<int>();
            if (Core.TerritoryService.IsClanShareOn(user))
            {
                var clan = Core.TerritoryService.GetClanLogisticsTerritoryIds(user);
                if (clan != null && clan.Count > 0)
                {
                    plots.AddRange(clan);
                    plots.Sort();
                    return plots;
                }
            }
            Core.TerritoryService.EachKnownPlot(id =>
            {
                if (Core.TerritoryService.TryGetTerritoryOwnerPlatformId(id, out var owner) && owner == user.PlatformId)
                    plots.Add(id);
            });
            plots.Sort();
            return plots;
        }

        static void ServantLines(int plot, List<string> lines)
        {
            var people = new List<string>();
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantCoffinstation>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var coffin = rows[i];
                    if (coffin == Entity.Null || !Core.EntityManager.Exists(coffin) || !coffin.Has<ServantCoffinstation>())
                        continue;
                    if (Core.TerritoryService.GetTerritoryId(coffin) != plot)
                        continue;
                    var st = coffin.Read<ServantCoffinstation>();
                    var name = st.ServantName.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (st.State == ServantCoffinState.Empty)
                        continue;
                    people.Add("  " + name + " — " + StatusOf(coffin, st, plot));
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            if (people.Count == 0)
                return;
            lines.Add("<color=yellow>" + Core.TerritoryService.FormatPlotLabel(plot) + "</color>");
            for (var i = 0; i < people.Count; i++)
                lines.Add(people[i]);
        }

        static string StatusOf(Entity coffin, ServantCoffinstation st, int plot)
        {
            if (st.State == ServantCoffinState.ServantRevivable)
                return "dead";
            if (st.State == ServantCoffinState.Reviving)
                return "reviving";
            var servant = st.ConnectedServant.GetEntityOnServer();
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                return st.State.ToString();
            if (st.Injury.GuidHash != 0)
                return "injured";
            if (TryHunt(servant, plot, out var dest, out var remain))
            {
                var s = "hunt";
                if (!string.IsNullOrWhiteSpace(dest))
                    s += " " + dest;
                if (remain >= 0)
                    s += " " + FormatRemain(remain);
                return s;
            }
            return "home";
        }

        static bool TryHunt(Entity servant, int plot, out string dest, out double remain)
        {
            dest = "";
            remain = -1;
            if (servant.Has<ServantData>() && !servant.Read<ServantData>().IsOnMission)
                return false;
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null || !heart.Has<ActiveServantMission>())
                return servant.Has<ServantData>() && servant.Read<ServantData>().IsOnMission;
            var buf = heart.ReadBuffer<ActiveServantMission>();
            for (var m = 0; m < buf.Length; m++)
            {
                var mission = buf[m];
                if (mission.Servant1.GetEntityOnServer() != servant
                    && mission.Servant2.GetEntityOnServer() != servant
                    && mission.Servant3.GetEntityOnServer() != servant)
                    continue;
                remain = Remain(mission);
                dest = RepeatHunts.DestForMission(mission.MissionID);
                return true;
            }
            return servant.Has<ServantData>() && servant.Read<ServantData>().IsOnMission;
        }

        static double Remain(ActiveServantMission mission)
        {
            var now = DateTime.UtcNow.Ticks;
            var remaining = (mission.MissionStartTimeTicks
                             + (long)(mission.MissionLengthSeconds * TimeSpan.TicksPerSecond)
                             - now) / (double)TimeSpan.TicksPerSecond;
            if (double.IsNaN(remaining) || double.IsInfinity(remaining) || remaining > mission.MissionLengthSeconds + 120)
                remaining = 0;
            if (remaining < 0)
                remaining = 0;
            return remaining;
        }

        static string FormatRemain(double seconds)
        {
            var total = (int)Math.Floor(seconds);
            if (total >= 3600)
                return (total / 3600) + "h " + ((total % 3600) / 60) + "m";
            if (total >= 60)
                return (total / 60) + "m";
            return total + "s";
        }

        static DateTime Since(ulong steamId, out double hours, out bool fromLogout)
        {
            var now = DateTime.UtcNow;
            var floor = now - MaxAge;
            fromLogout = logoutAt.TryGetValue(steamId, out var lo) && lo > floor;
            var since = fromLogout ? lo : floor;
            hours = (now - since).TotalHours;
            if (hours < 0)
                hours = 0;
            return since;
        }

        static List<string> HaulLines(List<int> plots, DateTime since, double hours, bool fromLogout)
        {
            var lines = new List<string>();
            var window = fromLogout
                ? "since logout (" + FormatHours(hours) + ")"
                : "last " + FormatHours(hours);
            var plotSet = new HashSet<int>(plots);
            var byPlot = new Dictionary<int, Dictionary<string, int>>();
            for (var i = 0; i < haul.Count; i++)
            {
                var row = haul[i];
                if (row.Utc < since)
                    continue;
                if (plotSet.Count > 0 && !plotSet.Contains(row.Plot))
                    continue;
                if (row.Items == null)
                    continue;
                if (!byPlot.TryGetValue(row.Plot, out var totals))
                {
                    totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    byPlot[row.Plot] = totals;
                }
                for (var k = 0; k < row.Items.Count; k++)
                {
                    var it = row.Items[k];
                    if (string.IsNullOrEmpty(it.name) || it.amount <= 0)
                        continue;
                    totals.TryGetValue(it.name, out var n);
                    totals[it.name] = n + it.amount;
                }
            }
            lines.Add("Hunt haul " + window);
            if (byPlot.Count == 0)
            {
                lines.Add("  none");
                return lines;
            }
            var plotOrder = new List<int>(byPlot.Keys);
            plotOrder.Sort();
            for (var p = 0; p < plotOrder.Count; p++)
            {
                var plot = plotOrder[p];
                lines.Add("<color=yellow>" + Core.TerritoryService.FormatPlotLabel(plot) + "</color>");
                var parts = new List<KeyValuePair<string, int>>(byPlot[plot]);
                parts.Sort((a, b) =>
                {
                    var c = a.Value.CompareTo(b.Value);
                    return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
                });
                for (var i = 0; i < parts.Count; i++)
                {
                    lines.Add("  <color=white>" + parts[i].Value + "</color>x <color=green>" + parts[i].Key + "</color>");
                }
            }
            return lines;
        }

        static string FormatHours(double hours)
        {
            if (hours < 1)
                return Math.Max(1, (int)Math.Round(hours * 60)) + "m";
            if (hours < 10)
                return hours.ToString("0.0", CultureInfo.InvariantCulture) + "h";
            return ((int)Math.Round(hours)).ToString(CultureInfo.InvariantCulture) + "h";
        }

        static void Tell(User user, string line)
        {
            if (string.IsNullOrEmpty(line) || !user.IsConnected)
                return;
            if (line.Length > MaxChat)
                line = line.Substring(0, MaxChat - 1) + "…";
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, line);
        }

        static void Load()
        {
            if (loaded)
                return;
            loaded = true;
            try
            {
                if (!File.Exists(PathFile))
                    return;
                var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(PathFile));
                if (dto == null)
                    return;
                if (dto.Logout != null)
                {
                    foreach (var kv in dto.Logout)
                    {
                        if (!ulong.TryParse(kv.Key, out var id))
                            continue;
                        if (DateTime.TryParse(kv.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                            logoutAt[id] = t.ToUniversalTime();
                    }
                }
                if (dto.Haul != null)
                {
                    for (var i = 0; i < dto.Haul.Count; i++)
                    {
                        var h = dto.Haul[i];
                        if (!DateTime.TryParse(h.Utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc))
                            continue;
                        var items = new List<(string name, int amount)>();
                        if (h.Items != null)
                        {
                            for (var k = 0; k < h.Items.Count; k++)
                            {
                                var it = h.Items[k];
                                if (it == null || string.IsNullOrEmpty(it.N) || it.A <= 0)
                                    continue;
                                items.Add((it.N, it.A));
                            }
                        }
                        haul.Add(new HaulRow { Utc = utc.ToUniversalTime(), Plot = h.Plot, Items = items });
                    }
                }
                Prune();
            }
            catch (Exception e)
            {
                Core.Log.LogWarning("HuntLoginNote load: " + e.Message);
            }
        }

        static void Prune()
        {
            var floor = DateTime.UtcNow - MaxAge;
            haul.RemoveAll(r => r.Utc < floor);
            var drop = new List<ulong>();
            foreach (var kv in logoutAt)
            {
                if (kv.Value < floor)
                    drop.Add(kv.Key);
            }
            for (var i = 0; i < drop.Count; i++)
                logoutAt.Remove(drop[i]);
        }

        static void Save()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(PathFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var dto = new FileDto
                {
                    Logout = new Dictionary<string, string>(),
                    Haul = new List<HaulDto>(haul.Count)
                };
                foreach (var kv in logoutAt)
                    dto.Logout[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value.ToUniversalTime().ToString("o");
                for (var i = 0; i < haul.Count; i++)
                {
                    var row = haul[i];
                    var items = new List<ItemDto>();
                    if (row.Items != null)
                    {
                        for (var k = 0; k < row.Items.Count; k++)
                            items.Add(new ItemDto { N = row.Items[k].name, A = row.Items[k].amount });
                    }
                    dto.Haul.Add(new HaulDto
                    {
                        Utc = row.Utc.ToUniversalTime().ToString("o"),
                        Plot = row.Plot,
                        Items = items
                    });
                }
                var tmp = PathFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOpts));
                File.Copy(tmp, PathFile, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Core.Log.LogDebug("HuntLoginNote save: " + e.Message);
            }
        }
    }
}
