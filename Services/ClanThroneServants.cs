using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using ProjectM.Shared.Systems;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        struct PendingPick
        {
            public List<int> Plots;
            public DateTime ExpiresUtc;
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
            sb.Append("ClanShare throne — hunt panel is this seat. Chat lists each plot.\n");
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
            var names = AliveNames(plot);
            var who = names.Count == 0 ? "no living servants" : string.Join(", ", names);
            return "Plot " + Core.TerritoryService.FormatPlotLabel(plot) + ": " + who
                + ". In-game Choose a Servant stays this castle (vanilla). Overlay later.";
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
                .Append(",\"lastResponseCount\":").Append(lastResponseCount)
                .Append(",\"lastResponseNames\":\"").Append(Esc(lastResponseNames)).Append('"')
                .Append(",\"picks\":[");
            var first = true;
            foreach (var kv in selectedPlot)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"steam\":").Append(kv.Key).Append(",\"plot\":").Append(kv.Value).Append('}');
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
                    .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
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
            if (!entity.Has<SendOnMissionEvent>() || !TryTargetFromEvent(entity, out var target, out var plot, out _, out _))
                return;
            if (!PatchThroneId(entity, new ComponentType(Il2CppType.Of<SendOnMissionEvent>()), target, out var fromPlot))
                return;
            DestDebugLog.Note("throne", plot, 0, "send " + fromPlot + " -> " + plot);
        }

        static void RewriteAbort(Entity entity)
        {
            if (!entity.Has<AbortMissionEvent>() || !TryTargetFromEvent(entity, out var target, out var plot, out _, out _))
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

        static int CountAlive(int plot) => AliveNames(plot).Count;

        static List<string> AliveNames(int plot)
        {
            var names = new List<string>();
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
                    if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                        continue;
                    var name = station.ServantName.ToString();
                    names.Add(string.IsNullOrWhiteSpace(name) ? "unnamed" : name);
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return names;
        }
    }
}
