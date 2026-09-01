using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Satisvampory.Services;

internal struct ScoopResult
{
    public int PilesTaken;
    public int ItemsTaken;
    public int PilesSkippedFull;
    public int PilesSkippedCap;
    public int PilesSkippedExclude;
    public int PilesSkippedClaimed;
    public bool Busy;
    public string Summary;
}

internal static class ScoopReport
{
    internal const string ClientToken = "[GroundScoop]";
    const int MaxNamedTypes = 3;
    static readonly Dictionary<ulong, string> LastByPlayer = new();

    public static string ForClientFlash(string summary)
    {
        if (string.IsNullOrEmpty(summary)) return null;
        return ClientToken + " " + summary;
    }

    public static string Format(List<(string name, int amount)> items)
    {
        if (items == null || items.Count == 0)
            return null;

        items.Sort((a, b) =>
        {
            var byAmount = b.amount.CompareTo(a.amount);
            return byAmount != 0 ? byAmount : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });

        var show = items.Count < MaxNamedTypes ? items.Count : MaxNamedTypes;
        var bits = new string[show];
        for (var i = 0; i < show; i++)
            bits[i] = $"{items[i].name} x{items[i].amount}";

        var line = "Scoop: " + string.Join(", ", bits);
        if (items.Count > MaxNamedTypes)
            line += $" +{items.Count - MaxNamedTypes} more";
        return line;
    }

    public static void Remember(ulong platformId, string summary)
    {
        if (string.IsNullOrEmpty(summary))
            return;
        LastByPlayer[platformId] = summary;
    }

    public static bool TryGetLast(ulong platformId, out string summary)
        => LastByPlayer.TryGetValue(platformId, out summary) && !string.IsNullOrEmpty(summary);

    public static void TellClient(User user, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        FixedString512Bytes msg = message;
        ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, user, ref msg);
    }
}

/// <summary>
/// World-drop vacuum. Fail-closed claim: exclusive process-wide lock around
/// the entire drain of one pickup. ItemAmount is zeroed BEFORE TryAddItem so a
/// second Drain can never read a still-positive amount on an in-flight or
/// already-granted entity. Auto assigns each pile to the closest eligible
/// player before any TryAddItem. Settings always come from the scooping
/// player's Steam id, never a heart owner.
/// </summary>
internal static class ScoopService
{
    public const float DefaultRadius = 10f;
    public const float AutoIntervalSeconds = 0.75f;

    static readonly object Gate = new();
    static readonly HashSet<ulong> InProgress = new();

    /// <summary>Entities whose ItemPickup is currently zeroed for an in-flight add.</summary>
    static readonly HashSet<Entity> InFlight = new();

    /// <summary>
    /// Fully granted (leftover 0) pickups. NOT cleared between players or
    /// between .s and auto. Survives ghost frames after DestroyUtility.
    /// Pruned only when the entity no longer exists.
    /// </summary>
    static readonly Dictionary<Entity, int> FullyGranted = new();

    static int DrainSerial;

    public static ScoopResult ScoopNow(Entity character, User user, bool auto = false)
    {
        lock (Gate)
        {
            if (!InProgress.Add(user.PlatformId))
                return new ScoopResult { Busy = true };
            try
            {
                PruneGranted();
                return Drain(character, user, auto);
            }
            finally
            {
                InProgress.Remove(user.PlatformId);
            }
        }
    }

    public static IEnumerator AutoScoopLoop()
    {
        var wait = new UnityEngine.WaitForSeconds(AutoIntervalSeconds);
        while (true)
        {
            yield return wait;
            if (!Core.HasInitialized) continue;
            TickAuto();
        }
    }

    sealed class AutoPlayerCtx
    {
        public Entity Character;
        public User User;
        public Entity Inventory;
        public float3 Origin;
        public float RadiusSq;
        public ulong PlatformId;
        public CapMode Mode;
        public AutoFilter Filter;
        public Dictionary<int, int> Snapshot;
        public Dictionary<int, int> TakenThisPass;
        public HashSet<Entity> Assigned;
    }

    struct AutoCandidate
    {
        public int PlayerIndex;
        public float DistSq;
        public PrefabGUID Item;
        public int PileAmount;
    }

    static void TickAuto()
    {
        try
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();
            var users = query.ToEntityArray(Allocator.Temp);
            try
            {
                // Closest eligible player wins each pickup. Assignment happens
                // BEFORE any TryAddItem so ECS query order cannot decide a
                // contested pile. One grant per entity still holds.
                lock (Gate)
                {
                    PruneGranted();
                    DropTracker.Discover();

                    var players = CollectAutoPlayers(users);
                    if (players.Count == 0)
                        return;

                    AssignClosestWinners(players);

                    foreach (var player in players)
                    {
                        if (player.Assigned.Count == 0) continue;
                        if (InProgress.Contains(player.PlatformId)) continue;
                        InProgress.Add(player.PlatformId);
                        try
                        {
                            Drain(player.Character, player.User, auto: true, player.Assigned);
                        }
                        finally
                        {
                            InProgress.Remove(player.PlatformId);
                        }
                    }
                }
            }
            finally
            {
                users.Dispose();
                query.Dispose();
            }
        }
        catch (Exception e)
        {
            Core.LogException(e);
        }
    }

    static List<AutoPlayerCtx> CollectAutoPlayers(NativeArray<Entity> users)
    {
        var players = new List<AutoPlayerCtx>();
        foreach (var userEntity in users)
        {
            if (!Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>()) continue;
            var user = userEntity.Read<User>();
            if (!user.IsConnected) continue;
            if (!Core.PlayerSettings.IsAutoEnabled(user.PlatformId)) continue;
            if (InProgress.Contains(user.PlatformId)) continue;
            var character = user.LocalCharacter.GetEntityOnServer();
            if (character == Entity.Null || !Core.EntityManager.Exists(character)) continue;
            if (!character.Has<Translation>()) continue;
            if (!InventoryCountService.TryGetPlayerInventory(character, out var inventory)) continue;

            var radius = Core.PlayerSettings.GetRadius(user.PlatformId);
            players.Add(new AutoPlayerCtx
            {
                Character = character,
                User = user,
                Inventory = inventory,
                Origin = character.Read<Translation>().Value,
                RadiusSq = radius * radius,
                PlatformId = user.PlatformId,
                Mode = Core.PlayerSettings.GetCapMode(user.PlatformId),
                Filter = Core.PlayerSettings.GetAutoFilter(user.PlatformId),
                Snapshot = new Dictionary<int, int>(),
                TakenThisPass = new Dictionary<int, int>(),
                Assigned = new HashSet<Entity>()
            });
        }
        return players;
    }

    static void AssignClosestWinners(List<AutoPlayerCtx> players)
    {
        var dropBuilder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(new(Il2CppType.Of<ItemPickup>(), ComponentType.AccessMode.ReadWrite))
            .AddAll(new(Il2CppType.Of<Translation>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<PlayerDeathContainer>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<Relic>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<CastleHeartConnection>(), ComponentType.AccessMode.ReadOnly));
        var dropQuery = Core.EntityManager.CreateEntityQuery(ref dropBuilder);
        dropBuilder.Dispose();
        var drops = dropQuery.ToEntityArray(Allocator.Temp);
        try
        {
            var byPickup = new Dictionary<Entity, List<AutoCandidate>>();
            foreach (var drop in drops)
            {
                if (!TryGetAutoPickup(drop, out var item, out var pileAmount, out var pos))
                    continue;

                List<AutoCandidate> cands = null;
                for (var i = 0; i < players.Count; i++)
                {
                    if (!IsAutoEligible(players[i], drop, item, pileAmount, pos, out var distSq))
                        continue;
                    cands ??= new List<AutoCandidate>();
                    cands.Add(new AutoCandidate
                    {
                        PlayerIndex = i,
                        DistSq = distSq,
                        Item = item,
                        PileAmount = pileAmount
                    });
                }
                if (cands != null)
                    byPickup[drop] = cands;
            }

            foreach (var kv in byPickup)
            {
                var drop = kv.Key;
                var cands = kv.Value;
                cands.Sort((a, b) =>
                {
                    var byDist = a.DistSq.CompareTo(b.DistSq);
                    if (byDist != 0) return byDist;
                    return players[a.PlayerIndex].PlatformId.CompareTo(players[b.PlayerIndex].PlatformId);
                });

                foreach (var cand in cands)
                {
                    var player = players[cand.PlayerIndex];
                    var remaining = RemainingBudget(
                        player.PlatformId, player.Character, player.User, cand.Item,
                        player.Mode, player.Snapshot, player.TakenThisPass);
                    if (remaining <= 0) continue;
                    if (!InventoryCanAcceptAtLeastOne(player.Inventory, cand.Item)) continue;

                    player.Assigned.Add(drop);
                    var want = cand.PileAmount < remaining ? cand.PileAmount : remaining;
                    player.TakenThisPass.TryGetValue(cand.Item.GuidHash, out var already);
                    player.TakenThisPass[cand.Item.GuidHash] = already + want;
                    if (cands.Count > 1)
                    {
                        Core.Log.LogInfo(
                            $"Scoop auto assign {player.User.CharacterName} ({player.PlatformId}): entity={Ent(drop)} distSq={cand.DistSq:0.##} beat {cands.Count - 1} other(s)");
                    }
                    break;
                }
            }
        }
        finally
        {
            drops.Dispose();
            dropQuery.Dispose();
        }
    }

    static bool TryGetAutoPickup(Entity drop, out PrefabGUID item, out int pileAmount, out float3 pos)
    {
        item = default;
        pileAmount = 0;
        pos = default;
        if (drop == Entity.Null || !Core.EntityManager.Exists(drop)) return false;
        if (IsAlreadyClaimed(drop)) return false;
        if (!IsEligibleWorldDrop(drop)) return false;
        if (!drop.Has<Translation>() || !drop.Has<ItemPickup>()) return false;
        if (DropTracker.IsPlayerDropped(drop)) return false;

        pos = drop.Read<Translation>().Value;
        var pickup = drop.Read<ItemPickup>();
        item = pickup.ItemId;
        pileAmount = pickup.ItemAmount;
        if (item.GuidHash == 0 || pileAmount <= 0) return false;
        if (IsForbiddenItem(item, drop)) return false;
        return true;
    }

    static bool IsAutoEligible(AutoPlayerCtx player, Entity drop, PrefabGUID item, int pileAmount, float3 pos, out float distSq)
    {
        distSq = math.lengthsq(pos - player.Origin);
        if (distSq > player.RadiusSq) return false;
        if (player.Filter == AutoFilter.Around && !DropTracker.IsFreshFor(drop, player.PlatformId))
            return false;
        if (Core.PlayerSettings.IsExcluded(player.PlatformId, item)) return false;
        var remaining = RemainingBudget(
            player.PlatformId, player.Character, player.User, item,
            player.Mode, player.Snapshot, player.TakenThisPass);
        if (remaining <= 0) return false;
        if (!InventoryCanAcceptAtLeastOne(player.Inventory, item)) return false;
        return pileAmount > 0;
    }

    static bool InventoryCanAcceptAtLeastOne(Entity inventory, PrefabGUID item)
    {
        if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
            return false;
        if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var buffer))
            return false;

        var maxStack = GetMaxStack(item);
        var hasEmpty = false;
        foreach (var entry in buffer)
        {
            if (entry.ItemType.GuidHash == 0)
            {
                hasEmpty = true;
                continue;
            }
            if (!entry.ItemType.Equals(item)) continue;
            var max = entry.MaxAmountOverride > 0 ? entry.MaxAmountOverride : maxStack;
            if (max <= 0)
                return true;
            if (entry.Amount < max)
                return true;
        }
        return hasEmpty;
    }

    static int GetMaxStack(PrefabGUID item)
    {
        if (Core.PrefabCollectionSystem != null &&
            Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab) &&
            prefab.Has<ItemData>())
        {
            var data = prefab.Read<ItemData>();
            return data.MaxAmount;
        }
        return 0;
    }

    static ScoopResult Drain(Entity character, User user, bool auto, HashSet<Entity> assignedPickups = null)
    {
        var result = new ScoopResult();
        if (character == Entity.Null || !Core.EntityManager.Exists(character))
            return result;
        if (!character.Has<Translation>())
            return result;
        if (!InventoryCountService.TryGetPlayerInventory(character, out var inventory))
            return result;

        DropTracker.Discover();

        var platformId = user.PlatformId;
        var radius = Core.PlayerSettings.GetRadius(platformId);
        var mode = Core.PlayerSettings.GetCapMode(platformId);
        var autoFilter = Core.PlayerSettings.GetAutoFilter(platformId);
        var origin = character.Read<Translation>().Value;
        var radiusSq = radius * radius;

        var snapshot = new Dictionary<int, int>();
        var takenThisPass = new Dictionary<int, int>();
        var takenNames = new Dictionary<int, string>();
        var seenThisDrain = new HashSet<Entity>();

        var builder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(new(Il2CppType.Of<ItemPickup>(), ComponentType.AccessMode.ReadWrite))
            .AddAll(new(Il2CppType.Of<Translation>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<PlayerDeathContainer>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<Relic>(), ComponentType.AccessMode.ReadOnly))
            .AddNone(new(Il2CppType.Of<CastleHeartConnection>(), ComponentType.AccessMode.ReadOnly));
        var query = Core.EntityManager.CreateEntityQuery(ref builder);
        builder.Dispose();
        var drops = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var drop in drops)
            {
                if (drop == Entity.Null || !Core.EntityManager.Exists(drop)) continue;
                if (!seenThisDrain.Add(drop)) continue;
                if (assignedPickups != null && !assignedPickups.Contains(drop)) continue;

                if (IsAlreadyClaimed(drop))
                {
                    result.PilesSkippedClaimed++;
                    LogSkipClaimed(user, drop);
                    continue;
                }

                if (!IsEligibleWorldDrop(drop)) continue;
                if (!drop.Has<Translation>() || !drop.Has<ItemPickup>()) continue;

                if (auto)
                {
                    if (DropTracker.IsPlayerDropped(drop)) continue;
                    if (autoFilter == AutoFilter.Around && !DropTracker.IsFreshFor(drop, platformId))
                        continue;
                }

                var pos = drop.Read<Translation>().Value;
                var delta = pos - origin;
                if (math.lengthsq(delta) > radiusSq) continue;

                var pickup = drop.Read<ItemPickup>();
                var item = pickup.ItemId;
                var pileAmount = pickup.ItemAmount;
                if (item.GuidHash == 0 || pileAmount <= 0) continue;
                if (IsForbiddenItem(item, drop)) continue;

                if (Core.PlayerSettings.IsExcluded(platformId, item))
                {
                    result.PilesSkippedExclude++;
                    continue;
                }

                var remaining = RemainingBudget(platformId, character, user, item, mode, snapshot, takenThisPass);
                if (remaining <= 0)
                {
                    result.PilesSkippedCap++;
                    continue;
                }

                var want = pileAmount < remaining ? pileAmount : remaining;
                if (want <= 0) continue;

                var added = TryAddThenReduce(inventory, drop, item, pileAmount, want, user);
                if (added <= 0)
                {
                    result.PilesSkippedFull++;
                    continue;
                }

                if (!takenThisPass.TryGetValue(item.GuidHash, out var already))
                    already = 0;
                takenThisPass[item.GuidHash] = already + added;
                if (!takenNames.ContainsKey(item.GuidHash))
                    takenNames[item.GuidHash] = item.PrefabName();
                result.ItemsTaken += added;
                result.PilesTaken++;
            }
        }
        finally
        {
            drops.Dispose();
            query.Dispose();
        }

        if (result.ItemsTaken > 0)
        {
            var parts = new List<(string name, int amount)>();
            foreach (var kv in takenThisPass)
            {
                takenNames.TryGetValue(kv.Key, out var name);
                if (string.IsNullOrEmpty(name))
                    name = new PrefabGUID(kv.Key).PrefabName();
                parts.Add((name, kv.Value));
            }
            result.Summary = ScoopReport.Format(parts);
            ScoopReport.Remember(platformId, result.Summary);
            Core.Log.LogInfo(
                $"Scoop {user.CharacterName} ({platformId}): +{result.ItemsTaken} from {result.PilesTaken} pile(s) r={radius:0.#} mode={mode} skippedClaimed={result.PilesSkippedClaimed}");
            if (auto && Core.PlayerSettings.GetNotifyMode(platformId) == NotifyMode.On)
                ScoopReport.TellClient(user, ScoopReport.ForClientFlash(result.Summary));
        }

        return result;
    }

    static int RemainingBudget(ulong platformId, Entity character, User user, PrefabGUID item, CapMode mode,
        Dictionary<int, int> snapshot, Dictionary<int, int> takenThisPass)
    {
        var cap = Core.PlayerSettings.GetCap(platformId, item);
        if (cap == 0) return 0;
        if (cap < 0) return int.MaxValue;

        if (!snapshot.TryGetValue(item.GuidHash, out var counted))
        {
            counted = InventoryCountService.CountForCap(character, user, item, mode);
            if (counted < 0) counted = 0;
            snapshot[item.GuidHash] = counted;
        }

        takenThisPass.TryGetValue(item.GuidHash, out var already);
        var have = counted + already;
        var left = cap - have;
        return left > 0 ? left : 0;
    }

    /// <summary>
    /// Claim the pickup (ItemAmount = 0 + exclusive sets) BEFORE TryAddItem.
    /// If add fails, restore the original amount and unclaim. After a full
    /// grant, amount stays 0 and DestroyUtility runs. A second Drain cannot
    /// see a positive amount on this entity.
    /// </summary>
    static int TryAddThenReduce(Entity inventory, Entity drop, PrefabGUID item, int pileAmount, int want, User user)
    {
        if (want <= 0 || pileAmount <= 0) return 0;
        if (drop == Entity.Null || !Core.EntityManager.Exists(drop) || !drop.Has<ItemPickup>()) return 0;

        if (IsAlreadyClaimed(drop) || !InFlight.Add(drop))
        {
            LogSkipClaimed(user, drop, item, pileAmount);
            return 0;
        }

        var claimedAmount = 0;
        var zeroed = false;
        try
        {
            var live = drop.Read<ItemPickup>();
            if (!live.ItemId.Equals(item))
            {
                Core.Log.LogWarning($"Scoop abort claim: drop item changed before add of {item.PrefabName()} entity={Ent(drop)}.");
                return 0;
            }

            claimedAmount = live.ItemAmount;
            if (claimedAmount <= 0)
            {
                MarkFullyGranted(drop);
                LogSkipClaimed(user, drop, item, 0);
                return 0;
            }

            // Claim FIRST: zero so any racy query / later player sees 0 and skips.
            live.ItemAmount = 0;
            drop.Write(live);
            zeroed = true;
            MarkFullyGranted(drop);

            var before = Core.ServerGameManager.GetInventoryItemCount(inventory, item);
            if (before < 0) before = 0;

            AddItemSettings settings = default;
            settings.EntityManager = Core.EntityManager;
            settings.ItemDataMap = Core.ServerGameManager.ItemLookupMap;
            settings.DropRemainder = false;
            settings.EquipIfPossible = false;
            settings.OnlyFillEmptySlots = false;

            var take = want < claimedAmount ? want : claimedAmount;
            InventoryUtilitiesServer.TryAddItem(settings, inventory, item, take);
            var after = Core.ServerGameManager.GetInventoryItemCount(inventory, item);
            if (after < 0) after = before;

            var added = after - before;
            if (added <= 0)
            {
                RestoreAmount(drop, item, claimedAmount);
                UnmarkFullyGranted(drop);
                return 0;
            }
            if (added > claimedAmount)
                added = claimedAmount;
            if (added > take)
                added = take;

            var leftover = claimedAmount - added;
            var itemName = item.PrefabName() ?? item.LookupName();
            var destroyed = false;

            if (leftover <= 0)
            {
                // Stay FullyGranted. Amount already 0. Destroy the ghost if we can.
                destroyed = TryDestroyPickup(drop, user, itemName, item, added);
                LogGrant(user, item, itemName, drop, added, leftover: 0, destroyed);
                return added;
            }

            // Partial take: leftover is a real remaining pile. Unclaim so another
            // player (or a later pass) can take the rest — that is not a clone.
            if (!RestoreAmount(drop, item, leftover))
            {
                // Could not write leftover back; keep granted so nobody clones
                // the missing remainder. Fail closed: leftover stays invisible.
                Core.Log.LogWarning(
                    $"Scoop {user.CharacterName} ({user.PlatformId}): granted {added}x {itemName} ({item.GuidHash}) entity={Ent(drop)} leftover={leftover} destroyed=no; leftover write failed, keeping claim");
                return added;
            }

            UnmarkFullyGranted(drop);
            LogGrant(user, item, itemName, drop, added, leftover, destroyed: false);
            return added;
        }
        catch (Exception e)
        {
            if (zeroed && claimedAmount > 0 && !FullyGranted.ContainsKey(drop))
                RestoreAmount(drop, item, claimedAmount);
            Core.Log.LogWarning($"Scoop claim/add failed entity={Ent(drop)}: {e.Message}");
            return 0;
        }
        finally
        {
            InFlight.Remove(drop);
        }
    }

    static bool TryDestroyPickup(Entity drop, User user, string itemName, PrefabGUID item, int added)
    {
        if (drop == Entity.Null || !Core.EntityManager.Exists(drop))
            return true;
        try
        {
            // EntityManager.DestroyEntity does not replicate ItemPickup despawn;
            // clients keep an F-to-pickup ghost. Networked destroy via TryRemoveBuff.
            DestroyUtility.Destroy(Core.EntityManager, drop, DestroyDebugReason.TryRemoveBuff);
            DropTracker.Forget(drop);
            return true;
        }
        catch (Exception e)
        {
            Core.Log.LogWarning(
                $"Scoop {user.CharacterName} ({user.PlatformId}): granted {added}x {itemName} ({item.GuidHash}) entity={Ent(drop)} leftover=0 destroyed=no ({e.Message}); amount already 0 so no clone.");
            return false;
        }
    }

    static bool RestoreAmount(Entity drop, PrefabGUID item, int amount)
    {
        if (drop == Entity.Null || !Core.EntityManager.Exists(drop) || !drop.Has<ItemPickup>())
            return false;
        var live = drop.Read<ItemPickup>();
        if (!live.ItemId.Equals(item))
            return false;
        live.ItemAmount = amount;
        drop.Write(live);
        return true;
    }

    static bool IsAlreadyClaimed(Entity drop)
        => InFlight.Contains(drop) || FullyGranted.ContainsKey(drop);

    static void MarkFullyGranted(Entity drop)
    {
        DrainSerial++;
        FullyGranted[drop] = DrainSerial;
    }

    static void UnmarkFullyGranted(Entity drop)
        => FullyGranted.Remove(drop);

    static void PruneGranted()
    {
        if (FullyGranted.Count == 0) return;
        List<Entity> dead = null;
        foreach (var kv in FullyGranted)
        {
            if (kv.Key == Entity.Null || !Core.EntityManager.Exists(kv.Key))
            {
                dead ??= new List<Entity>();
                dead.Add(kv.Key);
            }
        }
        if (dead == null) return;
        foreach (var e in dead)
            FullyGranted.Remove(e);
    }

    static string Ent(Entity e) => $"{e.Index}:{e.Version}";

    static void LogSkipClaimed(User user, Entity drop, PrefabGUID item = default, int amount = 0)
    {
        var itemName = "?";
        var guid = 0;
        if (item.GuidHash != 0)
        {
            itemName = item.PrefabName() ?? item.LookupName();
            guid = item.GuidHash;
        }
        else if (drop != Entity.Null && Core.EntityManager.Exists(drop) && drop.Has<ItemPickup>())
        {
            var p = drop.Read<ItemPickup>();
            itemName = p.ItemId.PrefabName() ?? p.ItemId.LookupName();
            guid = p.ItemId.GuidHash;
            amount = p.ItemAmount;
        }
        Core.Log.LogInfo(
            $"Scoop {user.CharacterName} ({user.PlatformId}): skippedBecauseAlreadyClaimed item={itemName} ({guid}) entity={Ent(drop)} amount={amount}");
    }

    static void LogGrant(User user, PrefabGUID item, string itemName, Entity drop, int granted, int leftover, bool destroyed)
    {
        Core.Log.LogInfo(
            $"Scoop {user.CharacterName} ({user.PlatformId}): granted {granted}x {itemName} ({item.GuidHash}) entity={Ent(drop)} leftover={leftover} destroyed={(destroyed ? "yes" : "no")}");
    }

    static bool IsEligibleWorldDrop(Entity drop)
    {
        if (drop.Has<PlayerDeathContainer>()) return false;
        if (drop.Has<DeathContainerMapIcon>()) return false;
        if (drop.Has<Relic>()) return false;
        if (drop.Has<CastleHeartConnection>()) return false;
        if (drop.Has<Refinementstation>()) return false;
        if (drop.Has<UnitSpawnerstation>()) return false;
        if (drop.Has<ResearchStation>()) return false;
        if (drop.Has<InventoryOwner>() && drop.Has<NameableInteractable>()) return false;
        return drop.Has<ItemPickup>();
    }

    static bool IsForbiddenItem(PrefabGUID item, Entity drop)
    {
        if (drop.Has<Relic>()) return true;
        if (Core.PrefabCollectionSystem != null &&
            Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(item, out var prefab))
        {
            if (prefab.Has<Relic>()) return true;
            if (prefab.Has<ItemData>())
            {
                var data = prefab.Read<ItemData>();
                if (data.ItemType == ItemType.VBloodEssence) return false;
            }
        }

        var lookup = item.LookupName() ?? string.Empty;
        if (lookup.IndexOf("SoulShard", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (lookup.IndexOf("Item_Building_Relic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (lookup.IndexOf("Relic_", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
