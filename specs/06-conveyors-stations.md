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
`.s need`: top 10 station inputs. Higher tier first, then lowest stock after reserve. Shows demand, total, reserve.

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
