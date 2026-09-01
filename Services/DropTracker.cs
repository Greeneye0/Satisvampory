using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Satisvampory.Services;

/// <summary>
/// Process-local tags for world ItemPickups. Not persisted.
/// After a restart, old piles are not FreshFor anyone (around-auto leaves them).
/// </summary>
internal static class DropTracker
{
    public const float PlayerDropMatchRadius = 15f;
    public const float PlayerDropWindowSeconds = 6f;
    public const float DiscoverIntervalSeconds = 0.5f;

    struct PendingPlayerDrop
    {
        public float3 Position;
        public int ItemHash;
        public int Amount;
        public DateTime ExpiresAt;
    }

    static readonly object Gate = new();
    static readonly List<PendingPlayerDrop> Pending = new();
    static readonly HashSet<Entity> PlayerDropped = new();
    static readonly Dictionary<Entity, HashSet<ulong>> FreshFor = new();
    static readonly Dictionary<Entity, int> SeenAmounts = new();
    static readonly List<Entity> Dead = new();
    static bool seeded;

    public static IEnumerator DiscoverLoop()
    {
        var wait = new UnityEngine.WaitForSeconds(DiscoverIntervalSeconds);
        while (true)
        {
            yield return wait;
            if (!Core.HasInitialized) continue;
            try { Discover(); }
            catch (Exception e) { Core.LogException(e); }
        }
    }

    public static void SeedExisting()
    {
        lock (Gate)
        {
            SeenAmounts.Clear();
            PlayerDropped.Clear();
            FreshFor.Clear();
            Pending.Clear();
            seeded = false;
            SnapshotExistingNoTags();
            seeded = true;
        }
    }

    public static void NotePlayerDrop(float3 position, PrefabGUID item, int amount)
    {
        lock (Gate)
        {
            Pending.Add(new PendingPlayerDrop
            {
                Position = position,
                ItemHash = item.GuidHash,
                Amount = amount,
                ExpiresAt = DateTime.UtcNow.AddSeconds(PlayerDropWindowSeconds)
            });
        }
    }

    public static void Discover()
    {
        if (!Core.HasInitialized) return;
        lock (Gate)
        {
            if (!seeded)
            {
                SnapshotExistingNoTags();
                seeded = true;
                return;
            }

            ExpirePending();
            var nowSeen = new HashSet<Entity>();
            ForEachPickup((drop, pickup, pos) =>
            {
                nowSeen.Add(drop);
                var amount = pickup.ItemAmount;
                if (!SeenAmounts.ContainsKey(drop))
                {
                    SeenAmounts[drop] = amount;
                    if (MatchesPendingPlayerDrop(pos, pickup.ItemId, amount))
                        PlayerDropped.Add(drop);
                    TagFreshForPlayersAt(drop, pos);
                    return;
                }

                if (amount > SeenAmounts[drop] && MatchesPendingPlayerDrop(pos, pickup.ItemId, amount))
                    PlayerDropped.Add(drop);
                SeenAmounts[drop] = amount;
            });

            Dead.Clear();
            foreach (var e in SeenAmounts.Keys)
            {
                if (!nowSeen.Contains(e))
                    Dead.Add(e);
            }
            foreach (var e in Dead)
                ForgetUnlocked(e);
        }
    }

    public static bool IsPlayerDropped(Entity drop)
    {
        lock (Gate) return PlayerDropped.Contains(drop);
    }

    public static bool IsFreshFor(Entity drop, ulong platformId)
    {
        lock (Gate)
            return FreshFor.TryGetValue(drop, out var set) && set.Contains(platformId);
    }

    public static void Forget(Entity drop)
    {
        lock (Gate) ForgetUnlocked(drop);
    }

    static void ForgetUnlocked(Entity drop)
    {
        SeenAmounts.Remove(drop);
        PlayerDropped.Remove(drop);
        FreshFor.Remove(drop);
    }

    static void SnapshotExistingNoTags()
    {
        ForEachPickup((drop, pickup, _) =>
        {
            SeenAmounts[drop] = pickup.ItemAmount;
        });
    }

    static void ForEachPickup(Action<Entity, ItemPickup, float3> action)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(new(Il2CppType.Of<ItemPickup>(), ComponentType.AccessMode.ReadOnly))
            .AddAll(new(Il2CppType.Of<Translation>(), ComponentType.AccessMode.ReadOnly));
        var query = Core.EntityManager.CreateEntityQuery(ref builder);
        builder.Dispose();
        var drops = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var drop in drops)
            {
                if (drop == Entity.Null || !Core.EntityManager.Exists(drop)) continue;
                if (!drop.Has<ItemPickup>() || !drop.Has<Translation>()) continue;
                action(drop, drop.Read<ItemPickup>(), drop.Read<Translation>().Value);
            }
        }
        finally
        {
            drops.Dispose();
            query.Dispose();
        }
    }

    static void ExpirePending()
    {
        var now = DateTime.UtcNow;
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (Pending[i].ExpiresAt <= now)
                Pending.RemoveAt(i);
        }
    }

    static bool MatchesPendingPlayerDrop(float3 pos, PrefabGUID item, int amount)
    {
        var radiusSq = PlayerDropMatchRadius * PlayerDropMatchRadius;
        foreach (var pending in Pending)
        {
            if (math.lengthsq(pos - pending.Position) > radiusSq) continue;
            if (pending.ItemHash != 0 && pending.ItemHash != item.GuidHash) continue;
            return true;
        }
        return false;
    }

    static void TagFreshForPlayersAt(Entity drop, float3 spawnPos)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
        var query = Core.EntityManager.CreateEntityQuery(ref builder);
        builder.Dispose();
        var users = query.ToEntityArray(Allocator.Temp);
        try
        {
            HashSet<ulong> set = null;
            foreach (var userEntity in users)
            {
                if (!Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>()) continue;
                var user = userEntity.Read<User>();
                if (!user.IsConnected) continue;
                var character = user.LocalCharacter.GetEntityOnServer();
                if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<Translation>())
                    continue;
                var radius = Core.PlayerSettings.GetRadius(user.PlatformId);
                if (radius < 1f) radius = ScoopService.DefaultRadius;
                var origin = character.Read<Translation>().Value;
                if (math.lengthsq(origin - spawnPos) > radius * radius) continue;
                set ??= new HashSet<ulong>();
                set.Add(user.PlatformId);
            }
            if (set != null)
                FreshFor[drop] = set;
        }
        finally
        {
            users.Dispose();
            query.Dispose();
        }
    }
}
