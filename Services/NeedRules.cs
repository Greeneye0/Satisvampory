using System;
using System.Collections.Generic;
using System.Linq;

namespace Satisvampory.Services
{
    // Pure rules shared by the report and its regression checks. No live state mutation.
    internal static class NeedRules
    {
        internal sealed class NumberWindow
        {
            bool armed;
            public void Arm() => armed = true;
            public void Cancel() => armed = false;
            public bool Consume() { var wasArmed = armed; armed = false; return wasArmed; }
        }
        internal sealed class MaterialDemand
        {
            public int Required, Covered, Priority;
            public int Missing => Short(Required, Covered);
        }
        internal static void AddMaterial(Dictionary<int, MaterialDemand> goals, int item, int required, int covered, int priority)
        {
            if (!goals.TryGetValue(item, out var goal)) goals[item] = goal = new();
            goal.Required = (int)Math.Min(int.MaxValue, (long)goal.Required + required);
            goal.Covered = (int)Math.Min(int.MaxValue, (long)goal.Covered + Math.Min(required, covered));
            goal.Priority = Math.Max(goal.Priority, priority);
        }
        public static int Short(int target, int held, int reachable = 0) =>
            (int)Math.Max(0L, (long)target - held - reachable);
        public static int Crafts(int amount, int yield) => amount <= 0 ? 0 : (int)Math.Min(int.MaxValue, ((long)amount + Math.Max(1, yield) - 1) / Math.Max(1, yield));
        public static int Cost(int amount, int crafts) => (int)Math.Min(int.MaxValue, (long)amount * crafts);
        public static int StockPriority(string name, int depth)
        {
            // Explicit endgame intent. Other products use recipe depth, never market/silver value.
            return name.ToLowerInvariant() switch {
                "onyx tear" or "onyx tears" => 100,
                "shadow weave" => 90,
                "power core" => 80,
                "gold ingot" => 70,
                "charged battery" => 60,
                _ => Math.Min(59, Math.Max(0, depth) * 5)
            };
        }
        public static List<T> BottomFirst<T>(IEnumerable<T> rows, Func<T, int> priority, Func<T, double> fraction, Func<T, int> id) =>
            rows.OrderByDescending(priority).ThenByDescending(fraction).ThenBy(id).Take(5).Reverse().ToList();
        public static int Draw(Dictionary<int, int> stock, int item, int amount)
        {
            stock.TryGetValue(item, out var held);
            var taken = Math.Min(Math.Max(0, held), Math.Max(0, amount));
            stock[item] = held - taken;
            return taken;
        }
    }
}
