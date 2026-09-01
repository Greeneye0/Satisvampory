using ProjectM;
using ProjectM.Network;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Satisvampory.Services
{
    internal sealed class ProxLights
    {
        readonly HeartBoundIndex index;
        readonly Dictionary<int, HashSet<Entity>> proxTouched;
        const float TickSeconds = 2.5f;
        const float Range = 20f;

        public ProxLights(HeartBoundIndex index, Dictionary<int, HashSet<Entity>> proxTouched)
        {
            this.index = index;
            this.proxTouched = proxTouched;
            Core.StartCoroutine(Tick());
        }
        IEnumerator Tick()
        {
            var wait = new WaitForSeconds(TickSeconds);
            while (true)
            {
                yield return wait;
                if (!Core.HasInitialized || !Core.PlayerSettings.IsSolarEnabled(0))
                    continue;
                foreach (var plot in index.OccupiedTerritoryIds())
                {
                    var heart = Core.TerritoryService.GetCastleHeart(plot);
                    if (heart == Entity.Null)
                        continue;
                    try
                    {
                        ApplyProximity(plot, heart);
                    }
                    catch (System.Exception e)
                    {
                        Core.LogException(e);
                    }
                }
            }
        }

        void ApplyProximity(int plot, Entity heart)
        {
            if (!Core.PlayerSettings.IsSolarEnabled(0) || !heart.Has<UserOwner>())
                return;
            var ownerEnt = heart.Read<UserOwner>().Owner.GetEntityOnServer();
            if (ownerEnt == Entity.Null || !ownerEnt.Has<User>())
                return;

            var nearby = new List<Entity>();
            CollectClanOnPlot(ownerEnt.Read<User>(), plot, nearby);
            var allowProx = nearby.Count > 0;
            if (!proxTouched.TryGetValue(plot, out var touched))
            {
                touched = new HashSet<Entity>();
                proxTouched[plot] = touched;
            }

            foreach (var brazier in index.OnTerritory(plot))
            {
                if (!brazier.Has<NameableInteractable>() || !brazier.Has<BurnContainer>())
                    continue;
                var plate = brazier.Read<NameableInteractable>().Name.ToString();
                if (string.IsNullOrEmpty(plate) || plate.IndexOf("prox", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (touched.Remove(brazier) && brazier.Has<Bonfire>())
                    {
                        var reset = brazier.Read<Bonfire>();
                        reset.TimeToGetToFullStrength = 15;
                        brazier.Write(reset);
                    }
                    continue;
                }

                var on = allowProx && AnyWithin(brazier, nearby, Range);
                var burn = brazier.Read<BurnContainer>();
                if (burn.Enabled == on)
                    continue;
                burn.Enabled = on;
                brazier.Write(burn);
                if (touched.Add(brazier) && brazier.Has<Bonfire>())
                {
                    var ramp = brazier.Read<Bonfire>();
                    ramp.TimeToGetToFullStrength = 0.5f;
                    brazier.Write(ramp);
                }
            }
        }

        static void CollectClanOnPlot(User owner, int plot, List<Entity> into)
        {
            var clan = owner.ClanEntity.GetEntityOnServer();
            if (clan == Entity.Null)
            {
                if (owner.IsConnected)
                {
                    var self = owner.LocalCharacter.GetEntityOnServer();
                    if (self != Entity.Null && Core.TerritoryService.GetStandingTerritoryId(self) == plot)
                        into.Add(self);
                }
                return;
            }
            if (!Core.EntityManager.HasBuffer<ClanMemberStatus>(clan) || !Core.EntityManager.HasBuffer<SyncToUserBuffer>(clan))
                return;
            var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clan);
            var users = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clan);
            var n = members.Length < users.Length ? members.Length : users.Length;
            for (var i = 0; i < n; i++)
            {
                if (!members[i].IsConnected)
                    continue;
                var character = users[i].UserEntity.Read<User>().LocalCharacter.GetEntityOnServer();
                if (character == Entity.Null)
                    continue;
                if (Core.TerritoryService.GetStandingTerritoryId(character) == plot)
                    into.Add(character);
            }
        }

        static bool AnyWithin(Entity brazier, List<Entity> people, float range)
        {
            if (!brazier.Has<Translation>())
                return false;
            var origin = brazier.Read<Translation>().Value.xz;
            for (var i = 0; i < people.Count; i++)
            {
                var person = people[i];
                if (person == Entity.Null || !Core.EntityManager.Exists(person) || !person.Has<Translation>())
                    continue;
                if (Vector2.Distance(origin, person.Read<Translation>().Value.xz) <= range)
                    return true;
            }
            return false;
        }
    }
}
