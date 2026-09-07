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

`.s need [item] [here|clan]` is a read-only goal report, not a transfer or craft command. Default scope is the standing authorized castle's goals with permitted ClanShare supplies; `here` restricts sources and servants to that castle, and `clan` requires ClanShare. Excluded castles and changed access are respected. Counts for a stock target are local castle counts, not the sum of every castle's protected stores. Item aliases use the castle/clan alias resolver; ambiguous names require clarification, not a guessed match.

- One header plus at most five ranked entries, followed by the tip `Details: .s # (e.g. .s 1) — use as your next command.` Omit the tip for an empty list. Select the highest five before reversing their display: #5 first, #1 last. Deterministic ties use deficit fraction then item ID. Colors supplement words: green ready, yellow protected/setup/goal shortfall, red inaccessible ingredient shortage. Empty reports do not invent demand.
- `.s <number>` uses the need list **once, as the next executed command**, within two minutes. Another executed command or a newer numbered list invalidates it. After use, bare numbers resume the existing item/throne selection behavior. A new need list clears older item/throne selections. `.s pick` and `.l` remain explicit item selectors.
- `.s needpage <page>` continues the already-selected need without reusing a bare number. Details are frozen at the report timestamp for up to 15 minutes; rank changes cannot retarget them. Recheck access before showing them. Pages have one header and four detail lines. A focused item request opens details directly. New `.s need` reports replace that user's previous snapshot.
- `.s needgoal auto|gear|stock` is personal and persistent. Auto prioritizes character upgrades over servant upgrades, then replenishment; gear and stock force the focus. Compare actual levels and slots, preserve the player's weapon family, select one next-tier alternative, prefer existing spare equipment and recipes that consume the currently worn item. Do not count equal-level sidegrades or empty/converting coffins as upgrades. Inventory-carried weapons count when assessing player weapon level. Missing equipment/progression data is unknown, not max gear or level zero.
- Spare equipment and available materials are claimed locally within the report across gear recipients, never reused for two upgrades. Personal carried ingredients are considered before shared stock; protected stock is not available material. Gear claims do not change live inventories/reservations. Show stored upgrades and ready-to-craft upgrades as actions, not fabricated ingredient shortages.
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
