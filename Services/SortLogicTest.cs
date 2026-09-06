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
            // Fish Bone: in the built-in bones group but not literally named "Bone", so a plate
            // "Bone" is a category (group-word) match for it, not an exact item-name match.
            var fishBone = new PrefabGUID(424158416);
            var copperIngot = new PrefabGUID(-1237019921);
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

                // ---- "--word" exclusions ----
                var exWords = StashRouting.ParseExclusions("Weapons --copper --iron", out var exClean);
                Check(checks, "excl: parse 'Weapons --copper --iron' -> clean 'Weapons'", exClean == "Weapons", "clean=" + exClean);
                Check(checks, "excl: words are copper, iron", exWords.Count == 2 && exWords[0] == "copper" && exWords[1] == "iron");
                Check(checks, "excl: plus then exclusions: 'Weapons --iron+' -> 1 plus, clean 'Weapons'",
                    StashRouting.TrailingPlus("Weapons --iron+") == 1 && StashRouting.StripExclusions(StashRouting.StripTrailingPlus("Weapons --iron+")) == "Weapons");
                Check(checks, "excl: '--copper' matches Copper Ingot", StashRouting.ExclusionMatches("copper", copperIngot, ownerId));
                Check(checks, "excl: '--copper' does not match Grave Dust", !StashRouting.ExclusionMatches("copper", graveDust, ownerId));
                Check(checks, "excl: '--alchemy' (group word) matches Grave Dust", StashRouting.ExclusionMatches("alchemy", graveDust, ownerId));
                if (FoundItemConverter.TryGetExact("Copper Sword", out var swordEx) && swordEx.prefab.GuidHash != 0)
                    Check(checks, "excl: '--copper' matches Copper Sword (equipment fragment allowed)", StashRouting.ExclusionMatches("copper", swordEx.prefab, ownerId));

                // ---- belt tokens are whole tokens ----
                Check(checks, "token: 'Tailor1 R6S3S6' receives only on 6", string.Join(",", StashRouting.ReceiverGroups("Tailor1 R6S3S6")) == "6");
                Check(checks, "token: 'Tailor1 R6S3S6' sends on 3,6", string.Join(",", StashRouting.SenderGroups("Tailor1 R6S3S6")) == "3,6");
                Check(checks, "token: 'Silver1' is not a belt", StashRouting.LineSignature("Silver1").Length == 0);
                Check(checks, "token: 'Ore S2' sends on 2", string.Join(",", StashRouting.SenderGroups("Ore S2")) == "2");
                Check(checks, "token: 's1r1' glued at start parses", string.Join(",", StashRouting.SenderGroups("s1r1")) == "1" && string.Join(",", StashRouting.ReceiverGroups("s1r1")) == "1");

                // ---- same-line signature ----
                Check(checks, "line: 'Misc Ingots R2S2' == 'Metal2 S2R2'", StashRouting.LineSignature("Misc Ingots R2S2") == StashRouting.LineSignature("Metal2 S2R2") && StashRouting.LineSignature("Misc Ingots R2S2").Length > 0);
                Check(checks, "line: 'Metal1 R2S2S0' differs from 'Metal2 R2S2'", StashRouting.LineSignature("Metal1 R2S2S0") != StashRouting.LineSignature("Metal2 R2S2"));
                Check(checks, "line: plate without tokens has no line", StashRouting.LineSignature("Weapons").Length == 0);
                Check(checks, "line: '+' and --word do not change the line", StashRouting.LineSignature(StashRouting.StripExclusions(StashRouting.StripTrailingPlus("Metal2 R2S2 --iron+"))) == StashRouting.LineSignature("Metal2 R2S2"));

                // ---- priority '+' plate parsing ----
                Check(checks, "plus: 'Stone Brick R1S1++' has 2", StashRouting.TrailingPlus("Stone Brick R1S1++") == 2);
                Check(checks, "plus: 'Stone Brick R1S1 + ' has 1", StashRouting.TrailingPlus("Stone Brick R1S1 + ") == 1);
                Check(checks, "plus: 'Wood + Stone' has 0 (inner + is AND)", StashRouting.TrailingPlus("Wood + Stone") == 0);
                Check(checks, "plus: strip leaves 'Stone Brick R1S1'", StashRouting.StripTrailingPlus("Stone Brick R1S1++") == "Stone Brick R1S1");
                Check(checks, "plus: stripped name still exact-matches", StashRouting.ExactItemNameMatch(StashRouting.StripTrailingPlus("Grave Dust S1+"), graveDust, out _));
                Check(checks, "plus: skip-quotes still seen behind '+'", StashRouting.IsSkipQuotesName("Lock Box''+"));

                // ---- equipment never matches by name fragment ----
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
                if (FoundItemConverter.TryGetExact("Ancestral Sword Shards Tier 1 Shattered", out var shardFound) && shardFound.prefab.GuidHash != 0)
                {
                    var shard = shardFound.prefab;
                    Check(checks, "shattered: 'Shattered' matches a shard at group tier", StashRouting.CategoryMatch("Shattered", shard, ownerId, out var shSpec) && Tier(shSpec) == StashRouting.TierGroup, "spec=" + shSpec);
                    Check(checks, "shattered: 'Weapons' does not match a shard", !StashRouting.CategoryMatch("Weapons", shard, ownerId, out _));
                    Check(checks, "shattered: 'Shattered Weapons' (AND mode) matches a shard", StashRouting.CategoryMatch("Shattered Weapons", shard, ownerId, out _));
                    Check(checks, "shattered: 'Shattered Weapons' does not match Copper Ingot", !StashRouting.CategoryMatch("Shattered Weapons", copperIngot, ownerId, out _));
                    Check(checks, "shattered: 'Shattered' does not match Copper Ingot", !StashRouting.CategoryMatch("Shattered", copperIngot, ownerId, out _));
                }
                else
                {
                    checks.Add(("shattered: shard item not resolvable, skipped", true, ""));
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
                Check(checks, "alias: '--" + Alias + "' excludes Grave Dust", StashRouting.ExclusionMatches(Alias, graveDust, ownerId));
                Check(checks, "alias: '--" + Alias + "' does not exclude Bone", !StashRouting.ExclusionMatches(Alias, bone, ownerId));

                // ---- castle alias (owner row) ----
                const string CAlias = "svtcalias";
                var cErr = Core.PlayerSettings.SetCastleAlias(ownerId, CAlias, graveDust.GuidHash, gdName);
                Check(checks, "castle alias: set", cErr == null, cErr ?? "");
                Check(checks, "castle alias: exact match with owner", StashRouting.ExactItemNameMatch(CAlias, graveDust, out _, ownerId));
                Check(checks, "castle alias: case-insensitive lookup", StashRouting.ExactItemNameMatch(CAlias.ToUpperInvariant(), graveDust, out _, ownerId) && ItemGroupService.TryExactItemAlias(ownerId, "SVTCALIAS", out var upHash) && upHash == graveDust.GuidHash);
                Check(checks, "castle alias: not visible without owner", !StashRouting.ExactItemNameMatch(CAlias, graveDust, out _, 0));
                Check(checks, "castle alias: '--" + CAlias + "' excludes Grave Dust", StashRouting.ExclusionMatches(CAlias, graveDust, ownerId));
                Check(checks, "castle alias: does not match Bone", !StashRouting.ExactItemNameMatch(CAlias, bone, out _, ownerId));
                Check(checks, "castle alias: removed", Core.PlayerSettings.RemoveCastleAlias(ownerId, CAlias));
                Check(checks, "castle alias: gone", !StashRouting.ExactItemNameMatch(CAlias, graveDust, out _, ownerId));

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
                    Core.PlayerSettings.AddItemToGroup(ownerId, Group, fishBone, StashRouting.ItemLabel(fishBone));
                    var fbCustom = StashRouting.CategoryMatch(Group, fishBone, ownerId, out var fbCustomSpec);
                    var builtOk = StashRouting.CategoryMatch("Bone", fishBone, ownerId, out var builtSpec);
                    Check(checks, "built-in 'Bone' plate matches Fish Bone at group tier", builtOk && Tier(builtSpec) == StashRouting.TierGroup, "spec=" + builtSpec);
                    var customBetter = fbCustom && builtOk && fbCustomSpec > builtSpec;
                    Check(checks, "custom group spec > built-in 'Bone' spec for Fish Bone", customBetter, "custom=" + fbCustomSpec + " builtin=" + builtSpec);
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
