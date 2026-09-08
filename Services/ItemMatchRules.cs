using System.Collections.Generic;
using System;
using System.Linq;

namespace Satisvampory.Services;

internal static class ItemMatchRules
{
    internal static int TypoScore(string input, string name, bool words = true)
    {
        input = (input ?? "").Trim().ToLowerInvariant();
        name = (name ?? "").Trim().ToLowerInvariant();
        if (input.Length < 4 || input.Length > 64 || name.Length > 160) return int.MaxValue;
        var limit = input.Length >= 8 ? 2 : 1;
        var candidates = new List<string> { name };
        if (words)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            for (var i = 0; i + count <= parts.Length; i++) candidates.Add(string.Join(" ", parts.Skip(i).Take(count)));
        }
        var best = int.MaxValue;
        foreach (var candidate in candidates)
        {
            if (Math.Abs(input.Length - candidate.Length) > limit) continue;
            var d = new int[input.Length + 1, candidate.Length + 1];
            for (var i = 0; i <= input.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= candidate.Length; j++) d[0, j] = j;
            for (var i = 1; i <= input.Length; i++)
            for (var j = 1; j <= candidate.Length; j++)
            {
                d[i,j] = Math.Min(Math.Min(d[i-1,j]+1, d[i,j-1]+1), d[i-1,j-1] + (input[i-1] == candidate[j-1] ? 0 : 1));
                if (i > 1 && j > 1 && input[i-1] == candidate[j-2] && input[i-2] == candidate[j-1])
                    d[i,j] = Math.Min(d[i,j], d[i-2,j-2]+1);
            }
            best = Math.Min(best, d[input.Length,candidate.Length]);
        }
        return best <= limit ? best : int.MaxValue;
    }
    internal static void DistinctItems<T>(List<T> matches)
    {
        var seen = new HashSet<T>();
        matches.RemoveAll(item => !seen.Add(item));
    }
}
