using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared.Systems;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// ClanShare throne: chat picks a plot, then ServantInfo / hunt events are retargeted
    /// at that plot's real throne. Vanilla listing stays Burst-safe (no extra entries).
    /// </summary>
    internal static class ClanThroneServants
    {
        static readonly Dictionary<ulong, int> selectedPlot = new();
        static readonly Dictionary<ulong, PendingPick> pendingPick = new();
        static readonly Dictionary<int, Entity> throneByPlot = new();
        static DateTime throneCacheAt;
        static readonly TimeSpan PickTtl = TimeSpan.FromMinutes(2);

        struct PendingPick
        {
            public List<int> Plots;
            public DateTime ExpiresUtc;
        }

        public static void RewriteInfoRequests(ServantInfoEventSystem_Server system)
        {
            if (system == null || !Core.HasInitialized)
                return;
            RewriteQuery(system._RequestQuery, RewriteInfo);
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
            sb.Append("ClanShare throne — default is this castle. Pick a number, then sit / reopen hunt UI.\n");
            for (var i = 0; i < ids.Count; i++)
            {
                var plot = ids[i];
                var n = CountAlive(plot);
                var mark = "";
                if (plot == standing && usingDefault)
                    mark = " <color=yellow>(here, default)</color>";
                else if (plot == standing)
                    mark = " <color=yellow>(here)</color>";
                if (plot == managing && !usingDefault)
                    mark += " <color=green>(managing)</color>";
                var noThrone = FindThrone(plot) == Entity.Null ? " <color=red>(no throne)</color>" : "";
                sb.Append(i + 1).Append(") ")
                    .Append(Core.TerritoryService.FormatPlotLabel(plot))
                    .Append("  servants ").Append(n)
                    .Append(mark).Append(noThrone).Append('\n');
            }
            sb.Append(".s 2  or  .s throne 2");
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
                return "Default: this castle's throne. Sit / reopen the hunt UI.";
            }
            if (!int.TryParse(arg, out var n))
                return "Use .s throne then .s 2, or .s throne here.";
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
            if (plot == standing)
            {
                selectedPlot.Remove(steam);
                return "Default: " + Core.TerritoryService.FormatPlotLabel(plot) + " (this castle). Sit / reopen the hunt UI.";
            }
            selectedPlot[steam] = plot;
            return "Managing servants on " + Core.TerritoryService.FormatPlotLabel(plot)
                + ". Sit a clan throne and reopen the hunt UI.";
        }

        public static string DebugDump(int plotFilter)
        {
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plotFilter).Append(",\"thrones\":[");
            var first = true;
            foreach (var kv in AllThrones())
            {
                if (plotFilter >= 0 && kv.Key != plotFilter)
                    continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"plot\":").Append(kv.Key)
                    .Append(",\"alive\":").Append(CountAlive(kv.Key))
                    .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
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
            if (!entity.Has<ServantInfoEvent.Request>() || !TryTargetFromEvent(entity, out var target, out var plot))
                return;
            var req = entity.Read<ServantInfoEvent.Request>();
            if (!Retarget(ref req.Throne, target))
                return;
            entity.Write(req);
            DestDebugLog.Note("throne", plot, 0, "info -> plot " + plot);
        }

        static void RewriteStart(Entity entity)
        {
            if (!entity.Has<SendOnMissionEvent>() || !TryTargetFromEvent(entity, out var target, out var plot))
                return;
            var ev = entity.Read<SendOnMissionEvent>();
            if (!Retarget(ref ev.Throne, target))
                return;
            entity.Write(ev);
            DestDebugLog.Note("throne", plot, 0, "send -> plot " + plot);
        }

        static void RewriteAbort(Entity entity)
        {
            if (!entity.Has<AbortMissionEvent>() || !TryTargetFromEvent(entity, out var target, out var plot))
                return;
            var ev = entity.Read<AbortMissionEvent>();
            if (!Retarget(ref ev.Throne, target))
                return;
            entity.Write(ev);
            DestDebugLog.Note("throne", plot, 0, "abort -> plot " + plot);
        }

        static bool TryTargetFromEvent(Entity entity, out Entity target, out int plot)
        {
            target = Entity.Null;
            plot = -1;
            if (!entity.Has<FromCharacter>())
                return false;
            var from = entity.Read<FromCharacter>();
            var character = from.Character;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !from.User.Has<User>())
                return false;
            var steam = from.User.Read<User>().PlatformId;
            if (!selectedPlot.TryGetValue(steam, out plot) || plot < 0)
                return false;
            var sitting = Core.TerritoryService.GetStandingTerritoryId(character);
            if (sitting < 0)
                return false;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(sitting);
            var ok = false;
            if (ids != null)
                for (var i = 0; i < ids.Count; i++)
                    if (ids[i] == plot) { ok = true; break; }
            if (!ok)
                return false;
            target = FindThrone(plot);
            return target != Entity.Null && Core.EntityManager.Exists(target) && target.Has<NetworkId>();
        }

        static bool Retarget(ref NetworkId throneId, Entity target)
        {
            var want = target.Read<NetworkId>();
            if (throneId == want)
                return false;
            throneId = want;
            return true;
        }

        static Entity FindThrone(int plot)
        {
            RefreshThroneCache();
            if (throneByPlot.TryGetValue(plot, out var throne)
                && throne != Entity.Null && Core.EntityManager.Exists(throne) && throne.Has<NetworkId>())
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
            RememberThrones(Il2CppType.Of<ActiveServantMission>());
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
                    if (throne.Has<PlayerCharacter>())
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

        static int CountAlive(int plot)
        {
            var n = 0;
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
                    if (servant != Entity.Null && Core.EntityManager.Exists(servant))
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
    }
}
