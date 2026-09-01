using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class AdminGive
    {
        public static void IntoPlot(Entity character, PrefabGUID item, int amount)
        {
            if (character == Entity.Null || !character.Has<PlayerCharacter>())
                return;
            var user = character.Read<PlayerCharacter>().UserEntity.Read<User>();
            var plot = Core.TerritoryService.GetStandingTerritoryId(character);
            if (plot < 0)
            {
                PlayerActionGate.Deny(user, "Unable to stash outside territories!");
                return;
            }
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null)
            {
                PlayerActionGate.Deny(user, "There is no heart on this territory!");
                return;
            }

            var sgm = Core.ServerGameManager;
            var remaining = amount;
            var moved = new Dictionary<Entity, int>();
            foreach (var chest in Core.Stash.ChestsOnPlot(plot))
            {
                var plate = StashRouting.RawName(chest);
                if (StashRouting.IsSpecialName(plate) || StashRouting.IsOverflowName(plate))
                    continue;
                if (!StashRouting.TryGetExternalInventory(chest, out _))
                    continue;
                try
                {
                    var response = sgm.TryAddInventoryItem(chest, item, remaining);
                    if (!response.Success)
                        continue;
                    var got = remaining - response.RemainingAmount;
                    remaining = response.RemainingAmount;
                    moved.TryGetValue(chest, out var have);
                    moved[chest] = have + got;
                    if (remaining <= 0)
                        break;
                }
                catch (Exception e)
                {
                    Core.LogException(e, "AdminGive");
                }
            }

            if (remaining > 0)
            {
                foreach (var overflow in Core.Stash.OverflowChests(plot))
                {
                    try
                    {
                        var response = sgm.TryAddInventoryItem(overflow, item, remaining);
                        if (!response.Success)
                            continue;
                        var got = remaining - response.RemainingAmount;
                        remaining = response.RemainingAmount;
                        moved.TryGetValue(overflow, out var have);
                        moved[overflow] = have + got;
                        if (remaining <= 0)
                            break;
                    }
                    catch (Exception e)
                    {
                        Core.LogException(e, "AdminGive overflow");
                    }
                }
            }

            if (moved.Count == 0)
                PlayerActionGate.Deny(user, "No items were able to admin stash!");
            else
                PlayerActionGate.Deny(user, "Admin stashed items to the current territory!");

            if (Core.PlayerSettings.IsSilentStashEnabled(user.PlatformId))
                return;
            foreach (var (chest, n) in moved)
                Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                    $"Admin Stashed <color=white>{n}</color>x <color=green>{item.PrefabName()}</color> to <color=#FFC0CB>{chest.EntityName()}</color>");
            if (remaining > 0)
                Utilities.SendSystemMessageToClient(Core.EntityManager, user,
                    $"Unable to admin stash <color=white>{remaining}</color>x <color=green>{item.PrefabName()}</color> due to insufficient space in stashes!");
        }
    }
}
