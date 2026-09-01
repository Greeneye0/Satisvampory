using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared.Systems;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// ClanShare ON: a throne sees and hunts servants from every included clan plot.
    /// Excluded plots (.s cse) stay local. Does not copy missions onto this throne.
    /// </summary>
    internal static class ClanThroneServants
    {
        public static bool TryGetSharePlots(Entity throne, out int homePlot, out IReadOnlyList<int> plots)
        {
            homePlot = -1;
            plots = Array.Empty<int>();
            if (!Core.HasInitialized)
                return false;
            if (throne == Entity.Null || !Core.EntityManager.Exists(throne) || !throne.Has<UseThroneComponent>())
                return false;
            homePlot = Core.TerritoryService.GetTerritoryId(throne);
            if (homePlot < 0)
                return false;
            if (Core.PlayerSettings.IsTerritoryClanShareExcluded(homePlot))
                return false;
            var ids = Core.TerritoryService.GetLogisticsTerritoryIds(homePlot);
            if (ids == null || ids.Count <= 1)
                return false;
            plots = ids;
            return true;
        }

        public static void AddMissingEntries(Entity throne,
            ref FixedList4096Bytes<ServantInfoEvent.Response.Entry> entries)
        {
            if (!TryGetSharePlots(throne, out var home, out var plots))
                return;
            var found = CollectAlive(plots);
            var added = 0;
            for (var i = 0; i < found.Count; i++)
            {
                if (entries.Length >= entries.Capacity)
                    break;
                var servant = found[i].servant;
                if (EntriesHave(ref entries, servant))
                    continue;
                var station = found[i].station;
                var state = StateOf(servant, ref station);
                var entry = ServantInfoEvent.Response.Entry.Create(Core.EntityManager, servant, ref station, state);
                entries.Add(ref entry);
                added++;
            }
            if (added > 0)
            {
                DestDebugLog.Note("throne", home, 0, "share entries +" + added + " total=" + entries.Length);
                Core.Log.LogInfo("ClanShare throne plot " + home + " extra servants +" + added + " total=" + entries.Length);
            }
        }

        public static void HonorMissionState(Entity servant, ref ServantInfoEvent.Response.ServantState state)
        {
            if (state == ServantInfoEvent.Response.ServantState.AwayOnHunt)
                return;
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant) || !servant.Has<ServantData>())
                return;
            if (servant.Read<ServantData>().IsOnMission)
                state = ServantInfoEvent.Response.ServantState.AwayOnHunt;
        }

        static ServantInfoEvent.Response.ServantState StateOf(Entity servant, ref ServantCoffinstation station)
        {
            var state = ServantInfoEvent.Response.ServantState.Free;
            if (station.Injury.GuidHash != 0)
                state = ServantInfoEvent.Response.ServantState.Injured;
            else if (servant.Has<ServantHasItemsInInventory>())
                state = ServantInfoEvent.Response.ServantState.HasItemInInventory;
            HonorMissionState(servant, ref state);
            return state;
        }

        public static bool StartQueryNeedsShare(ServantMissionActionSystem system)
        {
            if (system == null || !Core.HasInitialized)
                return false;
            var query = system._StartMissionEventQuery;
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var entity = rows[i];
                    if (entity == Entity.Null || !Core.EntityManager.Exists(entity) || !entity.Has<SendOnMissionEvent>())
                        continue;
                    var throneId = entity.Read<SendOnMissionEvent>().Throne;
                    if (!Core.TryGetEntityFromNetworkId(throneId, out var throne))
                        continue;
                    if (TryGetSharePlots(throne, out _, out _))
                        return true;
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
            return false;
        }

        public static string DebugDump(int plotFilter)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"plot\":").Append(plotFilter).Append(",\"thrones\":[");
            var first = true;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<UseThroneComponent>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var throne = rows[i];
                    if (throne == Entity.Null || !Core.EntityManager.Exists(throne))
                        continue;
                    var plot = Core.TerritoryService.GetTerritoryId(throne);
                    if (plotFilter >= 0 && plot != plotFilter)
                        continue;
                    var share = TryGetSharePlots(throne, out _, out var plots);
                    IReadOnlyList<int> countPlots = share ? plots : plot >= 0 ? new[] { plot } : Array.Empty<int>();
                    var visible = CollectAlive(countPlots).Count;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"plot\":").Append(plot)
                        .Append(",\"share\":").Append(share ? "true" : "false")
                        .Append(",\"plots\":").Append(countPlots.Count)
                        .Append(",\"visible\":").Append(visible)
                        .Append('}');
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            sb.Append("]}");
            return sb.ToString();
        }

        struct AliveServant
        {
            public Entity servant;
            public ServantCoffinstation station;
        }

        static List<AliveServant> CollectAlive(IReadOnlyList<int> plots)
        {
            var list = new List<AliveServant>();
            if (plots == null || plots.Count == 0)
                return list;
            var wanted = new HashSet<int>();
            for (var i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                var heart = Core.TerritoryService.GetCastleHeart(plot);
                if (heart == Entity.Null || TerritoryService.IsHeartRaided(heart))
                    continue;
                wanted.Add(plot);
            }
            if (wanted.Count == 0)
                return list;

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
                    var plot = Core.TerritoryService.GetTerritoryId(coffin);
                    if (!wanted.Contains(plot))
                        continue;
                    var station = coffin.Read<ServantCoffinstation>();
                    if (station.State != ServantCoffinState.ServantAlive)
                        continue;
                    var servant = station.ConnectedServant.GetEntityOnServer();
                    if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                        continue;
                    list.Add(new AliveServant { servant = servant, station = station });
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

        static bool EntriesHave(ref FixedList4096Bytes<ServantInfoEvent.Response.Entry> entries, Entity servant)
        {
            if (!servant.Has<NetworkId>())
                return false;
            var id = servant.Read<NetworkId>();
            for (var i = 0; i < entries.Length; i++)
                if (entries[i].NetworkId == id)
                    return true;
            return false;
        }
    }
}
