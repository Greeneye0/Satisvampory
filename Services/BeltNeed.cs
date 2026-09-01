using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System.Collections.Generic;
using Unity.Entities;

namespace Satisvampory.Services
{
    /// <summary>
    /// One conveyor sink: a station input or an r# chest inventory that still wants an item.
    /// Wanted -1 means "fill this seeded chest" (no numeric cap).
    /// </summary>
    internal struct BeltSink
    {
        public Entity Inventory;
        public PrefabGUID Item;
        public int Wanted;
        public bool Chest;

        public BeltSink(Entity inventory, PrefabGUID item, int wanted, bool chest)
        {
            Inventory = inventory;
            Item = item;
            Wanted = wanted;
            Chest = chest;
        }

        public bool Unlimited => Wanted < 0;
        public bool StillOpen => Wanted != 0;
    }

    /// <summary>
    /// Grouped station/chest demand for one conveyor pass. Keyed by (s#/r# group, item hash)
    /// so overflow can flatten across groups while senders stay group-local.
    /// </summary>
    internal sealed class BeltBook
    {
        readonly Dictionary<(int group, int hash), List<BeltSink>> grouped = new();

        public int Count => grouped.Count;

        public void Want(int group, PrefabGUID item, Entity inventory, int amount, bool chest)
        {
            if (inventory == Entity.Null || item.GuidHash == 0)
                return;
            var key = (group, item.GuidHash);
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<BeltSink>();
                grouped[key] = list;
            }
            list.Add(new BeltSink(inventory, item, amount, chest));
        }

        public bool TryGrouped(int group, PrefabGUID item, out List<BeltSink> sinks)
        {
            return grouped.TryGetValue((group, item.GuidHash), out sinks);
        }

        public Dictionary<int, List<List<BeltSink>>> FlattenByItem()
        {
            var map = new Dictionary<int, List<List<BeltSink>>>();
            foreach (var kv in grouped)
            {
                var hash = kv.Key.hash;
                if (!map.TryGetValue(hash, out var lists))
                {
                    lists = new List<List<BeltSink>>();
                    map[hash] = lists;
                }
                lists.Add(kv.Value);
            }
            return map;
        }
    }

    internal static class BeltOwner
    {
        public static bool TryPlatform(Entity castleHeart, out ulong platformId)
        {
            platformId = 0;
            if (castleHeart == Entity.Null || !castleHeart.Has<UserOwner>())
                return false;
            var owner = castleHeart.Read<UserOwner>().Owner.GetEntityOnServer();
            if (owner == Entity.Null || !owner.Has<User>())
                return false;
            platformId = owner.Read<User>().PlatformId;
            return true;
        }

        public static bool ConveyorOn(ulong platformId)
        {
            return Core.PlayerSettings.IsConveyorEnabled(0) && Core.PlayerSettings.IsConveyorEnabled(platformId);
        }
    }
}
