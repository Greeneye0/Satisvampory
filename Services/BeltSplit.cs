using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// Fair-share belt moves. Overflow ignores groups; senders stay on their s#/r# group.
    /// Chest senders fill stations first; chest→chest is a second pass gated by convloop.
    /// </summary>
    internal static class BeltSplit
    {
        internal static readonly PrefabGUID SiegeGolemT02 = new(-1461326411);

        public static Dictionary<PrefabGUID, int> CountStackable(Entity inventory)
        {
            var amounts = new Dictionary<PrefabGUID, int>();
            if (inventory == Entity.Null || !Core.EntityManager.Exists(inventory))
                return amounts;
            if (!Core.ServerGameManager.TryGetBuffer<InventoryBuffer>(inventory, out var slots))
                return amounts;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.ItemType.GuidHash == 0)
                    continue;
                if (!slot.ItemEntity.Equals(NetworkedEntity.Empty) && !slot.ItemType.Equals(SiegeGolemT02))
                    continue;
                amounts.TryGetValue(slot.ItemType, out var have);
                amounts[slot.ItemType] = have + slot.Amount;
            }
            return amounts;
        }

        public static void HonorReserve(Dictionary<PrefabGUID, int> amounts, ulong ownerId, bool apply)
        {
            if (!apply || amounts.Count == 0)
                return;
            var keys = new List<PrefabGUID>(amounts.Keys);
            for (var i = 0; i < keys.Count; i++)
            {
                var item = keys[i];
                var floor = ownerId != 0 ? Core.PlayerSettings.GetPullReserve(ownerId, item) : 0;
                amounts[item] = Math.Max(0, amounts[item] - floor);
            }
        }

        static int Slice(int wanted, int available, int totalWanted, ref int remainder)
        {
            if (wanted <= 0 || available <= 0 || totalWanted <= 0)
                return 0;
            var numerator = (long)wanted * available;
            var give = (int)(numerator / totalWanted);
            remainder += (int)(numerator % totalWanted);
            if (remainder >= totalWanted && give < wanted)
            {
                give++;
                remainder -= totalWanted;
            }
            return give;
        }

        static int Move(ServerGameManager sgm, Entity from, Entity to, PrefabGUID item, int amount)
        {
            if (amount <= 0 || from == Entity.Null || to == Entity.Null)
                return 0;
            return Utilities.TransferItems(sgm, from, to, item, amount);
        }

        static void DumpToOverflow(ServerGameManager sgm, Entity from, PrefabGUID item, int leftover, Entity[] overflow)
        {
            if (leftover <= 0 || overflow == null)
                return;
            for (var i = 0; i < overflow.Length && leftover > 0; i++)
            {
                var chest = overflow[i];
                if (!StashRouting.TryGetExternalInventory(chest, out var inv))
                    continue;
                leftover -= Move(sgm, from, inv, item, leftover);
            }
        }

        public static void FromOverflow(BeltBook book, ServerGameManager sgm, Entity inventory,
            ref Dictionary<int, List<List<BeltSink>>> ungrouped)
        {
            var stock = CountStackable(inventory);
            if (stock.Count == 0)
                return;
            ungrouped ??= book.FlattenByItem();

            foreach (var (item, available) in stock)
            {
                if (!ungrouped.TryGetValue(item.GuidHash, out var lists))
                    continue;

                var numbered = new List<(int list, int index, BeltSink sink)>();
                for (var li = 0; li < lists.Count; li++)
                {
                    var list = lists[li];
                    for (var i = list.Count - 1; i >= 0; i--)
                        numbered.Add((li, i, list[i]));
                }

                var totalWanted = 0;
                for (var n = 0; n < numbered.Count; n++)
                {
                    if (numbered[n].sink.Wanted > 0)
                        totalWanted += numbered[n].sink.Wanted;
                }

                if (totalWanted <= available)
                {
                    var extra = available - totalWanted;
                    for (var n = 0; n < numbered.Count; n++)
                    {
                        var (li, index, sink) = numbered[n];
                        if (!Core.EntityManager.Exists(sink.Inventory))
                        {
                            lists[li].RemoveAt(index);
                            continue;
                        }
                        if (sink.Wanted > 0)
                        {
                            Move(sgm, inventory, sink.Inventory, item, sink.Wanted);
                            lists[li].RemoveAt(index);
                        }
                        else if (extra > 0)
                        {
                            var given = Move(sgm, inventory, sink.Inventory, item, extra);
                            if (given < extra)
                                lists[li].RemoveAt(index);
                            extra -= given;
                        }
                    }
                }
                else
                {
                    var remainder = 0;
                    for (var n = 0; n < numbered.Count; n++)
                    {
                        var (li, index, sink) = numbered[n];
                        if (sink.Wanted <= 0)
                            continue;
                        if (!Core.EntityManager.Exists(sink.Inventory))
                        {
                            totalWanted -= sink.Wanted;
                            lists[li].RemoveAt(index);
                            continue;
                        }
                        var give = Slice(sink.Wanted, available, totalWanted, ref remainder);
                        var got = Move(sgm, inventory, sink.Inventory, item, give);
                        if (got < give)
                        {
                            remainder += (give - got) * totalWanted;
                            lists[li].RemoveAt(index);
                        }
                        else if (got >= sink.Wanted)
                        {
                            lists[li].RemoveAt(index);
                        }
                        else
                        {
                            sink.Wanted -= got;
                            lists[li][index] = sink;
                        }
                    }
                }
            }
        }

        public static void FromGroup(BeltBook book, ServerGameManager sgm, int group, Entity inventory,
            Entity[] overflow, ulong ownerId, bool chest, bool applyReserve, Entity sendingStash)
        {
            var stock = CountStackable(inventory);
            HonorReserve(stock, ownerId, applyReserve);

            foreach (var (item, available) in stock)
            {
                if (available <= 0)
                    continue;
                if (!book.TryGrouped(group, item, out var sinks) || sinks == null)
                {
                    if (!chest)
                        DumpToOverflow(sgm, inventory, item, available, overflow);
                    continue;
                }

                var totalWanted = 0;
                for (var i = 0; i < sinks.Count; i++)
                {
                    var sink = sinks[i];
                    if (chest && sink.Chest)
                        continue;
                    if (sink.Wanted > 0)
                        totalWanted += sink.Wanted;
                }

                if (totalWanted <= available)
                {
                    var extra = chest ? 0 : available - totalWanted;
                    for (var i = sinks.Count - 1; i >= 0; i--)
                    {
                        var sink = sinks[i];
                        if (chest && sink.Chest)
                            continue;
                        if (!Core.EntityManager.Exists(sink.Inventory))
                        {
                            sinks.RemoveAt(i);
                            continue;
                        }
                        if (sink.Wanted > 0)
                        {
                            var got = Move(sgm, inventory, sink.Inventory, item, sink.Wanted);
                            if (chest && got > 0 && sendingStash != Entity.Null)
                            {
                                var plot = Core.TerritoryService.GetTerritoryId(sendingStash);
                                DestDebugLog.Move("conveyor", plot, item, got, sendingStash, Entity.Null, "station",
                                    Core.PlayerSettings.GetPullReserve(ownerId, item), "station");
                            }
                            sinks.RemoveAt(i);
                        }
                        else if (!chest && extra > 0)
                        {
                            var given = Move(sgm, inventory, sink.Inventory, item, extra);
                            if (given < extra)
                                sinks.RemoveAt(i);
                            extra -= given;
                        }
                    }
                    if (extra > 0)
                        DumpToOverflow(sgm, inventory, item, extra, overflow);
                }
                else
                {
                    var remainder = 0;
                    var moved = 0;
                    for (var i = sinks.Count - 1; i >= 0; i--)
                    {
                        var sink = sinks[i];
                        if (chest && sink.Chest)
                            continue;
                        if (sink.Wanted <= 0)
                            continue;
                        if (!Core.EntityManager.Exists(sink.Inventory))
                        {
                            totalWanted -= sink.Wanted;
                            sinks.RemoveAt(i);
                            continue;
                        }
                        var give = Slice(sink.Wanted, available, totalWanted, ref remainder);
                        var got = Move(sgm, inventory, sink.Inventory, item, give);
                        if (chest && got > 0 && sendingStash != Entity.Null)
                        {
                            var plot = Core.TerritoryService.GetTerritoryId(sendingStash);
                            DestDebugLog.Move("conveyor", plot, item, got, sendingStash, Entity.Null, "station",
                                Core.PlayerSettings.GetPullReserve(ownerId, item), "station");
                        }
                        moved += got;
                        if (got < give)
                        {
                            remainder += totalWanted * (give - got);
                            sinks.RemoveAt(i);
                        }
                        else if (got >= sink.Wanted)
                        {
                            sinks.RemoveAt(i);
                        }
                        else
                        {
                            sink.Wanted -= got;
                            sinks[i] = sink;
                        }
                    }
                    if (!chest && moved < available)
                        DumpToOverflow(sgm, inventory, item, available - moved, overflow);
                }
            }

            if (chest)
                ChestToChest(book, sgm, group, inventory, ownerId, sendingStash);
        }

        static void ChestToChest(BeltBook book, ServerGameManager sgm, int group, Entity inventory,
            ulong ownerId, Entity sendingStash)
        {
            if (!Core.PlayerSettings.IsConveyorLoopsAllowed() && sendingStash != Entity.Null
                && StashHasToken(sendingStash, Core.Stash.ReceiverRegex, group))
            {
                // Default OFF: an r# source does not refill an s# dest on the same group.
            }

            var stock = CountStackable(inventory);
            HonorReserve(stock, ownerId, apply: true);
            var loops = Core.PlayerSettings.IsConveyorLoopsAllowed();
            var srcPlot = sendingStash != Entity.Null ? Core.TerritoryService.GetTerritoryId(sendingStash) : -1;
            var srcIsReceiver = sendingStash != Entity.Null && StashHasToken(sendingStash, Core.Stash.ReceiverRegex, group);

            foreach (var (item, available) in stock)
            {
                if (available <= 0)
                    continue;
                if (!book.TryGrouped(group, item, out var sinks) || sinks == null)
                    continue;
                var left = available;
                for (var i = 0; i < sinks.Count && left > 0; i++)
                {
                    var sink = sinks[i];
                    if (!sink.Chest)
                        continue;
                    if (sink.Inventory == Entity.Null || !Core.EntityManager.Exists(sink.Inventory) || sink.Inventory.Equals(inventory))
                        continue;
                    var destStash = StashFromInventory(sink.Inventory, sendingStash);
                    if (!loops && destStash != Entity.Null && destStash != sendingStash
                        && srcIsReceiver && StashHasToken(destStash, Core.Stash.SenderRegex, group))
                        continue;
                    var take = sink.Unlimited ? left : (sink.Wanted < left ? sink.Wanted : left);
                    if (take <= 0)
                        continue;
                    var got = Move(sgm, inventory, sink.Inventory, item, take);
                    if (got <= 0)
                        continue;
                    left -= got;
                    DestDebugLog.Move("conveyor", srcPlot, item, got, sendingStash, destStash, "chest-r#",
                        Core.PlayerSettings.GetPullReserve(ownerId, item), "chest");
                    if (sink.Wanted > 0)
                    {
                        sink.Wanted -= got;
                        if (sink.Wanted <= 0)
                            sinks.RemoveAt(i--);
                        else
                            sinks[i] = sink;
                    }
                }
            }
        }

        static Entity StashFromInventory(Entity destInv, Entity sendingStash)
        {
            var plot = Core.TerritoryService.GetTerritoryId(destInv);
            if (plot < 0 && sendingStash != Entity.Null)
                plot = Core.TerritoryService.GetTerritoryId(sendingStash);
            if (plot < 0)
                return Entity.Null;
            foreach (var stash in Core.Stash.GetStashesOnTerritory(plot))
            {
                if (StashRouting.TryGetExternalInventory(stash, out var inv) && inv.Equals(destInv))
                    return stash;
            }
            return Entity.Null;
        }

        static bool StashHasToken(Entity stash, Regex regex, int group)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return false;
            var plate = stash.Read<NameableInteractable>().Name.ToString();
            if (string.IsNullOrEmpty(plate))
                return false;
            foreach (Match hit in regex.Matches(plate.ToLowerInvariant()))
            {
                if (int.TryParse(hit.Groups[1].Value, out var g) && g == group)
                    return true;
            }
            return false;
        }

        public static void Ungrouped(Dictionary<PrefabGUID, List<(Entity receiver, int amount)>> needs,
            ServerGameManager sgm, Entity inventory, ulong ownerId, bool applyReserve)
        {
            var stock = CountStackable(inventory);
            HonorReserve(stock, ownerId, applyReserve);
            foreach (var (item, available) in stock)
            {
                if (available <= 0 || !needs.TryGetValue(item, out var sinks) || sinks == null)
                    continue;
                var totalWanted = 0;
                for (var i = 0; i < sinks.Count; i++)
                {
                    if (sinks[i].amount > 0)
                        totalWanted += sinks[i].amount;
                }
                if (totalWanted <= 0)
                    continue;
                if (totalWanted <= available)
                {
                    for (var i = sinks.Count - 1; i >= 0; i--)
                    {
                        var (dest, wanted) = sinks[i];
                        if (!Core.EntityManager.Exists(dest))
                        {
                            sinks.RemoveAt(i);
                            continue;
                        }
                        Move(sgm, inventory, dest, item, wanted);
                    }
                    sinks.Clear();
                    continue;
                }
                var remainder = 0;
                for (var i = sinks.Count - 1; i >= 0; i--)
                {
                    var (dest, wanted) = sinks[i];
                    if (!Core.EntityManager.Exists(dest))
                    {
                        totalWanted -= wanted;
                        sinks.RemoveAt(i);
                        continue;
                    }
                    var give = Slice(wanted, available, totalWanted, ref remainder);
                    var got = Move(sgm, inventory, dest, item, give);
                    if (got < give)
                    {
                        remainder += give - got;
                        sinks.RemoveAt(i);
                    }
                    else if (got >= wanted)
                    {
                        sinks.RemoveAt(i);
                    }
                    else
                    {
                        sinks[i] = (dest, wanted - got);
                    }
                }
            }
        }
    }
}
