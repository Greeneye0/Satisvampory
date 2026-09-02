using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared.Systems;
using ProjectM.Terrain;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Remember each hunt send. On return: chat loot or death, optionally send the same hunt again.
    /// </summary>
    internal static class RepeatHunts
    {
        struct Recipe
        {
            public int Plot;
            public NetworkId Throne;
            public NetworkId S1;
            public NetworkId S2;
            public NetworkId S3;
            public int MissionDataId;
            public MapZoneId Zone;
        }

        struct PendingReturn
        {
            public Recipe Hunt;
            public List<Entity> Servants;
            public Entity Heart;
        }

        static readonly Dictionary<int, Recipe> byPlot = new();
        static readonly List<PendingReturn> pending = new();
        static readonly Dictionary<ulong, List<int>> pendingList = new();
        static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(2);
        static readonly Dictionary<ulong, DateTime> pendingListAt = new();

        public static bool IsOn(int plot) => Core.PlayerSettings.IsRepeatHuntPlotOn(plot);

        public static void Remember(Entity eventEntity)
        {
            if (eventEntity == Entity.Null || !Core.EntityManager.Exists(eventEntity)
                || !eventEntity.Has<SendOnMissionEvent>())
                return;
            var ev = eventEntity.Read<SendOnMissionEvent>();
            if (!Core.TryGetEntityFromNetworkId(ev.Throne, out var throne) || throne == Entity.Null)
                return;
            var plot = Core.TerritoryService.GetTerritoryId(throne);
            if (plot < 0)
                return;
            byPlot[plot] = new Recipe
            {
                Plot = plot,
                Throne = ev.Throne,
                S1 = ev.Servant1,
                S2 = ev.Servant2,
                S3 = ev.Servant3,
                MissionDataId = ev.MissionDataID,
                Zone = ev.MapZoneId
            };
            DestDebugLog.Note("throne", plot, 0, "remember hunt");
        }

        public static void ForgetAbort(Entity eventEntity)
        {
            if (eventEntity == Entity.Null || !eventEntity.Has<AbortMissionEvent>())
                return;
            var ev = eventEntity.Read<AbortMissionEvent>();
            if (!Core.TryGetEntityFromNetworkId(ev.Throne, out var throne))
                return;
            var plot = Core.TerritoryService.GetTerritoryId(throne);
            if (plot >= 0)
                byPlot.Remove(plot);
        }

        public static void SnapshotFinishing(NativeList<ServantMissionUpdateSystem.MissionIdentifier> finished)
        {
            pending.Clear();
            if (!finished.IsCreated)
                return;
            for (var i = 0; i < finished.Length; i++)
            {
                var id = finished[i];
                var heart = id.MissionOwner;
                if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                    continue;
                DynamicBuffer<ActiveServantMission> buf;
                try
                {
                    buf = heart.ReadBuffer<ActiveServantMission>();
                }
                catch
                {
                    continue;
                }
                if (id.MissionIndex < 0 || id.MissionIndex >= buf.Length)
                    continue;
                var mission = buf[id.MissionIndex];
                var plot = Core.TerritoryService.GetTerritoryId(heart);
                if (plot < 0)
                    plot = Core.TerritoryService.GetTerritoryId(mission.Servant1.GetEntityOnServer());
                if (!byPlot.TryGetValue(plot, out var recipe))
                    recipe = RecipeFromMission(plot, heart, mission);
                var servants = new List<Entity>(3);
                AddServant(servants, mission.Servant1);
                AddServant(servants, mission.Servant2);
                AddServant(servants, mission.Servant3);
                pending.Add(new PendingReturn { Hunt = recipe, Servants = servants, Heart = heart });
            }
        }

        public static void AfterReturn()
        {
            if (pending.Count == 0)
                return;
            for (var i = 0; i < pending.Count; i++)
                HandleReturn(pending[i]);
            pending.Clear();
        }

        static void HandleReturn(PendingReturn row)
        {
            var alive = new List<NetworkId>();
            var aliveNames = new List<string>();
            var deadNames = new List<string>();
            var loot = new Dictionary<PrefabGUID, int>();
            for (var i = 0; i < row.Servants.Count; i++)
            {
                var servant = row.Servants[i];
                var name = ServantName(servant);
                if (servant == Entity.Null || !Core.EntityManager.Exists(servant) || IsDead(servant))
                {
                    deadNames.Add(name);
                    continue;
                }
                AddLoot(servant, loot);
                if (IsInjured(servant))
                {
                    deadNames.Add(name + " (injured)");
                    continue;
                }
                if (servant.Has<NetworkId>())
                {
                    alive.Add(servant.Read<NetworkId>());
                    aliveNames.Add(name);
                }
            }

            var sb = new StringBuilder();
            sb.Append("Hunt returned");
            if (row.Hunt.Plot >= 0)
                sb.Append(" (").Append(Core.TerritoryService.FormatPlotLabel(row.Hunt.Plot)).Append(')');
            sb.Append(": ");
            if (aliveNames.Count > 0)
                sb.Append(string.Join(", ", aliveNames));
            if (deadNames.Count > 0)
            {
                if (aliveNames.Count > 0)
                    sb.Append(". ");
                sb.Append("Died: ").Append(string.Join(", ", deadNames));
            }
            if (loot.Count > 0)
            {
                sb.Append(" — ");
                var first = true;
                foreach (var kv in loot)
                {
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append(kv.Value).Append(' ').Append(StashRouting.ItemLabel(kv.Key));
                }
            }
            else if (aliveNames.Count > 0 && deadNames.Count == 0)
                sb.Append(" — no loot");

            var repeatOn = IsOn(row.Hunt.Plot) && alive.Count > 0
                && Core.TryGetEntityFromNetworkId(row.Hunt.Throne, out _);
            if (repeatOn)
                sb.Append(". Repeat: sent again.");
            TellOwner(row.Heart, Trim(sb.ToString()));
            DestDebugLog.Note("throne", row.Hunt.Plot, 0, "return " + sb);

            if (repeatOn)
            {
                var hunt = row.Hunt;
                hunt.S1 = alive.Count > 0 ? alive[0] : default;
                hunt.S2 = alive.Count > 1 ? alive[1] : default;
                hunt.S3 = alive.Count > 2 ? alive[2] : default;
                Core.StartCoroutine(SendSoon(row.Heart, hunt));
            }
        }

        static IEnumerator SendSoon(Entity heart, Recipe hunt)
        {
            yield return new WaitForSeconds(2f);
            TrySend(heart, hunt);
        }

        static void TrySend(Entity heart, Recipe hunt)
        {
            try
            {
                if (!IsOn(hunt.Plot))
                    return;
                if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                    return;
                if (TerritoryService.IsHeartRaided(heart))
                    return;
                if (!Core.TryGetEntityFromNetworkId(hunt.Throne, out var throne)
                    || throne == Entity.Null || !Core.EntityManager.Exists(throne))
                    return;
                if (!TryFromHeart(heart, out var userEnt, out var character) || character == Entity.Null)
                    return;
                var entity = Core.EntityManager.CreateEntity();
                entity.Add<FromCharacter>();
                entity.Add<SendOnMissionEvent>();
                entity.Write(new FromCharacter { User = userEnt, Character = character });
                entity.Write(new SendOnMissionEvent
                {
                    Throne = hunt.Throne,
                    Servant1 = hunt.S1,
                    Servant2 = hunt.S2,
                    Servant3 = hunt.S3,
                    MissionDataID = hunt.MissionDataId,
                    MapZoneId = hunt.Zone
                });
                DestDebugLog.Note("throne", hunt.Plot, 0, "repeat send");
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
        }

        static Recipe RecipeFromMission(int plot, Entity heart, ActiveServantMission mission)
        {
            var throne = Entity.Null;
            // best-effort: plot throne is not on the mission struct
            return new Recipe
            {
                Plot = plot,
                Throne = default,
                S1 = NidOf(mission.Servant1),
                S2 = NidOf(mission.Servant2),
                S3 = NidOf(mission.Servant3),
                MissionDataId = mission.MissiontDataId,
                Zone = default
            };
        }

        static NetworkId NidOf(NetworkedEntity ne)
        {
            var e = ne.GetEntityOnServer();
            if (e == Entity.Null || !Core.EntityManager.Exists(e) || !e.Has<NetworkId>())
                return default;
            return e.Read<NetworkId>();
        }

        static void AddServant(List<Entity> list, NetworkedEntity ne)
        {
            var e = ne.GetEntityOnServer();
            if (e != Entity.Null)
                list.Add(e);
        }

        static bool IsDead(Entity servant)
        {
            var coffin = CoffinOf(servant);
            if (coffin == Entity.Null)
                return true;
            var state = coffin.Read<ServantCoffinstation>().State;
            return state == ServantCoffinState.ServantRevivable || state == ServantCoffinState.Empty;
        }

        static bool IsInjured(Entity servant)
        {
            var coffin = CoffinOf(servant);
            if (coffin == Entity.Null || !coffin.Has<ServantCoffinstation>())
                return false;
            var st = coffin.Read<ServantCoffinstation>();
            return st.Injury.GuidHash != 0;
        }

        static Entity CoffinOf(Entity servant)
        {
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                return Entity.Null;
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
                    if (!coffin.Has<ServantCoffinstation>())
                        continue;
                    var connected = coffin.Read<ServantCoffinstation>().ConnectedServant.GetEntityOnServer();
                    if (connected == servant)
                        return coffin;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return Entity.Null;
        }

        static string ServantName(Entity servant)
        {
            var coffin = CoffinOf(servant);
            if (coffin != Entity.Null && coffin.Has<ServantCoffinstation>())
            {
                var n = coffin.Read<ServantCoffinstation>().ServantName.ToString();
                if (!string.IsNullOrWhiteSpace(n))
                    return n;
            }
            return "servant";
        }

        static void AddLoot(Entity servant, Dictionary<PrefabGUID, int> loot)
        {
            if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, servant, out var inv)
                || inv == Entity.Null)
                return;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                return;
            for (var i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemType.GuidHash == 0 || buf[i].Amount <= 0)
                    continue;
                loot.TryGetValue(buf[i].ItemType, out var n);
                loot[buf[i].ItemType] = n + buf[i].Amount;
            }
        }

        static bool TryFromHeart(Entity heart, out Entity userEnt, out Entity character)
        {
            userEnt = Entity.Null;
            character = Entity.Null;
            if (heart == Entity.Null || !heart.Has<UserOwner>())
                return false;
            userEnt = heart.Read<UserOwner>().Owner.GetEntityOnServer();
            if (userEnt == Entity.Null || !Core.EntityManager.Exists(userEnt) || !userEnt.Has<User>())
                return false;
            character = userEnt.Read<User>().LocalCharacter.GetEntityOnServer();
            return character != Entity.Null && Core.EntityManager.Exists(character);
        }

        static void TellOwner(Entity heart, string text)
        {
            if (!TryFromHeart(heart, out var userEnt, out _) || !userEnt.Has<User>())
                return;
            var user = userEnt.Read<User>();
            if (!user.IsConnected)
                return;
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, text);
        }

        static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return s.Length <= Core.MaxChatReply ? s : s.Substring(0, Core.MaxChatReply);
        }

        public static string ChatRh(Entity character, User user, string arg)
        {
            if (!Core.PlayerSettings.IsRepeatHuntEnabled())
                return "Repeat hunts are <color=red>OFF</color> for the server. An admin must .sg rh first.";
            var ids = ListPlots(character, user);
            if (ids.Count == 0)
                return "Stand on a clan castle (or have ClanShare on) to see plots.";
            var steam = user.PlatformId;
            if (string.IsNullOrWhiteSpace(arg) || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
                return FormatList(character, steam, ids);
            if (arg.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < ids.Count; i++)
                    Core.PlayerSettings.SetRepeatHuntPlot(ids[i], true);
                return "Repeat hunts <color=green>ON</color> for all listed castles.\n" + FormatList(character, steam, ids);
            }
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
                if (standing < 0)
                    return "Stand on a castle to turn it " + arg + ".";
                var wantOn = arg.Equals("on", StringComparison.OrdinalIgnoreCase);
                Core.PlayerSettings.SetRepeatHuntPlot(standing, wantOn);
                return Core.TerritoryService.FormatPlotLabel(standing) + " repeat hunts "
                    + (wantOn ? "<color=green>ON</color>" : "<color=red>OFF</color>") + ".\n"
                    + FormatList(character, steam, ids);
            }
            if (int.TryParse(arg, out var n))
            {
                List<int> listed = null;
                if (pendingList.TryGetValue(steam, out var prev) && pendingListAt.TryGetValue(steam, out var at)
                    && DateTime.UtcNow <= at && prev != null && n >= 1 && n <= prev.Count)
                    listed = prev;
                else if (n >= 1 && n <= ids.Count)
                    listed = ids;
                if (listed == null)
                    return "Use .s rh then .s rh 2, or .s rh off / .s rh all.";
                var plot = listed[n - 1];
                var next = !Core.PlayerSettings.IsRepeatHuntPlotOn(plot);
                Core.PlayerSettings.SetRepeatHuntPlot(plot, next);
                return Core.TerritoryService.FormatPlotLabel(plot) + " repeat hunts "
                    + (next ? "<color=green>ON</color>" : "<color=red>OFF</color>") + ".\n"
                    + FormatList(character, steam, ids);
            }
            return "Use .s rh  |  .s rh all  |  .s rh off  |  .s rh 2";
        }

        static List<int> ListPlots(Entity character, User user)
        {
            var ids = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character);
            if (ids != null && ids.Count > 0)
                return new List<int>(ids);
            var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
            if (standing >= 0)
                return new List<int> { standing };
            return new List<int>();
        }

        static string FormatList(Entity character, ulong steam, List<int> ids)
        {
            pendingList[steam] = new List<int>(ids);
            pendingListAt[steam] = DateTime.UtcNow + ListTtl;
            var standing = character != Entity.Null ? Core.TerritoryService.GetStandingTerritoryId(character) : -1;
            var sb = new StringBuilder();
            sb.Append("Repeat hunts (server <color=green>ON</color>)\n");
            for (var i = 0; i < ids.Count; i++)
            {
                var plot = ids[i];
                var on = Core.PlayerSettings.IsRepeatHuntPlotOn(plot);
                sb.Append(i + 1).Append(") ")
                    .Append(Core.TerritoryService.FormatPlotLabel(plot));
                if (plot == standing)
                    sb.Append(" <color=yellow>(here)</color>");
                sb.Append(on ? "  <color=green>ON</color>" : "  <color=red>OFF</color>")
                    .Append('\n');
            }
            sb.Append(".s rh 2  toggle   .s rh all  enable all   .s rh off  this castle");
            var text = sb.ToString();
            return text.Length <= Core.MaxChatReply ? text : text.Substring(0, Core.MaxChatReply);
        }
    }
}
