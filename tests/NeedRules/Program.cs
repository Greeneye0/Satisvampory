using Satisvampory.Services;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
Check(NeedRules.Short(3, 0, 0) == 3, "Zero stock remains visible");
Check(NeedRules.Short(3, 0, 1) == 2, "Silk reserve blocker");
Check(NeedRules.Short(3, 3, 0) == 0, "Hopper already supplied");
Check(NeedRules.Short(3, 1, 2) == 0, "One complete craft available");
Check(NeedRules.Short(15, 0, 3) == 12, "Emery above reserve shortage");
Check(NeedRules.Short(10, 50) == 0, "Surplus is not negative demand");
Check(NeedRules.Short(0, 0) == 0, "Zero goal disables demand");
Check(NeedRules.Crafts(8, 3) == 3, "Round up multi-output craft count");
Check(NeedRules.Cost(int.MaxValue, 2) == int.MaxValue, "Large goal cost saturates");
Check(NeedRules.Short(int.MaxValue, int.MaxValue, int.MaxValue) == 0, "Large inventories do not wrap");
var stock = new Dictionary<int,int> { [1] = 9 };
Check(NeedRules.Draw(stock, 1, 6) == 6, "First upgrade reserves six locally");
Check(NeedRules.Draw(stock, 1, 6) == 3, "Second upgrade cannot reuse those six");
Check(stock[1] == 0, "No negative shared stock");
Check(NeedRules.Draw(stock, 2, 4) == 0, "Missing ingredient remains missing");
Check(NeedRules.Draw(stock, 1, -2) == 0, "Negative request cannot add stock");
var rows = new[] {
    (id:1, name:"Onyx Tear", have:2, target:10),
    (id:2, name:"Shadow Weave", have:48, target:100),
    (id:3, name:"Power Core", have:12, target:20),
    (id:4, name:"Gold Ingot", have:30, target:50),
    (id:5, name:"Charged Battery", have:6, target:10),
    (id:6, name:"Plank", have:0, target:1000) };
var ranked = NeedRules.BottomFirst(rows, r => NeedRules.StockPriority(r.name, 1), r => (r.target-r.have)/(double)r.target, r => r.id);
Check(ranked.Select(r=>r.id).SequenceEqual(new[]{5,4,3,2,1}), "Live stock example: highest five selected before reverse");
Check(ranked.Last().name == "Onyx Tear", "Highest priority at bottom");
Check(NeedRules.BottomFirst(rows.Take(2), _=>1, _=>0, r=>r.id).Select(r=>r.id).SequenceEqual(new[]{2,1}), "Stable ID ties");
Check(NeedRules.BottomFirst(rows.Reverse(), r=>NeedRules.StockPriority(r.name,1), _=>0, r=>r.id).Select(r=>r.id).SequenceEqual(new[]{5,4,3,2,1}), "Input enumeration order cannot change ranks");
Check(NeedRules.BottomFirst(Array.Empty<int>(), x=>x, x=>x, x=>x).Count == 0, "No manufactured empty goals");
Check(NeedRules.StockPriority("ONYX TEARS",0)==100, "Case-insensitive endgame intent");
var window = new NeedRules.NumberWindow();
var materialGoals = new Dictionary<(int item, bool servant),NeedRules.MaterialDemand>();
NeedRules.AddMaterial(materialGoals, 10, false, 20, 12, NeedRules.GearPriority(false, 20));
NeedRules.AddMaterial(materialGoals, 10, false, 30, 0, NeedRules.GearPriority(false, 30));
NeedRules.AddMaterial(materialGoals, 10, true, 10, 3, NeedRules.GearPriority(true, 20));
Check(materialGoals.Count == 2, "Same material has distinct player and servant goals");
Check(materialGoals[(10,false)].Required == 50 && materialGoals[(10,false)].Covered == 12 && materialGoals[(10,false)].Missing == 38, "Players aggregate only player needs");
Check(materialGoals[(10,true)].Required == 10 && materialGoals[(10,true)].Missing == 7, "Servant shortage remains separate");
Check(NeedRules.GearPriority(false, 0) > NeedRules.GearPriority(true, int.MaxValue), "Every player need outranks every servant need");
var splitRank = NeedRules.BottomFirst(materialGoals, x => x.Value.Priority, x => x.Value.Missing / (double)x.Value.Required, x => x.Key.item);
Check(!splitRank.Last().Key.servant, "Player goal is highest and printed last even for same item");
NeedRules.AddMaterial(materialGoals, 20, true, 5, 5, NeedRules.GearPriority(true, 0));
Check(materialGoals.Values.Count(g => g.Missing > 0) == 2, "Covered upgrades create no shortage row");
var shared = new Dictionary<int,int> { [10] = 40 };
var playerAllocated = NeedRules.Draw(shared, 10, 30);
var servantAllocated = NeedRules.Draw(shared, 10, 30);
Check(playerAllocated == 30 && servantAllocated == 10, "Players receive shared material before servants, without double counting");
Check(!window.Consume(), "No number context before a list");
window.Arm(); Check(window.Consume(), "First follow-up selects the list");
Check(!window.Consume(), "Selection cannot be reused");
window.Arm(); window.Cancel(); Check(!window.Consume(), "Intervening command expires selection");
window.Arm(); window.Cancel(); window.Arm(); Check(window.Consume(), "A newer list establishes fresh context");
var recipes = new Dictionary<int,NeedRules.ChainRecipe> {
    [100] = new() { Yield = 1, Inputs = new() { [101] = 2, [102] = 3 } },
    [101] = new() { Yield = 1, Inputs = new() { [103] = 4 } }
};
var emitted = new List<(int id, int amount, string action)>();
void Emit(int id, int amount, string action, IReadOnlyList<int> path) => emitted.Add((id,amount,action));
NeedRules.Expand(100, 2, new() { [101] = 1, [102] = 2 }, id => recipes.GetValueOrDefault(id), Emit);
Check(emitted.Any(x => x.id == 103 && x.amount == 12) && emitted.Any(x => x.id == 102 && x.amount == 4), "Both missing branches become separate full-goal collection needs");
Check(emitted.Count == 2 && !emitted.Any(x => x.id == 100), "Blocked finished product does not obscure its raw shortages");
emitted.Clear();
NeedRules.Expand(100, 1, new() { [101] = 2, [102] = 3 }, id => recipes.GetValueOrDefault(id), Emit);
Check(emitted.Count == 1 && emitted[0] == (100,1,"Craft"), "Supplied product requests crafting instead of more gathering");
emitted.Clear();
var chainStock = new Dictionary<int,int> { [103] = 8 };
NeedRules.Expand(101, 2, chainStock, id => recipes.GetValueOrDefault(id), Emit);
NeedRules.Expand(101, 1, chainStock, id => recipes.GetValueOrDefault(id), Emit);
Check(emitted.Any(x => x == (103,4,"Supply")), "Servant chain cannot reuse raw materials claimed by player chain");
emitted.Clear();
recipes[104] = new() { Yield = 3, Inputs = new() { [105] = 2 } };
var batchStock = new Dictionary<int,int>();
NeedRules.Expand(104, 1, batchStock, id => recipes.GetValueOrDefault(id), Emit);
NeedRules.Expand(104, 2, batchStock, id => recipes.GetValueOrDefault(id), Emit);
Check(emitted.Count == 1 && emitted[0].amount == 2, "Batch surplus prevents duplicate gathering goals");
emitted.Clear();
recipes[106] = new() { Inputs = new() { [106] = 1 } };
NeedRules.Expand(106, 1, new(), id => recipes.GetValueOrDefault(id), Emit);
Check(emitted.Single().action == "Unverified", "Recipe cycles stop with an honest unresolved action");
emitted.Clear();
NeedRules.Expand(100, 1, new(), id => recipes.GetValueOrDefault(id), Emit, true, new() { [101] = 2, [102] = 3 });
Check(emitted.Single().action == "Craft", "Carried upstream ingredients reduce farming demand");
Console.WriteLine($"{checks} need-rule regression checks passed.");
