# 04 — Stash, pull, find

## `.stash`

Dump inventory **except hotbar** into dest chests on the standing plot, or the **clan island** if ClanShare is on.

- Dest order: [01-dest-ranking.md](01-dest-ranking.md) (`OrderDepositDests` / `RankDeposit`).
- Matching seeded `s#` first, then exact name, then category, then generic, then custom-seeded, then overflow.
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
- Source pass: unnamed/generic/overflow first, then named, then `s#`/`r#` last-resort. Never `NS` / skip-quotes.
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

## `.fc` / `.findchest`

Find chests by name. Same plot / `(here)` labels as `.fi`.

## Ambiguous names

If an item name is ambiguous, the plugin lists numbered matches. Pick with `.s 2`, `.s pick 2`, or `.s <number>`.

## Admin

- `.adminstash <item> <n>`: spawn into standing-plot dests (admin).
- `.emptytrash`: empty `trash` chests on the standing plot (admin; `.sg trash` allow).
