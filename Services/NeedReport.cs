using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.Entities;
using VampireCommandFramework;
using Satisvampory.Commands.Converters;

namespace Satisvampory.Services
{
    internal static class NeedReport
    {
        // DTOs deliberately contain observations only; report calculations never mutate ECS.
        #pragma warning disable CS0649 // Populated from the read-only debug snapshot by System.Text.Json.
        public class Amount { public int guid, amount, reserve, stackable, availableAfterReserve, perCraft, inStation; public string name; public int? cap; }
        public class Source { public int plot; public string name, inventory; public bool overflow, noShare; public List<int> senderGroups = new(); public List<Amount> items = new(); }
        public class RecipeRow { public int guid; public bool unlocked, disabled; public List<Amount> requirements = new(), outputs = new(); }
        public class Station { public int plot; public string name, status, inputInventory, outputInventory; public bool disabled; public float floorScale; public List<int> receiverGroups = new(), senderGroups = new(); public List<Amount> input = new(), output = new(); public List<RecipeRow> recipes = new(); }
        public class Snapshot { public bool serverConveyorEnabled, plannerOwnerConveyorEnabled; public List<Source> sources = new(); public List<Station> stations = new(); }
        #pragma warning restore CS0649
        sealed class Entry { public int Id, Have, Target, Priority; public string Action, Purpose, Reason, Color = "yellow"; public List<string> Details = new(), Reasons = new(); public HashSet<int> Goals = new(); }
        sealed class Saved { public DateTime At; public List<int> Plots; public List<Entry> Rows; public NeedRules.NumberWindow Number = new(); public int Selected; }
        sealed class Context
        {
            public int Plot; public ulong Owner; public List<int> Plots; public Snapshot Data;
            public Dictionary<int, int> Local = new(), Island = new();
            public Dictionary<int, int> Remaining;
            public HashSet<int> GearProducts = new();
            public HashSet<int> Unlocked = new(), BuiltRecipes = new(), Blueprints = new();
            public Dictionary<int, string> CraftStations = new();
            public Dictionary<int, Dictionary<int, int>> CraftCosts = new();
            public bool UnlocksKnown; public JsonDocument Gear;
        }
        static readonly Dictionary<ulong, Saved> saved = new();
        static readonly JsonSerializerOptions json = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };
        static string L(int id) => NeedCatalog.Label(id);
        static string C(string text, string color = "yellow") => $"<color={color}>{text}</color>";
        static string Safe(string s) => (s ?? "Unnamed").Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
        static int N(Dictionary<int, int> dict, int id) => dict.TryGetValue(id, out var n) ? n : 0;
        static void Add(Dictionary<int, int> dict, int id, int n) => dict[id] = (int)Math.Min(int.MaxValue, (long)N(dict, id) + n);

        internal static void Show(ChatCommandContext ctx, string query = null, string scope = null)
        {
            if (!Core.HasInitialized) { ctx.Reply("Satisvampory is not ready."); return; }
            var plot = Core.TerritoryService.GetStandingTerritoryId(ctx.Event.SenderCharacterEntity);
            var permitted = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(ctx.Event.SenderCharacterEntity).ToList();
            if (plot < 0 || !permitted.Contains(plot)) { ctx.Reply("Stand in your castle to inspect needs."); return; }
            query = query?.Trim(); scope = scope?.Trim().ToLowerInvariant();
            if (query is "here" or "clan") { scope = query; query = null; }
            if (scope != null && scope != "here" && scope != "clan") { ctx.Reply("Scope: here or clan. Example: .s need \"Shadow Weave\" here"); return; }
            if (scope == "clan" && !Core.TerritoryService.IsClanShareOn(ctx.Event.User)) { ctx.Reply("Clan scope requires ClanShare enabled."); return; }
            var plots = scope == "here" ? new List<int> { plot } : permitted;
            var userId = ctx.Event.User.PlatformId;
            if (saved.TryGetValue(userId, out var recent) && (DateTime.UtcNow - recent.At).TotalSeconds < 2)
            { ctx.Reply("Please wait two seconds between need reports."); return; }
            NeedCatalog.Ensure();
            using var gear = JsonDocument.Parse(GearDebug.Snapshot(plot));
            var c = Capture(plot, plots, gear);
            var settings = Core.PlayerSettings.Snapshot(userId, false);
            var mode = settings.NeedGoal ?? "auto";
            if (!string.IsNullOrWhiteSpace(query))
            {
                var alias = ItemGroupService.TryExactItemAlias(c.Owner, query, out var aliasId);
                if (!alias && FoundItemConverter.TryResolve(query, out _, out _) != ItemResolveStatus.Unique)
                { ctx.Reply("Item name is unknown or ambiguous. Use its full name or an item alias."); return; }
                FoundItemConverter.TryResolve(query, out var item, out _);
                var entry = Stock(c, alias ? aliasId : item.prefab.GuidHash, settings, true);
                SaveAndPrint(ctx, c, new List<Entry> { entry }, "Item", true);
                return;
            }
            var rows = new List<Entry>();
            var unknownGear = false;
            if (mode != "stock") GearGoals(c, rows, ctx.Event.User.CharacterName.ToString(), plots.Count > 1, ref unknownGear);
            var gearCount = rows.Count;
            if (mode != "gear")
            {
                var ids = c.Local.Keys.Concat(NeedCatalog.Products.Keys).Distinct();
                if (settings.NeedTargets != null) ids = ids.Concat(settings.NeedTargets.Keys.Select(x => int.TryParse(x, out var id) ? id : 0)).Distinct();
                foreach (var id in ids)
                {
                    if (!IsStockMaterial(id, settings) || (!c.Local.ContainsKey(id) && settings.NeedTargets?.ContainsKey(id.ToString()) != true
                        && !NeedCatalog.Products.GetValueOrDefault(id, new()).Any(r => c.BuiltRecipes.Contains(r.Id)))) continue;
                    var e = Stock(c, id, settings, false);
                    if (e != null && !c.GearProducts.Contains(id) && !rows.Any(x => x.Id == id)) rows.Add(e);
                }
            }
            // At reserve, maintain enough surplus for one blocked downstream craft; never consume the reserve.
            if (mode != "gear" && rows.Count < 5)
            {
                foreach (var station in c.Data.stations.Where(s => s.plot == plot && !s.disabled && s.receiverGroups.Count > 0))
                foreach (var recipe in station.recipes.Where(r => r.unlocked && !r.disabled && r.outputs.Count > 0
                    && (!r.outputs[0].cap.HasValue || N(c.Island, r.outputs[0].guid) + r.outputs[0].perCraft <= r.outputs[0].cap.Value))
                    .GroupBy(r => r.outputs[0].guid)
                    .Select(g => g.OrderBy(r => r.requirements.Sum(a => NeedRules.Short(a.perCraft, a.inStation, Reach(c, station, a.guid)))).ThenBy(r => r.guid).First()))
                foreach (var input in recipe.requirements)
                {
                    if (rows.Any(r => r.Id == input.guid) || !IsStockMaterial(input.guid, settings)
                        || settings.NeedTargets?.ContainsKey(input.guid.ToString()) == true) continue;
                    var shortfall = NeedRules.Short(input.perCraft, input.inStation, Reach(c, station, input.guid));
                    if (shortfall <= 0) continue;
                    var reserve = Core.PlayerSettings.GetPullReserve(c.Owner, new PrefabGUID(input.guid));
                    var have = N(c.Local, input.guid);
                    if (have < reserve) continue;
                    var e = new Entry { Id = input.guid, Have = have, Target = have + shortfall,
                        Priority = Math.Max(0, NeedRules.StockPriority(L(input.guid), NeedCatalog.Depth(input.guid)) - 1),
                        Reason = $"{L(input.guid)} — need {shortfall} above reserve for {L(recipe.outputs.FirstOrDefault()?.guid ?? 0)}" };
                    rows.Add(e);
                }
            }
            ExpandStock(c, rows);
            var title = mode == "stock" ? "Stock" : gearCount > 0 ? "Gear first" : mode == "gear" ? "Gear" : "Stock (gear covered or unavailable)";
            if (unknownGear) title += "; some gear unknown";
            SaveAndPrint(ctx, c, rows, title);
        }

        static Context Capture(int plot, List<int> plots, JsonDocument gear)
        {
            Core.TerritoryService.TryGetTerritoryOwnerPlatformId(plot, out var owner);
            var c = new Context { Plot = plot, Owner = owner, Plots = plots, Gear = gear,
                Data = JsonSerializer.Deserialize<Snapshot>(NeedDebug.Snapshot(plot, plots), json) };
            var seen = new HashSet<string>();
            void Count(string inv, int home, IEnumerable<Amount> amounts)
            {
                if (inv == null || !seen.Add(inv)) return;
                foreach (var a in amounts) { Add(c.Island, a.guid, a.amount); if (home == plot) Add(c.Local, a.guid, a.amount); }
            }
            foreach (var source in c.Data.sources.Where(s => !s.noShare)) Count(source.inventory, source.plot, source.items);
            foreach (var station in c.Data.stations)
            { Count(station.inputInventory, station.plot, station.input); Count(station.outputInventory, station.plot, station.output); }
            if (gear.RootElement.TryGetProperty("players", out var players))
            foreach (var p in players.EnumerateArray())
                if (p.TryGetProperty("progression", out var prog) && prog.TryGetProperty("unlockedBufferPresent", out var present) && present.GetBoolean())
                {
                    c.UnlocksKnown = true;
                    foreach (var r in prog.GetProperty("unlocked").EnumerateArray()) c.Unlocked.Add(r.GetInt32());
                    if (prog.TryGetProperty("blueprints", out var blueprints)) foreach (var b in blueprints.EnumerateArray()) c.Blueprints.Add(b.GetInt32());
                }
            foreach (var s in c.Data.stations)
                foreach (var r in s.recipes)
                { c.BuiltRecipes.Add(r.guid); if (r.unlocked) c.Unlocked.Add(r.guid); }
            if (gear.RootElement.TryGetProperty("stations", out var benches))
            foreach (var s in benches.EnumerateArray())
            {
                if (!plots.Contains(s.GetProperty("plot").GetInt32())) continue;
                foreach (var r in s.GetProperty("recipes").EnumerateArray())
                {
                    var id = r.GetProperty("guid").GetInt32(); c.BuiltRecipes.Add(id); c.CraftStations[id] = s.GetProperty("name").GetString();
                    var costs = new Dictionary<int,int>();
                    foreach (var a in r.GetProperty("inputs").EnumerateArray()) Add(costs, a.GetProperty("item").GetProperty("guid").GetInt32(), a.GetProperty("amount").GetInt32());
                    if (!c.CraftCosts.TryGetValue(id, out var old) || costs.Values.Sum() < old.Values.Sum()) c.CraftCosts[id] = costs;
                }
            }
            return c;
        }
        static bool IsStockMaterial(int id, SettingsRow settings)
        {
            if (id == 0 || NeedCatalog.Gear.ContainsKey(id)) return false;
            if (settings.NeedTargets?.ContainsKey(id.ToString()) == true) return true;
            if (!Core.GameDataSystem.ItemHashLookupMap.TryGetValue(new PrefabGUID(id), out var item)) return false;
            var category = item.ItemCategory;
            if ((category & (ItemCategory.Weapon | ItemCategory.Armor | ItemCategory.Magic | ItemCategory.Knowledge)) != 0) return false;
            // Stock fallback is for craftable production materials, not every reserve-protected collectible.
            var prefabName = new PrefabGUID(id).PrefabName();
            if (prefabName.Contains("SoulShard", StringComparison.OrdinalIgnoreCase) || prefabName.Contains("Seed", StringComparison.OrdinalIgnoreCase)) return false;
            return NeedCatalog.Products.ContainsKey(id) && NeedCatalog.Ingredients.Contains(id);
        }
        static Entry Stock(Context c, int id, SettingsRow settings, bool always)
        {
            var target = Core.PlayerSettings.GetPullReserve(c.Owner, new PrefabGUID(id));
            if (settings.NeedTargets?.TryGetValue(id.ToString(), out var custom) == true) target = custom;
            var have = N(c.Local, id);
            if (!always && (target <= 0 || have >= target)) return null;
            var shortfall = NeedRules.Short(target, have);
            var e = new Entry { Id = id, Have = have, Target = target,
                Priority = NeedRules.StockPriority(L(id), NeedCatalog.Depth(id)),
                Color = shortfall == 0 ? "green" : "yellow", Reason = $"{L(id)}: {have}/{target} — " + (shortfall > 0 ? $"need {shortfall}" : "target met") };
            if (shortfall > 0 && NeedCatalog.Products.TryGetValue(id, out var recipes))
            {
                if (c.UnlocksKnown && !recipes.Any(r => r.Always || c.Unlocked.Contains(r.Id))) e.Reason += " • unlock needed";
                else if (!recipes.Any(r => c.BuiltRecipes.Contains(r.Id))) e.Reason += " • station unverified";
            }
            return e;
        }
        static int Reach(Context c, Station station, int id) => Sources(c, station, id).Sum(x => x.available);
        static List<(string name, int plot, int have, int reserve, int available)> Sources(Context c, Station station, int id)
        {
            var list = new List<(string, int, int, int, int)>(); var seen = new HashSet<string>();
            foreach (var source in c.Data.sources)
            {
                if (source.noShare || !seen.Add(source.inventory)) continue;
                if (station != null && !(source.overflow || source.senderGroups.Intersect(station.receiverGroups).Any())) continue;
                var a = source.items.FirstOrDefault(a => a.guid == id);
                if (a != null) list.Add((Safe(source.name), source.plot, a.amount, source.overflow ? 0 : a.reserve, a.availableAfterReserve));
            }
            foreach (var source in c.Data.stations)
            {
                if (!seen.Add(source.outputInventory)) continue;
                if (station != null && !source.senderGroups.Intersect(station.receiverGroups).Any()) continue;
                var a = source.output.FirstOrDefault(a => a.guid == id);
                if (a != null) list.Add((Safe(source.name) + " output", source.plot, a.amount, 0, a.amount));
            }
            return list;
        }
        static void Explain(Context c, Entry e)
        {
            if (e.Action != null)
            {
                e.Details.Add(C($"Next: {e.Action} {e.Target} {L(e.Id)} for {e.Purpose}.", e.Color));
                e.Details.AddRange(e.Reasons.Take(1));
                if (e.Action == "Collect") e.Details.Add("Gather/hunt supply: no crafting recipe is verified for this ingredient. Farming location is not verified.");
                else if (e.Action == "Craft") e.Details.Add("Ingredients are covered in this plan; craft this step before its downstream product.");
                else if (e.Action == "Setup") e.Details.Add("Production is not verified as usable. Check recipe unlock, station and enabled recipe below; this is not a farming shortage.");
                else e.Details.Add("Recipe cycle or depth limit: required source is unverified.");
                e.Details.Add($"Stock: castle {N(c.Local, e.Id)}, scoped total {N(c.Island, e.Id)}; {Reach(c, null, e.Id)} usable before gear allocation. Listed need is additional to allocated supply.");
            }
            else
            {
                e.Details.Add(C($"{L(e.Id)} • castle {c.Plot}: {N(c.Local, e.Id)} • clan stock {N(c.Island, e.Id)}", "white"));
                if (e.Purpose != null) {
                var supply = Sources(c, null, e.Id);
                e.Details.Add($"{e.Purpose}: requires {e.Target}; allocated {e.Have} including carried ingredients; still need {NeedRules.Short(e.Target, e.Have)}.");
                e.Details.Add($"Supply before gear allocation: {supply.Sum(x => x.available)} usable; {supply.Sum(x => Math.Min(x.have, x.reserve))} protected in source chests. Total stock above also includes station inputs.");
                e.Details.Add("Players get shared supplies first; servants get the remainder. Protected stock is not spent.");
                }
            }
            var locations = c.Data.sources.Where(s => !s.noShare).SelectMany(s => s.items.Where(a => a.guid == e.Id)
                .Select(a => $"{Safe(s.name)} @ {s.plot}: {a.amount}, reserve {a.reserve}"));
            Trace(c, e.Id, e.Details, new HashSet<int>(), 0);
            foreach (var line in locations.Take(20)) e.Details.Add("Stored: " + line);
            e.Details.AddRange(e.Reasons.Distinct());
        }
        static void Trace(Context c, int product, List<string> lines, HashSet<int> path, int depth)
        {
            if (lines.Count >= 80) return;
            if (depth >= 6 || !path.Add(product)) { lines.Add("Further prerequisites: cycle/depth limit; inspect the ingredient separately."); return; }
            var choices = c.Data.stations.SelectMany(s => s.recipes.Where(r => r.outputs.Any(o => o.guid == product)).Select(r => (s, r)))
                .OrderBy(x => x.s.disabled || !x.r.unlocked || x.r.disabled)
                .ThenBy(x => x.r.requirements.Sum(a => NeedRules.Short(a.perCraft, a.inStation, Reach(c, x.s, a.guid))))
                .ThenBy(x => x.r.guid).ToList();
            if (choices.Count > 0)
            {
                var (station, recipe) = choices[0];
                lines.Add($"Make {L(product)}: {Safe(station.name)} @ {station.plot} • R{string.Join(",R", station.receiverGroups)} • one craft");
                if (!recipe.unlocked) lines.Add(C(UnlockText(recipe.guid, c)));
                else if (recipe.disabled || station.disabled) lines.Add(C("BLOCKED: station or recipe disabled."));
                else if (station.receiverGroups.Count == 0) lines.Add(C("SETUP: no receiver line on this station."));
                else if (!c.Data.serverConveyorEnabled || !c.Data.plannerOwnerConveyorEnabled) lines.Add(C("BLOCKED: conveyor automation off."));
                if (station.status is "OutputFull" or "NotPowered") lines.Add(C("BLOCKED: " + station.status));
                var output = recipe.outputs.First(a => a.guid == product);
                if (output.cap.HasValue && N(c.Island, product) + output.perCraft > output.cap.Value) lines.Add(C($"CAP: clan stock {N(c.Island, product)}, production cap {output.cap}."));
                var blockers = new List<(int id, int shortfall)>();
                foreach (var a in recipe.requirements)
                {
                    var sources = Sources(c, station, a.guid);
                    var available = sources.Sum(x => x.available);
                    var missing = NeedRules.Short(a.perCraft, a.inStation, available);
                    var protectedStock = sources.Sum(x => Math.Max(0, x.have - x.available));
                    var status = missing == 0 ? "READY" : protectedStock >= missing ? "RESERVED" : "SHORT";
                    lines.Add(C($"{L(a.guid)}: needs {a.perCraft} • held {a.inStation} • available {available} • {status}" + (missing > 0 ? $" {missing}" : ""), missing == 0 ? "green" : protectedStock >= missing ? "yellow" : "red"));
                    foreach (var source in sources.OrderByDescending(x => x.available)) lines.Add($"  {source.name} @ {source.plot}: {source.have} • reserve {source.reserve} • available {source.available}");
                    if (missing > 0)
                    {
                        var elsewhere = c.Data.sources.Where(s => !s.noShare && !s.overflow && !s.senderGroups.Intersect(station.receiverGroups).Any())
                            .SelectMany(s => s.items.Where(x => x.guid == a.guid).Select(x => $"{Safe(s.name)} @ {s.plot}: {x.amount} (reserve {x.reserve})"));
                        foreach (var source in elsewhere) lines.Add("Off line: " + source);
                        lines.Add(protectedStock >= missing ? $"Supply {missing} more {L(a.guid)} above reserve, or deliberately lower the source reserve." : $"Supply {missing} more accessible {L(a.guid)} to this line.");
                        blockers.Add((a.guid, missing));
                    }
                }
                if (blockers.Count == 0) lines.Add(C("Ingredients ready for one craft; shared supplies may be claimed by other stations.", "green"));
                if (choices.Count > 1) lines.Add($"Showing the least-blocked recipe/station of {choices.Count}; alternatives are not added together.");
                foreach (var b in blockers.OrderByDescending(b => NeedCatalog.Depth(b.id)).Take(2))
                    if (NeedCatalog.Products.ContainsKey(b.id)) Trace(c, b.id, lines, path, depth + 1);
            }
            else if (NeedCatalog.Products.TryGetValue(product, out var recipes))
            {
                var recipe = recipes.OrderBy(r => !(r.Always || c.Unlocked.Contains(r.Id))).ThenBy(r => !c.BuiltRecipes.Contains(r.Id)).ThenBy(r => r.Id).First();
                lines.Add(C(UnlockText(recipe.Id, c), "yellow"));
                if (!c.BuiltRecipes.Contains(recipe.Id))
                {
                    lines.Add(C("STATION: " + (recipe.Stations.Count == 0 ? "required station not verified" : "build/use " + string.Join(" or ", recipe.Stations.Select(x => Safe(NeedCatalog.Prefab(x).EntityName())).Distinct()))));
                    // Explain one verified construction option, not the sum of every alternative station.
                    foreach (var stationId in recipe.Stations.Take(1))
                    {
                        var station = NeedCatalog.Prefab(stationId);
                        if (!Core.EntityManager.Exists(station) || !station.Has<BlueprintData>()) { lines.Add("Station construction requirements not verified."); continue; }
                        var blueprint = station.Read<BlueprintData>();
                        if (!blueprint.IsStartBlueprint && !c.Blueprints.Contains(blueprint.Guid.GuidHash))
                            lines.Add(C("Station unlock: " + (NeedCatalog.StationUnlocks.TryGetValue(blueprint.Guid.GuidHash, out var unlocks) ? string.Join(" or ", unlocks.Distinct()) : "research/progression requirement not verified")));
                        if (station.Has<BlueprintRequirementBuffer>())
                        foreach (var requirement in station.ReadBuffer<BlueprintRequirementBuffer>())
                        {
                            var cost = (int)Math.Ceiling(requirement.Amount * (double)Core.ServerGameSettingsSystem.Settings.BuildCostModifier);
                            lines.Add($"Build: {L(requirement.PrefabGUID.GuidHash)} {cost} • castle stock {N(c.Local, requirement.PrefabGUID.GuidHash)}");
                        }
                    }
                }
                else lines.Add("Craft at " + Safe(c.CraftStations.GetValueOrDefault(recipe.Id, "available crafting station")) + " (manual crafting).");
                var costs = c.CraftCosts.GetValueOrDefault(recipe.Id, recipe.Inputs);
                foreach (var a in costs)
                {
                    var available = Sources(c, null, a.Key).Sum(x => x.available);
                    var missing = NeedRules.Short(a.Value, available);
                    lines.Add(C($"{L(a.Key)}: cost {a.Value} • accessible stock {available}" + (missing > 0 ? $" • short {missing}" : " • ready"), missing > 0 ? "yellow" : "green"));
                    if (missing > 0 && NeedCatalog.Products.ContainsKey(a.Key)) Trace(c, a.Key, lines, path, depth + 1);
                }
                lines.Add(c.CraftCosts.ContainsKey(recipe.Id) ? "Costs use the best available matching floor; crafting is manual." : "Base costs shown: station discounts are not verified; crafting is manual.");
            }
            else lines.Add("No verified crafting recipe: gather, hunt, or inspect the item's source.");
            path.Remove(product);
        }
        static string UnlockText(int recipe, Context c)
        {
            if (NeedCatalog.Recipes.TryGetValue(recipe, out var r))
            {
                if (r.Always || c.Unlocked.Contains(recipe)) return "Recipe unlocked.";
                if (!c.UnlocksKnown) return "UNKNOWN: progression data unavailable; unlock not verified.";
                return "LOCKED: " + (r.Unlocks.Count > 0 ? string.Join(" or ", r.Unlocks.Distinct()) : "unlock source not verified");
            }
            return "UNKNOWN: recipe progression not verified.";
        }

        static NeedRules.ChainRecipe ChainRecipe(Context c, int id)
        {
            if (!NeedCatalog.Products.TryGetValue(id, out var recipes)) return null;
            var options = recipes.Where(r => (r.Always || c.Unlocked.Contains(r.Id)) && c.BuiltRecipes.Contains(r.Id))
                .Select(r => {
                    var live = c.Data.stations.Where(s => !s.disabled).SelectMany(s => s.recipes)
                        .Where(x => x.guid == r.Id && x.unlocked && !x.disabled)
                        .OrderBy(x => x.requirements.Sum(a => a.perCraft)).FirstOrDefault();
                    if (live == null && !c.CraftCosts.ContainsKey(r.Id)) return null;
                    return new NeedRules.ChainRecipe { Yield = live?.outputs.FirstOrDefault(x => x.guid == id)?.perCraft ?? r.Yield,
                        Inputs = live != null ? live.requirements.ToDictionary(a => a.guid, a => a.perCraft) : c.CraftCosts[r.Id] };
                }).Where(x => x != null).ToList();
            // Choose one alternative, never sum alternative recipes. Prefer the least missing inputs.
            return options.OrderBy(r => r.Inputs.Sum(a => (double)NeedRules.Short(a.Value, Reach(c, null, a.Key))) / Math.Max(1, r.Yield)).FirstOrDefault();
        }
        static Dictionary<int, int> PlanningStock(Context c)
        {
            var stock = c.Island.Keys.ToDictionary(id => id, id => NeedCatalog.Gear.ContainsKey(id) ? N(c.Island, id) : Reach(c, null, id));
            var seen = new HashSet<string>(c.Data.sources.Select(s => s.inventory));
            seen.UnionWith(c.Data.stations.Select(s => s.outputInventory));
            foreach (var station in c.Data.stations)
                if (station.inputInventory != null && seen.Add(station.inputInventory))
                    foreach (var item in station.input.Where(a => !NeedCatalog.Gear.ContainsKey(a.guid))) Add(stock, item.guid, item.amount);
            return stock;
        }
        static void AddChain(Context c, List<Entry> rows, int root, int amount, string purpose, int priority,
            Dictionary<int, int> stock, Dictionary<int, int> personal = null, string beneficiary = null)
        {
            NeedRules.Expand(root, amount, stock, id => ChainRecipe(c, id), (id, missing, action, path) => {
                if (action == "Supply") action = NeedCatalog.Products.ContainsKey(id) ? "Setup" : "Collect";
                var entry = rows.FirstOrDefault(e => e.Id == id && e.Purpose == purpose && e.Action == action);
                if (entry == null)
                {
                    entry = new Entry { Id = id, Purpose = purpose, Action = action, Priority = priority,
                        Color = action is "Setup" or "Unverified" ? "yellow" : purpose == "Servant gear" ? "#87CEFA" : "#90EE90" };
                    rows.Add(entry);
                }
                entry.Target = (int)Math.Min(int.MaxValue, (long)entry.Target + missing);
                entry.Priority = Math.Max(entry.Priority, priority);
                var chain = string.Join(" -> ", path.Reverse().Select(L));
                var reason = $"{missing} {L(id)}" + (chain.Length > 0 ? $" -> {chain}" : "") + $" for {purpose}";
                if (beneficiary != null) reason += $": {beneficiary}";
                entry.Reasons.Add(reason);
                entry.Goals.Add(root);
                entry.Reason = $"{action} {entry.Target} {L(id)} — {purpose}" + (id != root ? $" • {L(entry.Goals.First())}" : "") + (entry.Goals.Count > 1 ? " + other goals" : "");
            }, false, personal);
        }
        static void ExpandStock(Context c, List<Entry> rows)
        {
            var roots = rows.Where(e => e.Action == null && e.Purpose == null).OrderByDescending(e => e.Priority).ThenBy(e => e.Id).ToList();
            var stock = c.Remaining ?? PlanningStock(c);
            foreach (var root in roots)
            {
                rows.Remove(root);
                AddChain(c, rows, root.Id, NeedRules.Short(root.Target, root.Have), "Stock", root.Priority, stock);
            }
        }
        static void GearGoals(Context c, List<Entry> rows, string caller, bool clan, ref bool unknown)
        {
            var root = c.Gear.RootElement;
            var stored = PlanningStock(c); // claims are local to this report, never live reservations
            var hadUnknown = false;
            void Person(JsonElement person, bool servant)
            {
                var name = person.GetProperty("name").GetString();
                if (!servant && !clan && name != caller) return;
                if (servant && !c.Plots.Contains(person.GetProperty("plot").GetInt32())) return;
                if (!person.GetProperty("equipment").GetProperty("available").GetBoolean())
                { if (!servant) hadUnknown = true; return; }
                var bag = new Dictionary<int, int>();
                if (!servant && person.TryGetProperty("inventory", out var inventory))
                    foreach (var a in inventory.EnumerateArray()) Add(bag, a.GetProperty("item").GetProperty("guid").GetInt32(), a.GetProperty("amount").GetInt32());
                foreach (var slot in person.GetProperty("equipment").GetProperty("slots").EnumerateArray())
                {
                    var current = slot.GetProperty("item"); var id = current.GetProperty("guid").GetInt32();
                    if (!Enum.TryParse<EquipmentType>(slot.GetProperty("slot").GetString(), out var type)) continue;
                    var level = current.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetSingle() : id == 0 ? 0 : -1;
                    if (level < 0) { hadUnknown = true; continue; }
                    // An unequipped weapon in the player's bag must not look like an empty gear tier.
                    if (!servant && type == EquipmentType.Weapon)
                    {
                        var best = bag.Keys.Where(x => NeedCatalog.Gear.TryGetValue(x, out var g) && g.slot == type)
                            .OrderByDescending(x => NeedCatalog.Gear[x].level).FirstOrDefault();
                        if (best != 0 && NeedCatalog.Gear[best].level > level) { id = best; level = NeedCatalog.Gear[best].level; }
                    }
                    NeedCatalog.Gear.TryGetValue(id, out var currentGear);
                    var candidates = NeedCatalog.Gear.Where(x => x.Value.slot == type && x.Value.level > level && NeedCatalog.Products.ContainsKey(x.Key)
                        && (servant || type != EquipmentType.Weapon || id == 0 || x.Value.weapon == currentGear.weapon))
                        .OrderBy(x => x.Value.level).ThenBy(x => x.Key).ToList();
                    if (candidates.Count == 0) continue;
                    // Next tier, not every sidegrade/class and not the most expensive distant upgrade.
                    var nextLevel = candidates[0].Value.level;
                    var next = candidates.Where(x => x.Value.level == nextLevel)
                        .OrderByDescending(x => N(stored, x.Key) + N(bag, x.Key) > 0)
                        .ThenByDescending(x => NeedCatalog.Products[x.Key].Any(r => r.Inputs.ContainsKey(id)))
                        .ThenByDescending(x => NeedCatalog.Products[x.Key].Any(r => r.Always || c.Unlocked.Contains(r.Id)))
                        .ThenBy(x => x.Key).First();
                    if (N(stored, next.Key) + N(bag, next.Key) > 0)
                    {
                        if (N(bag, next.Key) > 0) bag[next.Key]--; else stored[next.Key]--;
                        continue; // Existing gear covers this upgrade; never spend a material-list slot on it.
                    }
                    var recipe = NeedCatalog.Products[next.Key]
                        .OrderByDescending(r => r.Inputs.ContainsKey(id))
                        .ThenByDescending(r => r.Always || c.Unlocked.Contains(r.Id)).ThenBy(r => r.Id).First();
                    var why = $"{Safe(name)}: {L(next.Key)}. {UnlockText(recipe.Id, c)}";
                    if (!c.BuiltRecipes.Contains(recipe.Id)) why += " Crafting station/discount not verified; base costs used.";
                    void Ingredient(int material, int amount, HashSet<int> path)
                    {
                        // A worn predecessor pays its own upgrade prerequisite exactly once.
                        if (material == id) return;
                        var personal = NeedRules.Draw(bag, material, amount);
                        var used = NeedRules.Draw(stored, material, amount - personal);
                        var shortage = NeedRules.Short(amount, personal + used);
                        var prefab = NeedCatalog.Prefab(material);
                        var equipment = NeedCatalog.Gear.ContainsKey(material)
                            || (Core.EntityManager.Exists(prefab) && prefab.Has<EquippableData>());
                        if (equipment)
                        {
                            if (shortage == 0) return;
                            if (path.Count >= 6 || !path.Add(material)) { hadUnknown = true; return; }
                            if (NeedCatalog.Products.TryGetValue(material, out var predecessors))
                            {
                                var predecessor = predecessors.OrderByDescending(r => r.Always || c.Unlocked.Contains(r.Id)).ThenBy(r => r.Id).First();
                                var crafts = NeedRules.Crafts(shortage, predecessor.Yield);
                                foreach (var cost in c.CraftCosts.GetValueOrDefault(predecessor.Id, predecessor.Inputs))
                                    Ingredient(cost.Key, NeedRules.Cost(cost.Value, crafts), path);
                            }
                            else hadUnknown = true;
                            path.Remove(material);
                            return;
                        }
                        c.GearProducts.Add(material);
                        AddChain(c, rows, material, shortage, servant ? "Servant gear" : "Player gear",
                            NeedRules.GearPriority(servant, NeedCatalog.Depth(material)), stored, bag, why);

                    }
                    foreach (var input in c.CraftCosts.GetValueOrDefault(recipe.Id, recipe.Inputs))
                        Ingredient(input.Key, input.Value, new HashSet<int>());
                }
            }
            if (root.TryGetProperty("players", out var players)) foreach (var p in players.EnumerateArray()) Person(p, false);
            if (root.TryGetProperty("servants", out var servants)) foreach (var p in servants.EnumerateArray()) Person(p, true);
            c.Remaining = stored;
            unknown = hadUnknown || !c.UnlocksKnown || NeedCatalog.Gear.Count == 0;
        }
        static void SaveAndPrint(ChatCommandContext ctx, Context c, List<Entry> entries, string title, bool focused = false)
        {
            var top = NeedRules.BottomFirst(entries, e => e.Priority, e => e.Target > 0 ? NeedRules.Short(e.Target, e.Have) / (double)e.Target : 0, e => e.Id);
            // Save #1 at index zero while displaying it last.
            var ranked = top.AsEnumerable().Reverse().ToList();
            foreach (var entry in ranked) Explain(c, entry);
            foreach (var id in saved.Where(x => (DateTime.UtcNow - x.Value.At).TotalMinutes > 15).Select(x => x.Key).ToList()) saved.Remove(id);
            PendingItemChoiceService.Clear(ctx.Event.User.PlatformId);
            ClanThroneServants.ClearNumberChoice(ctx.Event.User.PlatformId);
            var selection = new Saved { At = DateTime.UtcNow, Plots = c.Plots, Rows = ranked, Selected = focused ? 1 : 0 };
            if (!focused && ranked.Count > 0) selection.Number.Arm();
            saved[ctx.Event.User.PlatformId] = selection;
            if (focused) { Detail(ctx, 1); return; }
            ctx.Reply(C($"Needs • {title} • castle {c.Plot}" + (c.Plots.Count > 1 ? " + clan supplies" : "") + $" • #1 highest" + (entries.Count > 5 ? $" • {entries.Count - 5} more" : ""), "white"));
            if (ranked.Count == 0) ctx.Reply(C("No verified unmet goals. Use .s needgoal or .s needtarget to choose goals.", "green"));
            for (var i = ranked.Count - 1; i >= 0; i--) ctx.Reply(C($"{i + 1}. {ranked[i].Reason}", ranked[i].Color));
            if (ranked.Count > 0) ctx.Reply("Details: <color=white>.s #</color> (e.g. <color=white>.s 1</color>) — use as your next command.");
        }
        internal static void Detail(ChatCommandContext ctx, int number, int page = 1)
        {
            if (!saved.TryGetValue(ctx.Event.User.PlatformId, out var report) || (DateTime.UtcNow - report.At).TotalMinutes > 15)
            { ctx.Reply("Run .s need first; numbered results expire after 15 minutes."); return; }
            var allowed = Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(ctx.Event.SenderCharacterEntity);
            if (report.Plots.Any(p => !allowed.Contains(p))) { ctx.Reply("Castle access or ClanShare changed. Run .s need again."); return; }
            if (number < 1 || number > report.Rows.Count) { ctx.Reply($"Choose 1–{report.Rows.Count} from your last .s need."); return; }
            var row = report.Rows[number - 1]; var pages = Math.Max(1, (row.Details.Count + 3) / 4);
            if (page < 1 || page > pages) { ctx.Reply($"Choose page 1–{pages}."); return; }
            ctx.Reply(C($"#{number} {L(row.Id)}{(row.Purpose == null ? "" : " • " + row.Purpose)} • snapshot {report.At:HH:mm:ss} UTC • {page}/{pages}" + (page < pages ? $" • .s needpage {page + 1}" : ""), "white"));
            foreach (var line in row.Details.Skip((page - 1) * 4).Take(4)) ctx.Reply(line);
        }
        internal static void ClearNumber(ulong user) { if (saved.TryGetValue(user, out var s)) s.Number.Cancel(); }
        internal static bool TryNumber(ChatCommandContext ctx, int number)
        {
            if (!saved.TryGetValue(ctx.Event.User.PlatformId, out var s) || !s.Number.Consume()) return false;
            if ((DateTime.UtcNow - s.At).TotalMinutes > 2) { ctx.Reply("Need selection expired. Run .s need again."); return true; }
            if (number >= 1 && number <= s.Rows.Count) s.Selected = number;
            Detail(ctx, number);
            return true;
        }
        internal static void Page(ChatCommandContext ctx, int page)
        {
            if (!saved.TryGetValue(ctx.Event.User.PlatformId, out var s) || s.Selected == 0)
            { ctx.Reply("Select a need first with .s need then .s <number>."); return; }
            Detail(ctx, s.Selected, page);
        }

        internal static string Debug(int plot, string mode)
        {
            if (plot < 0) return JsonSerializer.Serialize(new { error = "no plot" });
            NeedCatalog.Ensure();
            using var gear = JsonDocument.Parse(GearDebug.Snapshot(plot));
            var c = Capture(plot, Core.TerritoryService.GetLogisticsTerritoryIds(plot).ToList(), gear);
            var rows = new List<Entry>(); var unknown = false;
            if (mode != "stock") GearGoals(c, rows, "", true, ref unknown);
            foreach (var id in c.Local.Keys.Where(id => IsStockMaterial(id, default)))
            { var entry = Stock(c, id, default, false); if (entry != null && !c.GearProducts.Contains(id) && !rows.Any(x => x.Id == id)) rows.Add(entry); }
            ExpandStock(c, rows);
            var ranked = NeedRules.BottomFirst(rows, x => x.Priority, x => x.Target > 0 ? NeedRules.Short(x.Target, x.Have) / (double)x.Target : 0, x => x.Id).AsEnumerable().Reverse().ToList();
            foreach (var e in ranked) Explain(c, e);
            return JsonSerializer.Serialize(new { plot, unknownGear = unknown, catalogRecipes = NeedCatalog.Recipes.Count, catalogGear = NeedCatalog.Gear.Count,
                craftStations = c.CraftStations.Count, unlockedRecipes = c.Unlocked.Count,
                rows = ranked.Select((e, index) => new { rank = index + 1, item = L(e.Id), purpose = e.Purpose ?? "Stock", totalStock = N(c.Island, e.Id), localStock = N(c.Local, e.Id), usableBeforeGear = Reach(c, null, e.Id), allocatedForGear = e.Purpose == null ? (int?)null : e.Have, have = e.Have, target = e.Target, reason = e.Reason, details = e.Details }) });
        }
    }
}
