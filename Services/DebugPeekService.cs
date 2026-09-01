using Il2CppInterop.Runtime;
using Satisvampory.Commands.Converters;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Local file mailbox so an operator can inspect live ECS without chat or a bounce.
    /// Write BepInEx/config/Satisvampory/debug/req.json then read res.json.
    /// Main-thread only. No give/spawn. Local disk, not a network socket.
    /// </summary>
    internal static class DebugPeekService
    {
        const float PollSeconds = 0.25f;
        static readonly string Dir = Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "debug");
        static readonly string ReqPath = Path.Combine(Dir, "req.json");
        static readonly string ResPath = Path.Combine(Dir, "res.json");
        static string lastId = "";
        static string lastText = "";

        internal static IEnumerator Loop()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            var wait = new WaitForSeconds(PollSeconds);
            while (true)
            {
                yield return wait;
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                }
            }
        }

        static void Tick()
        {
            if (!Core.HasInitialized)
                return;
            if (!File.Exists(ReqPath))
                return;
            string text;
            try
            {
                text = File.ReadAllText(ReqPath);
            }
            catch
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
                return;

            string id = "";
            string op = "help";
            var plot = -1;
            var guid = 0;
            string name = "";
            var apply = false;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl))
                    id = idEl.GetString() ?? "";
                if (root.TryGetProperty("op", out var opEl))
                    op = (opEl.GetString() ?? "help").Trim().ToLowerInvariant();
                if (root.TryGetProperty("plot", out var plotEl) && plotEl.ValueKind == JsonValueKind.Number)
                    plot = plotEl.GetInt32();
                if (root.TryGetProperty("guid", out var guidEl) && guidEl.ValueKind == JsonValueKind.Number)
                    guid = guidEl.GetInt32();
                if (root.TryGetProperty("name", out var nameEl))
                    name = nameEl.GetString() ?? "";
                if (root.TryGetProperty("item", out var itemEl))
                    name = itemEl.GetString() ?? name;
                if (root.TryGetProperty("apply", out var applyEl)
                    && (applyEl.ValueKind == JsonValueKind.True || (applyEl.ValueKind == JsonValueKind.String && applyEl.GetString() == "true")))
                    apply = true;
            }
            catch (Exception e)
            {
                WriteRes("{\"ok\":false,\"error\":\"bad json: " + Esc(e.Message) + "\"}");
                TryDeleteReq();
                return;
            }

            // Same id used to wedge the mailbox if req.json was not deleted. Hash the body.
            if (!string.IsNullOrEmpty(text) && text == lastText && !string.IsNullOrEmpty(id) && id == lastId)
                return;
            lastId = id;
            lastText = text;

            string body;
            try
            {
                body = Dispatch(op, plot, guid, name, apply);
            }
            catch (Exception e)
            {
                body = "{\"ok\":false,\"op\":\"" + Esc(op) + "\",\"error\":\"" + Esc(e.Message) + "\"}";
            }
            WriteRes("{\"id\":\"" + Esc(id) + "\",\"ok\":true,\"op\":\"" + Esc(op) + "\",\"data\":" + body + "}");
            TryDeleteReq();
        }

        static string Dispatch(string op, int plot, int guid, string name, bool apply)
        {
            switch (op)
            {
                case "help":
                    return "{\"ops\":[\"help\",\"players\",\"plot\",\"item\",\"covering\",\"dest\",\"sim\",\"cover\",\"unstick\",\"need\"],"
                        + "\"req\":\"" + Esc(ReqPath) + "\",\"res\":\"" + Esc(ResPath) + "\","
                        + "\"hint\":\"{\\\"op\\\":\\\"sim\\\",\\\"plot\\\":125,\\\"name\\\":\\\"plank\\\"}\","
                        + "\"sim\":\"dry-run covering as if you are standing on plot. apply:true actually moves\","
                        + "\"cover\":\"force a covering tick now\",\"unstick\":\"clear sticky-fail for plot\"}";
                case "players":
                    return PeekPlayers();
                case "plot":
                    if (plot < 0)
                        plot = FirstStandingPlot();
                    return PeekPlot(plot);
                case "item":
                    return PeekItem(plot, guid, name);
                case "covering":
                    if (plot < 0)
                        plot = FirstStandingPlot();
                    return PeekCovering(plot);
                case "dest":
                case "why":
                case "sim":
                    return SimItem(plot, guid, name, apply);
                case "cover":
                case "tick":
                    ClanTreasuryLend.DebugTick();
                    return "{\"ticked\":true,\"plot\":" + (plot < 0 ? FirstStandingPlot() : plot) + "}";
                case "unstick":
                    return "{\"cleared\":" + ClanTreasuryLend.DebugUnstick(plot) + ",\"plot\":" + plot + "}";
                case "need":
                    if (plot < 0)
                        plot = FirstStandingPlot();
                    return PeekNeed(plot);
                default:
                    return "{\"error\":\"unknown op\"}";
            }
        }

        static PrefabGUID ResolveItem(int guid, string name)
        {
            if (guid != 0)
                return new PrefabGUID(guid);
            if (!string.IsNullOrWhiteSpace(name)
                && FoundItemConverter.TryResolve(name, out var found, out _) == ItemResolveStatus.Unique)
                return found.prefab;
            return default;
        }

        static string SimItem(int plot, int guid, string name, bool apply)
        {
            if (plot < 0)
                plot = FirstStandingPlot();
            var type = ResolveItem(guid, name);
            if (type.GuidHash == 0)
                type = new PrefabGUID(ClanTreasuryLend.PlankHash);
            return ClanTreasuryLend.DebugSimulate(plot, type, apply);
        }

        static string PeekNeed(int plot)
        {
            if (plot < 0)
                return "{\"error\":\"no plot\"}";
            if (Core.ConveyorService == null)
                return "{\"error\":\"not ready\"}";
            var lines = Core.ConveyorService.DescribeConveyorNeed(plot);
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plot).Append(",\"lines\":[");
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Esc(lines[i])).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        internal static string PeekNow(int plot)
        {
            if (plot < 0)
                plot = FirstStandingPlot();
            var body = PeekPlot(plot);
            WriteRes("{\"id\":\"peek\",\"ok\":true,\"op\":\"plot\",\"data\":" + body + "}");
            return body;
        }

        static int FirstStandingPlot()
        {
            foreach (var row in Connected())
            {
                if (row.plot >= 0)
                    return row.plot;
            }
            return -1;
        }

        struct PlayerRow
        {
            public ulong steam;
            public string name;
            public int plot;
            public bool admin;
            public bool connected;
        }

        static List<PlayerRow> Connected()
        {
            var list = new List<PlayerRow>();
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
                    if (character != Entity.Null && Core.EntityManager.Exists(character))
                        plot = Core.TerritoryService.GetStandingTerritoryId(character);
                    list.Add(new PlayerRow
                    {
                        steam = user.PlatformId,
                        name = user.CharacterName.ToString(),
                        plot = plot,
                        admin = user.IsAdmin,
                        connected = true
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

        static string PeekPlayers()
        {
            var sb = new StringBuilder();
            sb.Append("{\"players\":[");
            var first = true;
            foreach (var p in Connected())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"steam\":\"").Append(p.steam).Append("\",\"name\":\"").Append(Esc(p.name))
                    .Append("\",\"plot\":").Append(p.plot).Append(",\"admin\":").Append(p.admin ? "true" : "false").Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string PeekPlot(int plot)
        {
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plot);
            if (plot < 0)
            {
                sb.Append(",\"error\":\"no plot\"}");
                return sb.ToString();
            }

            var heart = Core.TerritoryService.GetCastleHeart(plot);
            var heartLevel = -1;
            var heartState = "";
            var heartEvent = "";
            if (heart != Entity.Null && Core.EntityManager.Exists(heart) && heart.Has<CastleHeart>())
            {
                var ch = heart.Read<CastleHeart>();
                heartLevel = ch.Level;
                heartState = ch.State.ToString();
                heartEvent = ch.ActiveEvent.ToString();
            }
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var ownerId);
            var treas = 0;
            var chests = new List<string>();
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                    continue;
                var name = StashRouting.RawName(stash);
                var linked = ClanTreasuryShare.IsTreasuryLinked(stash);
                if (linked)
                    treas++;
                var match = stash.Has<CastleWorkstation>() ? stash.Read<CastleWorkstation>().MatchingFloorType.ToString() : "no-ws";
                var net = stash.Has<NetworkId>() ? stash.Read<NetworkId>().ToString() : "";
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                {
                    chests.Add("{\"name\":\"" + Esc(name) + "\",\"net\":\"" + Esc(net) + "\",\"treasuryLinked\":" + (linked ? "true" : "false")
                        + ",\"matchingFloor\":\"" + Esc(match) + "\",\"error\":\"no-inv\"}");
                    continue;
                }
                var items = new StringBuilder();
                var nStacks = 0;
                var firstItem = true;
                if (sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                {
                    for (var i = 0; i < buf.Length; i++)
                    {
                        if (buf[i].ItemType.GuidHash == 0 || buf[i].Amount <= 0)
                            continue;
                        nStacks++;
                        if (!firstItem) items.Append(',');
                        firstItem = false;
                        items.Append("{\"guid\":").Append(buf[i].ItemType.GuidHash)
                            .Append(",\"name\":\"").Append(Esc(StashRouting.ItemLabel(buf[i].ItemType)))
                            .Append("\",\"n\":").Append(buf[i].Amount).Append('}');
                    }
                }
                chests.Add("{\"name\":\"" + Esc(name) + "\",\"net\":\"" + Esc(net)
                    + "\",\"treasuryLinked\":" + (linked ? "true" : "false")
                    + ",\"matchingFloor\":\"" + Esc(match)
                    + "\",\"unnamed\":" + (StashRouting.IsUnnamedOrGeneric(name) ? "true" : "false")
                    + ",\"overflow\":" + (StashRouting.IsOverflowDestName(name) ? "true" : "false")
                    + ",\"conveyor\":" + (StashRouting.IsConveyorName(name) ? "true" : "false")
                    + ",\"stacks\":" + nStacks + ",\"items\":[" + items + "]}");
            }

            var destMode = treas > 0 ? "treasury" : "allShared";
            sb.Append(",\"owner\":\"").Append(ownerId)
                .Append("\",\"heartLevel\":").Append(heartLevel)
                .Append(",\"heartState\":\"").Append(Esc(heartState))
                .Append("\",\"heartEvent\":\"").Append(Esc(heartEvent))
                .Append("\",\"destMode\":\"").Append(destMode)
                .Append("\",\"treasuryChests\":").Append(treas)
                .Append(",\"holdKitOverflow\":").Append(ClanTreasuryLend.HoldKitOverflow(plot) ? "true" : "false")
                .Append(",\"chests\":[");
            for (var i = 0; i < chests.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(chests[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string PeekCovering(int plot)
        {
            if (plot < 0)
                plot = FirstStandingPlot();
            var one = ClanTreasuryLend.DebugCovering1x(plot);
            one.TryGetValue(ClanTreasuryLend.PlankHash, out var plank);
            one.TryGetValue(ClanTreasuryLend.CopperIngotHash, out var copper);
            one.TryGetValue(ClanTreasuryLend.GreaterBloodEssenceHash, out var gbe);
            one.TryGetValue(ClanTreasuryLend.StoneBrickHash, out var brick);
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(plot)
                .Append(",\"mats\":").Append(one.Count)
                .Append(",\"plank1x\":").Append(plank)
                .Append(",\"copper1x\":").Append(copper)
                .Append(",\"gbe1x\":").Append(gbe)
                .Append(",\"brick1x\":").Append(brick)
                .Append('}');
            return sb.ToString();
        }

        static string PeekItem(int plot, int guid, string name)
        {
            PrefabGUID type = default;
            if (guid != 0)
                type = new PrefabGUID(guid);
            else if (!string.IsNullOrWhiteSpace(name)
                     && FoundItemConverter.TryResolve(name, out var found, out _) == ItemResolveStatus.Unique)
                type = found.prefab;
            if (type.GuidHash == 0)
                return "{\"error\":\"item not found\"}";

            if (plot < 0)
                plot = FirstStandingPlot();
            var sb = new StringBuilder();
            sb.Append("{\"guid\":").Append(type.GuidHash)
                .Append(",\"name\":\"").Append(Esc(StashRouting.ItemLabel(type)))
                .Append("\",\"plot\":").Append(plot).Append(",\"chests\":[");
            var first = true;
            var total = 0;
            if (plot >= 0)
            {
                var sgm = Core.ServerGameManager;
                foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
                {
                    if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                        continue;
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                        continue;
                    var n = sgm.GetInventoryItemCount(inv, type);
                    if (n <= 0)
                        continue;
                    total += n;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":\"").Append(Esc(StashRouting.RawName(stash)))
                        .Append("\",\"n\":").Append(n)
                        .Append(",\"treasuryLinked\":").Append(ClanTreasuryShare.IsTreasuryLinked(stash) ? "true" : "false")
                        .Append('}');
                }
            }
            sb.Append("],\"total\":").Append(total).Append('}');
            return sb.ToString();
        }

        static void WriteRes(string json)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = ResPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Copy(tmp, ResPath, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Core.Log.LogWarning("[DebugPeek] write res failed: " + e.Message);
            }
        }

        static void TryDeleteReq()
        {
            try { File.Delete(ReqPath); } catch { }
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
