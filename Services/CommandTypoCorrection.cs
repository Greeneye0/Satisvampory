using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VampireCommandFramework;

namespace Satisvampory.Services;

// Correct only command words in this mod's explicit command groups. Leave arguments
// intact so the normal converters, ambiguity handling and permission checks still run.
[HarmonyPatch(typeof(CommandRegistry), nameof(CommandRegistry.Handle))]
internal static class CommandTypoCorrection
{
    static readonly Lazy<Dictionary<string, HashSet<string>>> verbs = new(Build);
    static Dictionary<string, HashSet<string>> Build()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            var group = type.GetCustomAttribute<CommandGroupAttribute>();
            if (group == null) continue;
            foreach (var groupName in new[] { group.Name, group.ShortHand }.Where(x => !string.IsNullOrEmpty(x)))
            {
                if (!result.TryGetValue(groupName, out var names)) result[groupName] = names = new(StringComparer.OrdinalIgnoreCase);
                foreach (var method in type.GetMethods())
                {
                    var command = method.GetCustomAttribute<CommandAttribute>();
                    if (command == null) continue;
                    names.Add(command.Name);
                    if (!string.IsNullOrEmpty(command.ShortHand)) names.Add(command.ShortHand);
                }
            }
        }
        return result;
    }
    static void Prefix(ICommandContext ctx, ref string input)
    {
        if (ctx is not ChatCommandContext chat || string.IsNullOrWhiteSpace(input) || !input.StartsWith(".")) return;
        var firstSpace = input.IndexOf(' ');
        if (firstSpace < 2 || !verbs.Value.TryGetValue(input.Substring(1, firstSpace - 1), out var names)) return;
        var start = firstSpace + 1;
        while (start < input.Length && char.IsWhiteSpace(input[start])) start++;
        var end = start;
        while (end < input.Length && !char.IsWhiteSpace(input[end])) end++;
        var word = input.Substring(start, end - start);
        if (names.Contains(word) || int.TryParse(word, out _)) return;
        var matches = names.Where(name => ItemMatchRules.TypoScore(word, name, false) != int.MaxValue)
            .OrderBy(name => ItemMatchRules.TypoScore(word, name, false)).ThenBy(name => name).ToList();
        if (matches.Count == 1)
        {
            input = input.Substring(0, start) + matches[0] + input.Substring(end);
            chat.Reply($"Corrected command: .{input.Substring(1, firstSpace - 1)} {matches[0]}");
        }
        else if (matches.Count > 1)
        {
            var original = input;
            chat.Reply("Possible commands: " + string.Join(" | ", matches.Take(3).Select(name => original.Substring(0, start) + name + original.Substring(end))));
        }
    }
}
