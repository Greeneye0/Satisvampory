using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// `.s item` report: per-plot chest counts, machines using the item, cap/reserve, total.
    /// </summary>
    internal static class ItemStock
    {
        public static List<string> Report(Entity character, PrefabGUID item)
        {
            var lines = new List<string>();
            if (character == Entity.Null || !character.Has<PlayerCharacter>() || item.GuidHash == 0)
            {
                lines.Add("Unable to report that item.");
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
                    ? "Unable to search — no clan castles available (ClanShare on)."
                    : "You must stand on a castle plot to list item stock.");
                return lines;
            }

            var itemName = StashRouting.ItemLabel(item);
            ulong ownerId = 0;
            if (standing >= 0)
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(standing, out ownerId);
            else
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plotIds[0], out ownerId);

            var conveyorOn = BeltOwner.ConveyorOn(ownerId);
            var senders = BeltRecipe.ScanSenders(plotIds, ownerId);
            var sgm = Core.ServerGameManager;
            var islandCounts = BeltCounts.OfPlots(plotIds);
            var total = 0;
            var prefix = csOn ? "  " : "";

            lines.Add("<color=green>" + itemName + "</color>");

            var ordered = OrderPlots(plotIds, standing);
            var anyChest = false;
            for (var p = 0; p < ordered.Count; p++)
            {
                var plotId = ordered[p];
                var heart = Core.TerritoryService.GetCastleHeart(plotId);
                if (heart == Entity.Null)
                    continue;
                if (!sgm.IsAllies(heart, character))
                    continue;
                if (TerritoryService.IsHeartRaided(heart))
                    continue;

                var hits = new List<(string name, int amount)>();
                foreach (var stash in Core.Stash.ChestsOnPlot(plotId))
                {
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                        continue;
                    var n = sgm.GetInventoryItemCount(inv, item);
                    if (n <= 0)
                        continue;
                    hits.Add((stash.EntityName(), n));
                    total += n;
                }

                if (hits.Count == 0)
                {
                    if (csOn && plotId == standing)
                        lines.Add("<color=yellow>" + Core.TerritoryService.FormatPlotLabel(plotId) + "</color> <color=yellow>(here)</color>: none");
                    continue;
                }

                anyChest = true;
                hits.Sort((a, b) =>
                {
                    var c = b.amount.CompareTo(a.amount);
                    return c != 0 ? c : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                });
                var here = plotId == standing ? " <color=yellow>(here)</color>" : "";
                lines.Add("<color=yellow>" + Core.TerritoryService.FormatPlotLabel(plotId) + "</color>" + here);
                for (var i = 0; i < hits.Count; i++)
                    lines.Add(prefix + "<color=white>" + hits[i].amount + "</color>x <color=#FFC0CB>" + hits[i].name + "</color>");
            }

            if (!anyChest)
                lines.Add("No chests hold this item.");

            lines.Add("Machines");
            var machines = new List<string>();
            var seen = new HashSet<Entity>();
            for (var p = 0; p < plotIds.Count; p++)
            {
                var plotId = plotIds[p];
                if (Core.RefinementStations == null)
                    continue;
                foreach (var station in Core.RefinementStations.BenchesOnPlot(plotId))
                {
                    if (!seen.Add(station) || !Core.EntityManager.Exists(station) || station.Has<Disabled>())
                        continue;
                    if (!station.Has<Refinementstation>() || !station.Has<RefinementstationRecipesBuffer>())
                        continue;
                    if (!TryDescribeMachine(station, item, ownerId, conveyorOn, senders, islandCounts, out var hopper, out var outputHave, out var asInput, out var asOutput, out var status))
                        continue;
                    if (asInput)
                        total += hopper;
                    if (asOutput)
                        total += outputHave;
                    var name = station.EntityName();
                    var bits = new List<string>();
                    if (asInput)
                        bits.Add("in <color=white>" + hopper + "</color>");
                    if (asOutput)
                        bits.Add("out <color=white>" + outputHave + "</color>");
                    var io = bits.Count > 0 ? " " + string.Join(" ", bits) : "";
                    machines.Add(prefix + "<color=#FFC0CB>" + name + "</color>" + io + " — " + status);
                }
            }
            if (machines.Count == 0)
                lines.Add(prefix + "none using this item");
            else
            {
                machines.Sort(StringComparer.OrdinalIgnoreCase);
                lines.AddRange(machines);
            }

            var reserve = ownerId != 0 ? Core.PlayerSettings.GetPullReserve(ownerId, item) : 0;
            var capText = "unlimited";
            if (ownerId != 0 && Core.PlayerSettings.TryGetItemCap(ownerId, item, out var cap))
                capText = cap.ToString();
            lines.Add("Cap <color=white>" + capText + "</color>  reserve <color=white>" + reserve + "</color>");
            lines.Add("Total <color=white>" + total + "</color>x <color=green>" + itemName + "</color>");
            return lines;
        }

        static List<int> OrderPlots(IReadOnlyList<int> plotIds, int standing)
        {
            var ordered = new List<int>(plotIds.Count);
            if (standing >= 0)
            {
                for (var i = 0; i < plotIds.Count; i++)
                {
                    if (plotIds[i] == standing)
                    {
                        ordered.Add(standing);
                        break;
                    }
                }
            }
            for (var i = 0; i < plotIds.Count; i++)
            {
                if (plotIds[i] != standing)
                    ordered.Add(plotIds[i]);
            }
            return ordered;
        }

        static List<int> ReceiverGroups(Entity station)
        {
            var groups = new List<int>();
            var name = StashRouting.RawName(station);
            if (string.IsNullOrEmpty(name) || Core.Stash?.ReceiveToken == null)
                return groups;
            foreach (Match match in Core.Stash.ReceiveToken.Matches(name.ToLowerInvariant()))
            {
                if (!int.TryParse(match.Groups[1].Value, out var group))
                    continue;
                if (!groups.Contains(group))
                    groups.Add(group);
            }
            return groups;
        }

        static bool TryDescribeMachine(Entity station, PrefabGUID item, ulong ownerId, bool conveyorOn,
            BeltRecipe.SenderPools senders, Dictionary<PrefabGUID, int> islandCounts,
            out int hopper, out int outputHave, out bool asInput, out bool asOutput, out string status)
        {
            hopper = 0;
            outputHave = 0;
            asInput = false;
            asOutput = false;
            status = "no conveyor";
            var rs = station.Read<Refinementstation>();
            var input = rs.InputInventoryEntity.GetEntityOnServer();
            var output = rs.OutputInventoryEntity.GetEntityOnServer();
            hopper = BeltRecipe.Count(input, item);
            outputHave = BeltRecipe.Count(output, item);
            var floor = BeltRecipe.FloorScale(station);
            var groups = ReceiverGroups(station);
            var recipes = station.ReadBuffer<RefinementstationRecipesBuffer>();
            var anyEnabled = false;
            var canCraft = false;
            var wantsMore = false;
            var capped = false;
            var outFull = false;
            for (var r = 0; r < recipes.Length; r++)
            {
                var recipe = recipes[r];
                if (!recipe.Unlocked || recipe.Disabled)
                    continue;
                if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipe.RecipeGuid, out var recipeEnt))
                    continue;
                if (recipeEnt == Entity.Null || !Core.EntityManager.Exists(recipeEnt))
                    continue;
                var consumes = false;
                var produces = false;
                var outputPerCraft = 1;
                PrefabGUID product = default;
                if (recipeEnt.Has<RecipeOutputBuffer>())
                {
                    var outputs = recipeEnt.ReadBuffer<RecipeOutputBuffer>();
                    for (var i = 0; i < outputs.Length; i++)
                    {
                        if (outputs[i].Guid.Equals(item))
                            produces = true;
                        if (product.GuidHash == 0 && outputs[i].Guid.GuidHash != 0)
                        {
                            product = outputs[i].Guid;
                            outputPerCraft = outputs[i].Amount > 0 ? outputs[i].Amount : 1;
                        }
                    }
                }
                if (!recipeEnt.Has<RecipeRequirementBuffer>())
                    continue;
                var requirements = recipeEnt.ReadBuffer<RecipeRequirementBuffer>();
                for (var q = 0; q < requirements.Length; q++)
                {
                    if (requirements[q].Guid.Equals(item))
                        consumes = true;
                }
                if (!consumes && !produces)
                    continue;
                anyEnabled = true;
                if (consumes)
                    asInput = true;
                if (produces)
                    asOutput = true;

                var remainingOutputs = int.MaxValue;
                if (product.GuidHash != 0 && Core.PlayerSettings.TryGetItemCap(ownerId, product, out var cap))
                {
                    islandCounts.TryGetValue(product, out var haveOut);
                    if (haveOut >= cap)
                    {
                        capped = true;
                        continue;
                    }
                    remainingOutputs = cap - haveOut;
                }
                if (product.GuidHash != 0 && output != Entity.Null && Core.EntityManager.Exists(output)
                    && Core.ServerGameManager.HasFullInventory(output))
                {
                    outFull = true;
                    continue;
                }

                var crafts = BeltRecipe.StationFeedMul;
                if (remainingOutputs != int.MaxValue)
                {
                    var fromCap = remainingOutputs / outputPerCraft;
                    if (fromCap < crafts)
                        crafts = fromCap;
                }
                for (var q = 0; q < requirements.Length; q++)
                {
                    var req = requirements[q];
                    if (req.Guid.GuidHash == 0)
                        continue;
                    var perCraft = BeltRecipe.PerCraft(req.Amount, floor);
                    if (perCraft <= 0)
                        continue;
                    var inStation = BeltRecipe.Count(input, req.Guid);
                    var available = inStation + senders.Of(groups, req.Guid);
                    var fromMat = available / perCraft;
                    if (fromMat < crafts)
                        crafts = fromMat;
                }
                if (crafts <= 0)
                    continue;
                canCraft = true;
                for (var q = 0; q < requirements.Length; q++)
                {
                    var req = requirements[q];
                    if (req.Guid.GuidHash == 0)
                        continue;
                    var perCraft = BeltRecipe.PerCraft(req.Amount, floor);
                    if (perCraft <= 0)
                        continue;
                    if (BeltRecipe.Count(input, req.Guid) < crafts * perCraft)
                        wantsMore = true;
                }
            }

            if (!anyEnabled && hopper <= 0 && outputHave <= 0)
                return false;
            if (!anyEnabled)
            {
                asInput = hopper > 0;
                asOutput = outputHave > 0;
            }

            if (groups.Count == 0)
                status = "no conveyor";
            else if (!conveyorOn)
                status = "<color=red>not moving</color> (conveyor off)";
            else if (capped && !canCraft)
                status = "<color=red>not moving</color> (plot cap)";
            else if (outFull && !canCraft)
                status = "<color=red>not moving</color> (output full)";
            else if (!canCraft)
                status = "<color=red>not moving</color> (not enough for a full craft)";
            else if (wantsMore)
                status = "<color=green>moving</color>";
            else
                status = "<color=red>not moving</color> (already has a craft)";
            return true;
        }
    }
}
