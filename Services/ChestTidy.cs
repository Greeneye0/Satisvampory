using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Player-triggered chest restack: every dest-eligible chest is ranked with the
    /// same dest rules as .stash, then stacks move from worse chests to better ones.
    /// ClanShare ON = whole logistics island. Never drains s#/r#, NS, salvage/trash/
    /// spoils/spawner/brazier. Matching s#/r# nameplates are dests only (Ghost Crystal
    /// leaves General into Crystal Stone S1). Overflow is a source only. Castle heart
    /// and treasury-floor chests are dests, never sources. Does not honor reserve
    /// (wrong chest should empty into the named dest).
    /// </summary>
    internal static class ChestTidy
    {
        static bool running;

        public static bool TryStart(Entity character, User user, out string deny)
        {
            deny = null;
            if (running)
            {
                deny = "Chest tidy is already running.";
                return false;
            }
            if (!PlayerActionGate.TryOpen(character, "tidy chests", requireAlliedHeart: true, out _, out deny))
                return false;
            running = true;
            Core.StartCoroutine(Run(character, user));
            return true;
        }

        static IEnumerator Run(Entity character, User user)
        {
            var moved = 0;
            var types = 0;
            var chestCount = 0;
            var clanWide = false;
            try
            {
                if (character == Entity.Null || !Core.EntityManager.Exists(character))
                    yield break;
                clanWide = Core.TerritoryService.IsClanShareOn(user);
                var standing = Core.TerritoryService.GetStandingTerritoryId(character);
                IReadOnlyList<int> plots = clanWide
                    ? Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character)
                    : standing >= 0 ? new[] { standing } : Array.Empty<int>();
                if (plots == null || plots.Count == 0)
                    yield break;

                ulong ownerId = 0;
                if (standing >= 0)
                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(standing, out ownerId);
                else if (plots.Count > 0)
                    Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plots[0], out ownerId);

                var chests = new List<(Entity stash, Entity inv, int plot)>();
                for (var p = 0; p < plots.Count; p++)
                {
                    var plot = plots[p];
                    var heart = Core.TerritoryService.GetCastleHeart(plot);
                    if (heart == Entity.Null || TerritoryService.IsHeartRaided(heart))
                        continue;
                    foreach (var stash in Core.Stash.ChestsOnPlot(plot))
                    {
                        if (stash.Has<Refinementstation>())
                            continue;
                        if (stash.Equals(heart) || stash.Has<CastleHeart>())
                            continue;
                        if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                            continue;
                        var plate = StashRouting.RawName(stash);
                        if (SkipPlate(plate))
                            continue;
                        chests.Add((stash, inv, plot));
                    }
                }
                chestCount = chests.Count;
                if (chestCount < 2)
                    yield break;

                var sgm = Core.ServerGameManager;
                var items = new List<PrefabGUID>();
                var seen = new HashSet<int>();
                for (var c = 0; c < chests.Count; c++)
                {
                    if (!sgm.TryGetBuffer<InventoryBuffer>(chests[c].inv, out var buf))
                        continue;
                    for (var i = 0; i < buf.Length; i++)
                    {
                        var item = buf[i].ItemType;
                        if (item.GuidHash == 0 || buf[i].Amount <= 0)
                            continue;
                        if (!seen.Add(item.GuidHash))
                            continue;
                        items.Add(item);
                    }
                }

                if (Core.TerritoryService != null)
                    Core.TerritoryService.StartTimer();

                for (var n = 0; n < items.Count; n++)
                {
                    try
                    {
                        TidyOne(sgm, chests, items[n], ownerId, ref moved, ref types);
                    }
                    catch (Exception e)
                    {
                        Core.LogException(e);
                    }
                    if (Core.TerritoryService != null && Core.TerritoryService.ShouldUpdateYield())
                    {
                        yield return null;
                        Core.TerritoryService.StartTimer();
                    }
                }
            }
            finally
            {
                running = false;
                var scope = clanWide ? "clan island" : "this plot";
                var line = moved > 0
                    ? $"Tidied {moved} items ({types} kinds) across {chestCount} chests on the {scope}."
                    : $"No better dests. Looked at {chestCount} chests on the {scope}.";
                try
                {
                    if (user.IsConnected)
                        Utilities.SendSystemMessageToClient(Core.EntityManager, user, line);
                }
                catch { }
            }
        }

        static void TidyOne(ProjectM.Scripting.ServerGameManager sgm,
            List<(Entity stash, Entity inv, int plot)> chests, PrefabGUID item, ulong ownerId,
            ref int moved, ref int types)
        {
            var rows = new List<(Entity stash, Entity inv, int plot, StashRouting.SortRank rank)>(chests.Count);
            var anyDest = false;
            for (var c = 0; c < chests.Count; c++)
            {
                var (stash, inv, plot) = chests[c];
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv) || !Core.EntityManager.Exists(stash))
                    continue;
                var has = StashRouting.InventoryHasItem(inv, item);
                var rank = TidyRank(stash, item, ownerId, has);
                rows.Add((stash, inv, plot, rank));
                if (rank.UsableDest)
                    anyDest = true;
            }
            if (!anyDest)
                return;
            rows.Sort((a, b) => a.rank.CompareTo(b.rank));

            var typeMoved = 0;
            for (var s = 0; s < rows.Count; s++)
            {
                var src = rows[s];
                if (!src.rank.UsableSource)
                    continue;
                var have = sgm.GetInventoryItemCount(src.inv, item);
                if (have <= 0)
                    continue;

                var isEntity = SlotIsEntity(sgm, src.inv, item);
                for (var d = 0; d < rows.Count && have > 0; d++)
                {
                    var dst = rows[d];
                    if (dst.inv.Equals(src.inv))
                        continue;
                    if (!dst.rank.StrictlyBetterDestThan(src.rank))
                        continue;
                    if (ClanTreasuryLend.InEmptyHold(dst.plot, dst.inv))
                        continue;

                    var before = sgm.GetInventoryItemCount(src.inv, item);
                    int got;
                    if (isEntity)
                    {
                        var slot = 0;
                        Utilities.TransferItemEntities(src.inv, dst.inv, item, have, ref slot, out got);
                    }
                    else
                    {
                        got = Utilities.TransferItems(sgm, src.inv, dst.inv, item, have);
                    }
                    var after = sgm.GetInventoryItemCount(src.inv, item);
                    if (got > 0 && after >= before)
                    {
                        var back = sgm.GetInventoryItemCount(dst.inv, item);
                        var takeBack = back < got ? back : got;
                        if (takeBack > 0)
                            sgm.TryRemoveInventoryItem(dst.inv, item, takeBack);
                        continue;
                    }
                    if (got <= 0)
                        continue;
                    have -= got;
                    typeMoved += got;
                    moved += got;
                    StashRouting.LogDestPick(dst.rank.Label, dst.plot, item, StashRouting.RawName(dst.stash), "tidy");
                    DestDebugLog.Move("tidy", dst.plot, item, got, src.stash, dst.stash, dst.rank.Label, 0, "stays");
                }
            }
            if (typeMoved > 0)
                types++;
        }

        static bool SkipPlate(string plate)
        {
            if (StashRouting.IsNoShareName(plate))
                return true;
            if (string.IsNullOrEmpty(plate))
                return false;
            var n = plate.ToLowerInvariant();
            return n.Contains("salvage") || n.Contains("trash")
                || n.Contains("brazier") || n.Contains("spawner");
        }

        static bool IsSpoilsOrOverflow(string plate)
        {
            if (string.IsNullOrEmpty(plate))
                return false;
            var n = plate.ToLowerInvariant();
            return n.Contains("overflow") || n.Contains("spoils");
        }

        static StashRouting.SortRank TidyRank(Entity stash, PrefabGUID item, ulong ownerId, bool hasItem)
        {
            var rank = StashRouting.RankSort(stash, item, ownerId, hasItem);
            var plate = StashRouting.RawName(stash);
            if (SkipPlate(plate))
            {
                rank.UsableDest = false;
                rank.UsableSource = false;
                return rank;
            }
            if (IsSpoilsOrOverflow(plate))
            {
                rank.Class = 8;
                rank.UsableSource = true;
                rank.UsableDest = false;
                rank.Label = StashRouting.LabelOverflow;
                return rank;
            }
            if (stash.Has<CastleHeart>() || StashRouting.IsTreasury(stash))
            {
                rank.UsableSource = false;
                if (!rank.UsableDest && rank.Class >= 3)
                    rank.UsableDest = true;
                return rank;
            }
            if (StashRouting.IsConveyorName(plate))
            {
                var dep = StashRouting.RankDeposit(stash, item, ownerId, hasItem);
                rank.UsableSource = false;
                if (dep.Class <= 2)
                {
                    rank.Class = dep.Class == 2 ? 1 : 0;
                    rank.Spec = dep.Spec;
                    rank.UsableDest = true;
                    rank.Seeded = dep.Seeded;
                    rank.Label = dep.Label;
                }
                else
                    rank.UsableDest = false;
                return rank;
            }
            if (!rank.UsableDest && rank.Class >= 3)
                rank.UsableDest = true;
            return rank;
        }

        static bool SlotIsEntity(ProjectM.Scripting.ServerGameManager sgm, Entity inv, PrefabGUID item)
        {
            if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                return false;
            for (var i = 0; i < buf.Length; i++)
            {
                if (!buf[i].ItemType.Equals(item))
                    continue;
                if (!buf[i].ItemEntity.GetEntityOnServer().Equals(Entity.Null))
                    return true;
            }
            return false;
        }
    }
}
