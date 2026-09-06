using Satisvampory.Commands.Converters;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Satisvampory.Services
{
    /// <summary>
    /// Mailbox op "sorttest": runs the dest-name matcher against real items with a temporary
    /// in-memory item alias and a temporary custom group on the given owner, then removes both.
    /// Proves: adding an alias does not change how the item's own name sorts; a custom group
    /// outranks a built-in group and never falls back on a miss; bare numbers never match.
    /// </summary>
    internal static class SortLogicTest
    {
        const string Alias = "svtalias";
        const string Group = "svtestgrp";

        public static string Run(ulong ownerId)
        {
            var checks = new List<(string name, bool ok, string detail)>();
            var graveDust = new PrefabGUID(-608131642);
            var bone = new PrefabGUID(1821405450);
            var gdName = StashRouting.ItemLabel(graveDust);
            var boneName = StashRouting.ItemLabel(bone);
            if (string.IsNullOrEmpty(gdName) || string.IsNullOrEmpty(boneName))
                return "{\"error\":\"item labels unavailable\"}";

            bool aliasBound = false, groupMade = false;
            try
            {
                // ---- baseline ----
                Check(checks, "exact: 'Grave Dust' matches Grave Dust", StashRouting.ExactItemNameMatch("Grave Dust", graveDust, out _));
                Check(checks, "exact: 'Grave Dust S1' matches Grave Dust", StashRouting.ExactItemNameMatch("Grave Dust S1", graveDust, out _));
                Check(checks, "exact: 'Grave Dust' does not match Bone", !StashRouting.ExactItemNameMatch("Grave Dust", bone, out _));
                var alchOk = StashRouting.CategoryMatch("Alchemy", graveDust, ownerId, out var alchSpec);
                Check(checks, "category: 'Alchemy' matches Grave Dust at built-in group tier", alchOk && Tier(alchSpec) == StashRouting.TierGroup, "spec=" + alchSpec);
                Check(checks, "no bare-number match: 'Alch 1' vs Grave Dust", !StashRouting.CategoryMatch("Alch 1", graveDust, ownerId, out _) || Tier(SpecOf("Alch 1", graveDust, ownerId)) != StashRouting.TierPartial);
                Check(checks, "alias unbound before test", !ItemGroupService.TryExactItemAlias(Alias, out _));

                // ---- equipment never matches by name fragment ----
                var copperIngot = new PrefabGUID(-1237019921);
                Check(checks, "partial: 'Copper Iron' matches Copper Ingot (material)", StashRouting.CategoryMatch("Copper Iron", copperIngot, ownerId, out var ciSpec) && Tier(ciSpec) == StashRouting.TierPartial, "spec=" + ciSpec);
                if (FoundItemConverter.TryGetExact("Copper Sword", out var swordFound) && swordFound.prefab.GuidHash != 0)
                {
                    var sword = swordFound.prefab;
                    Check(checks, "equipment: 'Copper Iron' does not match Copper Sword", !StashRouting.CategoryMatch("Copper Iron", sword, ownerId, out _));
                    Check(checks, "equipment: 'Copper Sword' exact still matches", StashRouting.ExactItemNameMatch("Copper Sword", sword, out _));
                    Check(checks, "equipment: 'Weapons' group word matches Copper Sword", StashRouting.CategoryMatch("Weapons", sword, ownerId, out var wSpec) && Tier(wSpec) >= StashRouting.TierCategory, "spec=" + wSpec);
                }
                else
                {
                    checks.Add(("equipment: Copper Sword not resolvable, skipped", true, ""));
                }

                // ---- add alias -> Grave Dust (memory only) ----
                ItemGroupService.RegisterAliasInMemory(Alias, graveDust);
                aliasBound = true;
                Check(checks, "alias: '" + Alias + "' matches Grave Dust as exact", StashRouting.ExactItemNameMatch(Alias, graveDust, out _));
                Check(checks, "alias: '" + Alias + " S1' matches Grave Dust as exact", StashRouting.ExactItemNameMatch(Alias + " S1", graveDust, out _));
                Check(checks, "alias: '" + Alias + "' does not match Bone", !StashRouting.ExactItemNameMatch(Alias, bone, out _) && !StashRouting.CategoryMatch(Alias, bone, ownerId, out _));
                Check(checks, "alias: item name 'Grave Dust' still exact", StashRouting.ExactItemNameMatch("Grave Dust", graveDust, out _));
                Check(checks, "alias: item name 'Grave Dust S1' still exact", StashRouting.ExactItemNameMatch("Grave Dust S1", graveDust, out _));
                var alchOk2 = StashRouting.CategoryMatch("Alchemy", graveDust, ownerId, out var alchSpec2);
                Check(checks, "alias: 'Alchemy' still built-in group tier for Grave Dust", alchOk2 && alchSpec2 == alchSpec, "spec=" + alchSpec2);
                Check(checks, "alias: 'Bone " + Alias + "' category-matches Grave Dust via alias token", StashRouting.CategoryMatch("Bone " + Alias, graveDust, ownerId, out _));

                // ---- custom group containing Bone only ----
                groupMade = Core.PlayerSettings.CreateItemGroup(ownerId, Group);
                Check(checks, "custom group created", groupMade);
                if (groupMade)
                {
                    Core.PlayerSettings.AddItemToGroup(ownerId, Group, bone, boneName);
                    var cgOk = StashRouting.CategoryMatch(Group, bone, ownerId, out var cgSpec);
                    Check(checks, "custom group: '" + Group + "' matches Bone at custom tier", cgOk && Tier(cgSpec) == StashRouting.TierCustomGroup, "spec=" + cgSpec);
                    Check(checks, "custom group: '" + Group + "' does not match Grave Dust (no fallback)", !StashRouting.CategoryMatch(Group, graveDust, ownerId, out _));
                    Check(checks, "custom group: '" + Group + " S1' matches Bone", StashRouting.CategoryMatch(Group + " S1", bone, ownerId, out _));
                    var bothOk = StashRouting.CategoryMatch(Group + " Alchemy", bone, ownerId, out var bothSpec);
                    Check(checks, "custom group beats built-in on same plate for Bone", bothOk && Tier(bothSpec) == StashRouting.TierCustomGroup, "spec=" + bothSpec);
                    var builtOk = StashRouting.CategoryMatch("Bone", bone, ownerId, out var builtSpec);
                    var customBetter = cgOk && builtOk && cgSpec > builtSpec;
                    Check(checks, "custom group spec > built-in 'Bone' spec", customBetter, "custom=" + cgSpec + " builtin=" + builtSpec);
                    Check(checks, "custom group: alias still exact for Grave Dust", StashRouting.ExactItemNameMatch(Alias, graveDust, out _));
                    Check(checks, "custom group: 'Alchemy' unchanged for Grave Dust", StashRouting.CategoryMatch("Alchemy", graveDust, ownerId, out var a3) && a3 == alchSpec);
                }
            }
            catch (Exception e)
            {
                checks.Add(("exception", false, e.Message));
            }
            finally
            {
                if (aliasBound)
                    ItemGroupService.UnregisterAliasInMemory(Alias);
                if (groupMade)
                    Core.PlayerSettings.DeleteItemGroup(ownerId, Group);
            }
            Check(checks, "cleanup: alias gone", !StashRouting.ExactItemNameMatch(Alias, graveDust, out _));
            Check(checks, "cleanup: custom group gone", !StashRouting.CategoryMatch(Group, bone, ownerId, out _));

            var allOk = true;
            var sb = new StringBuilder();
            sb.Append("{\"owner\":").Append(ownerId).Append(",\"checks\":[");
            for (var i = 0; i < checks.Count; i++)
            {
                var (name, ok, detail) = checks[i];
                if (!ok) allOk = false;
                if (i > 0) sb.Append(',');
                sb.Append("{\"ok\":").Append(ok ? "true" : "false")
                  .Append(",\"name\":\"").Append(Esc(name)).Append('"');
                if (!string.IsNullOrEmpty(detail))
                    sb.Append(",\"detail\":\"").Append(Esc(detail)).Append('"');
                sb.Append('}');
            }
            sb.Append("],\"ok\":").Append(allOk ? "true" : "false").Append('}');
            return sb.ToString();
        }

        static int SpecOf(string name, PrefabGUID item, ulong ownerId)
        {
            StashRouting.CategoryMatch(name, item, ownerId, out var spec);
            return spec;
        }

        static int Tier(int spec) => spec / StashRouting.TierStep;

        static void Check(List<(string, bool, string)> list, string name, bool ok, string detail = null)
            => list.Add((name, ok, detail));

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
