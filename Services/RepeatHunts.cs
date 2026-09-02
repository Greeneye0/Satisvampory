using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using ProjectM.Shared.Systems;
using ProjectM.Terrain;
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
            public PrefabGUID MissionPrefab;
            public MapZoneId Zone;
            public string DestName;
            public float SuccessPct;
        }

        static float lastSuccess = -1f;
        static bool capThisTick;
        static readonly List<(Recipe Hunt, Entity User)> pendingStarts = new();
        internal const string ClientJsonPath = @"C:\VRisingServer\BepInEx\config\Satisvampory\debug\repeathunt-client.json";

        public static void NoteSuccess(float chance)
        {
            if (chance < 0f)
                return;
            if (lastSuccess < 0f || chance < lastSuccess)
                lastSuccess = chance;
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
        static int capSuccessFrames;
        static Entity autoThrone;
        static Entity autoCharacter;
        static Entity autoUser;
        static int autoFrames;
        static bool skipSendChat;
        static string lastFail = "";

        public static bool IsOn(int plot) => Core.PlayerSettings.IsRepeatHuntPlotOn(plot);

        public static bool ShouldCapSuccess => capThisTick || capSuccessFrames > 0;

        public static bool IsAutoSend(Entity throne, Entity character)
        {
            if (autoFrames <= 0 || throne == Entity.Null || character == Entity.Null)
                return false;
            return throne == autoThrone && (character == autoCharacter || character == autoUser);
        }

        public static void TickAutoSend()
        {
            if (autoFrames > 0)
                autoFrames--;
            if (autoFrames <= 0)
                RestoreInteractor();
        }

        public static void TickCapFrames()
        {
            if (capSuccessFrames > 0)
                capSuccessFrames--;
        }

        public static void CaptureActiveMissions(int plotFilter = -1)
        {
            if (!Core.HasInitialized)
                return;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ActiveServantMission>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> hearts = default;
            try
            {
                hearts = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < hearts.Length; i++)
                {
                    var heart = hearts[i];
                    if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                        continue;
                    var plot = Core.TerritoryService.GetTerritoryId(heart);
                    if (plot < 0 || (plotFilter >= 0 && plot != plotFilter))
                        continue;
                    DynamicBuffer<ActiveServantMission> buf;
                    try { buf = heart.ReadBuffer<ActiveServantMission>(); }
                    catch { continue; }
                    for (var m = 0; m < buf.Length; m++)
                    {
                        var mission = buf[m];
                        var recipe = RecipeFromMission(plot, heart, mission);
                        if (!Core.TryGetEntityFromNetworkId(recipe.Throne, out _))
                            continue;
                        byPlot[plot] = recipe;
                        DestDebugLog.Note("throne", plot, 0, "capture in-flight hunt");
                    }
                }
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
            finally
            {
                if (hearts.IsCreated)
                    hearts.Dispose();
                query.Dispose();
            }
        }

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
            var recipe = new Recipe
            {
                Plot = plot,
                Throne = ev.Throne,
                S1 = ev.Servant1,
                S2 = ev.Servant2,
                S3 = ev.Servant3,
                MissionDataId = ev.MissionDataID,
                Zone = ev.MapZoneId,
                DestName = DestLabel(ev.MapZoneId, default),
                SuccessPct = -1f
            };
            if (byPlot.TryGetValue(plot, out var old))
                MergeRecipe(ref recipe, old);
            FillDest(ref recipe);
            byPlot[plot] = recipe;
            var userEnt = Entity.Null;
            if (eventEntity.Has<FromCharacter>())
            {
                var from = eventEntity.Read<FromCharacter>();
                userEnt = from.User;
            }
            pendingStarts.Add((recipe, userEnt));
            if (IsOn(plot))
                capThisTick = true;
            DestDebugLog.Note("throne", plot, 0, "remember hunt " + recipe.DestName
                + " z=" + ZoneTag(recipe.Zone) + " slot=" + ClampSetting(recipe.MissionDataId)
                + " prefab=" + recipe.MissionPrefab.GuidHash);
        }

        public static void AfterSends()
        {
            try
            {
                for (var i = 0; i < pendingStarts.Count; i++)
                {
                    var hunt = pendingStarts[i].Hunt;
                    var userEnt = pendingStarts[i].User;
                    if (lastSuccess >= 0f)
                        hunt.SuccessPct = lastSuccess;
                    if (byPlot.TryGetValue(hunt.Plot, out var keep))
                        MergeRecipe(ref hunt, keep);
                    FillDest(ref hunt);
                    byPlot[hunt.Plot] = hunt;
                    if (!skipSendChat)
                        ChatSent(hunt, userEnt);
                    DestDebugLog.Note("throne", hunt.Plot, 0, "sent " + hunt.DestName + " "
                        + (hunt.SuccessPct >= 0f ? (int)(hunt.SuccessPct * 100f + 0.5f) + "%" : "?")
                        + " rh=" + (IsOn(hunt.Plot) ? "on" : "off")
                        + (skipSendChat ? " auto" : ""));
                }
            }
            finally
            {
                pendingStarts.Clear();
                lastSuccess = -1f;
                capThisTick = false;
                skipSendChat = false;
            }
        }

        static void ChatSent(Recipe hunt, Entity userEnt)
        {
            var names = new List<string>(3);
            AddServantName(names, hunt.S1);
            AddServantName(names, hunt.S2);
            AddServantName(names, hunt.S3);
            var rh = IsOn(hunt.Plot);
            var sb = new StringBuilder();
            sb.Append("Hunt sent");
            if (hunt.Plot >= 0)
                sb.Append(" (").Append(Core.TerritoryService.FormatPlotLabel(hunt.Plot)).Append(')');
            if (!string.IsNullOrWhiteSpace(hunt.DestName))
                sb.Append(" — ").Append(hunt.DestName);
            if (hunt.SuccessPct >= 0f)
                sb.Append(" (").Append((int)(hunt.SuccessPct * 100f + 0.5f)).Append("%)");
            sb.Append(": ");
            if (names.Count > 0)
                sb.Append(string.Join(", ", names));
            else
                sb.Append("servants");
            sb.Append(". Repeat ").Append(rh ? "ON" : "OFF");
            if (rh)
                sb.Append(" (max ").Append(Core.PlayerSettings.GetRepeatHuntMaxSuccess()).Append("%)");
            var text = Trim(sb.ToString());
            var heart = hunt.Plot >= 0 ? Core.TerritoryService.GetCastleHeart(hunt.Plot) : Entity.Null;
            TellClan(heart, text);
        }

        static void AddServantName(List<string> names, NetworkId nid)
        {
            if (!TryServant(nid, out var servant))
                return;
            names.Add(ServantName(servant));
        }

        public static void PublishClientState()
        {
            try
            {
                var on = Core.PlayerSettings.IsRepeatHuntEnabled();
                var max = Core.PlayerSettings.GetRepeatHuntMaxSuccess();
                var off = "";
                if (Core.PlayerSettings.TryWorldOffPlots(out var list) && list != null)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                            off += ",";
                        off += "\"" + list[i] + "\"";
                    }
                }
                var json = "{\"on\":" + (on ? "true" : "false")
                    + ",\"max\":" + max.ToString(CultureInfo.InvariantCulture)
                    + ",\"off\":[" + off + "]}";
                var dir = Path.GetDirectoryName(ClientJsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmp = ClientJsonPath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(ClientJsonPath))
                    File.Replace(tmp, ClientJsonPath, null);
                else
                    File.Move(tmp, ClientJsonPath);
            }
            catch (Exception e)
            {
                Core.Log.LogDebug("RepeatHunt client json: " + e.Message);
            }
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
                else
                {
                    var live = RecipeFromMission(plot, heart, mission);
                    MergeRecipe(ref recipe, live);
                    if (recipe.MissionPrefab.GuidHash == 0)
                        recipe.MissionPrefab = live.MissionPrefab;
                }
                FillDest(ref recipe);
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
            if (!string.IsNullOrWhiteSpace(row.Hunt.DestName))
                sb.Append(" — ").Append(row.Hunt.DestName);
            if (row.Hunt.SuccessPct >= 0f)
                sb.Append(" (").Append((int)(row.Hunt.SuccessPct * 100f + 0.5f)).Append("%)");
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
                sb.Append(". Repeat ON — sending again.");
            TellClan(row.Heart, Trim(sb.ToString()));
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
            var names = new List<string>(3);
            AddServantName(names, hunt.S1);
            AddServantName(names, hunt.S2);
            AddServantName(names, hunt.S3);
            var who = names.Count > 0 ? string.Join(", ", names) : "servants";
            FillDest(ref hunt);
            var dest = string.IsNullOrWhiteSpace(hunt.DestName) ? "hunt" : hunt.DestName;
            lastFail = "";
            for (var attempt = 0; attempt < 8; attempt++)
            {
                yield return new WaitForSeconds(attempt == 0 ? 3f : 1f);
                if (!IsOn(hunt.Plot))
                    yield break;
                if (!ServantsReady(ref hunt))
                {
                    lastFail = "servants not ready";
                    DestDebugLog.Note("throne", hunt.Plot, 0, "repeat wait servants not ready try=" + (attempt + 1));
                    continue;
                }
                if (!TrySend(heart, hunt, requireRepeatOn: true))
                {
                    DestDebugLog.Note("throne", hunt.Plot, 0, "repeat TrySend false try=" + (attempt + 1) + " " + lastFail);
                    continue;
                }
                yield return new WaitForSeconds(1f);
                if (AnyOnMission(hunt))
                {
                    TellClan(heart, Trim("Repeat: sent " + who + " to " + dest + "."));
                    DestDebugLog.Note("throne", hunt.Plot, 0, "repeat accepted " + dest);
                    yield break;
                }
                lastFail = "vanilla did not accept";
                DestDebugLog.Note("throne", hunt.Plot, 0, "repeat not accepted try=" + (attempt + 1)
                    + " z=" + ZoneTag(hunt.Zone) + " slot=" + ClampSetting(hunt.MissionDataId));
            }
            var why = string.IsNullOrWhiteSpace(lastFail) ? "throne or servants not ready" : lastFail;
            TellClan(heart, Trim("Repeat: could not send " + who + " to " + dest + " — " + why + "."));
            DestDebugLog.Note("throne", hunt.Plot, 0, "repeat gave up " + dest + " " + why);
        }

        static bool TrySend(Entity heart, Recipe hunt, bool requireRepeatOn = true)
        {
            try
            {
                lastFail = "";
                if (requireRepeatOn && !IsOn(hunt.Plot))
                {
                    lastFail = "repeat off";
                    return false;
                }
                if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                {
                    lastFail = "no heart";
                    return false;
                }
                if (TerritoryService.IsHeartRaided(heart))
                {
                    lastFail = "heart raided";
                    return false;
                }
                if (!Core.TryGetEntityFromNetworkId(hunt.Throne, out var throne)
                    || throne == Entity.Null || !Core.EntityManager.Exists(throne))
                {
                    lastFail = "no throne";
                    return false;
                }
                FillDest(ref hunt);
                if (!ZoneLooksSet(hunt.Zone))
                {
                    lastFail = "no hunt zone";
                    DestDebugLog.Note("throne", hunt.Plot, 0, "repeat send missing dest name=" + hunt.DestName
                        + " z=" + ZoneTag(hunt.Zone) + " m=" + hunt.MissionDataId);
                    return false;
                }
                hunt.MissionDataId = ClampSetting(hunt.MissionDataId);
                skipSendChat = true;
                if (!ForceHunt(heart, hunt))
                {
                    lastFail = "could not start hunt";
                    return false;
                }
                DestDebugLog.Note("throne", hunt.Plot, 0, "force hunt " + hunt.DestName
                    + " z=" + ZoneTag(hunt.Zone) + " slot=" + hunt.MissionDataId
                    + " prefab=" + hunt.MissionPrefab.GuidHash);
                return true;
            }
            catch (Exception e)
            {
                lastFail = "send error";
                Core.LogException(e);
                return false;
            }
        }

        static bool ForceHunt(Entity heart, Recipe hunt)
        {
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<ActiveServantMission>())
                return false;
            var n1 = NetServant(hunt.S1, out var e1);
            var n2 = NetServant(hunt.S2, out var e2);
            var n3 = NetServant(hunt.S3, out var e3);
            var n = 0;
            if (e1 != Entity.Null) n++;
            if (e2 != Entity.Null) n++;
            if (e3 != Entity.Null) n++;
            if (n == 0)
                return false;
            if (e1 != Entity.Null && OnMission(e1))
                return false;
            if (e2 != Entity.Null && OnMission(e2))
                return false;
            if (e3 != Entity.Null && OnMission(e3))
                return false;
            var prefab = hunt.MissionPrefab;
            if (prefab.GuidHash == 0)
                TryZoneByName(hunt.DestName ?? "", out _, out _, out prefab);
            if (prefab.GuidHash == 0)
                return false;
            var mission = new ActiveServantMission
            {
                MissionID = prefab,
                MissionStartTimeTicks = DateTime.UtcNow.Ticks,
                MissionLengthSeconds = SettingLength(hunt.MissionDataId),
                MissiontDataId = hunt.MissionDataId,
                Servant1 = n1,
                Servant2 = n2,
                Servant3 = n3,
                NumberOfServants = n
            };
            var buf = heart.ReadBuffer<ActiveServantMission>();
            buf.Add(mission);
            MarkOnMission(e1);
            MarkOnMission(e2);
            MarkOnMission(e3);
            TryApplyMissionBuff(heart, e1);
            TryApplyMissionBuff(heart, e2);
            TryApplyMissionBuff(heart, e3);
            return true;
        }

        static NetworkedEntity NetServant(NetworkId nid, out Entity servant)
        {
            servant = Entity.Null;
            if (!TryServant(nid, out servant))
                return NetworkedEntity.Empty;
            var coffin = CoffinOf(servant);
            if (coffin != Entity.Null && coffin.Has<ServantCoffinstation>())
                return coffin.Read<ServantCoffinstation>().ConnectedServant;
            return NetworkedEntity.ServerEntity(servant);
        }

        static void MarkOnMission(Entity servant)
        {
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant) || !servant.Has<ServantData>())
                return;
            var data = servant.Read<ServantData>();
            data.IsOnMission = true;
            servant.Write(data);
        }

        static float SettingLength(int slot)
        {
            slot = ClampSetting(slot);
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantMissionSetting>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var e = rows[i];
                    if (e == Entity.Null || !e.Has<ServantMissionSetting>())
                        continue;
                    DynamicBuffer<ServantMissionSetting> buf;
                    try { buf = e.ReadBuffer<ServantMissionSetting>(); }
                    catch { continue; }
                    if (slot < buf.Length && buf[slot].MissionLength > 0)
                        return buf[slot].MissionLength;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return 7200f;
        }

        static void TryApplyMissionBuff(Entity heart, Entity servant)
        {
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                return;
            if (!TryFromHeart(heart, out var userEnt, out var character) || userEnt == Entity.Null)
                return;
            var buff = MissionBuffPrefab();
            if (buff.GuidHash == 0)
                return;
            FindBuff.TryApply(userEnt, servant, buff, 0, true);
        }

        static PrefabGUID MissionBuffPrefab()
        {
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantMissionSettingsSingleton>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var e = rows[i];
                    if (e == Entity.Null || !e.Has<ServantMissionSettingsSingleton>())
                        continue;
                    var guid = e.Read<ServantMissionSettingsSingleton>().MissionBuff;
                    if (guid.GuidHash != 0)
                        return guid;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return default;
        }

        static bool ServantsReady(ref Recipe hunt)
        {
            var ready = new List<NetworkId>(3);
            TryReady(hunt.S1, ready);
            TryReady(hunt.S2, ready);
            TryReady(hunt.S3, ready);
            if (ready.Count == 0)
                return false;
            hunt.S1 = ready.Count > 0 ? ready[0] : default;
            hunt.S2 = ready.Count > 1 ? ready[1] : default;
            hunt.S3 = ready.Count > 2 ? ready[2] : default;
            return true;
        }

        static void TryReady(NetworkId nid, List<NetworkId> ready)
        {
            if (!TryServant(nid, out var servant))
                return;
            if (IsDead(servant) || IsInjured(servant) || OnMission(servant))
                return;
            ready.Add(CoffinNid(nid));
        }

        static bool TryServant(NetworkId nid, out Entity servant)
        {
            servant = Entity.Null;
            if (!Core.TryGetEntityFromNetworkId(nid, out var e) || e == Entity.Null)
                return false;
            if (e.Has<ServantCoffinstation>())
            {
                servant = e.Read<ServantCoffinstation>().ConnectedServant.GetEntityOnServer();
                return servant != Entity.Null && Core.EntityManager.Exists(servant);
            }
            servant = e;
            return Core.EntityManager.Exists(servant);
        }

        static bool AnyOnMission(Recipe hunt)
        {
            return OnMissionNid(hunt.S1) || OnMissionNid(hunt.S2) || OnMissionNid(hunt.S3);
        }

        static bool OnMissionNid(NetworkId nid)
        {
            return TryServant(nid, out var servant) && OnMission(servant);
        }

        static bool OnMission(Entity servant)
        {
            if (servant == Entity.Null || !Core.EntityManager.Exists(servant))
                return false;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ActiveServantMission>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> hearts = default;
            try
            {
                hearts = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < hearts.Length; i++)
                {
                    var heart = hearts[i];
                    if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                        continue;
                    DynamicBuffer<ActiveServantMission> buf;
                    try { buf = heart.ReadBuffer<ActiveServantMission>(); }
                    catch { continue; }
                    for (var m = 0; m < buf.Length; m++)
                    {
                        var mission = buf[m];
                        if (mission.Servant1.GetEntityOnServer() == servant
                            || mission.Servant2.GetEntityOnServer() == servant
                            || mission.Servant3.GetEntityOnServer() == servant)
                            return true;
                    }
                }
            }
            finally
            {
                if (hearts.IsCreated)
                    hearts.Dispose();
                query.Dispose();
            }
            return false;
        }

        public static string DebugSend(int plot, string who, string dest)
        {
            if (plot < 0)
                return "{\"error\":\"need plot\"}";
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null)
                return "{\"error\":\"no heart on plot " + plot + "\"}";
            var throne = ClanThroneServants.FindThrone(plot);
            if (throne == Entity.Null || !throne.Has<NetworkId>())
                return "{\"error\":\"no throne on plot " + plot + "\"}";
            if (!TryPickIdle(plot, who, out var nids, out var names, out var pickErr))
                return "{\"error\":\"" + EscJson(pickErr) + "\"}";
            if (!TryResolveDest(plot, dest, out var zone, out var destName, out var missionId, out var destErr))
                return "{\"error\":\"" + EscJson(destErr) + "\"}";
            var hunt = new Recipe
            {
                Plot = plot,
                Throne = throne.Read<NetworkId>(),
                S1 = nids.Count > 0 ? nids[0] : default,
                S2 = nids.Count > 1 ? nids[1] : default,
                S3 = nids.Count > 2 ? nids[2] : default,
                MissionDataId = missionId,
                Zone = zone,
                DestName = destName,
                SuccessPct = -1f
            };
            byPlot[plot] = hunt;
            if (!TrySend(heart, hunt, requireRepeatOn: false))
                return "{\"error\":\"TrySend failed\",\"who\":\"" + EscJson(string.Join(", ", names)) + "\",\"dest\":\"" + EscJson(destName) + "\"}";
            DestDebugLog.Note("throne", plot, 0, "debug send " + string.Join(",", names) + " -> " + destName);
            return "{\"queued\":true,\"plot\":" + plot
                + ",\"who\":\"" + EscJson(string.Join(", ", names))
                + "\",\"dest\":\"" + EscJson(destName)
                + "\",\"hint\":\"check servants; Repeat: sent chat if vanilla accepts\"}";
        }

        public static string DebugSetTime(int plot, string who, int seconds)
        {
            if (plot < 0)
                return "{\"error\":\"need plot\"}";
            if (seconds < 1)
                seconds = 1;
            if (seconds > 24 * 3600)
                seconds = 24 * 3600;
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null || !heart.Has<ActiveServantMission>())
                return "{\"error\":\"no missions on plot " + plot + "\"}";
            DynamicBuffer<ActiveServantMission> buf;
            try { buf = heart.ReadBuffer<ActiveServantMission>(); }
            catch { return "{\"error\":\"no mission buffer\"}"; }
            var nowTicks = DateTime.UtcNow.Ticks;
            var changed = 0;
            var names = new List<string>();
            for (var m = 0; m < buf.Length; m++)
            {
                var mission = buf[m];
                if (!MissionMatchesWho(mission, who, names))
                    continue;
                mission.MissionStartTimeTicks = nowTicks;
                mission.MissionLengthSeconds = seconds;
                buf[m] = mission;
                changed++;
            }
            if (changed == 0)
                return "{\"error\":\"no matching active hunt\",\"who\":\"" + EscJson(who) + "\"}";
            DestDebugLog.Note("throne", plot, 0, "debug hunttime " + seconds + "s " + string.Join(",", names));
            return "{\"ok\":true,\"plot\":" + plot
                + ",\"seconds\":" + seconds
                + ",\"missions\":" + changed
                + ",\"who\":\"" + EscJson(string.Join(", ", names)) + "\"}";
        }

        public static string DebugRevive(int plot, string who)
        {
            if (plot < 0)
                return "{\"error\":\"need plot\"}";
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantCoffinstation>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            var names = new List<string>();
            var changed = 0;
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
                    var n = st.ServantName.ToString();
                    if (string.IsNullOrWhiteSpace(n))
                        n = "unnamed";
                    if (!NameHit(n, who))
                        continue;
                    if (st.State != ServantCoffinState.ServantRevivable && st.State != ServantCoffinState.Reviving)
                        continue;
                    st.State = ServantCoffinState.Reviving;
                    st.ConvertionProgress = 600f;
                    st.Injury = default;
                    st.InjuryEndTimeTicks = 1;
                    coffin.Write(st);
                    names.Add(n);
                    changed++;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            if (changed == 0)
                return "{\"error\":\"no revivable coffin\",\"who\":\"" + EscJson(who) + "\"}";
            DestDebugLog.Note("throne", plot, 0, "debug revive " + string.Join(",", names));
            return "{\"ok\":true,\"plot\":" + plot
                + ",\"reviving\":" + changed
                + ",\"who\":\"" + EscJson(string.Join(", ", names)) + "\"}";
        }

        static bool NameHit(string name, string who)
        {
            if (string.IsNullOrWhiteSpace(who))
                return true;
            var parts = who.Replace(',', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (name.IndexOf(parts[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static List<ServantMissionSetting> savedMissionSettings;

        public static string DebugRhMax(int percent)
        {
            if (percent < 1)
                percent = 1;
            if (percent > 100)
                percent = 100;
            var n = Core.PlayerSettings.SetRepeatHuntMaxSuccess(percent);
            var rolled = ApplyMissionRoll(percent >= 100);
            DestDebugLog.Note("throne", -1, 0, "debug rhmax " + n + " roll=" + (rolled ? "force" : "vanilla"));
            return "{\"ok\":true,\"max\":" + n + ",\"roll\":\"" + (rolled ? "force100" : "vanilla") + "\"}";
        }

        static bool ApplyMissionRoll(bool force100)
        {
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<ServantMissionSetting>(), ComponentType.AccessMode.ReadWrite));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            var wrote = false;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var e = rows[i];
                    if (e == Entity.Null || !e.Has<ServantMissionSetting>())
                        continue;
                    DynamicBuffer<ServantMissionSetting> buf;
                    try { buf = e.ReadBuffer<ServantMissionSetting>(); }
                    catch { continue; }
                    if (buf.Length == 0)
                        continue;
                    if (force100)
                    {
                        if (savedMissionSettings == null)
                        {
                            savedMissionSettings = new List<ServantMissionSetting>(buf.Length);
                            for (var s = 0; s < buf.Length; s++)
                                savedMissionSettings.Add(buf[s]);
                        }
                        for (var s = 0; s < buf.Length; s++)
                        {
                            var row = buf[s];
                            row.SuccessRateBonus = 1f;
                            row.InjuryChance = 0f;
                            buf[s] = row;
                        }
                        wrote = true;
                    }
                    else if (savedMissionSettings != null)
                    {
                        var n = Math.Min(buf.Length, savedMissionSettings.Count);
                        for (var s = 0; s < n; s++)
                            buf[s] = savedMissionSettings[s];
                        savedMissionSettings = null;
                        wrote = true;
                    }
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return force100 && wrote;
        }

        static bool TryPickIdle(int plot, string who, out List<NetworkId> nids, out List<string> names, out string error)
        {
            nids = new List<NetworkId>(3);
            names = new List<string>(3);
            error = "";
            var rows = ClanThroneServants.DebugIdleServants(plot);
            if (rows.Count == 0)
            {
                error = "no idle servants on plot " + plot;
                return false;
            }
            if (string.IsNullOrWhiteSpace(who))
            {
                var n = Math.Min(3, rows.Count);
                for (var i = 0; i < n; i++)
                {
                    nids.Add(rows[i].Nid);
                    names.Add(rows[i].Name);
                }
                return true;
            }
            var parts = who.Replace(',', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<int>();
            for (var i = 0; i < parts.Length && nids.Count < 3; i++)
            {
                var p = parts[i];
                if (int.TryParse(p, out var idx) && idx >= 1 && idx <= rows.Count)
                {
                    if (!seen.Add(idx))
                        continue;
                    nids.Add(rows[idx - 1].Nid);
                    names.Add(rows[idx - 1].Name);
                    continue;
                }
                for (var r = 0; r < rows.Count && nids.Count < 3; r++)
                {
                    if (seen.Contains(r + 1))
                        continue;
                    if (rows[r].Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    seen.Add(r + 1);
                    nids.Add(rows[r].Nid);
                    names.Add(rows[r].Name);
                    break;
                }
            }
            if (nids.Count == 0)
            {
                error = "no match for '" + who + "'";
                return false;
            }
            return true;
        }

        static bool TryResolveDest(int plot, string dest, out MapZoneId zone, out string destName, out int missionId, out string error)
        {
            zone = default;
            destName = "";
            missionId = 0;
            error = "";
            if (!string.IsNullOrWhiteSpace(dest) && TryZoneByName(dest, out zone, out destName, out _))
            {
                if (byPlot.TryGetValue(plot, out var named) && IsSettingIndex(named.MissionDataId))
                    missionId = named.MissionDataId;
                return true;
            }
            if (byPlot.TryGetValue(plot, out var last) && (ZoneLooksSet(last.Zone) || !string.IsNullOrWhiteSpace(last.DestName)))
            {
                zone = last.Zone;
                destName = last.DestName;
                missionId = ClampSetting(last.MissionDataId);
                if (string.IsNullOrWhiteSpace(destName))
                    destName = DestLabel(zone, last.MissionPrefab);
                return true;
            }
            error = "no dest (pass dest:\\\"Fishing Lake\\\" or send once from the map)";
            return false;
        }

        static bool TryZoneByName(string want, out MapZoneId zone, out string destName, out PrefabGUID asset)
        {
            zone = default;
            destName = "";
            asset = default;
            var needle = want.Trim();
            if (needle.Length < 2)
                return false;
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<MapZoneData>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            var bestLen = -1;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var e = rows[i];
                    if (e == Entity.Null || !e.Has<MapZoneData>())
                        continue;
                    var data = e.Read<MapZoneData>();
                    var label = ZoneLabel(data);
                    if (string.IsNullOrWhiteSpace(label))
                        continue;
                    if (label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && needle.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (label.Length < bestLen)
                        continue;
                    bestLen = label.Length;
                    zone = new MapZoneId { ChunkCoordinate = data.ChunkCoordinate, ZoneId = data.ZoneIndex };
                    destName = label;
                    asset = data.ServantMissionAsset;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            return bestLen >= 0;
        }

        static bool MissionMatchesWho(ActiveServantMission mission, string who, List<string> names)
        {
            var hit = false;
            hit |= MatchServant(mission.Servant1, who, names);
            hit |= MatchServant(mission.Servant2, who, names);
            hit |= MatchServant(mission.Servant3, who, names);
            if (string.IsNullOrWhiteSpace(who))
                return hit;
            return hit;
        }

        static bool MatchServant(NetworkedEntity ne, string who, List<string> names)
        {
            var e = ne.GetEntityOnServer();
            if (e == Entity.Null)
                return false;
            var n = ServantName(e);
            if (string.IsNullOrWhiteSpace(who))
            {
                names.Add(n);
                return true;
            }
            var parts = who.Replace(',', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (n.IndexOf(parts[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    names.Add(n);
                    return true;
                }
            }
            return false;
        }

        static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static Recipe RecipeFromMission(int plot, Entity heart, ActiveServantMission mission)
        {
            var throne = ClanThroneServants.FindThrone(plot);
            var throneNid = throne != Entity.Null && throne.Has<NetworkId>() ? throne.Read<NetworkId>() : default;
            TryZoneForMission(mission.MissionID, default, out var zone, out var dest);
            var recipe = new Recipe
            {
                Plot = plot,
                Throne = throneNid,
                S1 = CoffinNid(NidOf(mission.Servant1)),
                S2 = CoffinNid(NidOf(mission.Servant2)),
                S3 = CoffinNid(NidOf(mission.Servant3)),
                MissionDataId = mission.MissiontDataId,
                MissionPrefab = mission.MissionID,
                Zone = zone,
                DestName = dest,
                SuccessPct = -1f
            };
            FillDest(ref recipe);
            return recipe;
        }

        static string DestLabel(MapZoneId zone, PrefabGUID mission)
        {
            TryZoneForMission(mission, zone, out _, out var name);
            return name;
        }

        static void MergeRecipe(ref Recipe recipe, Recipe old)
        {
            if (string.IsNullOrWhiteSpace(recipe.DestName))
                recipe.DestName = old.DestName;
            if (!IsSettingIndex(recipe.MissionDataId) && IsSettingIndex(old.MissionDataId))
                recipe.MissionDataId = old.MissionDataId;
            if (recipe.MissionPrefab.GuidHash == 0)
                recipe.MissionPrefab = old.MissionPrefab;
            if (!ZoneLooksSet(recipe.Zone) && ZoneLooksSet(old.Zone))
                recipe.Zone = old.Zone;
            if (recipe.Throne.Equals(default(NetworkId)))
                recipe.Throne = old.Throne;
        }

        static bool FillDest(ref Recipe hunt)
        {
            if (!string.IsNullOrWhiteSpace(hunt.DestName)
                && TryZoneByName(hunt.DestName, out var zone, out var name, out var asset))
            {
                hunt.Zone = zone;
                hunt.DestName = name;
                if (hunt.MissionPrefab.GuidHash == 0)
                    hunt.MissionPrefab = asset;
                hunt.MissionDataId = ClampSetting(hunt.MissionDataId);
                return true;
            }
            if (hunt.MissionPrefab.GuidHash != 0
                && TryZoneForMission(hunt.MissionPrefab, hunt.Zone, out zone, out name)
                && ZoneLooksSet(zone))
            {
                hunt.Zone = zone;
                if (!string.IsNullOrWhiteSpace(name))
                    hunt.DestName = name;
                hunt.MissionDataId = ClampSetting(hunt.MissionDataId);
                return true;
            }
            if (ZoneLooksSet(hunt.Zone))
            {
                var label = DestLabel(hunt.Zone, hunt.MissionPrefab);
                if (!string.IsNullOrWhiteSpace(label))
                    hunt.DestName = label;
                hunt.MissionDataId = ClampSetting(hunt.MissionDataId);
                return true;
            }
            return false;
        }

        static bool IsSettingIndex(int id) => id >= 0 && id <= 4;

        static int ClampSetting(int id) => IsSettingIndex(id) ? id : 0;

        static bool ZoneLooksSet(MapZoneId zone)
        {
            return zone.ZoneId != 0 || !zone.ChunkCoordinate.Equals(default(int2));
        }

        static string ZoneTag(MapZoneId zone)
        {
            return zone.ChunkCoordinate + ":" + zone.ZoneId;
        }

        static bool TryZoneForMission(PrefabGUID mission, MapZoneId want, out MapZoneId zone, out string destName)
        {
            zone = default;
            destName = "";
            var qb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<MapZoneData>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref qb);
            qb.Dispose();
            NativeArray<Entity> rows = default;
            try
            {
                rows = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < rows.Length; i++)
                {
                    var e = rows[i];
                    if (e == Entity.Null || !e.Has<MapZoneData>())
                        continue;
                    var data = e.Read<MapZoneData>();
                    var matchAsset = mission.GuidHash != 0 && data.ServantMissionAsset.GuidHash == mission.GuidHash;
                    var wantSet = want.ZoneId != 0 || !want.ChunkCoordinate.Equals(default(int2));
                    var matchZone = wantSet && data.ZoneIndex == want.ZoneId
                        && data.ChunkCoordinate.Equals(want.ChunkCoordinate);
                    if (!matchAsset && !matchZone)
                        continue;
                    zone = new MapZoneId { ChunkCoordinate = data.ChunkCoordinate, ZoneId = data.ZoneIndex };
                    destName = ZoneLabel(data);
                    return destName.Length > 0 || matchAsset;
                }
            }
            finally
            {
                if (rows.IsCreated)
                    rows.Dispose();
                query.Dispose();
            }
            if (mission.GuidHash != 0)
            {
                destName = PrettyMission(mission);
                return destName.Length > 0;
            }
            return false;
        }

        static string ZoneLabel(MapZoneData data)
        {
            try
            {
                var loc = Core.Localization.GetLocalization(data.Name);
                if (!string.IsNullOrWhiteSpace(loc) && loc.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
                    return loc;
            }
            catch { }
            if (data.ServantMissionAsset.GuidHash != 0)
                return PrettyMission(data.ServantMissionAsset);
            return "";
        }

        static string PrettyMission(PrefabGUID guid)
        {
            var pretty = Core.Localization.GetPrefabName(guid);
            if (!string.IsNullOrWhiteSpace(pretty) && pretty.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
                return pretty;
            var raw = guid.LookupName();
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            var cut = raw.IndexOf(' ');
            if (cut > 0)
                raw = raw.Substring(0, cut);
            const string prefix = "ServantMission_";
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(prefix.Length);
            return raw.Replace('_', ' ');
        }

        static NetworkId NidOf(NetworkedEntity ne)
        {
            var e = ne.GetEntityOnServer();
            if (e == Entity.Null || !Core.EntityManager.Exists(e) || !e.Has<NetworkId>())
                return default;
            return e.Read<NetworkId>();
        }

        static NetworkId CoffinNid(NetworkId nid)
        {
            if (!Core.TryGetEntityFromNetworkId(nid, out var e) || e == Entity.Null)
                return nid;
            if (e.Has<ServantCoffinstation>() && e.Has<NetworkId>())
                return e.Read<NetworkId>();
            var coffin = CoffinOf(e);
            if (coffin != Entity.Null && coffin.Has<NetworkId>())
                return coffin.Read<NetworkId>();
            return nid;
        }

        static Entity autoInteractChar;
        static Entity autoInteractOldTarget;
        static NetworkId autoInteractOldNid;
        static bool autoInteractSaved;

        static unsafe void ArmInteractor(Entity character, Entity throne)
        {
            RestoreInteractor();
            if (character == Entity.Null || throne == Entity.Null
                || !Core.EntityManager.Exists(character) || !Core.EntityManager.Exists(throne)
                || !character.Has<Interactor>() || !throne.Has<NetworkId>())
                return;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return;
            var ptr = new IntPtr(raw);
            autoInteractChar = character;
            autoInteractOldNid = Marshal.PtrToStructure<NetworkId>(IntPtr.Add(ptr, 8));
            autoInteractOldTarget = Marshal.PtrToStructure<Entity>(IntPtr.Add(ptr, 20));
            autoInteractSaved = true;
            Marshal.StructureToPtr(throne.Read<NetworkId>(), IntPtr.Add(ptr, 8), false);
            Marshal.StructureToPtr(throne, IntPtr.Add(ptr, 20), false);
        }

        static unsafe void RestoreInteractor()
        {
            if (!autoInteractSaved)
                return;
            autoInteractSaved = false;
            var character = autoInteractChar;
            autoInteractChar = Entity.Null;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<Interactor>())
                return;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return;
            var ptr = new IntPtr(raw);
            Marshal.StructureToPtr(autoInteractOldNid, IntPtr.Add(ptr, 8), false);
            Marshal.StructureToPtr(autoInteractOldTarget, IntPtr.Add(ptr, 20), false);
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

        static bool TryFromSend(Entity heart, Entity throne, out Entity userEnt, out Entity character)
        {
            userEnt = Entity.Null;
            character = Entity.Null;
            var plot = throne != Entity.Null ? Core.TerritoryService.GetTerritoryId(throne) : Core.TerritoryService.GetTerritoryId(heart);
            var best = 0;
            var pickUser = Entity.Null;
            var pickChar = Entity.Null;
            Core.TerritoryService.EachUser((uent, user) =>
            {
                if (!user.IsConnected)
                    return;
                var ch = user.LocalCharacter.GetEntityOnServer();
                if (ch == Entity.Null || !Core.EntityManager.Exists(ch))
                    return;
                if (!Core.TerritoryService.IsSameClanAsHeartOwner(user, heart)
                    && !Core.TerritoryService.IsSameClanAsHeart(ch, heart))
                    return;
                var score = 1;
                var standing = Core.TerritoryService.GetStandingTerritoryId(ch);
                if (plot >= 0 && standing == plot)
                    score = 2;
                if (InteractorIs(ch, throne))
                    score = 3;
                if (score <= best)
                    return;
                best = score;
                pickUser = uent;
                pickChar = ch;
            });
            if (best > 0)
            {
                userEnt = pickUser;
                character = pickChar;
                return true;
            }
            return TryFromHeart(heart, out userEnt, out character);
        }

        static unsafe bool InteractorIs(Entity character, Entity throne)
        {
            if (character == Entity.Null || throne == Entity.Null
                || !Core.EntityManager.Exists(character) || !character.Has<Interactor>())
                return false;
            var type = new ComponentType(Il2CppType.Of<Interactor>());
            var raw = Core.EntityManager.GetComponentDataRawRW(character, type.TypeIndex);
            if (raw == null)
                return false;
            var target = Marshal.PtrToStructure<Entity>(IntPtr.Add(new IntPtr(raw), 20));
            return target == throne;
        }

        static string SenderName(Entity userEnt)
        {
            if (userEnt == Entity.Null || !Core.EntityManager.Exists(userEnt) || !userEnt.Has<User>())
                return "?";
            var n = userEnt.Read<User>().CharacterName.ToString();
            return string.IsNullOrWhiteSpace(n) ? "?" : n;
        }

        static void TellOwner(Entity heart, string text) => TellClan(heart, text);

        static void TellClan(Entity heart, string text)
        {
            if (string.IsNullOrEmpty(text) || heart == Entity.Null)
                return;
            var sent = new HashSet<ulong>();
            void Send(User user)
            {
                if (!user.IsConnected || !sent.Add(user.PlatformId))
                    return;
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, text);
            }
            if (TryFromHeart(heart, out var ownerEnt, out _) && ownerEnt.Has<User>())
                Send(ownerEnt.Read<User>());
            Core.TerritoryService.EachUser((_, user) =>
            {
                if (Core.TerritoryService.IsSameClanAsHeartOwner(user, heart))
                    Send(user);
            });
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
            sb.Append("Repeat hunts (server <color=green>ON</color>, max ")
                .Append(Core.PlayerSettings.GetRepeatHuntMaxSuccess()).Append("% )\n");
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
