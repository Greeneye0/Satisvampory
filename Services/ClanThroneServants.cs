using BepInEx;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using ProjectM.Shared.Systems;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Satisvampory.Services
{
    /// <summary>
    /// ClanShare throne: pick another plot's servants, then the vanilla hunt map click
    /// (fog, discovered zones) is rewritten to SendOnMissionEvent for that plot.
    /// </summary>
    internal static class ClanThroneServants
    {
        static readonly Dictionary<ulong, int> selectedPlot = new();
        static readonly Dictionary<ulong, float3> returnPos = new();
        static readonly Dictionary<ulong, PendingPick> pendingPick = new();
        static readonly Dictionary<ulong, List<ServantRow>> pendingHuntList = new();
        static readonly Dictionary<ulong, PendingHunt> pendingHunt = new();
        static readonly Dictionary<int, Entity> throneByPlot = new();
        static readonly Dictionary<int, Entity> learnedThroneByPlot = new();
        static DateTime throneCacheAt;
        static readonly TimeSpan PickTtl = TimeSpan.FromMinutes(2);
        static readonly List<(Entity character, Entity target, NetworkId nid)> interactRestore = new();
        static readonly List<Entity> infoEntities = new();
        static string lastSkip = "";
        static int lastSitting = -1;
        static int lastSelected = -1;
        static int lastFrom = -1;
        static int lastTo = -1;
        static int lastResponseCount = -1;
        static string lastResponseNames = "";
        static string lastPatched = "";
        static string lastGoto = "";
        static string lastSit = "";
        static string lastSend = "";

        struct PendingPick
        {
            public List<int> Plots;
            public DateTime ExpiresUtc;
        }

        struct PendingHunt
        {
            public int Plot;
            public List<NetworkId> Servants;
            public List<string> Names;
            public DateTime ExpiresUtc;
        }

        struct ServantRow
        {
            public string Name;
            public NetworkId Nid;
            public Entity Servant;
        }

        public static void RewriteInfoRequests(ServantInfoEventSystem_Server system)
        {
            RestoreInteract();
            interactRestore.Clear();
            infoEntities.Clear();
            if (system == null || !Core.HasInitialized)
                return;
            RewriteQuery(system._RequestQuery, RewriteInfo);
        }

        public static void AfterInfoUpdate()
        {
            try
            {
                CaptureResponse();
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }

        public static void RewriteMissionEvents(ServantMissionActionSystem system)
        {
            if (system == null || !Core.HasInitialized)
                return;
            RewriteQuery(system._StartMissionEventQuery, RewriteStart);
            RewriteQuery(system._AbortMissionEventQuery, RewriteAbort);
        }

        public static bool MayManageFrom(Entity character, Entity throne)
        {
            if (!Core.HasInitialized || character == Entity.Null || throne == Entity.Null)
                return false;
            if (!Core.EntityManager.Exists(character) || !Core.EntityManager.Exists(throne))
                return false;
            var sitting = Core.TerritoryService.GetStandingTerritoryId(character);
            var target = Core.TerritoryService.GetTerritoryId(throne);
            if (sitting < 0 || target < 0)
                return false;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(sitting);
            if (ids == null || ids.Count <= 1)
                return false;
            var sittingOk = false;
            var targetOk = false;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == sitting) sittingOk = true;
                if (ids[i] == target) targetOk = true;
            }
            return sittingOk && targetOk;
        }

        public static string ChatList(Entity character, ulong steam)
        {
            var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
            if (standing < 0)
                return "Stand on a clan castle (or sit its throne) to pick a plot.";
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standing);
            if (ids == null || ids.Count == 0)
                return "No castle under you.";
            if (ids.Count == 1)
                return "ClanShare is off or this plot is excluded. Sit this throne to manage its servants.";
            selectedPlot.TryGetValue(steam, out var managing);
            var usingDefault = managing <= 0 || managing == standing;
            pendingPick[steam] = new PendingPick
            {
                Plots = new List<int>(ids),
                ExpiresUtc = DateTime.UtcNow + PickTtl
            };
            var sb = new StringBuilder();
            sb.Append("ClanShare throne — pick a plot to hunt from this chair (vanilla map keeps fog).\n");
            for (var i = 0; i < ids.Count; i++)
            {
                var plot = ids[i];
                var names = AliveNames(plot);
                var mark = "";
                if (plot == standing && usingDefault)
                    mark = " <color=yellow>(here, default)</color>";
                else if (plot == standing)
                    mark = " <color=yellow>(here)</color>";
                if (plot == managing && !usingDefault)
                    mark += " <color=green>(managing)</color>";
                sb.Append(i + 1).Append(") ")
                    .Append(Core.TerritoryService.FormatPlotLabel(plot))
                    .Append(mark).Append("  ")
                    .Append(names.Count == 0 ? "no servants" : string.Join(", ", names))
                    .Append('\n');
            }
            sb.Append(".s 2  then  .s hunt 1 2  then click a discovered zone");
            var text = sb.ToString();
            return text.Length <= Core.MaxChatReply ? text : text.Substring(0, Core.MaxChatReply);
        }

        public static bool TryPickNumber(Entity character, ulong steam, int number, out string reply)
        {
            reply = null;
            if (!pendingPick.TryGetValue(steam, out var pending) || pending.Plots == null)
                return false;
            if (DateTime.UtcNow > pending.ExpiresUtc)
            {
                pendingPick.Remove(steam);
                return false;
            }
            if (number < 1 || number > pending.Plots.Count)
                return false;
            reply = ApplyPlot(character, steam, pending.Plots[number - 1]);
            return true;
        }

        public static string ChatSelect(Entity character, ulong steam, string arg)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
                return ChatList(character, steam);
            if (arg.Equals("here", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("local", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                selectedPlot.Remove(steam);
                pendingPick.Remove(steam);
                pendingHunt.Remove(steam);
                pendingHuntList.Remove(steam);
                if (returnPos.TryGetValue(steam, out var home) && TryTeleport(character, home, out var via))
                {
                    returnPos.Remove(steam);
                    return "Returned (" + via + "). This throne's servants again.";
                }
                returnPos.Remove(steam);
                return "Default: this castle's throne. Sit and use the vanilla hunt map.";
            }
            if (!int.TryParse(arg, out var n))
                return "Use .s throne then .s 2, then .s hunt 1 2, or .s throne here.";
            if (TryPickNumber(character, steam, n, out var fromIndex))
                return fromIndex;
            return ApplyPlot(character, steam, n);
        }

        static string ApplyPlot(Entity character, ulong steam, int plot)
        {
            var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
            if (standing < 0)
                return "Stand on a clan castle (or sit its throne) to pick a plot.";
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standing);
            var onIsland = false;
            if (ids != null)
                for (var i = 0; i < ids.Count; i++)
                    if (ids[i] == plot) { onIsland = true; break; }
            if (!onIsland)
                return "Plot " + plot + " is not on your ClanShare island (need .s cs, not .s cse).";
            if (FindThrone(plot) == Entity.Null)
                return "No throne on " + Core.TerritoryService.FormatPlotLabel(plot) + ".";
            pendingPick.Remove(steam);
            pendingHunt.Remove(steam);
            var names = AliveNames(plot);
            var who = names.Count == 0 ? "no living servants" : string.Join(", ", names);
            if (plot == standing)
            {
                selectedPlot.Remove(steam);
                return "Default: " + Core.TerritoryService.FormatPlotLabel(plot) + " (this castle). Sit this throne. " + who;
            }
            selectedPlot[steam] = plot;
            pendingHuntList[steam] = AliveRows(plot);
            return "Managing " + Core.TerritoryService.FormatPlotLabel(plot)
                + " from this chair. " + who
                + "  .s hunt 1 2 then click a discovered zone on this map (fog stays vanilla).";
        }

        public static string ChatHunt(Entity character, ulong steam, string arg)
        {
            var plot = ManagingPlot(character, steam);
            if (plot < 0)
                return "Stand on a clan castle (or sit its throne). .s throne to pick another plot.";
            var rows = AliveRows(plot);
            pendingHuntList[steam] = rows;
            if (string.IsNullOrWhiteSpace(arg)
                || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
                return FormatHuntList(plot, rows);
            if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                pendingHunt.Remove(steam);
                return "Cleared. Next map click is vanilla (this throne's servants).";
            }
            var picked = ParseHuntNumbers(arg, rows);
            if (picked.Count == 0)
                return rows.Count == 0
                    ? "No idle servants on " + Core.TerritoryService.FormatPlotLabel(plot) + "."
                    : "Use .s hunt 1 2 (max 3) from the numbered list.";
            var nids = new List<NetworkId>();
            var who = new List<string>();
            for (var i = 0; i < picked.Count; i++)
            {
                nids.Add(picked[i].Nid);
                who.Add(picked[i].Name);
            }
            pendingHunt[steam] = new PendingHunt
            {
                Plot = plot,
                Servants = nids,
                Names = who,
                ExpiresUtc = DateTime.UtcNow + PickTtl
            };
            selectedPlot[steam] = plot;
            return "Next click on this throne's map sends " + string.Join(", ", who)
                + " from " + Core.TerritoryService.FormatPlotLabel(plot)
                + ". Undiscovered zones stay fogged (vanilla map).";
        }

        internal static readonly string ClientJsonPath = Path.Combine(
            BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "debug", "thrones-client.json");

        public static void WriteClientSnapshot()
        {
            if (!Core.HasInitialized)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClientJsonPath));
                var sb = new StringBuilder();
                sb.Append("{\"plots\":[");
                var firstPlot = true;
                foreach (var p in ConnectedPlayers())
                {
                    var standing = p.plot;
                    if (standing < 0)
                        continue;
                    var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standing);
                    if (ids == null || ids.Count == 0)
                        continue;
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var plot = ids[i];
                        if (!firstPlot)
                            sb.Append(',');
                        firstPlot = false;
                        sb.Append("{\"plot\":").Append(plot)
                            .Append(",\"here\":").Append(plot == standing ? "true" : "false")
                            .Append(",\"label\":\"").Append(Esc(Core.TerritoryService.FormatPlotLabel(plot))).Append('"')
                            .Append(",\"servants\":[");
                        var rows = AliveRows(plot);
                        for (var s = 0; s < rows.Count; s++)
                        {
                            if (s > 0)
                                sb.Append(',');
                            sb.Append("{\"i\":").Append(s + 1)
                                .Append(",\"name\":\"").Append(Esc(rows[s].Name)).Append('"')
                                .Append(",\"nid\":\"").Append(Esc(rows[s].Nid.ToString())).Append("\"}");
                        }
                        sb.Append("]}");
                    }
                    break;
                }
                sb.Append("]}");
                var tmp = ClientJsonPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Copy(tmp, ClientJsonPath, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }

        public static string DebugHunt(int plot, string arg)
        {
            if (!TryFindConnected("", out var steam, out var character, out var playerName, out var error))
                return "{\"error\":\"" + Esc(error) + "\"}";
            if (plot >= 0)
            {
                selectedPlot[steam] = plot;
                pendingHuntList[steam] = AliveRows(plot);
            }
            var text = ChatHunt(character, steam, arg);
            return "{\"player\":\"" + Esc(playerName) + "\",\"text\":\"" + Esc(text)
                + "\",\"lastSend\":\"" + Esc(lastSend) + "\"}";
        }

        public static string DebugDump(int plotFilter)
        {
            RefreshThroneCache();
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plotFilter)
                .Append(",\"lastSitting\":").Append(lastSitting)
                .Append(",\"lastSelected\":").Append(lastSelected)
                .Append(",\"lastFrom\":").Append(lastFrom)
                .Append(",\"lastTo\":").Append(lastTo)
                .Append(",\"lastPatched\":\"").Append(Esc(lastPatched)).Append('"')
                .Append(",\"lastSkip\":\"").Append(Esc(lastSkip)).Append('"')
                .Append(",\"lastGoto\":\"").Append(Esc(lastGoto)).Append('"')
                .Append(",\"lastSit\":\"").Append(Esc(lastSit)).Append('"')
                .Append(",\"lastSend\":\"").Append(Esc(lastSend)).Append('"')
                .Append(",\"lastResponseCount\":").Append(lastResponseCount)
                .Append(",\"lastResponseNames\":\"").Append(Esc(lastResponseNames)).Append('"')
                .Append(",\"hint\":\"{\\\"op\\\":\\\"hunt\\\",\\\"plot\\\":86,\\\"name\\\":\\\"1 2\\\"} then click a vanilla map zone\"")
                .Append(",\"picks\":[");
            var first = true;
            foreach (var kv in selectedPlot)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"steam\":").Append(kv.Key).Append(",\"plot\":").Append(kv.Value).Append('}');
            }
            sb.Append("],\"returns\":[");
            first = true;
            foreach (var kv in returnPos)
            {
                if (!first) sb.Append(',');
                first = false;
                AppendPos(sb.Append("{\"steam\":").Append(kv.Key).Append(','), kv.Value, "pos").Append('}');
            }
            sb.Append("],\"players\":[");
            first = true;
            foreach (var p in ConnectedPlayers())
            {
                if (!first) sb.Append(',');
                first = false;
                var sitPlot = -1;
                if (TryGetInteractTarget(p.character, out var sitTarget, out _))
                    sitPlot = Core.TerritoryService.GetTerritoryId(sitTarget);
                sb.Append("{\"steam\":").Append(p.steam)
                    .Append(",\"name\":\"").Append(Esc(p.name)).Append('"')
                    .Append(",\"plot\":").Append(p.plot)
                    .Append(",\"sitPlot\":").Append(sitPlot)
                    .Append(",\"interactBuffs\":").Append(CountInteractBuffs(p.character)).Append(',');
                AppendPos(sb, p.pos, "pos").Append('}');
            }
            sb.Append("],\"thrones\":[");
            first = true;
            foreach (var kv in throneByPlot)
            {
                if (plotFilter >= 0 && kv.Key != plotFilter)
                    continue;
                if (!first) sb.Append(',');
                first = false;
                var nid = kv.Value.Has<NetworkId>() ? kv.Value.Read<NetworkId>().ToString() : "";
                sb.Append("{\"plot\":").Append(kv.Key)
                    .Append(",\"alive\":").Append(CountAlive(kv.Key))
                    .Append(",\"nid\":\"").Append(Esc(nid)).Append('"')
                    .Append(",\"useThrone\":").Append(kv.Value.Has<UseThrone>() ? "true" : "false")
                    .Append(",\"heart\":").Append(kv.Value.Has<CastleHeart>() ? "true" : "false")
                    .Append(",\"missions\":").Append(kv.Value.Has<ActiveServantMission>() ? "true" : "false")
                    .Append(",\"ability\":\"").Append(Esc(InteractAbilityName(kv.Value))).Append('"')
                    .Append(',');
                var pos = kv.Value.Has<Translation>() ? kv.Value.Read<Translation>().Value : default;
                AppendPos(sb, pos, "pos").Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Mailbox: move a connected player onto a plot's throne so the client is actually there.
        /// plot omitted = list. name=here returns. Does not set .s throne pick (vanilla sit test).
        /// </summary>
        public static string DebugGoto(int plot, string who)
        {
            RefreshThroneCache();
            var goHome = !string.IsNullOrWhiteSpace(who)
                && (who.Equals("here", StringComparison.OrdinalIgnoreCase)
                    || who.Equals("back", StringComparison.OrdinalIgnoreCase)
                    || who.Equals("return", StringComparison.OrdinalIgnoreCase));
            var filter = goHome ? "" : who;
            if (!TryFindConnected(filter, out var steam, out var character, out var playerName, out var error))
            {
                lastGoto = "no-player " + error;
                return "{\"error\":\"" + Esc(error) + "\",\"dump\":" + DebugDump(plot) + "}";
            }
            if (goHome)
            {
                if (!returnPos.TryGetValue(steam, out var home))
                {
                    lastGoto = "no-return " + playerName;
                    return "{\"error\":\"no saved return position\",\"player\":\"" + Esc(playerName) + "\"}";
                }
                return FinishGoto(character, steam, playerName, -1, "return", home, saveReturn: false, clearReturn: true);
            }
            if (plot < 0)
                return DebugDump(plot);
            var throne = FindThrone(plot);
            if (throne == Entity.Null || !Core.EntityManager.Exists(throne) || !throne.Has<Translation>())
            {
                lastGoto = "no-throne plot=" + plot;
                return "{\"error\":\"no throne on plot " + plot + "\",\"player\":\"" + Esc(playerName) + "\",\"dump\":" + DebugDump(plot) + "}";
            }
            var dest = throne.Read<Translation>().Value;
            dest.y += 1.5f;
            var nid = throne.Has<NetworkId>() ? throne.Read<NetworkId>().ToString() : "";
            var names = AliveNames(plot);
            var whoServants = names.Count == 0 ? "no living servants" : string.Join(", ", names);
            return FinishGoto(character, steam, playerName, plot, nid + " " + whoServants, dest, saveReturn: true, clearReturn: false);
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static void RewriteQuery(EntityQuery query, Action<Entity> rewrite)
        {
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var entity = rows[i];
                    if (entity == Entity.Null || !Core.EntityManager.Exists(entity))
                        continue;
                    rewrite(entity);
                }
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
            }
        }

        static void RewriteInfo(Entity entity)
        {
            infoEntities.Add(entity);
            if (!entity.Has<ServantInfoEvent.Request>())
            {
                lastSkip = "no-request";
                return;
            }
            LearnIncomingThrone(entity);
            if (!TryTargetFromEvent(entity, out var target, out var plot, out var sitting, out var skip))
            {
                lastSkip = skip;
                return;
            }
            lastSitting = sitting;
            lastSelected = plot;
            var patchedReq = PatchThroneId(entity, new ComponentType(Il2CppType.Of<ServantInfoEvent.Request>()), target, out var fromPlot);
            var patchedInteract = false;
            if (entity.Has<FromCharacter>())
            {
                var character = entity.Read<FromCharacter>().Character;
                patchedInteract = RetargetInteract(character, target);
            }
            lastFrom = fromPlot;
            lastTo = plot;
            lastPatched = "req=" + patchedReq + " interact=" + patchedInteract;
            lastSkip = "";
            if (patchedReq || patchedInteract)
                DestDebugLog.Note("throne", plot, 0, "info " + fromPlot + " -> " + plot + " " + lastPatched);
        }

        static void RewriteStart(Entity entity)
        {
            if (!entity.Has<SendOnMissionEvent>())
                return;
            try
            {
                if (!entity.Has<FromCharacter>())
                    return;
                var from = entity.Read<FromCharacter>();
                if (from.User == Entity.Null || !Core.EntityManager.Exists(from.User) || !from.User.Has<User>())
                    return;
                var steam = from.User.Read<User>().PlatformId;
                if (!pendingHunt.TryGetValue(steam, out var hunt) || hunt.Servants == null || hunt.Servants.Count == 0)
                    return;
                if (DateTime.UtcNow > hunt.ExpiresUtc)
                {
                    pendingHunt.Remove(steam);
                    return;
                }
                if (!TryTargetFromEvent(entity, out var target, out var plot, out _, out var skip))
                {
                    lastSkip = skip;
                    return;
                }
                if (hunt.Plot >= 0)
                {
                    var want = FindThrone(hunt.Plot);
                    if (want != Entity.Null)
                    {
                        target = want;
                        plot = hunt.Plot;
                    }
                }
                if (!PatchSend(entity, target, hunt.Servants, out var fromPlot))
                    return;
                lastSend = string.Join(",", hunt.Names) + " plot=" + plot;
                lastTo = plot;
                lastFrom = fromPlot;
                DestDebugLog.Note("throne", plot, 0, "send " + fromPlot + " -> " + plot + " " + lastSend);
            }
            finally
            {
                RepeatHunts.Remember(entity);
            }
        }

        static void RewriteAbort(Entity entity)
        {
            if (!entity.Has<AbortMissionEvent>())
                return;
            RepeatHunts.ForgetAbort(entity);
            if (!TryTargetFromEvent(entity, out var target, out var plot, out _, out _))
                return;
            if (!PatchThroneId(entity, new ComponentType(Il2CppType.Of<AbortMissionEvent>()), target, out var fromPlot))
                return;
            DestDebugLog.Note("throne", plot, 0, "abort " + fromPlot + " -> " + plot);
        }

        static bool TryTargetFromEvent(Entity entity, out Entity target, out int plot, out int sitting, out string skip)
        {
            target = Entity.Null;
            plot = -1;
            sitting = -1;
            skip = "ok";
            if (!entity.Has<FromCharacter>())
            {
                skip = "no-from";
                return false;
            }
            var from = entity.Read<FromCharacter>();
            var character = from.Character;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !from.User.Has<User>())
            {
                skip = "no-character";
                return false;
            }
            var steam = from.User.Read<User>().PlatformId;
            sitting = Core.TerritoryService.GetStandingTerritoryId(character);
            lastSitting = sitting;
            if (!selectedPlot.TryGetValue(steam, out plot) || plot < 0)
            {
                skip = "no-pick steam=" + steam;
                lastSelected = -1;
                return false;
            }
            lastSelected = plot;
            if (sitting < 0)
            {
                skip = "not-on-plot";
                return false;
            }
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(sitting);
            var ok = false;
            if (ids != null)
                for (var i = 0; i < ids.Count; i++)
                    if (ids[i] == plot) { ok = true; break; }
            if (!ok)
            {
                skip = "pick-not-on-island plot=" + plot;
                return false;
            }
            target = FindThrone(plot);
            if (target == Entity.Null || !Core.EntityManager.Exists(target) || !target.Has<NetworkId>())
            {
                skip = "no-throne plot=" + plot;
                return false;
            }
            skip = "";
            return true;
        }

        static unsafe bool RetargetInteract(Entity character, Entity target)
        {
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<Interactor>())
                return false;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return false;
            var ptr = new IntPtr(raw);
            var nidPtr = IntPtr.Add(ptr, 8);
            var tgtPtr = IntPtr.Add(ptr, 20);
            var oldNid = Marshal.PtrToStructure<NetworkId>(nidPtr);
            var oldTarget = Marshal.PtrToStructure<Entity>(tgtPtr);
            if (oldTarget == target)
                return false;
            interactRestore.Add((character, oldTarget, oldNid));
            Marshal.StructureToPtr(target.Read<NetworkId>(), nidPtr, false);
            Marshal.StructureToPtr(target, tgtPtr, false);
            return true;
        }

        static unsafe void RestoreInteract()
        {
            for (var i = 0; i < interactRestore.Count; i++)
            {
                var save = interactRestore[i];
                if (save.character == Entity.Null || !Core.EntityManager.Exists(save.character) || !save.character.Has<Interactor>())
                    continue;
                var type = new ComponentType(Il2CppType.Of<Interactor>());
                var raw = Core.EntityManager.GetComponentDataRawRW(save.character, type.TypeIndex);
                if (raw == null)
                    continue;
                var ptr = new IntPtr(raw);
                Marshal.StructureToPtr(save.nid, IntPtr.Add(ptr, 8), false);
                Marshal.StructureToPtr(save.target, IntPtr.Add(ptr, 20), false);
            }
            interactRestore.Clear();
        }

        static void CaptureResponse()
        {
            lastResponseCount = -1;
            lastResponseNames = "";
            for (var i = 0; i < infoEntities.Count; i++)
                ReadResponse(infoEntities[i]);
            infoEntities.Clear();
            if (lastResponseCount >= 0)
                return;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantInfoEvent.Response>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                    ReadResponse(rows[i]);
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
        }

        static void ReadResponse(Entity entity)
        {
            if (entity == Entity.Null || !Core.EntityManager.Exists(entity) || !entity.Has<ServantInfoEvent.Response>())
                return;
            var response = entity.Read<ServantInfoEvent.Response>();
            lastResponseCount = response.Result.Length;
            var sb = new StringBuilder();
            for (var e = 0; e < response.Result.Length && e < 12; e++)
            {
                if (e > 0) sb.Append(',');
                sb.Append(response.Result[e].Name.ToString());
            }
            lastResponseNames = sb.ToString();
            DestDebugLog.Note("throne", lastTo, 0, "response n=" + lastResponseCount + " " + lastResponseNames + " patched=" + lastPatched);
        }

        static void LearnIncomingThrone(Entity requestEntity)
        {
            var nid = requestEntity.Read<ServantInfoEvent.Request>().Throne;
            if (!Core.TryGetEntityFromNetworkId(nid, out var throne) || throne.Has<CastleHeart>() || throne.Has<PlayerCharacter>())
                return;
            var plot = Core.TerritoryService.GetTerritoryId(throne);
            if (plot < 0 || !throne.Has<NetworkId>())
                return;
            learnedThroneByPlot[plot] = throne;
            throneByPlot[plot] = throne;
        }

        public static void RemapGetEntries(ref Entity throneEntity)
        {
            if (lastSelected < 0 || throneEntity == Entity.Null)
                return;
            var want = FindThrone(lastSelected);
            if (want == Entity.Null || want == throneEntity)
                return;
            throneEntity = want;
            lastPatched += " getEntries=True";
        }

        static unsafe bool PatchThroneId(Entity entity, ComponentType type, Entity target, out int fromPlot)
        {
            fromPlot = -1;
            var want = target.Read<NetworkId>();
            var raw = Core.EntityManager.GetComponentDataRawRW(entity, type.TypeIndex);
            if (raw == null)
                return false;
            var ptr = new IntPtr(raw);
            var current = Marshal.PtrToStructure<NetworkId>(ptr);
            if (Core.TryGetEntityFromNetworkId(current, out var currentThrone))
            {
                fromPlot = Core.TerritoryService.GetTerritoryId(currentThrone);
                if (fromPlot == Core.TerritoryService.GetTerritoryId(target))
                    return false;
            }
            Marshal.StructureToPtr(want, ptr, false);
            var readback = Marshal.PtrToStructure<NetworkId>(ptr);
            lastPatched += " rb=" + readback + " want=" + want;
            return true;
        }

        static unsafe bool PatchSend(Entity entity, Entity throne, List<NetworkId> servants, out int fromPlot)
        {
            fromPlot = -1;
            if (throne == Entity.Null || !throne.Has<NetworkId>() || servants == null || servants.Count == 0)
                return false;
            var type = new ComponentType(Il2CppType.Of<SendOnMissionEvent>());
            var raw = Core.EntityManager.GetComponentDataRawRW(entity, type.TypeIndex);
            if (raw == null)
                return false;
            var ptr = new IntPtr(raw);
            var ev = Marshal.PtrToStructure<SendOnMissionEvent>(ptr);
            if (Core.TryGetEntityFromNetworkId(ev.Throne, out var currentThrone))
                fromPlot = Core.TerritoryService.GetTerritoryId(currentThrone);
            ev.Throne = throne.Read<NetworkId>();
            ev.Servant1 = servants[0];
            ev.Servant2 = servants.Count > 1 ? servants[1] : default;
            ev.Servant3 = servants.Count > 2 ? servants[2] : default;
            Marshal.StructureToPtr(ev, ptr, false);
            lastPatched = "send rb=" + Marshal.PtrToStructure<SendOnMissionEvent>(ptr).Throne;
            return true;
        }

        static int ManagingPlot(Entity character, ulong steam)
        {
            var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
            if (standing < 0)
                return -1;
            if (!selectedPlot.TryGetValue(steam, out var plot) || plot < 0)
                return standing;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(standing);
            if (ids == null)
                return standing;
            for (var i = 0; i < ids.Count; i++)
                if (ids[i] == plot)
                    return plot;
            return standing;
        }

        static string FormatHuntList(int plot, List<ServantRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("Hunt from ").Append(Core.TerritoryService.FormatPlotLabel(plot))
                .Append(" — pick up to 3, then click a discovered zone on this map.\n");
            if (rows.Count == 0)
                sb.Append("no idle servants");
            for (var i = 0; i < rows.Count; i++)
                sb.Append(i + 1).Append(") ").Append(rows[i].Name).Append('\n');
            if (rows.Count > 0)
                sb.Append(".s hunt 1 2");
            var text = sb.ToString();
            return text.Length <= Core.MaxChatReply ? text : text.Substring(0, Core.MaxChatReply);
        }

        static List<ServantRow> ParseHuntNumbers(string arg, List<ServantRow> rows)
        {
            var picked = new List<ServantRow>();
            if (string.IsNullOrWhiteSpace(arg) || rows == null || rows.Count == 0)
                return picked;
            var parts = arg.Replace(',', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<int>();
            for (var i = 0; i < parts.Length && picked.Count < 3; i++)
            {
                if (!int.TryParse(parts[i], out var n) || n < 1 || n > rows.Count)
                    continue;
                if (!seen.Add(n))
                    continue;
                picked.Add(rows[n - 1]);
            }
            return picked;
        }

        static IEnumerator SitSoon(Entity character, Entity throne)
        {
            yield return null;
            yield return null;
            if (character == Entity.Null || !Core.EntityManager.Exists(character)
                || throne == Entity.Null || !Core.EntityManager.Exists(throne))
            {
                lastSit = "gone";
                yield break;
            }
            TrySitThrone(character, throne, out var via);
            lastSit = via;
            lastGoto += " sit=" + via;
            DestDebugLog.Note("throne", Core.TerritoryService.GetTerritoryId(throne), 0, "sit " + via);
        }

        static string TryUnseat(Entity character)
        {
            if (character == Entity.Null || !Core.EntityManager.Exists(character))
                return "";
            var nBuff = DestroyInteractBuffs(character);
            var stopped = "";
            if (TryGetInteractTarget(character, out _, out var nid))
            {
                SpawnStopInteract(character, nid);
                ClearInteractTarget(character);
                stopped = " stop";
            }
            if (nBuff == 0 && stopped.Length == 0)
                return " standing";
            return " unseat buffs=" + nBuff + stopped;
        }

        static int DestroyInteractBuffs(Entity character)
        {
            var n = 0;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithOptions(EntityQueryOptions.IncludeDisabled)
                .AddAll(new(Il2CppType.Of<InteractBuff>(), ComponentType.AccessMode.ReadWrite));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var buff = rows[i];
                    if (buff == Entity.Null || !Core.EntityManager.Exists(buff))
                        continue;
                    var mine = false;
                    if (buff.Has<Buff>() && buff.Read<Buff>().Target == character)
                        mine = true;
                    if (buff.Has<EntityOwner>() && buff.Read<EntityOwner>().Owner == character)
                        mine = true;
                    if (!mine)
                        continue;
                    DestroyUtility.Destroy(Core.EntityManager, buff, DestroyDebugReason.TryRemoveBuff);
                    n++;
                }
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return n;
        }

        static int CountInteractBuffs(Entity character)
        {
            if (character == Entity.Null || !Core.EntityManager.Exists(character))
                return 0;
            var n = 0;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithOptions(EntityQueryOptions.IncludeDisabled)
                .AddAll(new(Il2CppType.Of<InteractBuff>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var buff = rows[i];
                    if (buff == Entity.Null || !Core.EntityManager.Exists(buff))
                        continue;
                    if (buff.Has<Buff>() && buff.Read<Buff>().Target == character)
                        n++;
                    else if (buff.Has<EntityOwner>() && buff.Read<EntityOwner>().Owner == character)
                        n++;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return n;
        }

        static void SpawnStopInteract(Entity character, NetworkId target)
        {
            if (!character.Has<PlayerCharacter>())
                return;
            var user = character.Read<PlayerCharacter>().UserEntity;
            var entity = Core.EntityManager.CreateEntity();
            entity.Add<FromCharacter>();
            entity.Add<StopInteractingWithObjectEvent>();
            entity.Write(new FromCharacter { User = user, Character = character });
            entity.Write(new StopInteractingWithObjectEvent { Target = target });
        }

        static bool TrySitThrone(Entity character, Entity throne, out string via)
        {
            via = "no-sit";
            if (character == Entity.Null || throne == Entity.Null
                || !Core.EntityManager.Exists(character) || !Core.EntityManager.Exists(throne))
                return false;
            if (!character.Has<PlayerCharacter>() || !throne.Has<NetworkId>())
                return false;
            var ability = InteractAbility(throne);
            if (ability.GuidHash == 0)
            {
                via = "no-interact-ability";
                return false;
            }
            try
            {
                var userEnt = character.Read<PlayerCharacter>().UserEntity;
                var user = userEnt.Read<User>();
                var from = new FromCharacter { User = userEnt, Character = character };
                var ev = new CastAbilityServerDebugEvent
                {
                    AbilityGroup = ability,
                    Who = throne.Read<NetworkId>()
                };
                Core.DebugEventsSystem.CastAbilityServerDebugEvent(user.Index, ref ev, ref from);
                via = "cast " + ability.LookupName();
                lastSit = via;
                return true;
            }
            catch (Exception e)
            {
                Core.LogException(e);
                via = "cast-fail";
                lastSit = via;
                return false;
            }
        }

        static PrefabGUID InteractAbility(Entity throne)
        {
            if (throne == Entity.Null || !Core.EntityManager.Exists(throne) || !throne.Has<InteractAbilityBuffer>())
                return default;
            var buf = throne.ReadBuffer<InteractAbilityBuffer>();
            if (buf.Length == 0)
                return default;
            return buf[0].Ability;
        }

        static string InteractAbilityName(Entity throne)
        {
            var ability = InteractAbility(throne);
            return ability.GuidHash == 0 ? "" : ability.LookupName();
        }

        static unsafe bool TryGetInteractTarget(Entity character, out Entity target, out NetworkId nid)
        {
            target = Entity.Null;
            nid = default;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<Interactor>())
                return false;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return false;
            var ptr = new IntPtr(raw);
            nid = Marshal.PtrToStructure<NetworkId>(IntPtr.Add(ptr, 8));
            target = Marshal.PtrToStructure<Entity>(IntPtr.Add(ptr, 20));
            return target != Entity.Null && Core.EntityManager.Exists(target);
        }

        static unsafe void ClearInteractTarget(Entity character)
        {
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<Interactor>())
                return;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return;
            var ptr = new IntPtr(raw);
            Marshal.StructureToPtr(default(NetworkId), IntPtr.Add(ptr, 8), false);
            Marshal.StructureToPtr(Entity.Null, IntPtr.Add(ptr, 20), false);
        }

        static bool TryTeleportToThrone(Entity character, Entity throne)
            => TryTeleportToThrone(character, throne, out _);

        static bool TryTeleportToThrone(Entity character, Entity throne, out string via)
        {
            via = "no-throne";
            if (character == Entity.Null || throne == Entity.Null || !Core.EntityManager.Exists(character)
                || !Core.EntityManager.Exists(throne) || !throne.Has<Translation>())
                return false;
            var pos = throne.Read<Translation>().Value;
            pos.y += 1.5f;
            return TryTeleport(character, pos, out via);
        }

        static bool TryTeleport(Entity character, float3 pos)
            => TryTeleport(character, pos, out _);

        static bool TryTeleport(Entity character, float3 pos, out string via)
        {
            via = "missing";
            if (character == Entity.Null || !Core.EntityManager.Exists(character))
                return false;
            var util = false;
            try
            {
                TeleportUtilityServer.Teleport(
                    Core.EntityManager,
                    character,
                    pos,
                    default(Il2CppSystem.Nullable_Unboxed<quaternion>));
                util = true;
                via = "TeleportUtilityServer";
            }
            catch (Exception e)
            {
                Core.LogException(e);
                via = "TeleportUtilityServer-fail";
            }
            var wrote = false;
            try
            {
                if (character.Has<Translation>())
                {
                    character.Write(new Translation { Value = pos });
                    wrote = true;
                }
                if (character.Has<LastTranslation>())
                {
                    character.Write(new LastTranslation { Value = pos });
                    wrote = true;
                }
                if (wrote)
                    via = util ? "TeleportUtilityServer+Translation" : "Translation";
            }
            catch (Exception e)
            {
                Core.LogException(e);
                if (!util)
                {
                    via += "+Translation-fail";
                    return false;
                }
            }
            return util || wrote;
        }

        static string FinishGoto(Entity character, ulong steam, string playerName, int toPlot, string extra, float3 dest, bool saveReturn, bool clearReturn)
        {
            var fromPlot = Core.TerritoryService.GetStandingTerritoryId(character);
            var sitPlot = -1;
            Entity sitTarget;
            if (TryGetInteractTarget(character, out sitTarget, out _))
                sitPlot = Core.TerritoryService.GetTerritoryId(sitTarget);
            var unseat = TryUnseat(character);
            var from = character.Has<Translation>() ? character.Read<Translation>().Value : default;
            if (saveReturn && character.Has<Translation>() && !returnPos.ContainsKey(steam))
                returnPos[steam] = from;
            var moved = TryTeleport(character, dest, out var via);
            if (clearReturn)
                returnPos.Remove(steam);
            Entity sitThrone = Entity.Null;
            if (toPlot >= 0)
                sitThrone = FindThrone(toPlot);
            if (moved && sitThrone != Entity.Null)
                Core.StartCoroutine(SitSoon(character, sitThrone));
            var afterPlot = Core.TerritoryService.GetStandingTerritoryId(character);
            var after = character.Has<Translation>() ? character.Read<Translation>().Value : default;
            lastGoto = (moved ? "ok " : "fail ") + playerName + " " + fromPlot + "->" + toPlot + " sitWas=" + sitPlot + " " + via + unseat;
            DestDebugLog.Note("throne", toPlot, 0, lastGoto + " " + extra);
            var sb = new StringBuilder();
            sb.Append("{\"moved\":").Append(moved ? "true" : "false")
                .Append(",\"player\":\"").Append(Esc(playerName)).Append('"')
                .Append(",\"steam\":").Append(steam)
                .Append(",\"fromPlot\":").Append(fromPlot)
                .Append(",\"sitWas\":").Append(sitPlot)
                .Append(",\"toPlot\":").Append(toPlot)
                .Append(",\"afterPlot\":").Append(afterPlot)
                .Append(",\"via\":\"").Append(Esc(via + unseat)).Append('"')
                .Append(",\"sitPending\":").Append(moved && sitThrone != Entity.Null ? "true" : "false")
                .Append(",\"extra\":\"").Append(Esc(extra)).Append('"')
                .Append(",\"savedReturn\":").Append(returnPos.ContainsKey(steam) ? "true" : "false")
                .Append(',');
            AppendPos(sb, from, "from").Append(',');
            AppendPos(sb, dest, "dest").Append(',');
            AppendPos(sb, after, "after").Append('}');
            return sb.ToString();
        }

        struct ConnectedPlayer
        {
            public ulong steam;
            public string name;
            public int plot;
            public float3 pos;
            public Entity character;
        }

        static bool TryFindConnected(string who, out ulong steam, out Entity character, out string playerName, out string error)
        {
            steam = 0;
            character = Entity.Null;
            playerName = "";
            error = "no connected player";
            ulong wantSteam = 0;
            var matchSteam = !string.IsNullOrWhiteSpace(who) && ulong.TryParse(who, out wantSteam);
            ConnectedPlayer first = default;
            var any = false;
            foreach (var p in ConnectedPlayers())
            {
                if (!any)
                {
                    first = p;
                    any = true;
                }
                if (matchSteam && p.steam == wantSteam)
                {
                    steam = p.steam;
                    character = p.character;
                    playerName = p.name;
                    error = "";
                    return true;
                }
                if (!matchSteam && !string.IsNullOrWhiteSpace(who)
                    && p.name.IndexOf(who, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    steam = p.steam;
                    character = p.character;
                    playerName = p.name;
                    error = "";
                    return true;
                }
            }
            if (any && string.IsNullOrWhiteSpace(who))
            {
                steam = first.steam;
                character = first.character;
                playerName = first.name;
                error = "";
                return true;
            }
            if (any && !string.IsNullOrWhiteSpace(who))
                error = "no connected player matching " + who;
            return false;
        }

        static List<ConnectedPlayer> ConnectedPlayers()
        {
            var list = new List<ConnectedPlayer>();
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            NativeArray<Entity> users = default;
            try
            {
                users = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < users.Length; i++)
                {
                    var userEntity = users[i];
                    if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                        continue;
                    var user = userEntity.Read<User>();
                    if (!user.IsConnected)
                        continue;
                    var character = user.LocalCharacter.GetEntityOnServer();
                    var plot = -1;
                    var pos = default(float3);
                    if (character != Entity.Null && Core.EntityManager.Exists(character))
                    {
                        plot = Core.TerritoryService.GetStandingTerritoryId(character);
                        if (character.Has<Translation>())
                            pos = character.Read<Translation>().Value;
                    }
                    list.Add(new ConnectedPlayer
                    {
                        steam = user.PlatformId,
                        name = user.CharacterName.ToString(),
                        plot = plot,
                        pos = pos,
                        character = character
                    });
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();
                query.Dispose();
            }
            return list;
        }

        static StringBuilder AppendPos(StringBuilder sb, float3 pos, string prefix = null)
        {
            if (!string.IsNullOrEmpty(prefix))
                sb.Append('"').Append(prefix).Append("\":");
            sb.Append("{\"x\":").Append(F(pos.x))
                .Append(",\"y\":").Append(F(pos.y))
                .Append(",\"z\":").Append(F(pos.z)).Append('}');
            return sb;
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static Entity FindThrone(int plot)
        {
            if (learnedThroneByPlot.TryGetValue(plot, out var learned)
                && learned != Entity.Null && Core.EntityManager.Exists(learned) && learned.Has<NetworkId>()
                && !learned.Has<CastleHeart>())
                return learned;
            RefreshThroneCache();
            if (throneByPlot.TryGetValue(plot, out var throne)
                && throne != Entity.Null && Core.EntityManager.Exists(throne) && throne.Has<NetworkId>()
                && !throne.Has<CastleHeart>())
                return throne;
            return Entity.Null;
        }

        static Dictionary<int, Entity> AllThrones()
        {
            RefreshThroneCache();
            return throneByPlot;
        }

        static void RefreshThroneCache()
        {
            if (throneCacheAt != default && (DateTime.UtcNow - throneCacheAt).TotalSeconds < 2)
                return;
            throneByPlot.Clear();
            throneCacheAt = DateTime.UtcNow;
            RememberThrones(Il2CppType.Of<UseThrone>());
            RememberPrefabThrones();
        }

        static void RememberPrefabThrones()
        {
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithOptions(EntityQueryOptions.IncludeDisabled)
                .AddAll(new(Il2CppType.Of<PrefabGUID>(), ComponentType.AccessMode.ReadOnly))
                .AddAll(new(Il2CppType.Of<CastleHeartConnection>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var throne = rows[i];
                    if (throne == Entity.Null || !Core.EntityManager.Exists(throne) || !throne.Has<NetworkId>())
                        continue;
                    if (throne.Has<PlayerCharacter>() || throne.Has<CastleHeart>())
                        continue;
                    if (!throne.Has<PrefabGUID>())
                        continue;
                    var name = throne.Read<PrefabGUID>().LookupName();
                    if (name == null || name.IndexOf("Throne", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var plot = Core.TerritoryService.GetTerritoryId(throne);
                    if (plot < 0)
                        continue;
                    if (!throneByPlot.ContainsKey(plot))
                        throneByPlot[plot] = throne;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
        }

        static void RememberThrones(Il2CppSystem.Type component)
        {
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .WithOptions(EntityQueryOptions.IncludeDisabled)
                .AddAll(new(component, ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var throne = rows[i];
                    if (throne == Entity.Null || !Core.EntityManager.Exists(throne) || !throne.Has<NetworkId>())
                        continue;
                    if (throne.Has<PlayerCharacter>() || throne.Has<CastleHeart>())
                        continue;
                    var plot = Core.TerritoryService.GetTerritoryId(throne);
                    if (plot < 0)
                        continue;
                    if (!throneByPlot.ContainsKey(plot))
                        throneByPlot[plot] = throne;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
        }

        static int CountAlive(int plot) => AliveRows(plot).Count;

        static List<string> AliveNames(int plot)
        {
            var rows = AliveRows(plot);
            var names = new List<string>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
                names.Add(rows[i].Name);
            return names;
        }

        static List<ServantRow> AliveRows(int plot)
        {
            var list = new List<ServantRow>();
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
                    var station = coffin.Read<ServantCoffinstation>();
                    if (station.State != ServantCoffinState.ServantAlive)
                        continue;
                    var servant = station.ConnectedServant.GetEntityOnServer();
                    if (servant == Entity.Null || !Core.EntityManager.Exists(servant) || !servant.Has<NetworkId>())
                        continue;
                    var name = station.ServantName.ToString();
                    list.Add(new ServantRow
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "unnamed" : name,
                        Nid = servant.Read<NetworkId>(),
                        Servant = servant
                    });
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return list;
        }
    }
}
