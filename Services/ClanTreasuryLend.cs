using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory.Services
{
    /// <summary>
    /// Server-side LEND + same-plot self-sort. NEVER dump 16 of every item.
    /// Only MOVE: (1) standing heart UpgradeCosts onto treasury dest,
    /// (2) allShared starter kit into the first NON-overflow dest (chest recipe first) with
    ///     spend-refill until treasury; manual empty of a seeded chest opts that chest
    ///     NetworkId out (no refill so unbuild works); new chest NetworkId first-fills again,
    /// (3) occupied treasury plots: covering buffer so the standing player can place 3 of
    ///     whichever unlocked castle blueprint is hungriest per material (1 if clan takeable
    ///     after reserve cannot cover 3). Blood essences excluded. Named last-resort.
    /// (4) heart fuel seed / auto-feed into heart fuel slots (not a chest).
    /// No dest chest = fuel only. Kit OFF when destMode==treasury.
    /// Kit seed/opt-out is dest-chest NetworkId only (n{net}); ignore legacy StarterKitSeeded t{plot}.
    /// Heart fuel seed/opt-out is heart NetworkId only (n{net}); t{plot} must not stick to a replacement heart.
    /// Skip raided (ActiveEvent &gt;= Attacked). Return unused ledgered kit/upgrade leftovers to source
    /// chest NetworkId. Do not return heart fuel seed. IntervalSeconds = 5. Chat push off.
    /// Clan conveyors must not drain overflow on occupied allShared plots (kit park).
    /// </summary>
    internal static class ClanTreasuryLend
    {
        const float IntervalSeconds = 5.0f;
        // ItemGroupService GroupWood "Plank" / PrefabNames 3719afd2-979c-4881-9917-d079a701443c
        internal const int PlankHash = -1017402979;
        // PrefabNames 1e3ef569-8fb6-480c-b78f-a7f80c5c408c "Stone Brick"
        internal const int StoneBrickHash = 1788016417;
        // PrefabNames 58ef5c2c-ee41-4e62-8c67-7a224272e8cb "Copper Ingot"
        internal const int CopperIngotHash = -1237019921;
        // PrefabNames 37e872e1-4aa1-4f0a-8e2e-a67883b5a645 "Blood Essence" (heart fuel)
        internal const int BloodEssenceHash = 862477668;

        // Greater Blood Essence (heart upgrade / throne). Vanilla treasury accepts GBE in chests.
        internal const int GreaterBloodEssenceHash = 271594022;
        // ItemGroupService GroupGems "Gem Dust"
        internal const int GemDustHash = 820932258;
        // ClanTreasuryShare IronIngotHash / Item_Ingredient_Mineral_IronBar
        internal const int IronIngotHash = -1750550553;
        // PrefabNames 291c32e8-d894-4ff2-8715-51ed602caa0c "Stone" (gatherable rock; GroundScoop / HUD)
        internal const int StoneHash = -1531666018;
        const int KitPlank = 288;
        const int KitStoneBrick = 456;
        const int KitCopper = 24;
        const int KitIron = 24;
        const int KitGemDust = 32;
        const int KitStone = 192;
        const int KitChestPlank = 72;
        const int KitChestCopper = 24;
        const int HeartFuelStack = 500;

        /// <summary>
        /// Chest identity: NetworkId.Index of the stash if present, else inventory
        /// Entity Index+Version, always with source territoryId.
        /// </summary>
        struct SourceChest : IEquatable<SourceChest>
        {
            public int TerritoryId;
            public NetworkId NetId;
            public int InvIndex;
            public int InvVersion;

            public bool HasNet;

            public bool Equals(SourceChest other)
            {
                if (TerritoryId != other.TerritoryId)
                    return false;
                if (HasNet && other.HasNet)
                    return NetId.Equals(other.NetId);
                return InvIndex == other.InvIndex && InvVersion == other.InvVersion;
            }

            public override bool Equals(object obj) => obj is SourceChest other && Equals(other);

            public override int GetHashCode()
            {
                if (HasNet)
                    return HashCode.Combine(TerritoryId, NetId.GetHashCode());
                return HashCode.Combine(TerritoryId, InvIndex, InvVersion);
            }

            public override string ToString() =>
                HasNet
                    ? $"net={NetId} plot={TerritoryId} inv={InvIndex}:{InvVersion}"
                    : $"inv={InvIndex}:{InvVersion} plot={TerritoryId}";
        }

        struct LedgerKey : IEquatable<LedgerKey>
        {
            public int DestPlot;
            public int Guid;
            public SourceChest Source;

            public bool Equals(LedgerKey other) =>
                DestPlot == other.DestPlot && Guid == other.Guid && Source.Equals(other.Source);

            public override bool Equals(object obj) => obj is LedgerKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(DestPlot, Guid, Source);
        }

        struct LastMove
        {
            public SourceChest Source;
            public int Amount;
        }

        static readonly Dictionary<LedgerKey, int> ledger = new();
        static readonly Dictionary<long, LastMove> lastMove = new();
        static readonly HashSet<long> pendingVerify = new();
        static readonly HashSet<long> stickyFailed = new();
        static readonly HashSet<int> warnedNoDest = new();
        static readonly HashSet<int> loggedDestMode = new();
        static readonly HashSet<long> loggedFirstSuccess = new();
        static readonly HashSet<int> previouslyOccupied = new();
        static readonly HashSet<int> stickyFailedAllSharedPlots = new();
        static readonly HashSet<int> loggedAllSharedPlotSkip = new();
        static readonly HashSet<int> loggedUpgradeCosts = new();
        static readonly HashSet<long> loggedNamedBorrow = new();
        static readonly Dictionary<int, Dictionary<int, int>> heartUpgradeCache = new();
        static readonly Dictionary<int, byte> heartUpgradeCacheLevel = new();
        static readonly HashSet<int> loggedHeartFuel = new();
        static readonly HashSet<int> loggedCovering = new();
        static readonly HashSet<int> occupiedPlots = new();
        static readonly HashSet<int> holdKitOverflowPlots = new();
        static readonly Dictionary<int, List<Entity>> workstationsByPlot = new();
        static List<(int blueprint, bool start, List<(int guid, int amount)> costs)> blueprintCosts;
        static Dictionary<int, int> coveringCastle1x;
        static bool loggedEmptySkip;
        const int BuildCoverCopies = 3;

        internal static bool HoldKitOverflow(int plot) =>
            plot >= 0 && holdKitOverflowPlots.Contains(plot);

        internal static Dictionary<int, int> DebugCovering1x(int plot) =>
            GetCovering1x(plot);

        internal static void DebugTick() => Tick();

        internal static int DebugUnstick(int plot)
        {
            if (plot < 0)
            {
                var n = stickyFailed.Count + stickyFailedAllSharedPlots.Count;
                stickyFailed.Clear();
                stickyFailedAllSharedPlots.Clear();
                return n;
            }
            var removed = 0;
            var drop = new List<long>();
            foreach (var k in stickyFailed)
            {
                if ((int)(k >> 32) == plot)
                    drop.Add(k);
            }
            for (var i = 0; i < drop.Count; i++)
            {
                stickyFailed.Remove(drop[i]);
                removed++;
            }
            if (stickyFailedAllSharedPlots.Remove(plot))
                removed++;
            return removed;
        }

        internal static bool DebugSticky(int plot, int guid) =>
            stickyFailed.Contains(FailKey(plot, guid));

        /// <summary>
        /// Dry-run covering for one item as if destPlot is occupied. apply=true actually transfers.
        /// </summary>
        internal static string DebugSimulate(int destPlot, PrefabGUID type, bool apply)
        {
            var sb = new StringBuilder();
            sb.Append("{\"plot\":").Append(destPlot)
                .Append(",\"guid\":").Append(type.GuidHash)
                .Append(",\"name\":\"").Append(EscSim(StashRouting.ItemLabel(type))).Append('"');

            if (destPlot < 0)
            {
                sb.Append(",\"error\":\"no plot\"}");
                return sb.ToString();
            }

            var destInvs = GetDestInventories(destPlot, out var destMode);
            var park = VanillaVisibleDests(destPlot, destInvs);
            if (park.Count == 0)
                park = destInvs;
            var covering1 = GetBuildCovering1x(destPlot);
            covering1.TryGetValue(type.GuidHash, out var t1);
            var t3 = t1 * BuildCoverCopies;
            var local = CountIn(park, type);
            var sticky = stickyFailed.Contains(FailKey(destPlot, type.GuidHash));
            var room = DestHasRoomFor(park, type);
            var ranked = StashRouting.OrderDepositInventories(destPlot, park, type);
            var clanIds = Core.TerritoryService.GetLogisticsTerritoryIds(destPlot);
            var occupiedLive = new List<int>();
            foreach (var id in occupiedPlots)
            {
                if (clanIds == null)
                    continue;
                var inClan = false;
                for (var c = 0; c < clanIds.Count; c++)
                {
                    if (clanIds[c] == id)
                    {
                        inClan = true;
                        break;
                    }
                }
                if (inClan)
                    occupiedLive.Add(id);
            }
            // Sim pretends dest is occupied even if nobody is standing there.
            var occupiedSim = new List<int>(occupiedLive);
            if (!occupiedSim.Contains(destPlot))
                occupiedSim.Add(destPlot);

            sb.Append(",\"destMode\":\"").Append(EscSim(destMode)).Append('"')
                .Append(",\"park\":").Append(park.Count)
                .Append(",\"destInvs\":").Append(destInvs.Count)
                .Append(",\"ranked\":").Append(ranked.Count)
                .Append(",\"room\":").Append(room ? "true" : "false")
                .Append(",\"sticky\":").Append(sticky ? "true" : "false")
                .Append(",\"covering1\":").Append(t1)
                .Append(",\"covering3\":").Append(t3)
                .Append(",\"local\":").Append(local)
                .Append(",\"need1\":").Append(Math.Max(0, t1 - local))
                .Append(",\"need3\":").Append(Math.Max(0, t3 - local))
                .Append(",\"occupiedLive\":[");
            for (var i = 0; i < occupiedLive.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(occupiedLive[i]);
            }
            sb.Append("],\"occupiedSim\":[");
            for (var i = 0; i < occupiedSim.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(occupiedSim[i]);
            }
            sb.Append(']');

            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(destPlot, out var destOwner);
            sb.Append(",\"dests\":[");
            var firstDest = true;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
            {
                if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                    continue;
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                var inPark = IsDest(park, inv);
                if (!inPark && !IsDest(destInvs, inv))
                    continue;
                var has = StashRouting.InventoryHasItem(inv, type);
                var rank = StashRouting.RankDeposit(stash, type, destOwner, has);
                var inRanked = false;
                for (var r = 0; r < ranked.Count; r++)
                {
                    if (ranked[r].Equals(inv))
                    {
                        inRanked = true;
                        break;
                    }
                }
                var stacks = 0;
                var slots = 0;
                var empty = 0;
                if (Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                {
                    slots = buf.Length;
                    for (var s = 0; s < buf.Length; s++)
                    {
                        if (buf[s].ItemType.GuidHash == 0 || buf[s].Amount <= 0)
                            empty++;
                        else
                            stacks++;
                    }
                }
                if (!firstDest) sb.Append(',');
                firstDest = false;
                sb.Append("{\"name\":\"").Append(EscSim(StashRouting.RawName(stash))).Append('"')
                    .Append(",\"class\":").Append(rank.Class)
                    .Append(",\"label\":\"").Append(EscSim(rank.Label)).Append('"')
                    .Append(",\"usable\":").Append(rank.IsDepositUsable ? "true" : "false")
                    .Append(",\"ranked\":").Append(inRanked ? "true" : "false")
                    .Append(",\"park\":").Append(inPark ? "true" : "false")
                    .Append(",\"overflow\":").Append(StashRouting.IsOverflowDestName(StashRouting.RawName(stash)) ? "true" : "false")
                    .Append(",\"hasItem\":").Append(has ? "true" : "false")
                    .Append(",\"stacks\":").Append(stacks)
                    .Append(",\"slots\":").Append(slots)
                    .Append(",\"empty\":").Append(empty)
                    .Append('}');
            }
            sb.Append(']');

            var need = Math.Max(0, t1 - local);
            if (need <= 0)
                need = Math.Max(0, t3 - local);
            if (need <= 0 && t1 <= 0)
                need = 40;

            var would = 0;
            var moved = 0;
            sb.Append(",\"sources\":[");
            var firstSrc = true;
            if (clanIds != null)
            {
                var sgm = Core.ServerGameManager;
                for (var pass = 0; pass < 3; pass++)
                {
                    foreach (var srcPlot in clanIds)
                    {
                        if (srcPlot == destPlot)
                            continue;
                        var occupiedSrc = occupiedSim.Contains(srcPlot);
                        var namedBypass = pass == 2;
                        foreach (var stash in Core.Stash.GetStashesOnTerritory(srcPlot))
                        {
                            if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                                continue;
                            var sourcePass = StashRouting.SourcePass(stash);
                            if (!StashRouting.TryGetExternalInventory(stash, out var inventory))
                                continue;
                            var have = sgm.GetInventoryItemCount(inventory, type);
                            if (have <= 0)
                                continue;
                            if (sourcePass != pass)
                                continue;

                            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(srcPlot, out var sourceOwnerId);
                            var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                            var skip = "";
                            if (occupiedSrc && !namedBypass)
                                skip = "occupied";
                            else if (sourcePass < 0)
                                skip = "ns";
                            else if (IsDest(park, inventory) || IsDest(destInvs, inventory))
                                skip = "is-dest";
                            var availBypass = have;
                            var availHonor = reserve > 0 ? have - reserve : have;
                            if (availHonor < 0)
                                availHonor = 0;
                            var take = 0;
                            if (skip.Length == 0)
                            {
                                take = availBypass < need ? availBypass : need;
                                if (take < 0)
                                    take = 0;
                                would += take;
                            }

                            var got = 0;
                            var destRejected = false;
                            if (apply && skip.Length == 0 && take > 0 && !sticky)
                            {
                                got = MoveIntoDest(destPlot, inventory, park, type, take, out destRejected);
                                moved += got;
                                if (got > 0)
                                    need -= got;
                            }

                            if (!firstSrc) sb.Append(',');
                            firstSrc = false;
                            sb.Append("{\"plot\":").Append(srcPlot)
                                .Append(",\"name\":\"").Append(EscSim(StashRouting.RawName(stash))).Append('"')
                                .Append(",\"pass\":").Append(sourcePass)
                                .Append(",\"have\":").Append(have)
                                .Append(",\"reserve\":").Append(reserve)
                                .Append(",\"availBypass\":").Append(availBypass)
                                .Append(",\"availHonor\":").Append(availHonor)
                                .Append(",\"occupied\":").Append(occupiedSrc ? "true" : "false")
                                .Append(",\"namedBypass\":").Append(namedBypass ? "true" : "false")
                                .Append(",\"skip\":\"").Append(EscSim(skip)).Append('"')
                                .Append(",\"take\":").Append(take)
                                .Append(",\"got\":").Append(got)
                                .Append(",\"destRejected\":").Append(destRejected ? "true" : "false")
                                .Append('}');
                        }
                    }
                }
            }
            sb.Append("],\"wouldMove\":").Append(would)
                .Append(",\"apply\":").Append(apply ? "true" : "false")
                .Append(",\"moved\":").Append(moved)
                .Append('}');
            return sb.ToString();
        }

        static string EscSim(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        internal static void AddWorkstation(Entity station)
        {
            var plot = Core.TerritoryService.GetTerritoryId(station);
            if (plot < 0)
                return;
            if (!workstationsByPlot.TryGetValue(plot, out var list))
            {
                list = new List<Entity>();
                workstationsByPlot[plot] = list;
            }
            if (!list.Contains(station))
                list.Add(station);
        }

        internal static void RemoveWorkstation(Entity station)
        {
            var plot = Core.TerritoryService.GetTerritoryId(station);
            if (plot < 0)
                return;
            if (!workstationsByPlot.TryGetValue(plot, out var list))
                return;
            list.Remove(station);
        }

        static long FailKey(int plot, int guid) => ((long)plot << 32) | (uint)guid;

        internal static IEnumerator Loop()
        {
            var wait = new WaitForSeconds(IntervalSeconds);
            while (true)
            {
                yield return wait;
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                }
            }
        }

        static bool IsStarterKitGuid(int guid) =>
            guid == PlankHash || guid == StoneBrickHash || guid == CopperIngotHash
            || guid == IronIngotHash || guid == GemDustHash || guid == StoneHash;

        static bool IsRaided(Entity heart)
        {
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart) || !heart.Has<CastleHeart>())
                return false;
            return heart.Read<CastleHeart>().ActiveEvent >= CastleHeartEvent.Attacked;
        }

        static Dictionary<int, int> StarterKitTargets()
        {
            return new Dictionary<int, int>
            {
                [PlankHash] = KitPlank,
                [StoneBrickHash] = KitStoneBrick,
                [CopperIngotHash] = KitCopper,
                [IronIngotHash] = KitIron,
                [GemDustHash] = KitGemDust,
                [StoneHash] = KitStone,
            };
        }

        static int ChestTarget(int guid, Dictionary<int, int> upgrade, Dictionary<int, int> kit)
        {
            var t = 0;
            if (upgrade != null && upgrade.TryGetValue(guid, out var u) && u > t)
                t = u;
            if (kit != null && kit.TryGetValue(guid, out var k) && k > t)
                t = k;
            return t;
        }


        static bool IsTreasuryFloor(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash))
                return false;
            if (ClanTreasuryShare.IsTreasuryLinked(stash))
                return true;
            if (stash.Has<CastleWorkstation>())
                return stash.Read<CastleWorkstation>().MatchingFloorType == CastleFloorTypes.Treasury;
            return false;
        }

        static bool IsTreasuryRejectedBlood(PrefabGUID item)
        {
            var g = item.GuidHash;
            if (g == 0)
                return false;
            // Regular Blood Essence is heart fuel (slots, not the build menu). GBE/primal
            // are castle costs and vanilla treasury chests accept them.
            return g == BloodEssenceHash;
        }

        static string FirstDestName(int plot, List<Entity> destInvs)
        {
            if (destInvs == null)
                return "";
            foreach (var dest in destInvs)
            {
                foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
                {
                    if (StashRouting.TryGetExternalInventory(stash, out var inv) && inv.Equals(dest))
                        return StashRouting.RawName(stash);
                }
            }
            return "";
        }

        static List<Entity> GetBloodAcceptingDests(int plot, PrefabGUID item)
        {
            var list = new List<Entity>();
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var ownerId);
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (stash.Has<Refinementstation>())
                    continue;
                var name = StashRouting.RawName(stash);
                if (StashRouting.IsNoShareName(name) || StashRouting.IsSpecialName(name) || StashRouting.IsConveyorName(name))
                    continue;
                if (IsTreasuryFloor(stash))
                    continue;
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                var generic = StashRouting.IsGenericName(name);
                var named = item.GuidHash != 0 && (StashRouting.ExactItemNameMatch(name, item, out _)
                    || StashRouting.CategoryMatch(name, item, ownerId, out _));
                if (!generic && !named)
                    continue;
                list.Add(inv);
            }
            return StashRouting.OrderDepositInventories(plot, list, item);
        }

        static List<Entity> DestInvsForUpgradeItem(int plot, PrefabGUID type, List<Entity> defaultDests)
        {
            if (!IsTreasuryRejectedBlood(type))
                return defaultDests ?? new List<Entity>();
            var blood = GetBloodAcceptingDests(plot, type);
            if (blood.Count > 0)
                return blood;
            return new List<Entity>();
        }

        static int ChestTargetForPlot(int plot, int guid)
        {
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            Dictionary<int, int> upgrade = null;
            if (heart != Entity.Null && !IsRaided(heart))
            {
                if (GetDestInventories(plot, out _).Count > 0)
                    upgrade = GetHeartUpgradeCosts(plot, heart);
                return ChestTarget(guid, upgrade, null);
            }
            return ChestTarget(guid, upgrade, null);
        }


        static bool DestHasEmptySlots(List<Entity> destInvs)
        {
            var sgm = Core.ServerGameManager;
            if (destInvs == null)
                return false;
            for (var i = 0; i < destInvs.Count; i++)
            {
                var inv = destInvs[i];
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                    continue;
                for (var s = 0; s < buf.Length; s++)
                {
                    var slot = buf[s];
                    if (slot.Amount <= 0 || slot.ItemType.GuidHash == 0)
                        return true;
                }
            }
            return false;
        }

        static bool DestHasRoomFor(List<Entity> destInvs, PrefabGUID type)
        {
            if (DestHasEmptySlots(destInvs))
                return true;
            var sgm = Core.ServerGameManager;
            if (destInvs == null)
                return false;
            for (var i = 0; i < destInvs.Count; i++)
            {
                var inv = destInvs[i];
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                    continue;
                for (var s = 0; s < buf.Length; s++)
                {
                    var slot = buf[s];
                    if (!slot.ItemType.Equals(type) || slot.Amount <= 0)
                        continue;
                    if (slot.MaxAmountOverride > 0 && slot.Amount < slot.MaxAmountOverride)
                        return true;
                }
            }
            return false;
        }

        static bool DestHasAnyKit(List<Entity> destInvs)
        {
            return CountIn(destInvs, new PrefabGUID(PlankHash)) > 0
                || CountIn(destInvs, new PrefabGUID(StoneBrickHash)) > 0
                || CountIn(destInvs, new PrefabGUID(CopperIngotHash)) > 0
                || CountIn(destInvs, new PrefabGUID(IronIngotHash)) > 0
                || CountIn(destInvs, new PrefabGUID(GemDustHash)) > 0
                || CountIn(destInvs, new PrefabGUID(StoneHash)) > 0;
        }

        static int GetItemMaxStack(PrefabGUID type)
        {
            try
            {
                if (Core.GameDataSystem.ItemHashLookupMap.TryGetValue(type, out var itemData))
                {
                    var max = itemData.MaxAmount;
                    if (max > 0)
                        return max;
                }
            }
            catch { }
            try
            {
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(type, out var prefab)
                    && prefab.Has<ItemData>() && Core.EntityManager.Exists(prefab))
                {
                    var max = prefab.Read<ItemData>().MaxAmount;
                    if (max > 0)
                        return max;
                }
            }
            catch { }
            return 0;
        }

        static int KitBypassTarget(int guid, int locked)
        {
            var stack = GetItemMaxStack(new PrefabGUID(guid));
            // MaxStack <= 1: keep locked leftover-bypass (kit items are stackable).
            if (stack <= 1)
                return locked;
            return stack > locked ? stack : locked;
        }

        static int LendStarterKitOnce(int destPlot, List<Entity> kitDest, IReadOnlyList<int> clanIds, List<int> occupiedInClan, string destMode)
        {
            var movedAll = 0;
            // Chest-first leftover-bypass (named last-resort + raw stack count, never leftover-subtract).
            // Target is max(locked chest-first amount, one vanilla MaxStack). Dest-full stops.
            // Do not pull a second extra stack beyond that one fill. Remaining 216 plank honors leftover.
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, PlankHash, KitBypassTarget(PlankHash, KitChestPlank), ignoreLeftoverNamed: true);
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, CopperIngotHash, KitBypassTarget(CopperIngotHash, KitChestCopper), ignoreLeftoverNamed: true);
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, IronIngotHash, KitBypassTarget(IronIngotHash, KitIron), ignoreLeftoverNamed: true);
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, GemDustHash, KitBypassTarget(GemDustHash, KitGemDust), ignoreLeftoverNamed: true);
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, StoneBrickHash, KitBypassTarget(StoneBrickHash, KitStoneBrick), ignoreLeftoverNamed: true);
            movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, StoneHash, KitBypassTarget(StoneHash, KitStone), ignoreLeftoverNamed: true);
            if (DestHasEmptySlots(kitDest))
                movedAll += LendKitGuid(destPlot, kitDest, clanIds, occupiedInClan, destMode, PlankHash, KitPlank, ignoreLeftoverNamed: false);
            return movedAll;
        }

        static int LendKitGuid(int destPlot, List<Entity> kitDest, IReadOnlyList<int> clanIds, List<int> occupiedInClan, string destMode, int guid, int target, bool ignoreLeftoverNamed)
        {
            if (kitDest == null || kitDest.Count == 0 || guid == 0 || target <= 0)
                return 0;
            var type = new PrefabGUID(guid);
            var local = CountIn(kitDest, type);
            if (local >= target)
                return 0;
            if (!DestHasRoomFor(kitDest, type))
            {
                Core.Log.LogInfo($"[ClanTreasuryLend] kit destPlot={destPlot} guid={guid} moved=0 dest-full local={local} target={target} dest={destMode}");
                return 0;
            }
            var need = target - local;
            var fail = false;
            var leftoverBlocked = 0;
            var leftoverHave = 0;
            var leftoverReserve = 0;
            var moved = PullFromSources(destPlot, kitDest, type, need, target, clanIds, occupiedInClan, destMode,
                occupiedSpareOnly: false, allowNamed: true, allowSamePlot: false, ledgerMoves: true, ignoreLeftoverNamed, ref fail,
                ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
            if (!fail)
            {
                need = target - CountIn(kitDest, type);
                if (need > 0)
                    moved += PullFromSources(destPlot, kitDest, type, need, target, clanIds, occupiedInClan, destMode,
                        occupiedSpareOnly: true, allowNamed: true, allowSamePlot: false, ledgerMoves: true, ignoreLeftoverNamed, ref fail,
                        ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
            }
            if (moved <= 0)
            {
                if (fail)
                    Core.Log.LogInfo($"[ClanTreasuryLend] kit destPlot={destPlot} guid={guid} moved=0 dest-rejected-or-copy-fail local={CountIn(kitDest, type)} target={target} dest={destMode}");
                else if (leftoverBlocked > 0)
                    Core.Log.LogInfo($"[ClanTreasuryLend] kit destPlot={destPlot} guid={guid} moved=0 no-source local={CountIn(kitDest, type)} target={target} dest={destMode} leftover={leftoverReserve} have={leftoverHave} leftover-blocked={leftoverBlocked} named-bypass={ignoreLeftoverNamed}");
                else
                    Core.Log.LogInfo($"[ClanTreasuryLend] kit destPlot={destPlot} guid={guid} moved=0 no-source local={CountIn(kitDest, type)} target={target} dest={destMode}");
                return 0;
            }
            pendingVerify.Add(FailKey(destPlot, guid));
            return moved;
        }

        static bool InventoryIsOverflow(int plot, Entity inv)
        {
            if (inv == Entity.Null)
                return false;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (!StashRouting.TryGetExternalInventory(stash, out var found) || !found.Equals(inv))
                    continue;
                return StashRouting.IsOverflowDestName(StashRouting.RawName(stash));
            }
            return false;
        }

        static Entity StashForInventory(int plot, Entity inv)
        {
            if (inv == Entity.Null)
                return Entity.Null;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (StashRouting.TryGetExternalInventory(stash, out var found) && found.Equals(inv))
                    return stash;
            }
            return Entity.Null;
        }

        /// <summary>
        /// Chests the vanilla build menu can see: treasury-floor first, else unnamed/generic.
        /// Named dests (Wood Stone Bone, s#) are invisible to the 1.1 HUD.
        /// </summary>
        static List<Entity> VanillaVisibleDests(int destPlot, List<Entity> destInvs)
        {
            var treasury = new List<Entity>();
            var unnamed = new List<Entity>();
            if (destInvs == null)
                return treasury;
            foreach (var inv in destInvs)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (InventoryIsOverflow(destPlot, inv))
                    continue;
                var stash = StashForInventory(destPlot, inv);
                var name = StashRouting.RawName(stash);
                if (IsTreasuryFloor(stash) || ClanTreasuryShare.IsTreasuryLinked(stash))
                    treasury.Add(inv);
                else if (StashRouting.IsUnnamedOrGeneric(name))
                    unnamed.Add(inv);
            }
            return treasury.Count > 0 ? treasury : unnamed;
        }

        static List<Entity> NonOverflowDests(int destPlot, List<Entity> destInvs)
        {
            var list = new List<Entity>();
            if (destInvs == null)
                return list;
            foreach (var inv in destInvs)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (InventoryIsOverflow(destPlot, inv))
                    continue;
                list.Add(inv);
            }
            return list;
        }

        static List<Entity> FirstUsableKitDest(int destPlot, List<Entity> destInvs)
        {
            var list = new List<Entity>();
            var ordered = StashRouting.OrderDepositInventories(destPlot, destInvs, default);
            foreach (var inv in ordered)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (InventoryIsOverflow(destPlot, inv))
                    continue;
                var keys = KitChestKeys(destPlot, inv);
                if (AnyHeartKey(keys, Core.PlayerSettings.IsStarterKitChestOptOut))
                    continue;
                list.Add(inv);
                foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
                {
                    if (StashRouting.TryGetExternalInventory(stash, out var found) && found.Equals(inv))
                    {
                        var rank = StashRouting.RankDeposit(stash, default, 0, false);
                        StashRouting.LogDestPick(rank.Label, destPlot, default, StashRouting.RawName(stash), "kit-first-dest");
                        break;
                    }
                }
                return list;
            }
            return FirstDestOnly(destPlot, destInvs);
        }

        static List<Entity> FirstDestOnly(int destPlot, List<Entity> destInvs)
        {
            var list = new List<Entity>();
            var ordered = StashRouting.OrderDepositInventories(destPlot, destInvs, default);
            if (ordered.Count == 0)
                return list;
            Entity picked = Entity.Null;
            foreach (var inv in ordered)
            {
                if (InventoryIsOverflow(destPlot, inv))
                    continue;
                picked = inv;
                break;
            }
            if (picked == Entity.Null)
                picked = ordered[0];
            list.Add(picked);
            foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
            {
                if (StashRouting.TryGetExternalInventory(stash, out var inv) && inv.Equals(picked))
                {
                    var rank = StashRouting.RankDeposit(stash, default, 0, false);
                    StashRouting.LogDestPick(rank.Label, destPlot, default, StashRouting.RawName(stash), "kit-first-dest");
                    break;
                }
            }
            return list;
        }


        static void MarkStickyFail(int destPlot, long key, string destMode)
        {
            stickyFailed.Add(key);
            if (destMode == "allShared")
                stickyFailedAllSharedPlots.Add(destPlot);
        }

        static bool IsNamedStash(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return false;
            var name = stash.Read<NameableInteractable>().Name.ToString();
            return !string.IsNullOrWhiteSpace(name);
        }

        /// <summary>
        /// Treasury-floor or unnamed chests are dest-compatible and must not starve conveyors.
        /// Named chests (Kindred conveyor sources) are last-resort borrows only.
        /// </summary>
        static bool IsPreferredSourceStash(Entity stash)
        {
            if (ClanTreasuryShare.IsTreasuryLinked(stash))
                return true;
            return !IsNamedStash(stash);
        }

        static Dictionary<int, int> GetHeartUpgradeCosts(int destPlot, Entity heartEntity)
        {
            var empty = new Dictionary<int, int>();
            if (heartEntity == Entity.Null || !Core.EntityManager.Exists(heartEntity) || !heartEntity.Has<CastleHeart>())
                return empty;
            try
            {
                var ch = heartEntity.Read<CastleHeart>();
                if (heartUpgradeCache.TryGetValue(destPlot, out var cached)
                    && heartUpgradeCacheLevel.TryGetValue(destPlot, out var cachedLevel)
                    && cachedLevel == ch.Level)
                    return cached;

                var map = ParseHeartUpgradeCosts(ch);
                heartUpgradeCache[destPlot] = map;
                heartUpgradeCacheLevel[destPlot] = ch.Level;
                if (loggedUpgradeCosts.Add(destPlot))
                {
                    var parts = new List<string>();
                    foreach (var kv in map)
                        parts.Add($"{kv.Key}={kv.Value}");
                    var summary = parts.Count == 0 ? "(none)" : string.Join(",", parts);
                    Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heartLevel={ch.Level} UpgradeCosts {summary}");
                }
                return map;
            }
            catch (Exception e)
            {
                Core.Log.LogWarning($"[ClanTreasuryLend] destPlot={destPlot} UpgradeCosts unreadable: {e.Message}");
                return empty;
            }
        }

        static Dictionary<int, int> ParseHeartUpgradeCosts(CastleHeart ch)
        {
            var map = new Dictionary<int, int>();
            try
            {
                ref var levelData = ref ch.GetLevelData();
                AddUpgradeCosts(map, levelData.UpgradeCosts);
            }
            catch (Exception e)
            {
                Core.Log.LogWarning($"[ClanTreasuryLend] GetLevelData failed, trying blob: {e.Message}");
                TryParseUpgradeCostsFromBlob(ch, map);
            }
            return map;
        }

        static void TryParseUpgradeCostsFromBlob(CastleHeart ch, Dictionary<int, int> map)
        {
            try
            {
                if (!ch.Data.IsCreated)
                    return;
                ref var blob = ref ch.Data.Value;
                var levels = blob.Levels;
                var idx = ch.Level;
                if (idx < 0 || idx >= levels.Length)
                    return;
                ref var levelData = ref levels[idx];
                AddUpgradeCosts(map, levelData.UpgradeCosts);
            }
            catch (Exception e)
            {
                Core.Log.LogWarning($"[ClanTreasuryLend] blob UpgradeCosts failed: {e.Message}");
            }
        }

        static void AddUpgradeCosts(Dictionary<int, int> map, BlobArray<CastleHeartCost> costs)
        {
            var len = costs.Length;
            for (var i = 0; i < len; i++)
            {
                var cost = costs[i];
                var g = cost.ItemType.GuidHash;
                var a = cost.Amount;
                if (g == 0 || a <= 0)
                    continue;
                if (!map.TryGetValue(g, out var have) || a > have)
                    map[g] = a;
            }
        }

        static void Tick()
        {
            if (!Core.HasInitialized)
                return;

            var occupied = CollectOccupiedClanSharePlots(out var connectedPlayers);
            if (connectedPlayers == 0)
            {
                ReturnAllLedgers("empty-server");
                previouslyOccupied.Clear();
                pendingVerify.Clear();
                if (!loggedEmptySkip)
                {
                    Core.Log.LogInfo("[ClanTreasuryLend] empty server (0 connected players) -- zero lends, returned leftovers");
                    loggedEmptySkip = true;
                }
                return;
            }
            loggedEmptySkip = false;

            foreach (var plot in previouslyOccupied)
            {
                if (!occupied.Contains(plot))
                    ReturnPlot(plot, "unoccupied");
            }
            previouslyOccupied.Clear();
            occupiedPlots.Clear();
            holdKitOverflowPlots.Clear();
            foreach (var plot in occupied)
            {
                previouslyOccupied.Add(plot);
                occupiedPlots.Add(plot);
                GetDestInventories(plot, out var occupyMode);
                if (occupyMode == "allShared")
                    holdKitOverflowPlots.Add(plot);
            }

            CheckStickyRetain(occupied);

            foreach (var plot in occupied)
            {
                try
                {
                    LendToPlot(plot, occupied);
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                }
                try
                {
                    SelfSortPlot(plot);
                }
                catch (Exception e)
                {
                    Core.LogException(e);
                }
            }
        }

        static void CheckStickyRetain(HashSet<int> occupied)
        {
            if (pendingVerify.Count == 0)
                return;

            var keys = new List<long>(pendingVerify);
            pendingVerify.Clear();
            foreach (var key in keys)
            {
                if (stickyFailed.Contains(key))
                    continue;
                var destPlot = (int)(key >> 32);
                var guid = (int)(key & 0xFFFFFFFF);
                if (!occupied.Contains(destPlot))
                    continue;

                var destInvs = GetDestInventories(destPlot, out var destMode);
                if (destInvs.Count == 0)
                    continue;
                var local = CountIn(destInvs, new PrefabGUID(guid));
                if (local > 0)
                    continue;

                // 1.6.1.33: allShared kit spend-refill. Dest chest still exists/writable.
                // Vanishing kit GUID = player spent or RR'd them. Do NOT sticky-fail the plot.
                // Real dest-not-sticky (copy / dest-rejected / dest entity gone at move time)
                // is already MarkStickyFail in PullFromSources.
                if (destMode == "allShared" && IsStarterKitGuid(guid))
                    continue;

                MarkStickyFail(destPlot, key, destMode);
                var reversed = 0;
                var from = "";
                if (lastMove.TryGetValue(key, out var last))
                {
                    from = last.Source.ToString();
                    reversed = ReverseToSource(destInvs, last.Source, new PrefabGUID(guid), last.Amount);
                }
                Core.Log.LogWarning($"[ClanTreasuryLend] FAIL sticky destPlot={destPlot} guid={guid} local=0 next-tick after move dest={destMode} lastFrom={from} reversed={reversed} -- stop this boot" + (destMode == "allShared" ? " (skip entire allShared dest plot this boot)" : ""));
            }
        }

        static HashSet<int> CollectOccupiedClanSharePlots(out int connectedPlayers)
        {
            connectedPlayers = 0;
            var occupied = new HashSet<int>();
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref builder);
            builder.Dispose();

            NativeArray<Entity> users = default;
            try
            {
                users = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < users.Length; i++)
                {
                    var userEntity = users[i];
                    if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                        continue;
                    var user = userEntity.Read<User>();
                    if (!user.IsConnected)
                        continue;
                    var character = user.LocalCharacter.GetEntityOnServer();
                    if (character == Entity.Null || !Core.EntityManager.Exists(character))
                        continue;
                    if (!character.Has<PlayerCharacter>())
                        continue;
                    connectedPlayers++;
                    var territoryId = Core.TerritoryService.GetStandingTerritoryId(character);
                    if (territoryId < 0)
                        continue;
                    var heart = Core.TerritoryService.GetCastleHeart(territoryId);
                    if (!ClanTreasuryShare.ShouldShare(heart))
                        continue;
                    occupied.Add(territoryId);
                }
            }
            finally
            {
                if (users.IsCreated)
                    users.Dispose();
                query.Dispose();
            }
            return occupied;
        }

        static void LendToPlot(int destPlot, HashSet<int> occupiedAll)
        {
            var heart = Core.TerritoryService.GetCastleHeart(destPlot);
            if (heart == Entity.Null || !ClanTreasuryShare.ShouldShare(heart))
                return;

            var clanIds = Core.TerritoryService.GetLogisticsTerritoryIds(destPlot);
            if (clanIds == null || clanIds.Count <= 1)
                return;

            var occupiedInClan = new List<int>();
            foreach (var id in clanIds)
            {
                if (occupiedAll.Contains(id))
                    occupiedInClan.Add(id);
            }
            occupiedInClan.Sort();
            if (occupiedInClan.Count == 0)
                return;

            if (IsRaided(heart))
            {
                if (loggedDestMode.Add(destPlot))
                    Core.Log.LogInfo($"[ClanTreasuryLend] skip destPlot={destPlot} raided ActiveEvent={heart.Read<CastleHeart>().ActiveEvent}");
                return;
            }

            HandleHeartFuel(destPlot, heart, clanIds, occupiedInClan);

            var destInvs = GetDestInventories(destPlot, out var destMode);
            if (destInvs.Count == 0)
            {
                if (warnedNoDest.Add(destPlot))
                    Core.Log.LogWarning($"[ClanTreasuryLend] no dest chest plot={destPlot} -- kit/upgrade skipped, fuel seed only");
                return;
            }
            if (destMode == "allShared" && stickyFailedAllSharedPlots.Contains(destPlot))
            {
                if (loggedAllSharedPlotSkip.Add(destPlot))
                    Core.Log.LogWarning($"[ClanTreasuryLend] skip destPlot={destPlot} dest=allShared sticky-fail this boot -- kit GUIDs vanished, no further dumps");
                return;
            }

            Dictionary<int, int> upgrade = null;
            upgrade = GetHeartUpgradeCosts(destPlot, heart);

            if (loggedDestMode.Add(destPlot))
            {
                Core.Log.LogInfo($"[ClanTreasuryLend] dest plot={destPlot} mode={destMode} invs={destInvs.Count} occupiedInClan={occupiedInClan.Count} (upgrade=yes; kit={(destMode == "allShared" ? "spend-refill until treasury, never overflow if other dest exists" : "first-fill plank/brick/gemdust/copper/iron/stone if dest has none, then covering")}; covering=kit mats first then hungriest unlocked blueprint per mat, 3x (1x if reserve-tight); leftover-bypass plank={KitChestPlank}+stack copper={KitChestCopper}+stack iron={KitIron}+stack gemdust={KitGemDust}+stack brick={KitStoneBrick}+stack stone={KitStone}+stack; remaining plank={KitPlank - KitChestPlank} honor leftover; named last-resort kit/upgrade/covering; dest=never-NS seeded-s# generic exact category overflow custom-last; self-sort same-plot)");
                foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
                {
                    if (stash.Has<Refinementstation>() || StashRouting.IsNoShare(stash))
                        continue;
                    var name = StashRouting.RawName(stash);
                    var match = stash.Has<CastleWorkstation>() ? stash.Read<CastleWorkstation>().MatchingFloorType.ToString() : "no-ws";
                    Core.Log.LogInfo($"[ClanTreasuryLend] dest-chest plot={destPlot} name=\"{name}\" treasuryLinked={ClanTreasuryShare.IsTreasuryLinked(stash)} matchingFloor={match} unnamed={StashRouting.IsUnnamedOrGeneric(name)} overflow={StashRouting.IsOverflowDestName(name)}");
                }
            }

            // allShared: spend-refill until treasury, per dest-chest NetworkId opt-out.
            // Treasury: still first-fill plank/brick/gemdust/copper/iron/stone when the dest
            // has none of those yet (new L1 chest). Do not spend-refill established treasuries.
            // Covering runs after so a 1-chest castle is not filled with fibre/hide first.
            var kitCount = VanillaVisibleDests(destPlot, destInvs);
            if (kitCount.Count == 0)
                kitCount = NonOverflowDests(destPlot, destInvs);
            if (kitCount.Count == 0)
                kitCount = destInvs;
            if (destMode == "allShared")
            {
                var kitDest = FirstUsableKitDest(destPlot, kitCount);
                if (kitDest.Count > 0)
                {
                    var chestKeys = KitChestKeys(destPlot, kitDest[0]);
                    var kitOpted = AnyHeartKey(chestKeys, Core.PlayerSettings.IsStarterKitChestOptOut);
                    var kitSeeded = AnyHeartKey(chestKeys, Core.PlayerSettings.IsStarterKitChestSeeded);
                    var hasKit = DestHasAnyKit(kitCount);
                    var keyLabel = chestKeys.Length > 0 ? chestKeys[0] : "no-net";

                    if (kitOpted)
                    {
                        if (loggedFirstSuccess.Add(FailKey(destPlot, PlankHash) ^ 6))
                            Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} kit skip consumed chest={keyLabel} (manual empty opt-out) -- no refill, unbuild ok");
                    }
                    else if (kitSeeded && !hasKit)
                    {
                        Core.PlayerSettings.SetStarterKitChestOptOut(true, chestKeys);
                        if (loggedFirstSuccess.Add(FailKey(destPlot, PlankHash) ^ 7))
                            Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} kit empty after seeded chest={keyLabel} -- opt-out (manual empty; 5s tick will not restuff)");
                    }
                    else
                    {
                        // Empty never-seeded = first fill. Partial kit below targets = spend-refill.
                        var kitMoved = LendStarterKitOnce(destPlot, kitCount, clanIds, occupiedInClan, destMode);
                        hasKit = DestHasAnyKit(kitCount);
                        if ((hasKit || kitMoved > 0) && chestKeys.Length > 0)
                            Core.PlayerSettings.MarkStarterKitChestSeeded(chestKeys);
                        if (kitMoved > 0 && loggedFirstSuccess.Add(FailKey(destPlot, PlankHash) ^ (kitSeeded ? 3 : 2)))
                        {
                            var kind = kitSeeded ? "spend-refill" : "first-fill";
                            Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} kit {kind} moved={kitMoved} chest={keyLabel}");
                        }
                        else if (chestKeys.Length == 0 && loggedFirstSuccess.Add(FailKey(destPlot, PlankHash) ^ 5))
                        {
                            Core.Log.LogWarning($"[ClanTreasuryLend] destPlot={destPlot} kit dest chest has no NetworkId -- fill/top-off without empty-opt-out latch");
                        }
                    }
                }
            }
            else if (!DestHasAnyKit(kitCount) && kitCount.Count > 0)
            {
                var kitMoved = LendStarterKitOnce(destPlot, kitCount, clanIds, occupiedInClan, destMode);
                if (kitMoved > 0 && loggedFirstSuccess.Add(FailKey(destPlot, PlankHash) ^ 17))
                    Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} treasury first-fill kit moved={kitMoved} (plank/brick/gemdust before covering)");
            }

            var parkInvs = VanillaVisibleDests(destPlot, destInvs);
            if (parkInvs.Count == 0)
                parkInvs = destInvs;
            var covering1 = GetBuildCovering1x(destPlot);
            var covering3 = ScaleCovering(covering1, BuildCoverCopies);
            if (covering1.Count > 0 && loggedCovering.Add(destPlot))
            {
                covering1.TryGetValue(PlankHash, out var p1);
                covering1.TryGetValue(CopperIngotHash, out var c1);
                covering1.TryGetValue(GreaterBloodEssenceHash, out var g1);
                Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} build-covering mats={covering1.Count} copies={BuildCoverCopies} park={parkInvs.Count}/{destInvs.Count} plank1x={p1} copper1x={c1} gbe1x={g1} (1x leftover-bypass, 3x honor reserve)");
            }
            LendTargetAmounts(destPlot, parkInvs, covering1, clanIds, occupiedInClan, destMode, ignoreLeftoverNamed: true);
            LendTargetAmounts(destPlot, parkInvs, covering3, clanIds, occupiedInClan, destMode, ignoreLeftoverNamed: false);

            if (upgrade != null)
                LendTargetAmounts(destPlot, destInvs, upgrade, clanIds, occupiedInClan, destMode, ignoreLeftoverNamed: true);
        }

        static List<KeyValuePair<int, int>> OrderedCoveringTargets(Dictionary<int, int> targets)
        {
            var result = new List<KeyValuePair<int, int>>();
            if (targets == null || targets.Count == 0)
                return result;
            // New-chest priority: build mats before fibre/hide/paintings.
            var kitOrder = new[] { PlankHash, StoneBrickHash, GemDustHash, CopperIngotHash, IronIngotHash, StoneHash };
            var seen = new HashSet<int>();
            for (var i = 0; i < kitOrder.Length; i++)
            {
                var g = kitOrder[i];
                if (!targets.TryGetValue(g, out var n) || n <= 0)
                    continue;
                seen.Add(g);
                result.Add(new KeyValuePair<int, int>(g, n));
            }
            foreach (var kv in targets)
            {
                if (seen.Contains(kv.Key))
                    continue;
                result.Add(kv);
            }
            return result;
        }

        static void LendTargetAmounts(int destPlot, List<Entity> destInvs, Dictionary<int, int> targets,
            IReadOnlyList<int> clanIds, List<int> occupiedInClan, string destMode, bool ignoreLeftoverNamed)
        {
            if (targets == null || targets.Count == 0)
                return;

            var destFull = false;
            foreach (var kv in OrderedCoveringTargets(targets))
            {
                var guid = kv.Key;
                var target = kv.Value;
                if (guid == 0 || target <= 0)
                    continue;
                var key = FailKey(destPlot, guid);
                if (stickyFailed.Contains(key))
                    continue;

                var type = new PrefabGUID(guid);
                var useInvs = DestInvsForUpgradeItem(destPlot, type, destInvs);
                if (useInvs.Count == 0)
                {
                    if (IsTreasuryRejectedBlood(type) && loggedFirstSuccess.Add(FailKey(destPlot, guid) ^ 11))
                        Core.Log.LogWarning($"[ClanTreasuryLend] destPlot={destPlot} guid={guid} upgrade dest skipped -- no non-treasury/unnamed/name-match chest accepts this blood essence");
                    continue;
                }

                var local = CountIn(useInvs, type);
                if (local >= target)
                    continue;
                if (destFull && local == 0)
                    continue;
                if (local == 0 && !DestHasRoomFor(useInvs, type))
                {
                    destFull = true;
                    continue;
                }
                var need = target - local;
                if (need <= 0)
                    continue;

                var fail = false;
                var leftoverBlocked = 0;
                var leftoverHave = 0;
                var leftoverReserve = 0;
                var moved = PullFromSources(destPlot, useInvs, type, need, target, clanIds, occupiedInClan, destMode,
                    occupiedSpareOnly: false, allowNamed: true, allowSamePlot: false, ledgerMoves: true, ignoreLeftoverNamed, ref fail,
                    ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
                if (fail)
                    continue;
                need = target - CountIn(useInvs, type);
                if (need > 0)
                    moved += PullFromSources(destPlot, useInvs, type, need, target, clanIds, occupiedInClan, destMode,
                        occupiedSpareOnly: true, allowNamed: true, allowSamePlot: false, ledgerMoves: true, ignoreLeftoverNamed, ref fail,
                        ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);

                if (moved <= 0 && local == 0 && !DestHasRoomFor(useInvs, type))
                    destFull = true;

                if (moved > 0)
                    pendingVerify.Add(key);
            }
        }

        static void EnsureBlueprintCostCache()
        {
            if (blueprintCosts != null)
                return;
            blueprintCosts = new List<(int, bool, List<(int, int)>)>();
            try
            {
                var eqb = new EntityQueryBuilder(Allocator.Temp)
                    .AddAll(new(Il2CppType.Of<BlueprintData>(), ComponentType.AccessMode.ReadOnly))
                    .AddAll(new(Il2CppType.Of<BlueprintRequirementBuffer>(), ComponentType.AccessMode.ReadOnly))
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab);
                var query = Core.EntityManager.CreateEntityQuery(ref eqb);
                eqb.Dispose();
                NativeArray<Entity> entities = default;
                try
                {
                    entities = query.ToEntityArray(Allocator.Temp);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        if (entity == Entity.Null || !Core.EntityManager.Exists(entity))
                            continue;
                        if (entity.Has<CastleHeart>())
                            continue;
                        var data = entity.Read<BlueprintData>();
                        var bp = data.Guid.GuidHash;
                        if (bp == 0 && entity.Has<PrefabGUID>())
                            bp = entity.Read<PrefabGUID>().GuidHash;
                        if (bp == 0)
                            continue;
                        if (!entity.Has<BlueprintRequirementBuffer>())
                            continue;
                        var buf = entity.ReadBuffer<BlueprintRequirementBuffer>();
                        var costs = new List<(int guid, int amount)>();
                        for (var r = 0; r < buf.Length; r++)
                        {
                            var req = buf[r];
                            if (req.Amount <= 0 || req.PrefabGUID.GuidHash == 0)
                                continue;
                            if (IsTreasuryRejectedBlood(req.PrefabGUID))
                                continue;
                            costs.Add((req.PrefabGUID.GuidHash, req.Amount));
                        }
                        if (costs.Count == 0)
                            continue;
                        blueprintCosts.Add((bp, data.IsStartBlueprint, costs));
                    }
                }
                finally
                {
                    if (entities.IsCreated)
                        entities.Dispose();
                    query.Dispose();
                }
                Core.Log.LogInfo($"[ClanTreasuryLend] covering-cost cache castleBlueprints={blueprintCosts.Count} (station recipes added per plot)");
            }
            catch (Exception e)
            {
                Core.LogException(e);
                if (blueprintCosts.Count == 0)
                    blueprintCosts = new List<(int, bool, List<(int, int)>)>();
            }
        }

        static HashSet<int> CollectUnlocksOnPlot(int plot)
        {
            var unlocked = new HashSet<int>();
            try
            {
                var builder = new EntityQueryBuilder(Allocator.Temp)
                    .AddAll(new(Il2CppType.Of<User>(), ComponentType.AccessMode.ReadOnly));
                var query = Core.EntityManager.CreateEntityQuery(ref builder);
                builder.Dispose();
                NativeArray<Entity> users = default;
                try
                {
                    users = query.ToEntityArray(Allocator.Temp);
                    for (var i = 0; i < users.Length; i++)
                    {
                        var userEntity = users[i];
                        if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity) || !userEntity.Has<User>())
                            continue;
                        var user = userEntity.Read<User>();
                        if (!user.IsConnected)
                            continue;
                        var character = user.LocalCharacter.GetEntityOnServer();
                        if (character == Entity.Null || !Core.EntityManager.Exists(character) || !character.Has<PlayerCharacter>())
                            continue;
                        if (Core.TerritoryService.GetStandingTerritoryId(character) != plot)
                            continue;
                        AddUnlockedBlueprints(userEntity, unlocked);
                    }
                }
                finally
                {
                    if (users.IsCreated)
                        users.Dispose();
                    query.Dispose();
                }
            }
            catch (Exception e)
            {
                Core.LogException(e);
            }
            return unlocked;
        }

        static void AddUnlockedBlueprints(Entity userEntity, HashSet<int> unlocked)
        {
            if (!ProgressionUtility.TryGetProgressionEntity(Core.EntityManager, userEntity, out var prog)
                || prog == Entity.Null || !Core.EntityManager.Exists(prog))
                return;
            if (prog.Has<UnlockedBlueprintElement>())
            {
                var buf = prog.ReadBuffer<UnlockedBlueprintElement>();
                for (var i = 0; i < buf.Length; i++)
                {
                    var g = buf[i].UnlockedBlueprint.GuidHash;
                    if (g != 0)
                        unlocked.Add(g);
                }
            }
            if (prog.Has<UnlockedProgressionElement>())
            {
                var buf = prog.ReadBuffer<UnlockedProgressionElement>();
                for (var i = 0; i < buf.Length; i++)
                {
                    var prefab = buf[i].UnlockedPrefab;
                    if (prefab.GuidHash == 0)
                        continue;
                    unlocked.Add(prefab.GuidHash);
                    try
                    {
                        if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(prefab, out var tech)
                            && tech != Entity.Null && Core.EntityManager.Exists(tech)
                            && tech.Has<TechUnlockBlueprintBuffer>())
                        {
                            var techBuf = tech.ReadBuffer<TechUnlockBlueprintBuffer>();
                            for (var t = 0; t < techBuf.Length; t++)
                            {
                                var bp = techBuf[t].Guid.GuidHash;
                                if (bp != 0)
                                    unlocked.Add(bp);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        static float BuildCostMod()
        {
            try
            {
                var mod = Core.ServerGameSettingsSystem.Settings.BuildCostModifier;
                if (mod < 0)
                    return 0f;
                return mod;
            }
            catch
            {
                return 1f;
            }
        }

        static int ApplyBuildCost(int amount, float mod)
        {
            if (amount <= 0 || mod <= 0)
                return 0;
            if (Math.Abs(mod - 1f) < 0.0001f)
                return amount;
            var scaled = (int)Math.Ceiling(amount * (double)mod);
            return scaled < 1 ? 1 : scaled;
        }

        static Dictionary<int, int> GetCovering1x(int destPlot)
        {
            EnsureBlueprintCostCache();
            var mod = BuildCostMod();
            if (mod <= 0 || blueprintCosts == null)
                return new Dictionary<int, int>();
            if (coveringCastle1x == null)
            {
                coveringCastle1x = new Dictionary<int, int>();
                foreach (var row in blueprintCosts)
                    MergeCosts(coveringCastle1x, row.costs, mod);
            }
            var max = new Dictionary<int, int>(coveringCastle1x);
            if (destPlot >= 0)
                AddPlotStationRecipeCosts(destPlot, max, mod);
            return max;
        }

        static void MergeCosts(Dictionary<int, int> max, List<(int guid, int amount)> costs, float mod)
        {
            if (costs == null)
                return;
            foreach (var (guid, amount) in costs)
            {
                var need = ApplyBuildCost(amount, mod);
                if (need <= 0)
                    continue;
                if (!max.TryGetValue(guid, out var have) || need > have)
                    max[guid] = need;
            }
        }

        static void AddRecipeGuidCosts(PrefabGUID recipeGuid, Dictionary<int, int> max, float mod)
        {
            if (recipeGuid.GuidHash == 0)
                return;
            if (!Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(recipeGuid, out var recipeEntity))
                return;
            if (recipeEntity == Entity.Null || !Core.EntityManager.Exists(recipeEntity))
                return;
            if (recipeEntity.Has<RecipeData>() && recipeEntity.Read<RecipeData>().HideInStation)
                return;
            if (!recipeEntity.Has<RecipeRequirementBuffer>())
                return;
            var buf = recipeEntity.ReadBuffer<RecipeRequirementBuffer>();
            var costs = new List<(int guid, int amount)>();
            for (var r = 0; r < buf.Length; r++)
            {
                var req = buf[r];
                if (req.Amount <= 0 || req.Guid.GuidHash == 0)
                    continue;
                if (IsTreasuryRejectedBlood(req.Guid))
                    continue;
                costs.Add((req.Guid.GuidHash, req.Amount));
            }
            MergeCosts(max, costs, mod);
        }

        static void AddPlotStationRecipeCosts(int plot, Dictionary<int, int> max, float mod)
        {
            if (Core.RefinementStations != null)
            {
                foreach (var station in Core.RefinementStations.GetAllStationsOnTerritory(plot))
                {
                    if (station.Has<Disabled>())
                        continue;
                    if (!station.Has<RefinementstationRecipesBuffer>())
                        continue;
                    var recipes = station.ReadBuffer<RefinementstationRecipesBuffer>();
                    for (var i = 0; i < recipes.Length; i++)
                    {
                        if (!recipes[i].Unlocked || recipes[i].Disabled)
                            continue;
                        AddRecipeGuidCosts(recipes[i].RecipeGuid, max, mod);
                    }
                }
            }

            if (!workstationsByPlot.ContainsKey(plot))
                SeedWorkstationsOnPlot(plot);
            if (!workstationsByPlot.TryGetValue(plot, out var benches))
                return;
            for (var i = benches.Count - 1; i >= 0; i--)
            {
                var station = benches[i];
                if (station == Entity.Null || !Core.EntityManager.Exists(station))
                {
                    benches.RemoveAt(i);
                    continue;
                }
                if (station.Has<Disabled>())
                    continue;
                if (!station.Has<WorkstationRecipesBuffer>())
                    continue;
                if (station.Has<RefinementstationRecipesBuffer>())
                    continue;
                var recipes = station.ReadBuffer<WorkstationRecipesBuffer>();
                for (var r = 0; r < recipes.Length; r++)
                    AddRecipeGuidCosts(recipes[r].RecipeGuid, max, mod);
            }
        }

        static void SeedWorkstationsOnPlot(int plot)
        {
            var list = new List<Entity>();
            workstationsByPlot[plot] = list;
            var eqb = new EntityQueryBuilder(Allocator.Temp)
                .AddAll(new(Il2CppType.Of<WorkstationRecipesBuffer>(), ComponentType.AccessMode.ReadOnly))
                .AddAll(new(Il2CppType.Of<CastleHeartConnection>(), ComponentType.AccessMode.ReadOnly));
            var query = Core.EntityManager.CreateEntityQuery(ref eqb);
            eqb.Dispose();
            NativeArray<Entity> entities = default;
            try
            {
                entities = query.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < entities.Length; i++)
                {
                    var station = entities[i];
                    if (station == Entity.Null || !Core.EntityManager.Exists(station))
                        continue;
                    if (Core.TerritoryService.GetTerritoryId(station) != plot)
                        continue;
                    list.Add(station);
                }
            }
            finally
            {
                if (entities.IsCreated)
                    entities.Dispose();
                query.Dispose();
            }
        }

        static int CountClanTakeable(int destPlot, PrefabGUID type, IReadOnlyList<int> clanIds)
        {
            var total = 0;
            var sgm = Core.ServerGameManager;
            if (clanIds == null)
                return 0;
            foreach (var srcPlot in clanIds)
            {
                if (srcPlot == destPlot)
                    continue;
                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(srcPlot, out var sourceOwnerId);
                var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                var have = 0;
                foreach (var stash in Core.Stash.GetStashesOnTerritory(srcPlot))
                {
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (StashRouting.IsNoShare(stash))
                        continue;
                    if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                        continue;
                    foreach (var attachedBuffer in buffer)
                    {
                        var inventory = attachedBuffer.Entity;
                        if (!IsValidExternalInv(inventory))
                            continue;
                        have += sgm.GetInventoryItemCount(inventory, type);
                    }
                }
                if (reserve > 0)
                    have -= reserve;
                if (have > 0)
                    total += have;
            }
            return total;
        }

        static Dictionary<int, int> GetBuildCovering1x(int destPlot)
        {
            try
            {
                return GetCovering1x(destPlot) ?? new Dictionary<int, int>();
            }
            catch (Exception e)
            {
                Core.LogException(e);
                return new Dictionary<int, int>();
            }
        }

        static Dictionary<int, int> ScaleCovering(Dictionary<int, int> one, int copies)
        {
            var result = new Dictionary<int, int>();
            if (one == null)
                return result;
            foreach (var kv in one)
            {
                if (kv.Key == 0 || kv.Value <= 0)
                    continue;
                result[kv.Key] = kv.Value * copies;
            }
            return result;
        }

        /// <summary>
        /// occupiedSpareOnly false: unoccupied sibling plots (except dest invs).
        /// occupiedSpareOnly true: occupied siblings' excess above max(needed-for-this-lend, source reserve).
        /// Never sources dest plot unless allowSamePlot (heart fuel from own treasury).
        /// Pass 0: treasury-floor / unnamed. Pass 1: named conveyor chests last resort (kit/upgrade only).
        /// Named borrows still ledger stash NetworkId and return to THAT chest.
        /// ignoreLeftoverNamed: chest-first kit leftover-bypass. Uses raw stack count
        /// (do not subtract leftover). Named last-resort (pass 1) may also pull occupied
        /// named farm belts. Remaining 216 plank honors leftover.
        /// </summary>
        static int PullFromSources(int destPlot, List<Entity> destInvs, PrefabGUID type, int need, int destTarget,
            IReadOnlyList<int> clanIds, List<int> occupiedInClan, string destMode,
            bool occupiedSpareOnly, bool allowNamed, bool allowSamePlot, bool ledgerMoves, ref bool fail)
        {
            var leftoverBlocked = 0;
            var leftoverHave = 0;
            var leftoverReserve = 0;
            return PullFromSources(destPlot, destInvs, type, need, destTarget, clanIds, occupiedInClan, destMode,
                occupiedSpareOnly, allowNamed, allowSamePlot, ledgerMoves, ignoreLeftoverNamed: false, ref fail,
                ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
        }

        static int PullFromSources(int destPlot, List<Entity> destInvs, PrefabGUID type, int need, int destTarget,
            IReadOnlyList<int> clanIds, List<int> occupiedInClan, string destMode,
            bool occupiedSpareOnly, bool allowNamed, bool allowSamePlot, bool ledgerMoves, bool ignoreLeftoverNamed, ref bool fail,
            ref int leftoverBlocked, ref int leftoverHave, ref int leftoverReserve)
        {
            if (need <= 0 || fail)
                return 0;

            var movedAll = 0;
            var sgm = Core.ServerGameManager;
            var key = FailKey(destPlot, type.GuidHash);

            var spareByPlot = new Dictionary<int, int>();
            if (occupiedSpareOnly)
            {
                foreach (var pid in occupiedInClan)
                {
                    if (pid == destPlot && !allowSamePlot)
                        continue;
                    var siblingNeed = ChestTargetForPlot(pid, type.GuidHash);
                    if (siblingNeed < 0)
                        siblingNeed = 0;
                    spareByPlot[pid] = CountVanillaOnPlot(pid, type) - siblingNeed;
                }
            }

            for (var pass = 0; pass < 3 && need > 0 && !fail; pass++)
            {
            if (pass > 0 && !allowNamed)
                continue;
            foreach (var srcPlot in clanIds)
            {
                if (need <= 0 || fail)
                    break;
                if (srcPlot == destPlot && !allowSamePlot)
                    continue;

                var occupiedSrc = occupiedInClan.Contains(srcPlot);
                var namedBypass = ignoreLeftoverNamed && pass == 2 && allowNamed;
                if (occupiedSpareOnly)
                {
                    if (!occupiedSrc)
                        continue;
                    if (!namedBypass && (!spareByPlot.TryGetValue(srcPlot, out var spare) || spare <= 0))
                        continue;
                }
                else if (occupiedSrc)
                {
                    if (!namedBypass)
                        continue;
                }

                Core.TerritoryService.TryGetTerritoryOwnerPlatformId(srcPlot, out var sourceOwnerId);

                foreach (var stash in Core.Stash.GetStashesOnTerritory(srcPlot))
                {
                    if (need <= 0 || fail)
                        break;
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (StashRouting.IsNoShare(stash))
                        continue;
                    var sourcePass = StashRouting.SourcePass(stash);
                    if (sourcePass < 0 || sourcePass != pass)
                        continue;

                    if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                        continue;

                    foreach (var attachedBuffer in buffer)
                    {
                        if (need <= 0 || fail)
                            break;

                        var inventory = attachedBuffer.Entity;
                        if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                            continue;
                        if (!inventory.Has<PrefabGUID>())
                            continue;
                        if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                            continue;
                        if (IsDest(destInvs, inventory))
                            continue;
                        if (occupiedSpareOnly && !(ignoreLeftoverNamed && pass == 2 && allowNamed))
                        {
                            var siblingVanilla = GetTreasuryInventories(srcPlot);
                            if (siblingVanilla.Count == 0)
                                siblingVanilla = GetAllSharedInventories(srcPlot);
                            if (!IsDest(siblingVanilla, inventory))
                                continue;
                        }

                        var have = sgm.GetInventoryItemCount(inventory, type);
                        if (have <= 0)
                            continue;

                        var available = have;
                        var reserve = Core.PlayerSettings.GetPullReserve(sourceOwnerId, type);
                        var namedBypassHere = ignoreLeftoverNamed && pass == 2 && allowNamed;
                        // 1.6.1.33: leftover-bypass uses raw stack count, not count-after-leftover.
                        // have=249 leftover=250 must still yield the kit take (24 iron).
                        if (reserve > 0 && !ignoreLeftoverNamed)
                            available -= reserve;
                        if (available <= 0)
                        {
                            if (reserve > 0 && have > 0)
                            {
                                leftoverBlocked++;
                                leftoverHave = have;
                                leftoverReserve = reserve;
                            }
                            continue;
                        }

                        var take = available < need ? available : need;
                        if (occupiedSpareOnly && !namedBypassHere)
                        {
                            if (!spareByPlot.TryGetValue(srcPlot, out var spare) || spare <= 0)
                                continue;
                            if (take > spare)
                                take = spare;
                        }
                        if (take <= 0)
                            continue;

                        var srcBefore = sgm.GetInventoryItemCount(inventory, type);
                        var moved = MoveIntoDest(destPlot, inventory, destInvs, type, take, out var destRejected);
                        var srcAfter = sgm.GetInventoryItemCount(inventory, type);

                        if (moved > 0 && srcAfter >= srcBefore)
                        {
                            fail = true;
                            MarkStickyFail(destPlot, key, destMode);
                            var reversed = 0;
                            foreach (var dest in destInvs)
                            {
                                if (dest == Entity.Null || !Core.EntityManager.Exists(dest))
                                    continue;
                                var haveDest = sgm.GetInventoryItemCount(dest, type);
                                var takeBack = haveDest < moved ? haveDest : moved;
                                if (takeBack <= 0)
                                    continue;
                                if (sgm.TryRemoveInventoryItem(dest, type, takeBack))
                                    reversed += takeBack;
                                if (reversed >= moved)
                                    break;
                            }
                            Core.Log.LogWarning($"[ClanTreasuryLend] FAIL copy destPlot={destPlot} guid={type.GuidHash} movedReported={moved} fromPlot={srcPlot} dest={destMode} src={Identify(stash, inventory, srcPlot)} srcAfter={srcAfter} reversedDest={reversed} -- stop this boot");
                            return movedAll;
                        }

                        if (moved <= 0)
                        {
                            if (destRejected)
                            {
                                fail = true;
                                MarkStickyFail(destPlot, key, destMode);
                                Core.Log.LogWarning($"[ClanTreasuryLend] FAIL dest-rejected destPlot={destPlot} guid={type.GuidHash} take={take} fromPlot={srcPlot} dest={destMode} src={Identify(stash, inventory, srcPlot)} -- stop this boot (will not hold)");
                                return movedAll;
                            }
                            continue;
                        }

                        need -= moved;
                        movedAll += moved;
                        if (occupiedSpareOnly)
                            spareByPlot[srcPlot] = spareByPlot[srcPlot] - moved;

                        var chest = Identify(stash, inventory, srcPlot);
                        if (ledgerMoves)
                        {
                            CreditLedger(destPlot, type.GuidHash, chest, moved);
                            lastMove[key] = new LastMove { Source = chest, Amount = moved };
                        }

                        if (pass == 2 && loggedNamedBorrow.Add(key))
                            Core.Log.LogInfo($"[ClanTreasuryLend] named-chest borrow destPlot={destPlot} guid={type.GuidHash} moved={moved} from={chest} dest={destMode} (treasury/unnamed exhausted; return to this NetworkId)");
                        if (loggedFirstSuccess.Add(key))
                            Core.Log.LogInfo($"[ClanTreasuryLend] lend destPlot={destPlot} guid={type.GuidHash} moved={moved} from={chest} dest={destMode} (first-success; further stacks this guid silent)");
                    }
                }
            }
            }
            return movedAll;
        }

        static int MoveIntoDest(int destPlot, Entity source, List<Entity> destInvs, PrefabGUID type, int amount, out bool destRejected)
        {
            destRejected = false;
            if (amount <= 0)
                return 0;
            var remaining = amount;
            var moved = 0;
            var attempted = false;
            var sgm = Core.ServerGameManager;
            var ordered = StashRouting.OrderDepositInventories(destPlot, destInvs, type);
            // Covering/HUD lend: empty nameplate treasury used to rank Class 6 (prefab
            // "Small Chest") so planks had zero dests while copper used "Ore Ingots".
            // Named match first, then any remaining park chest with room.
            if (destInvs != null)
            {
                for (var i = 0; i < destInvs.Count; i++)
                {
                    var inv = destInvs[i];
                    if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                        continue;
                    if (InventoryIsOverflow(destPlot, inv))
                        continue;
                    var already = false;
                    for (var j = 0; j < ordered.Count; j++)
                    {
                        if (ordered[j].Equals(inv))
                        {
                            already = true;
                            break;
                        }
                    }
                    if (!already)
                        ordered.Add(inv);
                }
            }
            if (ordered.Count == 0 && destInvs != null && destInvs.Count > 0
                && loggedFirstSuccess.Add(FailKey(destPlot, type.GuidHash) ^ 21))
                Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} guid={type.GuidHash} no-ranked-dest park={destInvs.Count} -- fallback empty");
            foreach (var dest in ordered)
            {
                if (remaining <= 0)
                    break;
                if (dest == Entity.Null || !Core.EntityManager.Exists(dest) || dest.Equals(source))
                    continue;
                attempted = true;
                var got = Utilities.TransferItems(sgm, source, dest, type, remaining);
                if (got <= 0)
                    continue;
                remaining -= got;
                moved += got;
                foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
                {
                    if (!StashRouting.TryGetExternalInventory(stash, out var inv) || !inv.Equals(dest))
                        continue;
                    var rank = StashRouting.RankDeposit(stash, type, 0, true);
                    StashRouting.LogDestPick(rank.Label, destPlot, type, StashRouting.RawName(stash), "lend-dest");
                    break;
                }
            }
            if (attempted && moved == 0)
                destRejected = DestHasRoomFor(destInvs, type);
            return moved;
        }

        static void CreditLedger(int destPlot, int guid, SourceChest source, int amount)
        {
            var key = new LedgerKey { DestPlot = destPlot, Guid = guid, Source = source };
            if (ledger.TryGetValue(key, out var have))
                ledger[key] = have + amount;
            else
                ledger[key] = amount;
        }

        static SourceChest Identify(Entity stash, Entity inventory, int territoryId)
        {
            var chest = new SourceChest
            {
                TerritoryId = territoryId,
                HasNet = false,
                InvIndex = inventory.Index,
                InvVersion = inventory.Version
            };
            if (stash != Entity.Null && Core.EntityManager.Exists(stash) && stash.Has<NetworkId>())
            {
                chest.NetId = stash.Read<NetworkId>();
                chest.HasNet = true;
            }
            return chest;
        }

        static Entity ResolveSourceInventory(SourceChest src)
        {
            var inv = new Entity { Index = src.InvIndex, Version = src.InvVersion };
            if (IsValidExternalInv(inv))
                return inv;

            if (src.HasNet)
            {
                foreach (var stash in Core.Stash.GetStashesOnTerritory(src.TerritoryId))
                {
                    if (stash.Has<Refinementstation>())
                        continue;
                    if (!stash.Has<NetworkId>())
                        continue;
                    if (!stash.Read<NetworkId>().Equals(src.NetId))
                        continue;
                    var found = FirstExternalInv(stash);
                    if (found != Entity.Null)
                        return found;
                }
            }
            return Entity.Null;
        }

        static List<Entity> OtherChestsOnSourcePlot(int territoryId, Entity except)
        {
            var list = new List<Entity>();
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(territoryId))
            {
                if (stash.Has<Refinementstation>())
                    continue;
                if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var inventory = attachedBuffer.Entity;
                    if (!IsValidExternalInv(inventory))
                        continue;
                    if (except != Entity.Null && inventory.Equals(except))
                        continue;
                    list.Add(inventory);
                }
            }
            return list;
        }

        static void ReturnAllLedgers(string reason)
        {
            var plots = new HashSet<int>();
            foreach (var kv in ledger)
                plots.Add(kv.Key.DestPlot);
            foreach (var plot in plots)
                ReturnPlot(plot, reason);
        }

        static void ReturnPlot(int destPlot, string reason)
        {
            var keys = new List<LedgerKey>();
            foreach (var kv in ledger)
            {
                if (kv.Key.DestPlot == destPlot && kv.Value > 0)
                    keys.Add(kv.Key);
            }
            if (keys.Count == 0)
            {
                ClearPlotState(destPlot);
                return;
            }

            var destInvs = GetDestInventories(destPlot, out _);
            var returnedTotal = 0;
            var leftoverTotal = 0;

            var byGuid = new Dictionary<int, List<LedgerKey>>();
            foreach (var k in keys)
            {
                if (!byGuid.TryGetValue(k.Guid, out var list))
                {
                    list = new List<LedgerKey>();
                    byGuid[k.Guid] = list;
                }
                list.Add(k);
            }

            foreach (var guidKv in byGuid)
            {
                var type = new PrefabGUID(guidKv.Key);
                var remaining = CountIn(destInvs, type);
                foreach (var k in guidKv.Value)
                {
                    if (!ledger.TryGetValue(k, out var owed) || owed <= 0)
                        continue;
                    var want = owed;
                    if (want > remaining)
                        want = remaining;
                    var put = 0;
                    if (want > 0)
                    {
                        var exact = ResolveSourceInventory(k.Source);
                        if (exact != Entity.Null)
                            put = MoveFromDestTo(destInvs, exact, type, want);
                        if (put < want)
                        {
                            foreach (var other in OtherChestsOnSourcePlot(k.Source.TerritoryId, exact))
                            {
                                if (put >= want)
                                    break;
                                put += MoveFromDestTo(destInvs, other, type, want - put);
                            }
                        }
                    }
                    remaining -= put;
                    returnedTotal += put;
                    leftoverTotal += want - put;
                    ledger.Remove(k);
                }
            }

            ClearPlotState(destPlot);
            Core.Log.LogInfo($"[ClanTreasuryLend] return destPlot={destPlot} reason={reason} returned={returnedTotal} leftover={leftoverTotal} entries={keys.Count}");
        }

        static void ClearPlotState(int destPlot)
        {
            var drop = new List<long>();
            foreach (var key in pendingVerify)
            {
                if ((int)(key >> 32) == destPlot)
                    drop.Add(key);
            }
            foreach (var key in drop)
                pendingVerify.Remove(key);

            var dropLast = new List<long>();
            foreach (var kv in lastMove)
            {
                if ((int)(kv.Key >> 32) == destPlot)
                    dropLast.Add(kv.Key);
            }
            foreach (var key in dropLast)
                lastMove.Remove(key);
        }

        static int ReverseToSource(List<Entity> destInvs, SourceChest source, PrefabGUID type, int amount)
        {
            if (amount <= 0)
                return 0;
            var exact = ResolveSourceInventory(source);
            var put = 0;
            if (exact != Entity.Null)
                put = MoveFromDestTo(destInvs, exact, type, amount);
            if (put < amount)
            {
                foreach (var other in OtherChestsOnSourcePlot(source.TerritoryId, exact))
                {
                    if (put >= amount)
                        break;
                    put += MoveFromDestTo(destInvs, other, type, amount - put);
                }
            }
            return put;
        }

        static int MoveFromDestTo(List<Entity> destInvs, Entity target, PrefabGUID type, int amount)
        {
            if (amount <= 0 || target == Entity.Null || !Core.EntityManager.Exists(target))
                return 0;
            var remaining = amount;
            var moved = 0;
            var sgm = Core.ServerGameManager;
            foreach (var dest in destInvs)
            {
                if (remaining <= 0)
                    break;
                if (dest == Entity.Null || !Core.EntityManager.Exists(dest) || dest.Equals(target))
                    continue;
                var have = sgm.GetInventoryItemCount(dest, type);
                if (have <= 0)
                    continue;
                var take = have < remaining ? have : remaining;
                var got = Utilities.TransferItems(sgm, dest, target, type, take);
                if (got <= 0)
                    continue;
                remaining -= got;
                moved += got;
            }
            return moved;
        }

        /// <summary>
        /// Heart fuel seed/opt-out is per heart NetworkId only.
        /// Territory t{plot} must never stick to a replacement heart on the same plot.
        /// Old persisted t{plot} fuel keys are ignored.
        /// </summary>
        static string[] HeartFuelKeys(Entity heart)
        {
            if (heart != Entity.Null && Core.EntityManager.Exists(heart) && heart.Has<NetworkId>())
            {
                var net = heart.Read<NetworkId>();
                return new[] { "n" + net.ToString() };
            }
            return System.Array.Empty<string>();
        }

        /// <summary>
        /// Kit seed/opt-out keys are dest-chest NetworkId only (n{net}).
        /// Legacy plot t{plot} StarterKitSeeded is ignored.
        /// </summary>
        static string[] KitChestKeys(int destPlot, Entity inventory)
        {
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return System.Array.Empty<string>();
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(destPlot))
            {
                if (stash.Has<Refinementstation>())
                    continue;
                if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                var owns = false;
                for (var i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].Entity.Equals(inventory))
                    {
                        owns = true;
                        break;
                    }
                }
                if (!owns)
                    continue;
                if (stash.Has<NetworkId>())
                {
                    var net = stash.Read<NetworkId>();
                    return new[] { "n" + net.ToString() };
                }
                break;
            }
            return System.Array.Empty<string>();
        }

        static bool AnyHeartKey(string[] keys, System.Func<string, bool> pred)
        {
            if (keys == null)
                return false;
            for (var i = 0; i < keys.Length; i++)
            {
                if (pred(keys[i]))
                    return true;
            }
            return false;
        }

        static bool IsBloodEssenceName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var n = name.Replace("Item_Ingredient_", "").Replace("_", " ");
            if (n.IndexOf("Greater", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (n.IndexOf("Primal", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (n.IndexOf("Ancestral", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return n.IndexOf("Blood Essence", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("BloodEssence", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static PrefabGUID BloodEssenceType() => new PrefabGUID(BloodEssenceHash);

        static List<Entity> GetHeartFuelInventories(Entity heart)
        {
            var list = new List<Entity>();
            if (heart == Entity.Null || !Core.EntityManager.Exists(heart))
                return list;
            var sgm = Core.ServerGameManager;
            var be = BloodEssenceType();

            void consider(Entity inv)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    return;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Equals(inv))
                        return;
                }
                list.Add(inv);
            }

            try
            {
                if (sgm.TryGetBuffer<InventoryInstanceElement>(heart, out var iieBuffer))
                {
                    foreach (var iie in iieBuffer)
                    {
                        var restricted = iie.RestrictedType;
                        var match = restricted.GuidHash == BloodEssenceHash || restricted.Equals(be);
                        if (!match && restricted.GuidHash != 0)
                        {
                            var name = restricted.LookupName();
                            if (string.IsNullOrEmpty(name))
                                name = ClanTreasuryShare.PrefabName(restricted.GuidHash);
                            match = IsBloodEssenceName(name);
                        }
                        if (!match)
                            continue;
                        consider(iie.ExternalInventoryEntity.GetEntityOnServer());
                    }
                }
            }
            catch (Exception e)
            {
                Core.Log.LogWarning($"[ClanTreasuryLend] InventoryInstanceElement fuel scan: {e.Message}");
            }

            if (list.Count == 0)
            {
                try
                {
                    if (sgm.TryGetBuffer<AttachedBuffer>(heart, out var buffer))
                    {
                        foreach (var attachedBuffer in buffer)
                        {
                            var inv = attachedBuffer.Entity;
                            if (inv == Entity.Null || !Core.EntityManager.Exists(inv) || !inv.Has<PrefabGUID>())
                                continue;
                            if (inv.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                                continue;
                            if (sgm.GetInventoryItemCount(inv, be) > 0 || IsValidExternalInv(inv) || inv.Has<InventoryBuffer>())
                                consider(inv);
                        }
                    }
                }
                catch { }
            }

            if (list.Count == 0)
            {
                if (InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, heart, out var inv))
                    consider(inv);
            }
            return list;
        }

        static int CountHeartFuel(List<Entity> fuelInvs, Entity heart)
        {
            var n = CountIn(fuelInvs, BloodEssenceType());
            // Grey/empty placeholder: amount<=0 is empty. Do not treat FuelQuantity as fuel.
            if (n <= 0)
                return 0;
            return n;
        }


        /// <summary>
        /// Unlocked heart-fuel slot count. Locked slots (above castle-heart ItemSlots /
        /// InventoryInstanceElement.Slots) must not receive auto-feed.
        /// </summary>
        static int UnlockedFuelSlotCount(Entity heart, Entity fuelInv, int bufferLength)
        {
            var unlocked = bufferLength;
            try
            {
                if (heart != Entity.Null && Core.EntityManager.Exists(heart) && heart.Has<CastleHeart>())
                {
                    var ch = heart.Read<CastleHeart>();
                    ref var levelData = ref ch.GetLevelData();
                    if (levelData.ItemSlots > 0 && levelData.ItemSlots < unlocked)
                        unlocked = levelData.ItemSlots;
                }
            }
            catch { }

            try
            {
                var sgm = Core.ServerGameManager;
                if (heart != Entity.Null && sgm.TryGetBuffer<InventoryInstanceElement>(heart, out var iieBuffer))
                {
                    foreach (var iie in iieBuffer)
                    {
                        var inv = iie.ExternalInventoryEntity.GetEntityOnServer();
                        if (inv.Equals(fuelInv) && iie.Slots > 0 && iie.Slots < unlocked)
                            unlocked = iie.Slots;
                    }
                }
            }
            catch { }

            if (unlocked < 0)
                unlocked = 0;
            return unlocked;
        }

        /// <summary>
        /// Room in existing unlocked BE stacks only (cap 500). Does not count empty
        /// unlocked slots or locked slots. 428/500/500 with two locked => 72.
        /// </summary>
        static int HeartFuelTopOffNeed(Entity heart, List<Entity> fuelInvs)
        {
            var be = BloodEssenceType();
            var room = 0;
            var sgm = Core.ServerGameManager;
            foreach (var inv in fuelInvs)
            {
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                    continue;
                var unlocked = UnlockedFuelSlotCount(heart, inv, buf.Length);
                for (var i = 0; i < unlocked && i < buf.Length; i++)
                {
                    var slot = buf[i];
                    if (!slot.ItemType.Equals(be))
                        continue;
                    if (slot.Amount <= 0)
                        continue;
                    var cap = HeartFuelStack;
                    if (slot.MaxAmountOverride > 0 && slot.MaxAmountOverride < cap)
                        cap = slot.MaxAmountOverride;
                    if (slot.Amount < cap)
                        room += cap - slot.Amount;
                }
            }
            return room;
        }

        static void HandleHeartFuel(int destPlot, Entity heart, IReadOnlyList<int> clanIds, List<int> occupiedInClan)
        {
            var keys = HeartFuelKeys(heart);
            if (keys.Length == 0)
            {
                if (loggedHeartFuel.Add(destPlot))
                    Core.Log.LogWarning($"[ClanTreasuryLend] destPlot={destPlot} heart has no NetworkId -- skip fuel seed/opt-out");
                return;
            }
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(destPlot, out var ownerId);
            var feedOn = Core.PlayerSettings.IsHeartFeedEnabled(ownerId, destPlot);
            var seeded = AnyHeartKey(keys, Core.PlayerSettings.IsHeartFuelSeeded);
            var opted = AnyHeartKey(keys, Core.PlayerSettings.IsHeartFuelOptOut);

            var fuelInvs = GetHeartFuelInventories(heart);
            if (fuelInvs.Count == 0)
            {
                if (loggedHeartFuel.Add(destPlot))
                    Core.Log.LogWarning($"[ClanTreasuryLend] destPlot={destPlot} no heart fuel inventory (RestrictedType Blood Essence)");
                return;
            }

            var fuel = CountHeartFuel(fuelInvs, heart);

            if (opted)
            {
                if (fuel > 0)
                {
                    Core.PlayerSettings.SetHeartFuelOptOut(false, keys);
                    opted = false;
                    if (loggedHeartFuel.Add(destPlot + 100000))
                        Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heart fuel opt-out cleared (player added BE fuel={fuel})");
                }
                else
                    return;
            }

            // Seeded + empty means they dumped fuel. Opt out immediately (not a one-tick window).
            // Auto-feed must never restock a fully empty heart.
            if (seeded && fuel <= 0)
            {
                Core.PlayerSettings.SetHeartFuelOptOut(true, keys);
                if (loggedHeartFuel.Add(destPlot + 200000))
                    Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heart emptied after seed -- opted-out, no re-seed/auto-feed until they add BE");
                return;
            }

            var type = BloodEssenceType();
            var destMode = "heartFuel";
            if (!seeded && fuel <= 0)
            {
                var need = HeartFuelStack;
                var fail = false;
                var leftoverBlocked = 0;
                var leftoverHave = 0;
                var leftoverReserve = 0;
                var moved = PullFromSources(destPlot, fuelInvs, type, need, HeartFuelStack, clanIds, occupiedInClan, destMode,
                    occupiedSpareOnly: false, allowNamed: true, allowSamePlot: true, ledgerMoves: false, ignoreLeftoverNamed: true, ref fail,
                    ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
                if (!fail)
                {
                    need = HeartFuelStack - CountHeartFuel(fuelInvs, heart);
                    if (need > 0)
                        moved += PullFromSources(destPlot, fuelInvs, type, need, HeartFuelStack, clanIds, occupiedInClan, destMode,
                            occupiedSpareOnly: true, allowNamed: true, allowSamePlot: true, ledgerMoves: false, ignoreLeftoverNamed: true, ref fail,
                            ref leftoverBlocked, ref leftoverHave, ref leftoverReserve);
                }
                if (moved > 0)
                {
                    Core.PlayerSettings.MarkHeartFuelSeeded(keys);
                    if (loggedFirstSuccess.Add(FailKey(destPlot, BloodEssenceHash)))
                        Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heart fuel SEED moved={moved} (not returned)");
                }
                else if (loggedHeartFuel.Add(destPlot + 300000))
                {
                    var takeable = leftoverHave > leftoverReserve ? leftoverHave - leftoverReserve : leftoverHave;
                    if (leftoverBlocked > 0)
                        takeable = leftoverHave;
                    Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heart fuel SEED moved=0 leftover-blocked={leftoverBlocked} leftover={leftoverReserve} have={leftoverHave} takeable={takeable} fail={fail}");
                }
                return;
            }

            if (!feedOn || opted)
                return;

            // 1.6.1.28: top off each UNLOCKED existing BE stack to 500. Do not use
            // CountHeartFuel>=500 as a total cap (428+500+500 was skipping). Do not
            // fill empty unlocked slots. Do not add into locked slots. destTarget
            // stays HeartFuelStack only as occupied-sibling source keep, not dest cap.
            var top = HeartFuelTopOffNeed(heart, fuelInvs);
            if (top <= 0)
                return;

            var failFeed = false;
            var fed = PullFromSources(destPlot, fuelInvs, type, top, HeartFuelStack, clanIds, occupiedInClan, destMode,
                occupiedSpareOnly: false, allowNamed: false, allowSamePlot: true, ledgerMoves: false, ref failFeed);
            if (!failFeed)
            {
                top = HeartFuelTopOffNeed(heart, fuelInvs);
                if (top > 0)
                    fed += PullFromSources(destPlot, fuelInvs, type, top, HeartFuelStack, clanIds, occupiedInClan, destMode,
                        occupiedSpareOnly: true, allowNamed: false, allowSamePlot: true, ledgerMoves: false, ref failFeed);
            }
            if (fed > 0 && loggedFirstSuccess.Add(FailKey(destPlot, BloodEssenceHash) ^ 1))
                Core.Log.LogInfo($"[ClanTreasuryLend] destPlot={destPlot} heart auto-feed moved={fed} (stack top-off, first-success)");
        }

        static bool IsDest(List<Entity> destInvs, Entity inventory)
        {
            for (var i = 0; i < destInvs.Count; i++)
            {
                if (destInvs[i].Equals(inventory))
                    return true;
            }
            return false;
        }

        static bool IsValidExternalInv(Entity inventory)
        {
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return false;
            if (!inventory.Has<PrefabGUID>())
                return false;
            return inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab);
        }

        static Entity FirstExternalInv(Entity stash)
        {
            if (!Core.ServerGameManager.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                return Entity.Null;
            foreach (var attachedBuffer in buffer)
            {
                var inventory = attachedBuffer.Entity;
                if (IsValidExternalInv(inventory))
                    return inventory;
            }
            return Entity.Null;
        }

        static int CountVanillaOnPlot(int plot, PrefabGUID type)
        {
            var invs = GetTreasuryInventories(plot);
            if (invs.Count == 0)
                invs = GetAllSharedInventories(plot);
            return CountIn(invs, type);
        }

        static int CountIn(List<Entity> invs, PrefabGUID type)
        {
            var n = 0;
            var sgm = Core.ServerGameManager;
            if (invs == null)
                return 0;
            for (var i = 0; i < invs.Count; i++)
            {
                var inv = invs[i];
                if (inv == Entity.Null || !Core.EntityManager.Exists(inv))
                    continue;
                n += sgm.GetInventoryItemCount(inv, type);
            }
            return n;
        }

        const int SelfSortMovesPerTick = 24;

        static void SelfSortPlot(int plot)
        {
            var heart = Core.TerritoryService.GetCastleHeart(plot);
            if (heart == Entity.Null || IsRaided(heart))
                return;
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var ownerId);
            GetDestInventories(plot, out var destMode);
            var skipKit = destMode == "allShared";
            var sgm = Core.ServerGameManager;

            var chests = new List<(Entity stash, Entity inv)>();
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (stash.Has<Refinementstation>())
                    continue;
                if (StashRouting.IsNoShare(stash))
                    continue;
                if (!StashRouting.TryGetExternalInventory(stash, out var inv))
                    continue;
                chests.Add((stash, inv));
            }
            if (chests.Count < 2)
                return;

            var items = new List<PrefabGUID>();
            var seen = new HashSet<int>();
            foreach (var (stash, inv) in chests)
            {
                if (!sgm.TryGetBuffer<InventoryBuffer>(inv, out var buf))
                    continue;
                for (var i = 0; i < buf.Length; i++)
                {
                    var item = buf[i].ItemType;
                    if (item.GuidHash == 0)
                        continue;
                    if (skipKit && IsStarterKitGuid(item.GuidHash))
                        continue;
                    if (!seen.Add(item.GuidHash))
                        continue;
                    items.Add(item);
                }
            }
            if (items.Count == 0)
                return;

            var moves = 0;
            foreach (var item in items)
            {
                if (moves >= SelfSortMovesPerTick)
                    break;
                var reserve = Core.PlayerSettings.GetPullReserve(ownerId, item);
                var rows = new List<(Entity stash, Entity inv, StashRouting.SortRank rank)>();
                var anyDest = false;
                foreach (var (stash, inv) in chests)
                {
                    var has = StashRouting.InventoryHasItem(inv, item);
                    var rank = StashRouting.RankSort(stash, item, ownerId, has);
                    rows.Add((stash, inv, rank));
                    if (rank.UsableDest)
                        anyDest = true;
                }
                if (!anyDest)
                    continue;
                rows.Sort((a, b) => a.rank.CompareTo(b.rank));

                foreach (var (srcStash, srcInv, srcRank) in rows)
                {
                    if (moves >= SelfSortMovesPerTick)
                        break;
                    if (!srcRank.UsableSource)
                        continue;
                    var have = sgm.GetInventoryItemCount(srcInv, item);
                    if (have <= 0)
                        continue;
                    var surplus = have - reserve;
                    if (surplus <= 0)
                        continue;

                    var isEntity = false;
                    if (sgm.TryGetBuffer<InventoryBuffer>(srcInv, out var srcBuf))
                    {
                        for (var s = 0; s < srcBuf.Length; s++)
                        {
                            if (!srcBuf[s].ItemType.Equals(item))
                                continue;
                            if (!srcBuf[s].ItemEntity.GetEntityOnServer().Equals(Entity.Null))
                            {
                                isEntity = true;
                                break;
                            }
                        }
                    }

                    foreach (var (dstStash, dstInv, dstRank) in rows)
                    {
                        if (moves >= SelfSortMovesPerTick || surplus <= 0)
                            break;
                        if (dstInv.Equals(srcInv))
                            continue;
                        if (!dstRank.StrictlyBetterDestThan(srcRank))
                            continue;
                        if (StashRouting.IsOverflowDestName(StashRouting.RawName(dstStash)))
                            continue;
                        if (StashRouting.IsConveyorName(StashRouting.RawName(srcStash)))
                            continue;

                        var srcBefore = sgm.GetInventoryItemCount(srcInv, item);
                        int got;
                        if (isEntity)
                        {
                            var slot = 0;
                            Utilities.TransferItemEntities(srcInv, dstInv, item, surplus, ref slot, out got);
                        }
                        else
                        {
                            got = Utilities.TransferItems(sgm, srcInv, dstInv, item, surplus);
                        }
                        var srcAfter = sgm.GetInventoryItemCount(srcInv, item);
                        if (got > 0 && srcAfter >= srcBefore)
                        {
                            var back = sgm.GetInventoryItemCount(dstInv, item);
                            var takeBack = back < got ? back : got;
                            if (takeBack > 0)
                                sgm.TryRemoveInventoryItem(dstInv, item, takeBack);
                            Core.Log.LogWarning($"[Satisvampory] self-sort copy-fail plot={plot} guid={item.GuidHash} reversed={takeBack}");
                            continue;
                        }
                        if (got <= 0)
                            continue;
                        surplus -= got;
                        moves += 1;
                        StashRouting.LogDestPick(dstRank.Label, plot, item, StashRouting.RawName(dstStash), "self-sort");
                        DestDebugLog.Move("self-sort", plot, item, got, srcStash, dstStash, dstRank.Label, reserve, "stays");
                    }
                }
            }
            if (moves > 0)
                Core.Log.LogInfo($"[ClanTreasuryLend] self-sort plot={plot} moves={moves} destMode={destMode}");
        }

        static List<Entity> GetDestInventories(int territoryId, out string destMode)
        {
            var list = GetTreasuryInventories(territoryId);
            if (list.Count > 0)
            {
                destMode = "treasury";
                return list;
            }
            destMode = "allShared";
            return GetAllSharedInventories(territoryId);
        }

        static List<Entity> GetTreasuryInventories(int territoryId)
        {
            var list = new List<Entity>();
            AddPlotInventories(territoryId, list, treasuryOnly: true);
            return list;
        }

        static List<Entity> GetAllSharedInventories(int territoryId)
        {
            var list = new List<Entity>();
            AddPlotInventories(territoryId, list, treasuryOnly: false);
            return list;
        }

        static void AddPlotInventories(int territoryId, List<Entity> list, bool treasuryOnly)
        {
            var sgm = Core.ServerGameManager;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(territoryId))
            {
                if (stash.Has<Refinementstation>())
                    continue;
                if (StashRouting.IsNoShare(stash))
                {
                    StashRouting.LogDestPick(StashRouting.SkipLabel(StashRouting.RawName(stash)), territoryId, default, StashRouting.RawName(stash), "dest-filter");
                    continue;
                }
                if (treasuryOnly && !ClanTreasuryShare.IsTreasuryLinked(stash))
                    continue;
                if (!sgm.TryGetBuffer<AttachedBuffer>(stash, out var buffer))
                    continue;
                foreach (var attachedBuffer in buffer)
                {
                    var inventory = attachedBuffer.Entity;
                    if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                        continue;
                    if (!inventory.Has<PrefabGUID>())
                        continue;
                    if (!inventory.Read<PrefabGUID>().Equals(StashService.ExternalInventoryPrefab))
                        continue;
                    list.Add(inventory);
                }
            }
        }
    }
}
