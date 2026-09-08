# 04 — Stash, pull, find

## `.stash`

Dump inventory **except hotbar** into dest chests on the standing plot, or the **clan island** if ClanShare is on.

- Dest order: [01-dest-ranking.md](01-dest-ranking.md) (`OrderDepositDests` / `RankDeposit`).
- `s#` (seeded or name-matched) first, then exact name, then category, then seeded (generic or custom), then empty generic, then overflow.
- Skip `NS` / skip-quotes. Skip special salvage/trash/spawner/brazier as normal dump dests.
- Off-plot (not on a clan island plot): also needs `.s rrglobal` and `.sg rrg`.
- Bat form allowed. Downed / dead / PvP combat / unallied / raided: denied.
- `.s ssh` hides dest-chest names in chat.
- Cooldown ~1s per character.

## RR / `.s ss`

Double-click sort / double-tap R stashes with the **same dest ranking** as `.stash` when the player toggle `.s ss` and server `.sg ss` are on.

`.s rrglobal` only gates **off-plot**. On-plot ClanShare dest is unchanged.

## `.s tidy`

See [05-tidy.md](05-tidy.md). Same dest rank as `.stash` / RR. Player-triggered restack of **existing chests**, not bag dump.

## `.pull <item> [amount]`

Take items into bags from dest chests on this plot or clan island.

- **Does not honor reserve.**
- Source order (1.0.100): the **inverse of dest ranking** for that item, so the chest the item would be stashed **into** is drained **last**: overflow, then empty generic, then seeded generic/custom, then category, then exact name, then `s#`. Never `NS` / skip-quotes. **Never castle hearts** (heart fuel is not a store).
- Craft-pull (`.s cr`) uses the same order.
- `.s dpl` (default ON): do not pull the last stack from a container.
- `.s sp` silent pull (no “from where” chat).
- Needs `.sg p` allow and player pull toggle where that gate is used.

## `.pull <group> [amount]`

Pull **one max stack of each member** of a built-in or castle group (`seeds`, `ore`, `flowers`, …).

- Exact item names still win (`plank` is the item; `planks` is the group).
- Omit amount, or `1`: one stack of each. Amount `> 1`: that many of each.
- Same source pass and reserve ignore as item pull. Chat is a summary (not per chest).

## Craft-pull / repair / forge (`.s cr`)

Right-click recipe (and repair/forge retrieve) pulls missing ingredients.

- **Honors reserve.**
- Needs `.sg cr` and player `.s cr`.
- `.s dpl` still applies.

## `.fi` / `.s fi` / `.finditem`

Find an item in chests. Always shows the plot you are standing on (`plot {id} L{level}`). ClanShare ON: groups by plot + heart level, marks current `(here)`.

Spotlights matching chests for the searching player.

## `.s item <item>`

Stock report on the standing plot, or the clan island if ClanShare is on. One chat line per fact (no number dump).

1. Each chest that holds the item, grouped by plot (`(here)` on the standing plot).
2. Each refinement station that consumes or produces it, with hopper/output counts and conveyor status: **moving**, **not moving** (reason), or **no conveyor**.
3. Castle **cap** and **reserve**.
4. **Total** (chests + station hoppers/outputs) last.

## `.fc` / `.findchest`

Find chests by name. Same plot / `(here)` labels as `.fi`.

## Ambiguous names

If an item name is ambiguous, the plugin lists numbered matches. Pick with `.s 2`, `.s pick 2`, or `.s <number>`.

## Admin

- `.adminstash <item> <n>`: spawn into standing-plot dests (admin).
- `.emptytrash`: empty `trash` chests on the standing plot (admin; `.sg trash` allow).

### Shared item lookup (1.0.142)

All item-taking command paths use the shared case-insensitive resolver: exact item/alias first, one distinct partial item resolves directly, multiple distinct partial items show numbered choices. Deduplicate prefab IDs before deciding ambiguity, so aliases or repeated split-name hits for one item do not create false multiple matches. Focused .s need and .s needtarget retain their scope/amount when replaying the selected item. Existing exact group precedence is unchanged. A fresh item-choice list clears old need and throne number contexts. Partial matching is not typo correction: deplet/depleted match Depleted Battery; depleated is not a substring and is not guessed. Container labels follow the separate destination matching rules.

### Typo correction (1.0.143)

After exact, partial and split-word item matching all fail, compare normalized item labels and matching word spans with adjacent-transposition edit distance. One plausible distinct item automatically runs the original action; multiple plausible items use the existing numbered picker. Corrected item names are announced by the converter. Do not fuzzy-match aliases of fewer than four characters. Allow one edit for 4-7 characters and two for 8-64; bound candidate length and never guess unrelated input. This supersedes the earlier no-typo behavior: depleated can resolve to Depleted Battery.

For command words inside the mod's explicit groups (.s, .sg and their full group names), preserve valid commands and numeric picks. A single plausible misspelled subcommand is corrected before normal parsing/execution, preserving all arguments and permission checks. Multiple plausible command corrections are shown as corrected commands to run; none is selected automatically. No correction of standalone commands or other mods' namespaces is introduced. Correction does not bypass item ambiguity, argument validation or admin checks.
