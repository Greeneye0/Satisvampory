using System.Collections.Generic;

namespace Satisvampory.Services;

internal static class ItemMatchRules
{
    internal static void DistinctItems<T>(List<T> matches)
    {
        var seen = new HashSet<T>();
        matches.RemoveAll(item => !seen.Add(item));
    }
}
