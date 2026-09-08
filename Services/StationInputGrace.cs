using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// 1.0.145: items a player drops into a station hopper by hand are left alone for
    /// <see cref="GraceSeconds"/> before the belt tick may dump them back to the line.
    ///
    /// Detection is by bookkeeping, not by patching the game's move events: the belt tick
    /// records the hopper count it last saw per item (<see cref="Observe"/>), and every mod
    /// move through <c>InventoryMove.CopyStacks</c> adjusts that record (<see cref="NoteModMove"/>).
    /// A count that rose since then came from a player (a station only ever consumes), so the
    /// item is stamped with the server time and <see cref="IsProtected"/> holds for the window.
    /// </summary>
    internal static class StationInputGrace
    {
        public const double GraceSeconds = 300;

        struct Row
        {
            public int Seen;
            public double ManualAt;
        }

        static readonly Dictionary<Entity, Dictionary<PrefabGUID, Row>> byInput = new();
        static int observeCalls;

        /// <summary>Plan-time snapshot of one hopper. Flags items whose count rose since the mod last saw or moved them.</summary>
        public static void Observe(Entity input, Dictionary<PrefabGUID, int> have)
        {
            if (input == Entity.Null || have == null)
                return;
            if (++observeCalls % 500 == 0)
                Prune();
            var now = Core.ServerTime;
            if (!byInput.TryGetValue(input, out var rows))
            {
                // First sight of this hopper (boot / new station): seed without flagging so
                // stock already sitting there is not held for five minutes after a restart.
                rows = new Dictionary<PrefabGUID, Row>();
                foreach (var kv in have)
                    rows[kv.Key] = new Row { Seen = kv.Value, ManualAt = double.NegativeInfinity };
                byInput[input] = rows;
                return;
            }
            foreach (var kv in have)
            {
                rows.TryGetValue(kv.Key, out var row);
                if (kv.Value > row.Seen)
                    row.ManualAt = now;
                row.Seen = kv.Value;
                rows[kv.Key] = row;
            }
            List<PrefabGUID> gone = null;
            foreach (var kv in rows)
            {
                if (!have.ContainsKey(kv.Key))
                    (gone ??= new List<PrefabGUID>()).Add(kv.Key);
            }
            if (gone != null)
                foreach (var g in gone)
                    rows.Remove(g);
        }

        /// <summary>True while a hand-dropped stack of <paramref name="item"/> in this hopper is inside the grace window.</summary>
        public static bool IsProtected(Entity input, PrefabGUID item) => SecondsLeft(input, item) > 0;

        /// <summary>Seconds of grace remaining, or 0.</summary>
        public static double SecondsLeft(Entity input, PrefabGUID item)
        {
            if (input == Entity.Null || !byInput.TryGetValue(input, out var rows) || !rows.TryGetValue(item, out var row))
                return 0;
            var left = GraceSeconds - (Core.ServerTime - row.ManualAt);
            return left > 0 ? left : 0;
        }

        /// <summary>Called after every mod-side stack move so the mod's own feeds and dumps never read as manual.</summary>
        public static void NoteModMove(Entity from, Entity to, PrefabGUID item, int moved)
        {
            if (moved <= 0 || item.GuidHash == 0)
                return;
            if (to != Entity.Null && byInput.TryGetValue(to, out var toRows))
            {
                toRows.TryGetValue(item, out var row);
                if (row.Seen == 0 && row.ManualAt == 0)
                    row.ManualAt = double.NegativeInfinity;
                row.Seen += moved;
                toRows[item] = row;
            }
            if (from != Entity.Null && byInput.TryGetValue(from, out var fromRows) && fromRows.TryGetValue(item, out var src))
            {
                src.Seen = Math.Max(0, src.Seen - moved);
                fromRows[item] = src;
            }
        }

        static void Prune()
        {
            List<Entity> dead = null;
            foreach (var kv in byInput)
            {
                if (!Core.EntityManager.Exists(kv.Key))
                    (dead ??= new List<Entity>()).Add(kv.Key);
            }
            if (dead != null)
                foreach (var e in dead)
                    byInput.Remove(e);
        }
    }
}
