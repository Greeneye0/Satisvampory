using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// `.s why <item> [container]`: why an item does or does not sort to a container.
    /// Half 1: dest ranking of every chest on the plot / clan island for that item, with the reason
    /// behind each class (exact, group word, item category, partial name, generic, overflow, NS...).
    /// Half 2: belts. Which chests send it on which s# group, who receives, whether the link is
    /// mutual (dest can send it back) and which side wins under dest ranking, same-group convloop.
    /// </summary>
    internal static class WhyReport
    {
        const int MaxRankLines = 10;
        const int MaxBeltLines = 12;

        struct Row
        {
            public Entity Stash;
            public Entity Inv;
            public int Plot;
            public string Name;
            public int Count;
            public bool Has;
            public bool Room;
            public StashRouting.DepositRank Rank;
            public string Why;
        }

        public static List<string> Report(Entity character, PrefabGUID item, string containerFilter)
        {
            var lines = new List<string>();
            if (character == Entity.Null || !character.Has<PlayerCharacter>() || item.GuidHash == 0)
            {
                lines.Add("Unable to explain that item.");
                return lines;
            }

            var userEntity = character.Read<PlayerCharacter>().UserEntity;
            var user = userEntity.Read<User>();
            var standing = Core.TerritoryService.GetStandingTerritoryId(character);
            var csOn = Core.TerritoryService.IsClanShareOn(user);
            var plotIds = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character);
            if (plotIds == null || plotIds.Count == 0)
            {
                lines.Add(csOn
                    ? "Unable to explain — no clan castles available (ClanShare on)."
                    : "You must stand on a castle plot.");
                return lines;
            }

            ulong ownerId = 0;
            if (standing >= 0)
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(standing, out ownerId);
            else
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plotIds[0], out ownerId);

            var sgm = Core.ServerGameManager;
            var itemName = StashRouting.ItemLabel(item);
            var filter = string.IsNullOrWhiteSpace(containerFilter) ? null : containerFilter.Trim();

            // ---- gather every chest on the island ----
            var all = new List<Row>();
            foreach (var plot in plotIds)
            {
                var heart = Core.TerritoryService.GetCastleHeart(plot);
                if (heart == Entity.Null || !sgm.IsAllies(heart, character))
                    continue;
                foreach (var stash in Core.Stash.ChestsOnPlot(plot))
                {
                    if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                        continue;
                    if (stash.Has<Refinementstation>() || stash.Has<CastleHeart>())
                        continue;
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                        continue;
                    var row = new Row
                    {
                        Stash = stash,
                        Inv = inv,
                        Plot = plot,
                        Name = StashRouting.DestName(stash),
                        Count = sgm.GetInventoryItemCount(inv, item),
                    };
                    row.Has = row.Count > 0;
                    row.Room = HasRoom(inv, item);
                    row.Why = StashRouting.ExplainRank(stash, item, ownerId, row.Has, standing, out row.Rank);
                    all.Add(row);
                }
            }
            if (all.Count == 0)
            {
                lines.Add("No chests found on this plot / island.");
                return lines;
            }

            List<Row> shown = all;
            if (filter != null)
            {
                shown = new List<Row>();
                foreach (var r in all)
                {
                    if (r.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || StashRouting.RawName(r.Stash).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        shown.Add(r);
                }
                if (shown.Count == 0)
                {
                    lines.Add($"No container named <color=white>{filter}</color> on this plot / island.");
                    return lines;
                }
            }

            all.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            var winner = Entity.Null;
            var winnerName = "";
            foreach (var r in all)
            {
                if (!r.Rank.IsDepositUsable || !r.Room)
                    continue;
                winner = r.Stash;
                winnerName = r.Name;
                break;
            }

            // 1.0.121: one item, one container -> a direct yes/no answer with the reason and what beats it.
            if (filter != null)
            {
                FocusedReport(lines, all, shown, winner, winnerName, item, itemName, ownerId, standing, plotIds.Count > 1);
                return lines;
            }

            var where = standing >= 0 ? $"plot {standing} (here)" : "no plot";
            lines.Add($"<color=green>{itemName}</color> — why. {where}{(csOn ? $", island {plotIds.Count} castles" : "")}");
            lines.Add(winner == Entity.Null
                ? "<color=red>No usable dest</color> (every candidate is unusable or full)."
                : $"Stash / tidy would pick: <color=white>{winnerName}</color>");

            // ---- half 1: ranking ----
            lines.Add("<color=#88c>Dest ranking</color> (lower class wins; then spec, seeded, treasury, here):");
            var rankSet = filter != null ? Sorted(shown) : all;
            var n = 0;
            var hidden = 0;
            foreach (var r in rankSet)
            {
                var interesting = filter != null || r.Rank.IsDepositUsable || r.Has;
                if (!interesting)
                    continue;
                if (n >= MaxRankLines)
                {
                    hidden++;
                    continue;
                }
                n++;
                lines.Add(RankLine(n, r, standing, plotIds.Count > 1));
            }
            if (hidden > 0)
                lines.Add($"  … {hidden} more. Narrow with <color=white>.s why \"{itemName}\" <container></color>");

            // ---- half 2: belts ----
            var beltLines = BeltLines(all, shown, filter != null, item, ownerId, standing);
            if (beltLines.Count > 0)
            {
                lines.Add("<color=#88c>Belts</color> (s# sends, r# receives; r# chests only pull items they already hold):");
                lines.AddRange(beltLines);
            }
            else
            {
                lines.Add("<color=#88c>Belts</color>: no s# chest holds this item on the island. Stations: <color=white>.s conv</color>");
            }
            return lines;
        }

        static List<Row> Sorted(List<Row> rows)
        {
            var copy = new List<Row>(rows);
            copy.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            return copy;
        }

        static string ClassWord(StashRouting.DepositRank r)
        {
            if (r.Class < 0) return $"priority +{-r.Class}";
            switch (r.Class)
            {
                case 0: return "s# belt match";
                case 1: return "exact item name";
                case 2: return "category match";
                case 3: return "already holds the item";
                case 4: return "empty generic chest";
                case 5: return "overflow";
                case 6: return "custom name, no match, empty";
                case StashRouting.ClassRestricted: return "restricted furniture";
                case StashRouting.ClassExcluded: return "excluded by --word";
                case 99: return "skipped (NS / '')";
            }
            return "class " + r.Class;
        }

        static string LoseReason(Row r, Row w)
        {
            if (r.Rank.Class != w.Rank.Class)
                return $"{ClassWord(w.Rank)} beats {ClassWord(r.Rank)} (class {w.Rank.Class} vs {r.Rank.Class})";
            if (r.Rank.Spec != w.Rank.Spec)
                return "same class, but the winner's name is a more specific match";
            if (r.Rank.Seeded != w.Rank.Seeded)
                return "same rank, but the winner already holds the item";
            if (r.Rank.Treasury != w.Rank.Treasury)
                return "same rank, but the winner is on a treasury floor";
            if (r.Rank.Local != w.Rank.Local)
                return "same rank, but the winner is on the plot you are standing on";
            if (r.Count != w.Count)
                return "twins: the winner holds less of it right now";
            return "tie broken by scan order";
        }

        static void FocusedReport(List<string> lines, List<Row> all, List<Row> shown, Entity winner, string winnerName,
            PrefabGUID item, string itemName, ulong ownerId, int standing, bool multiPlot)
        {
            var total = all.Count;
            Row w = default;
            var haveWinner = false;
            foreach (var r in all)
            {
                if (r.Stash == winner) { w = r; haveWinner = true; break; }
            }
            var shownSorted = Sorted(shown);
            var limit = shownSorted.Count > 3 ? 3 : shownSorted.Count;
            for (var i = 0; i < limit; i++)
            {
                var r = shownSorted[i];
                var pos = all.IndexOf(r) + 1;
                var usable = r.Rank.IsDepositUsable;
                string verdict;
                if (haveWinner && r.Stash == winner)
                    verdict = $"<color=green>YES</color> — stash and tidy put <color=green>{itemName}</color> in <color=white>{r.Name}</color> (rank 1 of {total}).";
                else if (!usable)
                    verdict = $"<color=red>NEVER</color> — <color=white>{r.Name}</color> cannot take <color=green>{itemName}</color>: {r.Why}.";
                else if (!r.Room)
                    verdict = $"<color=red>NOT NOW</color> — <color=white>{r.Name}</color> is full. It ranks {pos} of {total} for <color=green>{itemName}</color>" + (haveWinner ? $"; with room it would still lose to <color=white>{w.Name}</color>." : ".");
                else if (haveWinner)
                    verdict = $"<color=red>NO</color> — <color=green>{itemName}</color> goes to <color=white>{w.Name}</color>, not <color=white>{r.Name}</color> (rank {pos} of {total}).";
                else
                    verdict = $"<color=red>NO</color> — no usable dest at all; <color=white>{r.Name}</color> ranks {pos} of {total}.";
                lines.Add(verdict);

                var flags = new List<string>();
                flags.Add(r.Has ? $"holds {r.Count}" : "holds none");
                flags.Add(r.Room ? "has room" : "full");
                if (r.Rank.Treasury) flags.Add("treasury floor");
                if (multiPlot) flags.Add(r.Plot == standing ? "this plot" : $"plot {r.Plot}");
                lines.Add($"  <color=white>{r.Name}</color>: {ClassWord(r.Rank)} (c{r.Rank.Class}) — {r.Why}. {string.Join(", ", flags)}.");

                if (haveWinner && r.Stash != winner && usable)
                {
                    lines.Add($"  <color=white>{w.Name}</color>: {ClassWord(w.Rank)} (c{w.Rank.Class}) — {w.Why}. {(w.Has ? $"holds {w.Count}" : "holds none")}.");
                    lines.Add($"  Why it loses: {LoseReason(r, w)}.");
                    if (r.Rank.Class > 1)
                        lines.Add($"  To make <color=white>{r.Name}</color> win: add a trailing <color=white>+</color> to its plate, or name it exactly <color=white>{itemName}</color> (or an alias).");
                    else
                        lines.Add($"  To make <color=white>{r.Name}</color> win: add a trailing <color=white>+</color> to its plate (more + than the winner).");
                }
            }
            if (shownSorted.Count > limit)
                lines.Add($"  … {shownSorted.Count - limit} more containers match that name; be more specific.");

            var beltLines = BeltLines(all, shown, true, item, ownerId, standing);
            if (beltLines.Count > 0)
            {
                lines.Add("Belts:");
                lines.AddRange(beltLines);
            }
        }

        static string RankLine(int n, Row r, int standing, bool multiPlot)
        {
            var usable = r.Rank.IsDepositUsable;
            var color = !usable ? "#a66" : (r.Room ? "white" : "#aa8");
            var flags = new List<string>();
            if (r.Has) flags.Add($"has {r.Count}");
            else flags.Add("empty");
            if (r.Rank.Treasury) flags.Add("treasury");
            if (multiPlot) flags.Add(r.Plot == standing ? "here" : $"plot {r.Plot}");
            if (!r.Room) flags.Add("<color=red>FULL</color>");
            if (!usable) flags.Add("<color=red>not a dest</color>");
            return $"  {n}. <color={color}>{r.Name}</color> — c{r.Rank.Class} {r.Why} [{string.Join(", ", flags)}]";
        }

        static List<string> BeltLines(List<Row> all, List<Row> shown, bool filtered, PrefabGUID item, ulong ownerId, int standing)
        {
            var lines = new List<string>();
            var byPlotSendGroups = new Dictionary<Entity, List<int>>();
            var recvGroups = new Dictionary<Entity, List<int>>();
            foreach (var r in all)
            {
                var plate = StashRouting.RawName(r.Stash);
                byPlotSendGroups[r.Stash] = StashRouting.SenderGroups(plate);
                recvGroups[r.Stash] = StashRouting.ReceiverGroups(plate);
            }
            var loops = Core.PlayerSettings.IsConveyorLoopsAllowed();

            // Senders worth showing: hold the item (or are the filtered chest).
            foreach (var src in all)
            {
                if (lines.Count >= MaxBeltLines)
                    break;
                var sends = byPlotSendGroups[src.Stash];
                var srcShown = !filtered || Contains(shown, src.Stash);
                if (sends.Count == 0)
                    continue;
                if (!src.Has && !srcShown)
                    continue;
                foreach (var g in sends)
                {
                    foreach (var dst in all)
                    {
                        if (lines.Count >= MaxBeltLines)
                            break;
                        if (dst.Stash == src.Stash || !recvGroups[dst.Stash].Contains(g))
                            continue;
                        if (filtered && !srcShown && !Contains(shown, dst.Stash))
                            continue;
                        lines.Add(BeltPair(src, dst, g, byPlotSendGroups, recvGroups, loops));
                    }
                }
            }
            if (filtered)
            {
                foreach (var r in shown)
                {
                    var s = byPlotSendGroups[r.Stash];
                    var rc = recvGroups[r.Stash];
                    if (s.Count == 0 && rc.Count == 0)
                        lines.Add($"  <color=white>{r.Name}</color>: no s#/r# token — not on any belt.");
                    else if (!r.Has && rc.Count > 0)
                        lines.Add($"  <color=white>{r.Name}</color> receives on r{string.Join(" r", rc)} but is empty of this item — an r# chest only pulls items it already holds. Seed one stack, or rely on stash/tidy ranking above.");
                }
            }
            return lines;
        }

        static string BeltPair(Row src, Row dst, int g, Dictionary<Entity, List<int>> send, Dictionary<Entity, List<int>> recv, bool loops)
        {
            var head = $"  <color=white>{src.Name}</color> s{g} → <color=white>{dst.Name}</color> r{g}";
            if (!dst.Has)
                return head + ": <color=#aa8>won't move</color> (receiver is empty of this item)";
            if (!src.Has)
                return head + ": nothing to send (sender is empty)";

            // Same-group loop: src also receives on g and dst also sends on g.
            if (!loops && recv[src.Stash].Contains(g) && send[dst.Stash].Contains(g))
                return head + ": <color=red>same-group loop</color>, blocked (needs .sg convloop, off)";

            // Cross-group mutual link: dst sends on any group src receives.
            var mutual = false;
            foreach (var sg in send[dst.Stash])
            {
                if (recv[src.Stash].Contains(sg))
                {
                    mutual = true;
                    break;
                }
            }
            if (mutual && !loops)
            {
                var c = dst.Rank.CompareTo(src.Rank);
                if (c < 0)
                    return head + $": mutual link; <color=green>moves</color> ({dst.Name} outranks {src.Name}: c{dst.Rank.Class} vs c{src.Rank.Class})";
                if (c > 0)
                    return head + $": mutual link; <color=#aa8>stays</color> ({src.Name} outranks {dst.Name}: c{src.Rank.Class} vs c{dst.Rank.Class})";
                return head + ": mutual link; <color=#aa8>stays</color> (tie — rename one so ranking can decide)";
            }
            if (!dst.Room)
                return head + ": <color=red>receiver FULL</color>";
            return head + ": <color=green>moves</color>";
        }

        static bool Contains(List<Row> rows, Entity stash)
        {
            foreach (var r in rows)
            {
                if (r.Stash == stash)
                    return true;
            }
            return false;
        }

        static bool HasRoom(Entity inv, PrefabGUID item)
        {
            if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                return false;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                return false;
            for (var s = 0; s < buf.Length; s++)
            {
                var slot = buf[s];
                if (slot.Amount <= 0 || slot.ItemType.GuidHash == 0)
                    return true;
                if (slot.ItemType.Equals(item) && slot.MaxAmountOverride > 0 && slot.Amount < slot.MaxAmountOverride)
                    return true;
            }
            return false;
        }
    }
}
