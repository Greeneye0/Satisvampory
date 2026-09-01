using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal sealed class FindReport
    {
        readonly FindSpotlight glow;

        public FindReport(FindSpotlight glow)
        {
            this.glow = glow;
        }
        static string FormatCurrentPlotLine(int standing)
        {
            if (standing < 0)
                return "Current plot: <color=yellow>none</color> (not standing on a castle)";
            return $"Current plot: <color=yellow>{Core.TerritoryService.FormatPlotLabel(standing)}</color>";
        }

        static List<int> OrderPlotsCurrentFirst(IReadOnlyList<int> plotIds, int standing)
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

        void ReplyPlotHeader(User user, int plotId, int standing)
        {
            var here = plotId == standing ? " <color=yellow>(here)</color>" : "";
            Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                $"<color=yellow>{Core.TerritoryService.FormatPlotLabel(plotId)}</color>{here}");
        }

        public void Items(Entity charEntity, PrefabGUID item)
        {
            var userEntity = charEntity.Read<PlayerCharacter>().UserEntity;
            var user = userEntity.Read<User>();

            glow.Clear(userEntity);

            var standing = Core.TerritoryService.GetStandingTerritoryId(charEntity);
            var csOn = Core.TerritoryService.IsClanShareOn(user);
            var plotIds = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(charEntity);
            if (plotIds.Count == 0)
            {
                if (csOn)
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Unable to search — no clan castles available (ClanShare on).");
                else
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Unable to search for items outside territories!");
                return;
            }

            var itemName = item.PrefabName();
            var header = csOn
                ? $"Find Item Report — ClanShare ON\n{FormatCurrentPlotLine(standing)}\n--------------------------------"
                : $"Find Item Report\n{FormatCurrentPlotLine(standing)}\n--------------------------------";
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, header);

            var serverGameManager = Core.ServerGameManager;
            var scannedAny = false;
            var totalFound = 0;
            foreach (var plotId in OrderPlotsCurrentFirst(plotIds, standing))
            {
                var heart = Core.TerritoryService.GetCastleHeart(plotId);
                if (heart == Entity.Null) continue;
                if (!serverGameManager.IsAllies(heart, charEntity)) continue;
                if (TerritoryService.IsHeartRaided(heart)) continue;

                var hits = new List<(Entity stash, int amount)>();
                foreach (var stash in Core.Stash.GetStashesOnTerritory(plotId))
                {
                    scannedAny = true;
                    if (!StashRouting.TryGetExternalInventory(stash, out var attachedEntity))
                        continue;
                    var amountFound = serverGameManager.GetInventoryItemCount(attachedEntity, item);
                    if (amountFound > 0)
                        hits.Add((stash, amountFound));
                }

                var isHere = plotId == standing;
                if (hits.Count == 0)
                {
                    if (csOn && isHere)
                        Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                            $"<color=yellow>{Core.TerritoryService.FormatPlotLabel(plotId)}</color> <color=yellow>(here)</color>: none");
                    continue;
                }

                if (csOn)
                    ReplyPlotHeader(user, plotId, standing);

                var prefix = csOn ? "  " : "";
                foreach (var (stash, amountFound) in hits)
                {
                    totalFound += amountFound;
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                        $"{prefix}<color=white>{amountFound}</color>x <color=green>{itemName}</color> found in <color=#FFC0CB>{stash.EntityName()}</color>");
                    glow.Mark(stash, userEntity);
                }
            }

            if (!scannedAny)
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                    csOn
                        ? "No available stashes found on clan plots!"
                        : "No available stashes found in your current territory!");
                return;
            }

            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Total <color=green>{itemName}</color> found: <color=white>{totalFound}</color>");
        }

        public void Chests(Entity charEntity, string chestName)
        {
            var userEntity = charEntity.Read<PlayerCharacter>().UserEntity;
            var user = userEntity.Read<User>();
            glow.Clear(userEntity);

            var standing = Core.TerritoryService.GetStandingTerritoryId(charEntity);
            var csOn = Core.TerritoryService.IsClanShareOn(user);
            var plotIds = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(charEntity);
            if (plotIds.Count == 0)
            {
                if (csOn)
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Unable to search — no clan castles available (ClanShare on).");
                else
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Unable to search for chests outside territories!");
                return;
            }

            var header = csOn
                ? $"Find Chest Report — ClanShare ON\n{FormatCurrentPlotLine(standing)}\n--------------------------------"
                : $"Find Chest Report\n{FormatCurrentPlotLine(standing)}\n--------------------------------";
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, header);

            var foundStash = false;
            var totalFound = 0;
            var searchName = chestName.ToLower();
            foreach (var plotId in OrderPlotsCurrentFirst(plotIds, standing))
            {
                var heart = Core.TerritoryService.GetCastleHeart(plotId);
                if (heart == Entity.Null) continue;
                if (!Core.ServerGameManager.IsAllies(heart, charEntity)) continue;
                if (TerritoryService.IsHeartRaided(heart)) continue;

                var hits = new List<(Entity stash, string highlightedName)>();
                foreach (var stash in Core.Stash.GetStashesOnTerritory(plotId))
                {
                    var stashName = stash.Read<NameableInteractable>().Name.ToString();
                    if (!stashName.ToLower().Contains(searchName)) continue;
                    var highlightedName = stashName.Replace(chestName, $"<color=yellow><b>{chestName}</b></color>", StringComparison.OrdinalIgnoreCase);
                    hits.Add((stash, highlightedName));
                }

                var isHere = plotId == standing;
                if (hits.Count == 0)
                {
                    if (csOn && isHere)
                        Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                            $"<color=yellow>{Core.TerritoryService.FormatPlotLabel(plotId)}</color> <color=yellow>(here)</color>: none");
                    continue;
                }

                foundStash = true;
                if (csOn)
                    ReplyPlotHeader(user, plotId, standing);

                var prefix = csOn ? "  " : "";
                foreach (var (stash, highlightedName) in hits)
                {
                    totalFound++;
                    Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                        $"{prefix}Found chest: <color=#FFC0CB>{highlightedName}</color>");
                    glow.Mark(stash, userEntity);
                }
            }

            if (!foundStash)
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                    csOn
                        ? "No matching stashes found on clan plots!"
                        : "No matching stashes found in your current territory!");
                return;
            }
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, $"Total chests matching <color=green>{chestName}</color>: <color=white>{totalFound}</color>");
        }


    }
}
