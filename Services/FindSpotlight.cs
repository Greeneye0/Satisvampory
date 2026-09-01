using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// .fi / .fc chest glow. Owned here so StashService stays a dest façade.
    /// </summary>
    internal sealed class FindSpotlight
    {
        static readonly PrefabGUID Glow = new(-2014639169);
        const float Seconds = 15f;
        readonly Dictionary<Entity, (double until, List<Entity> chests)> active = new();

        public void Clear(Entity userEntity)
        {
            if (!active.TryGetValue(userEntity, out var glow))
                return;
            active.Remove(userEntity);
            if (glow.until < Core.ServerTime)
                return;
            for (var i = 0; i < glow.chests.Count; i++)
                Buffs.RemoveBuff(glow.chests[i], Glow);
        }

        public void Mark(Entity chest, Entity userEntity)
        {
            if (!active.TryGetValue(userEntity, out var glow))
            {
                glow = (Core.ServerTime + Seconds, new List<Entity>());
                active[userEntity] = glow;
            }
            glow.chests.Add(chest);
            Buffs.RemoveAndAddBuff(userEntity, chest, Glow, Seconds, buff =>
            {
                var character = userEntity.Read<User>().LocalCharacter;
                buff.Write(new SpellTarget { Target = character });
                buff.Write(new EntityOwner { Owner = character.GetEntityOnServer() });
                buff.Write(new EntityCreator { Creator = character.GetEntityOnServer() });
            });
        }
    }
}
