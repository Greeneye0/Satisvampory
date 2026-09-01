using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using System.Collections;
using Unity.Entities;

namespace Satisvampory.Services
{
    internal static class FindBuff
    {
        public static bool TryApply(Entity user, Entity character, PrefabGUID prefab, float duration, bool immortal)
        {
            if (BuffUtility.TryGetBuff(Core.Server.EntityManager, character, prefab, out _))
                return false;

            var debug = Core.Server.GetExistingSystemManaged<DebugEventsSystem>();
            debug.ApplyBuff(new FromCharacter { User = user, Character = character }, new ApplyBuffDebugEvent { BuffPrefabGUID = prefab });

            if (!BuffUtility.TryGetBuff(Core.Server.EntityManager, character, prefab, out var buff))
                return false;

            StripSpawnListeners(buff);
            if (immortal)
                MakePersist(buff);
            ApplyDuration(buff, duration);
            return true;
        }

        public static void Clear(Entity character, PrefabGUID prefab)
        {
            if (!BuffUtility.TryGetBuff(Core.EntityManager, character, prefab, out var buff))
                return;
            DestroyUtility.Destroy(Core.EntityManager, buff, DestroyDebugReason.TryRemoveBuff);
        }

        public static void Refresh(Entity user, Entity target, PrefabGUID prefab, float duration, Buffs.BuffCreated callback)
        {
            if (!BuffUtility.HasBuff(Core.EntityManager, target, prefab))
            {
                TryApply(user, target, prefab, duration, true);
                InvokeIfPresent(target, prefab, callback);
                return;
            }
            Core.StartCoroutine(ClearThenApply(user, target, prefab, duration, callback));
        }

        static IEnumerator ClearThenApply(Entity user, Entity target, PrefabGUID prefab, float duration, Buffs.BuffCreated callback)
        {
            Clear(target, prefab);
            while (BuffUtility.HasBuff(Core.EntityManager, target, prefab))
                yield return null;
            TryApply(user, target, prefab, duration, true);
            InvokeIfPresent(target, prefab, callback);
        }

        static void InvokeIfPresent(Entity target, PrefabGUID prefab, Buffs.BuffCreated callback)
        {
            if (callback == null)
                return;
            if (BuffUtility.TryGetBuff(Core.Server.EntityManager, target, prefab, out var buff))
                callback(buff);
        }

        static void StripSpawnListeners(Entity buff)
        {
            if (buff.Has<CreateGameplayEventsOnSpawn>())
                buff.Remove<CreateGameplayEventsOnSpawn>();
            if (buff.Has<GameplayEventListeners>())
                buff.Remove<GameplayEventListeners>();
        }

        static void MakePersist(Entity buff)
        {
            buff.Add<Buff_Persists_Through_Death>();
            DropRemoveOnEvent(buff);
        }

        static void DropRemoveOnEvent(Entity buff)
        {
            if (buff.Has<RemoveBuffOnGameplayEvent>())
                buff.Remove<RemoveBuffOnGameplayEvent>();
            if (buff.Has<RemoveBuffOnGameplayEventEntry>())
                buff.Remove<RemoveBuffOnGameplayEventEntry>();
        }

        static void ApplyDuration(Entity buff, float duration)
        {
            if (duration > -1 && duration != 0)
            {
                if (!buff.Has<LifeTime>())
                {
                    buff.Add<LifeTime>();
                    buff.Write(new LifeTime { EndAction = LifeTimeEndAction.Destroy });
                }
                var life = buff.Read<LifeTime>();
                life.Duration = duration;
                buff.Write(life);
                return;
            }

            if (duration != -1)
                return;

            if (buff.Has<LifeTime>())
            {
                var life = buff.Read<LifeTime>();
                life.Duration = -1;
                life.EndAction = LifeTimeEndAction.None;
                buff.Write(life);
            }
            DropRemoveOnEvent(buff);
        }
    }
}
