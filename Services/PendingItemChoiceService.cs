using System;
using System.Collections.Generic;
using Satisvampory;
using VampireCommandFramework;
using Stunlock.Core;

namespace Satisvampory.Services
{
    internal enum PendingItemCommand
    {
        None = 0,
        Pull,
        ReserveShow,
        ReserveSet,
        ReserveClear,
        CapShow,
        CapSet,
        CapClear,
        GroupAdd,
        GroupRemove,
        FindItem,
        AdminStash,
        Conv,
        ExcludeToggle,
        BagCapShow,
        BagCapSet
    }

    internal sealed class PendingItemChoice
    {
        public List<(PrefabGUID Prefab, string Name)> Candidates;
        public PendingItemCommand Command;
        public bool CommandAttached;
        public int Amount;
        public string GroupName;
        public DateTime ExpiresUtc;
    }

    internal static class PendingItemChoiceService
    {
        static readonly Dictionary<ulong, PendingItemChoice> pending = new();
        static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
        public const int MaxListed = 25;

        public static void BeginAmbiguous(ulong platformId, List<(PrefabGUID Prefab, string Name)> candidates)
        {
            if (candidates == null)
                candidates = new List<(PrefabGUID Prefab, string Name)>();

            pending[platformId] = new PendingItemChoice
            {
                Candidates = candidates,
                Command = PendingItemCommand.None,
                CommandAttached = false,
                Amount = 0,
                GroupName = null,
                ExpiresUtc = DateTime.UtcNow + Ttl
            };
        }

        public static void AttachCommand(ulong platformId, PendingItemCommand command, int amount = 0, string groupName = null)
        {
            if (!pending.TryGetValue(platformId, out var choice) || choice == null)
                return;
            if (DateTime.UtcNow > choice.ExpiresUtc)
            {
                pending.Remove(platformId);
                return;
            }
            choice.Command = command;
            choice.CommandAttached = true;
            choice.Amount = amount;
            choice.GroupName = groupName;
            choice.ExpiresUtc = DateTime.UtcNow + Ttl;
        }

        public static void ReplyNumberedList(ChatCommandContext ctx)
        {
            var platformId = ctx.Event.User.PlatformId;
            if (!pending.TryGetValue(platformId, out var choice) || choice?.Candidates == null || choice.Candidates.Count == 0)
            {
                ctx.Reply("Multiple matches. Be more specific.");
                return;
            }

            ctx.Reply("Multiple matches. Type <color=white>.l <number></color> (or <color=white>.l pick <number></color>) to choose:");
            var limit = Math.Min(choice.Candidates.Count, MaxListed);
            for (var i = 0; i < limit; i++)
            {
                var name = choice.Candidates[i].Name;
                if (string.IsNullOrEmpty(name))
                    name = choice.Candidates[i].Prefab.PrefabName();
                ctx.Reply($"  {i + 1}. <color=green>{name}</color>");
            }
            if (choice.Candidates.Count > MaxListed)
                ctx.Reply("  ...");
        }

        public static bool TryPick(ulong platformId, int index, out PendingItemChoice choice, out (PrefabGUID Prefab, string Name) picked, out string error)
        {
            choice = null;
            picked = default;
            error = null;

            if (!pending.TryGetValue(platformId, out var existing) || existing == null)
            {
                error = "No pending item choice. Search again if a name had multiple matches.";
                return false;
            }

            if (DateTime.UtcNow > existing.ExpiresUtc)
            {
                pending.Remove(platformId);
                error = "That item choice expired. Search again.";
                return false;
            }

            if (!existing.CommandAttached || existing.Candidates == null || existing.Candidates.Count == 0)
            {
                pending.Remove(platformId);
                error = "No pending item choice. Search again if a name had multiple matches.";
                return false;
            }

            if (index < 1 || index > existing.Candidates.Count)
            {
                error = $"Invalid choice. Pick a number from 1 to {existing.Candidates.Count}.";
                return false;
            }

            choice = existing;
            picked = existing.Candidates[index - 1];
            pending.Remove(platformId);
            return true;
        }
    }
}
