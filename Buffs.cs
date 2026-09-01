using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using System.Collections;
using Unity.Entities;

namespace Satisvampory;

internal class Buffs
{
    public delegate void BuffCreated(Entity buffEntity);

    public static bool AddBuff(Entity User, Entity Character, PrefabGUID buffPrefab, float duration = 0, bool immortal = true)
        => Services.FindBuff.TryApply(User, Character, buffPrefab, duration, immortal);

    public static void RemoveBuff(Entity Character, PrefabGUID buffPrefab)
        => Services.FindBuff.Clear(Character, buffPrefab);

    public static void RemoveAndAddBuff(Entity userEntity, Entity targetEntity, PrefabGUID buffPrefab, float duration = -1, BuffCreated callback = null)
        => Services.FindBuff.Refresh(userEntity, targetEntity, buffPrefab, duration, callback);
}
