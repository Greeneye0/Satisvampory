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
var materialGoals = new Dictionary<int,NeedRules.MaterialDemand>();
NeedRules.AddMaterial(materialGoals, 10, 20, 12, 320); // character one
NeedRules.AddMaterial(materialGoals, 10, 30, 0, 330); // character two, same resource
NeedRules.AddMaterial(materialGoals, 10, 10, 3, 220); // servant, same resource
Check(materialGoals.Count == 1, "Shared upgrade material occupies one list entry");
Check(materialGoals[10].Required == 60 && materialGoals[10].Covered == 15 && materialGoals[10].Missing == 45, "Combined gear goal has honest covered and missing totals");
Check(materialGoals[10].Priority == 330, "Servant contribution does not lower character priority");
NeedRules.AddMaterial(materialGoals, 20, 5, 5, 300);
Check(materialGoals.Values.Count(g => g.Missing > 0) == 1, "Covered upgrades create no extra shortage row");
Check(!window.Consume(), "No number context before a list");
window.Arm(); Check(window.Consume(), "First follow-up selects the list");
Check(!window.Consume(), "Selection cannot be reused");
window.Arm(); window.Cancel(); Check(!window.Consume(), "Intervening command expires selection");
window.Arm(); window.Cancel(); window.Arm(); Check(window.Consume(), "A newer list establishes fresh context");
Console.WriteLine($"{checks} need-rule regression checks passed.");
