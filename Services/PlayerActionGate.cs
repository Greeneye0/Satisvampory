using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Shared player/castle gates for .stash .pull empty-trash: downed, dead, combat, ally.
    /// </summary>
    internal static class PlayerActionGate
    {
        static readonly PrefabGUID Downed = new(-1992158531);
        static readonly PrefabGUID BatForm = new(1205505492);

        public readonly struct Context
        {
            public readonly Entity Character;
            public readonly Entity UserEntity;
            public readonly User User;
            public readonly int StandingPlot;
            public readonly Entity Heart;
            public Context(Entity character, Entity userEntity, User user, int standingPlot, Entity heart)
            {
                Character = character;
                UserEntity = userEntity;
                User = user;
                StandingPlot = standingPlot;
                Heart = heart;
            }
        }

        public static bool TryOpen(Entity character, string verb, bool requireAlliedHeart, out Context ctx, out string deny)
        {
            ctx = default;
            deny = null;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>())
            {
                deny = "No character.";
                return false;
            }

            var userEntity = character.Read<PlayerCharacter>().UserEntity;
            var user = userEntity.Read<User>();
            var plot = Core.TerritoryService.GetStandingTerritoryId(character);
            var heart = plot >= 0 ? Core.TerritoryService.GetCastleHeart(plot) : Entity.Null;
            ctx = new Context(character, userEntity, user, plot, heart);

            if (BuffUtility.TryGetBuff(Core.EntityManager, character, Downed, out _))
            {
                deny = $"Unable to {verb} while downed!";
                return false;
            }
            if (character.Has<Health>() && character.Read<Health>().IsDead)
            {
                deny = $"Unable to {verb} when dead!";
                return false;
            }
            if (BuffUtility.TryGetBuff(Core.EntityManager, character, BatForm, out _))
            {
                deny = $"Cannot {verb} items while in batform.";
                return false;
            }
            if (BuffUtility.TryGetBuff(Core.EntityManager, character, Const.Buff_InCombat_PvPVampire, out _))
            {
                deny = $"Unable to {verb} while in PvP combat.";
                return false;
            }

            if (requireAlliedHeart)
            {
                var cs = Core.TerritoryService.IsClanShareOn(user);
                if (plot < 0 && !cs)
                {
                    deny = $"Unable to {verb} outside territories!";
                    return false;
                }
                if (cs && Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character).Count == 0)
                {
                    deny = $"Unable to {verb} — no clan castles available (ClanShare on).";
                    return false;
                }
                if (plot >= 0)
                {
                    if (heart == Entity.Null)
                    {
                        deny = "There is no heart on this territory!";
                        return false;
                    }
                    if (!Core.ServerGameManager.IsAllies(heart, character))
                    {
                        deny = "You aren't allies with the heart on this territory!";
                        return false;
                    }
                    if (heart.Has<CastleHeart>() && heart.Read<CastleHeart>().ActiveEvent >= CastleHeartEvent.Attacked)
                    {
                        deny = $"Unable to {verb} while castle is {heart.Read<CastleHeart>().ActiveEvent}";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// .stash / RR: same castle gates as TryOpen, but bat form is allowed (legacy stash)
        /// and off-plot requires RR-global plus ClanShare plots.
        /// </summary>
        public static bool TryOpenForStash(Entity character, out Context ctx, out string deny)
        {
            ctx = default;
            deny = null;
            if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>())
            {
                deny = "No character.";
                return false;
            }

            var userEntity = character.Read<PlayerCharacter>().UserEntity;
            var user = userEntity.Read<User>();
            var plot = Core.TerritoryService.GetStandingTerritoryId(character);
            var heart = plot >= 0 ? Core.TerritoryService.GetCastleHeart(plot) : Entity.Null;
            ctx = new Context(character, userEntity, user, plot, heart);

            if (BuffUtility.TryGetBuff(Core.EntityManager, character, Downed, out _))
            {
                deny = "Unable to stash while downed!";
                return false;
            }
            if (character.Has<Health>() && character.Read<Health>().IsDead)
            {
                deny = "Unable to stash when dead!";
                return false;
            }
            if (BuffUtility.TryGetBuff(Core.EntityManager, character, Const.Buff_InCombat_PvPVampire, out _))
            {
                deny = "Unable to stash while in PvP combat.";
                return false;
            }

            var cs = Core.TerritoryService.IsClanShareOn(user);
            var rrGlobal = Core.PlayerSettings.IsRrGlobalEnabled(user.PlatformId);
            if (plot < 0)
            {
                if (!rrGlobal)
                {
                    deny = "Unable to stash outside territories! (RR global off)";
                    return false;
                }
                if (!cs || Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character).Count == 0)
                {
                    deny = "Unable to stash outside territories — no clan plots (need ClanShare).";
                    return false;
                }
                return true;
            }

            if (heart == Entity.Null)
            {
                deny = "There is no heart on this territory!";
                return false;
            }
            if (!Core.ServerGameManager.IsAllies(heart, character))
            {
                deny = "You aren't allies with the heart on this territory!";
                return false;
            }
            if (heart.Has<CastleHeart>() && heart.Read<CastleHeart>().ActiveEvent >= CastleHeartEvent.Attacked)
            {
                deny = $"Unable to stash while castle is {heart.Read<CastleHeart>().ActiveEvent}";
                return false;
            }
            if (cs && Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character).Count == 0)
            {
                deny = "Unable to stash -- no clan castles available (ClanShare on).";
                return false;
            }
            return true;
        }

        public static void Deny(User user, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            Utilities.SendSystemMessageToClient(Core.EntityManager, user, message);
        }
    }
}
