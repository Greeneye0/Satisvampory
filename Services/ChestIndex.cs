using ProjectM;
using ProjectM.CastleBuilding;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Shared-castle chest lists keyed by plot. Group (s#/r#) and named (salvage/overflow/…)
    /// filters live here so StashService is not a Kindred GetStashesOnTerritory fork.
    /// </summary>
    internal sealed class ChestIndex
    {
        readonly Dictionary<int, (DateTime At, List<Entity> List)> cache = new();
        readonly Regex send;
        readonly Regex receive;

        public ChestIndex(Regex sendToken, Regex receiveToken)
        {
            send = sendToken;
            receive = receiveToken;
        }

        public void Forget(int plot) => cache.Remove(plot);
        public void ForgetAll() => cache.Clear();

        public IEnumerable<Entity> OnPlot(int plot)
        {
            var now = DateTime.UtcNow;
            if (cache.TryGetValue(plot, out var hit) && (now - hit.At).TotalSeconds < 0.25 && hit.List != null)
            {
                for (var i = 0; i < hit.List.Count; i++)
                {
                    var chest = hit.List[i];
                    if (chest == Entity.Null || !Core.EntityManager.Exists(chest))
                        continue;
                    yield return chest;
                }
                yield break;
            }

            var list = ScanPlot(plot);
            cache[plot] = (now, list);
            for (var i = 0; i < list.Count; i++)
                yield return list[i];
        }

        static List<Entity> ScanPlot(int plot)
        {
            var list = new List<Entity>();
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null || !heart.Has<SharedCastleInventoryConnection>())
                return list;
            var manager = heart.Read<SharedCastleInventoryConnection>().SharedInventoryManager.GetEntityOnServer();
            if (manager == Entity.Null || !Core.EntityManager.Exists(manager) || !Core.EntityManager.HasBuffer<SharedCastleInventories>(manager))
                return list;
            var rows = Core.EntityManager.GetBuffer<SharedCastleInventories>(manager);
            for (var i = 0; i < rows.Length; i++)
            {
                var chest = rows[i].InventorySource;
                if (chest == Entity.Null || !Core.EntityManager.Exists(chest) || !chest.Has<NameableInteractable>())
                    continue;
                var plate = chest.Read<NameableInteractable>().Name.ToString();
                if (!string.IsNullOrEmpty(plate) && plate.EndsWith("''", StringComparison.Ordinal))
                    continue;
                if (StashRouting.IsNoShareName(plate))
                    continue;
                list.Add(chest);
            }
            return list;
        }

        public IEnumerable<(int group, Entity chest)> Senders(int plot) => Grouped(send, plot, skipStations: true, skipOverflow: false);
        public IEnumerable<(int group, Entity chest)> Receivers(int plot) => Grouped(receive, plot, skipStations: true, skipOverflow: true);

        IEnumerable<(int group, Entity chest)> Grouped(Regex token, int plot, bool skipStations, bool skipOverflow)
        {
            foreach (var chest in OnPlot(plot))
            {
                if (skipStations && chest.Has<Refinementstation>())
                    continue;
                if (Core.TerritoryService.GetTerritoryId(chest) != plot)
                    continue;
                var plate = StashRouting.RawName(chest);
                if (string.IsNullOrEmpty(plate) || StashRouting.IsNoShareName(plate))
                    continue;
                if (skipOverflow && (plate.IndexOf("overflow", StringComparison.OrdinalIgnoreCase) >= 0 || StashRouting.IsOverflowDestName(plate)))
                    continue;
                foreach (Match hit in token.Matches(plate.ToLowerInvariant()))
                {
                    if (int.TryParse(hit.Groups[1].Value, out var group))
                        yield return (group, chest);
                }
            }
        }

        public IEnumerable<Entity> Named(int plot, string needle)
        {
            needle = needle.ToLowerInvariant();
            foreach (var chest in OnPlot(plot))
            {
                var plate = StashRouting.RawName(chest).ToLowerInvariant();
                if (string.IsNullOrEmpty(plate) || plate.EndsWith("''", StringComparison.Ordinal))
                    continue;
                if (StashRouting.IsNoShareName(plate))
                    continue;
                if (plate.IndexOf(needle, StringComparison.Ordinal) < 0)
                    continue;
                yield return chest;
            }
        }
    }
}
