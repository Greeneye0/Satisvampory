# 06 — Conveyors, salvage, spawner, brazier, trash

Per-plot work runs from the FIFO **work queue**. ClanShare ON: one island drain per generation (sibling plots marked consumed so the island is not processed N times).

Player `.s co` + server `.sg co` to run conveyors.

## Belts (`s#` / `r#`)

Nameplate tokens: `s(\d+)` sender, `r(\d+)` receiver, **not preceded by a letter** (1.0.115). Group number is the digit. `Tailor1` and `Silver1` are names, not `r1`; `R6S3S6`, `s1r1`, and `Ore S2` parse. Before 1.0.115 `Tailor1 R6S3S6` was read as receiver 1 with the match name `Tailo`, so its custom group never matched.

- **Stations first.** Chest senders fill receiving stations (`r#`) that want the item, then chest receivers.
- Honor **reserve** and **production cap** (`.s cap`) counted on the island.
- Overflow chests ignore groups; senders stay on their `s#`/`r#` group.
- Fair-share split when several sinks want the same item.
- Never drain `s#`/`r#` as generic covering/tidy sources (see dest ranking + tidy).
- **Full crafts only.** An `r#` station pulls input only when the hopper plus senders on **every** `r#` on that plate (plus overflow) can supply **every** ingredient for **one** craft. Cost uses the **matching-floor discount** (`WorkstationLevel.MatchingFloor` → 0.75, same rounding as vanilla). Do not queue extra crafts. A plate like `R5R4S4` is one station, not one dump/pull per token.
- **Shared senders are claimed, not shared.** Stations on the same line are planned in order of **most input already held** first. When a station books a want, that amount is **claimed** out of the sender pool (`SenderPools.Claim`) before the next station plans, and a dumped leftover is **credited** back to the pool (`SenderPools.Credit`) in the same pass. Two `S2R2` stations over one chest holding 9 Venom Sap must resolve to one station with a full craft and one empty, not both pulling 4/5 and dumping every tick (1.0.93 and earlier flickered).
- **Push-back is by chest priority** (1.0.109). Station **output** that no `r#` on the line wants (no receiver, or the receiver is at production cap) is pushed back by **dest ranking** on the island — priority `+`, `s#`, exact, category, seeded, generic — with overflow as the ranked last resort. A Stone Brick at cap lands in `Stone Brick R1S1+`, not in overflow, while that chest has room.
- **Dump leftover.** If the hopper cannot complete any enabled recipe, leftover input goes back to **source chests on that line**, ordered by **dest ranking** for that item (priority `+`, seeded `s#`, exact, category), then overflow. Silent. Extra above the complete-craft keep is dumped the same way. Do not leave a partial recipe sitting in the machine, and do not dest-rank it into product chests.

### Chest → chest and convloop

Chest senders fill **stations first**. Chest → chest is a **second pass**.

- Chest → `r#` that is **not** also `s#` on the same group: allowed even when convloop is off.
- Chest → `r#` that **is** also `s#` on the same group (a loop): requires **`.sg convloop` / `.sg cloop`**, default **OFF**.

- **Same line = one pool, rank decides** (1.0.111). Chests with **identical** `s#`/`r#` token sets (`Misc Ingots R2S2`, `Metal2 S2R2`) are on the same line. Between them there is **no seed rule and no convloop guard**: a receiver wants any item a same-line chest holds when it **strictly outranks** that holder for the item (dest ranking), so a new, empty, higher-ranked chest on the line pulls the stock over. A tie stays put. `Metal1 R2S2S0` is a different line from `Metal2 R2S2`. Production cap still applies.
- **Mutual links sort, never ping-pong** (1.0.96). If the dest chest sends on **any** group the source chest receives (cross-group two-cycle: `Alchemy S1R2` → `Bone Grave R1S2` → back), the move happens only when the dest **strictly outranks** the source under dest ranking for that item. A tie stays put. 279 Grave Dust bouncing between two chests every 30 ms (1.0.95) must not happen.

Do not turn convloop on by default. Loops will vacuum a castle.

`.s conv <item>`: why that product is not moving (station, line, cap, reserve).
### Need goals and follow-ups (1.0.133)

`.s need [item] [here|clan]` is a read-only goal report, not a transfer or craft command. Default scope is the standing authorized castle's goals with permitted ClanShare supplies; `here` restricts sources and servants to that castle, and `clan` requires ClanShare. Excluded castles and changed access are respected. Counts for a stock target are local castle counts, not the sum of every castle's protected stores. Item aliases use the castle/clan alias resolver; ambiguous item names produce the shared numbered picker, preserving scope for the selected item (1.0.142).

- One header plus at most five ranked entries, followed by the tip `Details: .s # (e.g. .s 1) — use as your next command.` Omit the tip for an empty list. Select the highest five before reversing their display: #5 first, #1 last. Deterministic ties use deficit fraction then item ID. Colors supplement words: green ready, yellow protected/setup/goal shortfall, red inaccessible ingredient shortage. Empty reports do not invent demand.
- `.s <number>` uses the need list **once, as the next executed command**, within two minutes. Another executed command or a newer numbered list invalidates it. After use, bare numbers resume the existing item/throne selection behavior. A new need list clears older item/throne selections. `.s pick` and `.l` remain explicit item selectors.
- `.s needpage <page>` continues the already-selected need without reusing a bare number. Details are frozen at the report timestamp for up to 15 minutes; rank changes cannot retarget them. Recheck access before showing them. Pages have one header and four detail lines. A focused item request opens details directly. New `.s need` reports replace that user's previous snapshot.
- `.s needgoal auto|gear|stock` is personal and persistent. Auto prioritizes character upgrades over servant upgrades, then replenishment; gear and stock force the focus. Compare actual levels and slots, preserve the player's weapon family, select one next-tier alternative, prefer existing spare equipment and recipes that consume the currently worn item. Do not count equal-level sidegrades or empty/converting coffins as upgrades. Inventory-carried weapons count when assessing player weapon level. Missing equipment/progression data is unknown, not max gear or level zero.
- Spare equipment and available materials are claimed locally within the report across gear recipients, never reused for two upgrades. Personal carried ingredients are considered before shared stock; protected stock is not available material. Gear claims do not change live inventories/reservations. **The five main rows are materials, never individual player/servant gear pieces or unlock tasks** (1.0.136). As of 1.0.137, combine each material separately for player gear and servant gear. Every player material goal ranks above every servant material goal, which ranks above stock replenishment. Allocate shared supplies to all players before any servants, without double counting. Label each main row Player gear (yellow) or Servant gear (light blue), with remaining shortage and total scoped castle stock; total stock is not the amount available after reserves. The same material can occupy two distinct numbered rows. First-page details distinguish total/local stock, usable supply before allocation, protected source stock, and the amount allocated (including personal carried ingredients) to the selected purpose. Numbered details retain that purpose. Existing gear or fully supplied upgrades create no shortage row. Recursively expand missing predecessor equipment into its materials. Keep beneficiary names, gear pieces, locked recipes, bosses, and station requirements in `.s #` details. Missing station detection must not turn a material need into a "build station" ranking. For selected locked upgrades, their material requirements are planning goals, with the lock and any unverified floor discounts explicitly described in details.
- `.s needtarget <item> <amount>` sets a personal finished-product target; -1 removes it and 0 suppresses that stock goal. Without a target, the standing castle owner's reserve is the minimum. Only relevant production materials become implicit stock goals, not seeds, soul shards, equipment, or every collectible with the default reserve. The agreed endgame fallback order is Onyx Tears, Shadow Weave, Power Cores, Gold Ingots, Charged Batteries; other materials use bounded recipe depth. Market/silver value never determines priority. At reserve, a lower-priority surplus goal may cover one downstream craft. Never interpret a target/reserve as permission to consume protected stock.
- Details trace one complete craft through actual station recipe costs, hopper contents, eligible sender/overflow inventories, per-source reserves, off-line stock, production caps, receiver setup, disabled recipes, power/output status, and upstream shortages. Deduplicate inventory IDs across line tokens. Alternative recipes/stations are evaluated independently; show the least-blocked option rather than summing alternatives. These are one-craft projections, not promises of simultaneous delivery to competing machines. Recipe recursion is bounded and cycles terminate.
- The game prefab catalog supplies equipment levels, recipes, station associations, V Blood technology links, research links, and construction requirements. Live progression buffers establish unlocks; candidate recipe presence alone does not. Missing links explicitly say unverified. Research discovery is not guaranteed. Crafting-station costs use known matching-floor costs; otherwise label base costs. Missing stations show their verified blueprint costs and unlock hints when available. Progression analysis only promotes upgrades relevant to recipients or selected stock goals, not every locked recipe.
- Cache the immutable recipe catalog per game prefab system. Capture inventory counts once per request and reuse them. Build detailed traces only for displayed goals. Requests are throttled to one per player per two seconds. Conveyor transfer planning and sorting behavior remain unchanged.

Regression command: `dotnet run --project tests/NeedRules -c Release`. Live operator mailbox `need` returns ranked goals and detail evidence (`name: "stock"` for stock-only evaluation), including catalog, crafting-station and unlock counts for verification after deployment.

Local debug mailbox `needraw` (optional `plot`, otherwise first connected player's standing plot) returns a versioned, timestamped, read-only logistics snapshot for investigating shortages. It includes all indexed refinement stations, locked/disabled recipes, floor-adjusted ingredient costs, output caps, station inventories, source chest groups/overflow status, stackable stock, per-source reserves, and inventory slots. Recipes with missing ingredients remain visible. Sources include non-senders so stock on the wrong line can be identified. Inventory IDs permit deduplication across lines. These are raw observations, not allocated demand: consumers must account for alternative recipes and shared-stock claims before ranking shortages. The operation never transfers items, claims live stock, or changes settings. It does not change `.s need` chat behavior.

Station feed is **one complete craft**. Clan island item counts are snapshotted per drain generation.

Local debug mailbox `gearraw` accepts the same optional `plot` as `needraw`. It exposes clan character equipment and carried items, servant equipment on logistics plots (including mission status), progression/research buffers where present, and candidate crafting-station recipes with floor-adjusted costs and equipment levels. Missing equipment components or level fields are unknown, not zero; candidate recipes alone do not establish unlock eligibility. Empty/converting coffins do not count as servants needing gear. This is read-only evidence for equipment-driven need analysis, not an automatic crafting or equipping feature. Analysis should trace endgame targets such as Shadow Weave and Onyx Tears through their prerequisites instead of ranking products solely by low stock. Player/servant upgrade eligibility and existing equipment must be checked before calculating material demand.

## Salvage (`.s sal`)

Feed chests named **`salvage`** into the Devourer on **this plot**.

- Plot toggle (clanmate of the heart owner). Default **OFF**.
- Needs `.sg sal` allow (default ON).
- Not a tidy dest/source.

## Unit spawners (`.s us` / `.s sp` player; `.sg us` server)

Chests named **`spawner`** fill unit stations. Spawner feed multiplier **2**.

## Braziers (`.s bz`; `.sg bz`)

Chests named **`brazier`** fill braziers. Keep at least **10** in the chest (`BrazierMin`).

`.sg nam`: allow **night** / **prox** named braziers. Prox range **20**. Tick **2.5s**.

## Trash (`.sg trash`)

Chests named **`trash`**. `.emptytrash` wipes them on the standing plot (admin). Player empty uses the trash service when allowed.

Not a tidy dest. Tidy may treat overflow/spoils as sources; trash plates are skipped entirely.

## Special chests vs dest ranking

`salvage` / `spoils` / `brazier` / `spawner` / `trash` are dest class **90**. They are not normal `.stash` dumps. Conveyor names are never inferred from prefab names.

### Actionable production needs (1.0.138)

Main needs expand gear and stock material shortages recursively through one available unlocked recipe per product, using verified floor-adjusted costs and batch yields. Subtract usable intermediate stock at every step, claim raw ingredients for players before servants before stock goals, and share planned batch surplus without reusing its inputs. Each missing terminal ingredient branch becomes its own numbered Collect row; preserve the originating product and player/servant purpose. Do not also list the blocked parent product. If inputs are covered, show Craft; if recipes exist but no usable unlocked station/recipe is verified, show Setup rather than claiming a farming shortage. Cycles/depth bounds show Unverified. Alternative recipes are not summed. These are manual production plans; line connectivity, power and caps remain detail diagnostics rather than promises of automatic delivery.

The first detail page leads with the concrete action and quantity, its chain/beneficiary, source or setup status, then concise stock accounting. Do not put a page of reserves ahead of the action. No unverified farming location is invented. Keep five numbered rows, highest at the bottom, one-use .s # and existing pagination. Quantities reflect the complete selected upgrade/stock goal, not just one craft. Shared raw material rows aggregate within a purpose and action; details retain all supported chains.

### Two-line chain presentation (1.0.139)

Each numbered need now uses two chat messages: `N. Raw material -> Intermediate -> Goal - Player gear` followed by `Need X Raw material for Y Intermediate for Z Goal`. The route is stored explicitly; different routes/purposes remain separate rather than merging into a misleading chain. Quantities follow the complete plan with available inputs and batch surplus applied; the final quantity is the full gear/stock goal. Keep at most five needs, descending display numbers with #1 at the bottom. Colors distinguish purpose/setup; the quantity line is white. The existing one-use .s # footer remains.

The first detail page identifies the total finished material required and the count of distinct selected gear items, then the selected chain quantities. Other direct ingredients show total and usable source stock, how much finished product that ingredient alone supports, and the remaining planned shortage. Never say the whole product is ready solely because one ingredient is sufficient. Covered sibling branches must still be observed for this explanation. Reserves/locations and unlock details follow the goal explanation. The debug need response exposes the same route and quantity line plus gear item count and goal amount. This supersedes the previous one-line main-list format.

### One ranked row per unresolved branch (1.0.140)

Before selecting the top five, remove a shorter route when a longer route contains that entire goal-to-ingredient prefix for the same purpose. For example, Ghost Yarn -> Shadow Weave for player gear is represented by Ghost Shroom -> Ghost Yarn -> Shadow Weave for player gear; Silk behaves likewise under Silkworm. Keep the ancestor action and beneficiary information in the surviving branch details, without adding its quantity to the raw material shortage. Distinct sibling branches, finished-product goals, and player/servant purposes remain separate. Standalone ready Craft goals remain visible when no deeper unresolved branch exists. Fill the freed top-five slots with the next distinct needs. Numbering, two-line formatting and highest priority at the bottom are unchanged. Chat and mailbox use the same collapse operation.

### Defer duplicate servant materials (1.0.141)

After collapsing redundant routes and before selecting five needs, hide a servant row if any remaining player-gear row needs the same terminal item. Compare item IDs, even if the downstream product/route differs. Preserve distinct servant materials (such as Cotton when players need Ghost Shroom and Silkworm). Put the deferred servant route and quantities in the matching player need's details, explicitly separate from its player quantity; do not add servant demand to the displayed player requirement. Allocation and reserves are unchanged. When that player material need is satisfied and disappears on a subsequent report, the servant need may return. Fill freed slots with the next distinct needs. Apply equally to chat and mailbox; highest priority remains at the bottom.

### Farming slots and empty player armor (1.0.144)

A fully supplied Craft step never occupies a ranked farming slot, even when no deeper shortage exists in that branch. Keep up to three ready-material names in one unnumbered line with .s need <item> for production details; mailbox exposes readyToCraft separately. This supersedes the earlier standalone-Craft ranking. Preserve real shortages: do not invent Ghost Shroom demand when available ingredients already cover the player production plan. Distinct servant gathering needs remain eligible after player gathering needs.

Carried player gear establishes the level of every equipment slot, not just weapons. When a player armor slot is empty and has no carried equipment, use the lowest positive level of the other worn armor slots as its replacement floor. Choose at least that tier, including a same-tier replacement for the empty slot, rather than starting at Boneguard. If no armor tier is known, retain starter progression. Worn slots still require a strictly higher level; servant progression is unchanged. Base-cost and unlock uncertainty remain in details.
