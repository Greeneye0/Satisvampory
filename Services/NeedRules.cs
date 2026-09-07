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
        internal static void AddMaterial(Dictionary<(int item, bool servant), MaterialDemand> goals, int item, bool servant, int required, int covered, int priority)
        {
            if (!goals.TryGetValue((item, servant), out var goal)) goals[(item, servant)] = goal = new();
            goal.Required = (int)Math.Min(int.MaxValue, (long)goal.Required + required);
            goal.Covered = (int)Math.Min(int.MaxValue, (long)goal.Covered + Math.Min(required, covered));
            goal.Priority = Math.Max(goal.Priority, priority);
        }
        internal static int GearPriority(bool servant, int depth) => (servant ? 200 : 300) + Math.Clamp(depth, 0, 50);
        internal sealed class ChainRecipe
        {
            public int Yield = 1;
            public Dictionary<int, int> Inputs = new();
        }
        // Emit each missing branch, claiming supplies once across every goal in priority order.
        // A null recipe is a terminal material or an explicitly unresolved production step.
        internal static void Expand(int item, int amount, Dictionary<int, int> stock,
            Func<int, ChainRecipe> recipeFor, Action<int, int, string, IReadOnlyList<int>> emit,
            bool claimRoot = true, Dictionary<int, int> personal = null)
        {
            var emissions = 0;
            void Visit(int id, int required, List<int> path, bool claim)
            {
                var carried = claim && personal != null ? Draw(personal, id, required) : 0;
                var missing = Short(required, carried + (claim ? Draw(stock, id, required - carried) : 0));
                if (missing == 0) return;
                if (path.Contains(id) || path.Count >= 12)
                { emit(id, missing, "Unverified", path); return; }
                var recipe = recipeFor(id);
                if (recipe == null || recipe.Inputs.Count == 0)
                { emit(id, missing, "Supply", path); return; }
                var next = new List<int>(path) { id };
                var crafts = Crafts(missing, recipe.Yield);
                var emitted = false;
                // Track whether this branch needs gathering or only an already-supplied craft.
                var before = emissions;
                foreach (var input in recipe.Inputs.OrderBy(x => x.Key))
                    Visit(input.Key, Cost(input.Value, crafts), next, true);
                stock[id] = (int)Math.Min(int.MaxValue, (long)stock.GetValueOrDefault(id) + Math.Max(0, Cost(recipe.Yield, crafts) - missing));
                emitted = emissions != before;
                if (!emitted) Send(id, missing, "Craft", path);
            }
            var sink = emit;
            void Send(int id, int missing, string action, IReadOnlyList<int> path)
            { emissions++; sink(id, missing, action, path); }
            emit = Send;
            Visit(item, amount, new(), claimRoot);
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
