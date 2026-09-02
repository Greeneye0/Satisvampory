# 09 — Settings, reserve, caps, groups

Settings persist per Steam id (player) and world id `0` (server allows). Heart-owner id holds castle floors (reserve, cap, groups, plot salvage, heart feed).

There is **no** `.leftover` command. Chest floors are **`.s reserve`**.

## Reserve (castle, heart owner)

Stand on the plot. Default **10**. `0` disables reserve.

- `.s reserve` / `.s reserve 10` — default for every item.
- `.s reserve plank` / `.s reserve plank 50` — per item or group.
- `.s reserve plank -1` or `.s rsvc plank` — clear override (back to default).
- Per-item `0` is valid: leave nothing of **this** item.

Who honors reserve:

| Feature | Honor reserve? |
| --- | --- |
| `.pull` | **No** |
| `.s tidy` | **No** |
| Craft-pull / repair / forge | **Yes** |
| Conveyors | **Yes** |
| Covering 3× | **Yes** |
| Covering 1× leftover-bypass | **No** (raw stacks, when ≤1 occupied clan plot) |
| Kit remaining after chest-first | **Yes** |

`.s dpl` (don't pull last) default ON. Setting default reserve to 0 also turns dpl off.

## Production cap (castle)

Conveyors stop feeding an item once the **island** has this many. Default unlimited.

- `.s cap` / `.s cap plank` / `.s cap plank 200`
- `0` = make none. `-1` = unlimited.
- `.s capclear plank`

Not scoop bagcap.

## Item groups (castle)

Built-in groups (aliases in parentheses): ore, flowers (herb/herbs), mushrooms, tailoring (thread), hides (leather), wood, gems, alchemy, blood, bones, ingots, planks, stone, coins, fish, knowledge (scroll/paper/book), minerals (material/tech), consumables (potion), weapons, armor, jewels, magic, soulshards, bags, saddles, relics.

- Hell’s Clarion is **mushrooms**, not flowers. `.s group restore` puts that back.
- First edit of a built-in **copies** the default list, then mutates the copy.
- `.s group create|delete|restore <name>`
- `.s group <name> add|remove <item> …` — quote names with spaces.

Chest dest words resolve through these groups (see [01-dest-ranking.md](01-dest-ranking.md)).

## Player logistics toggles (`.s`)

| Toggle | Default | What |
| --- | --- | --- |
| `.s ss` | OFF | RR / double-click sort stash |
| `.s cr` | OFF | Craft-pull |
| `.s dpl` | ON | Don't pull last |
| `.s asm` | OFF | Servant auto-stash |
| `.s co` | OFF | Conveyors |
| `.s us` | OFF | Spawner chests |
| `.s bz` | OFF | Brazier chests |
| `.s sp` (silent pull) | OFF | Hide dest names on pull |
| `.s ssh` | OFF | Hide dest names on stash |
| `.s rrglobal` | OFF | Off-plot stash/RR |
| `.s cs` / `.s gs` | OFF (clan) | ClanShare island |
| `.s cse` | not excluded | Owner: exclude this plot |
| `.s rh` | per castle ON (if `.sg rh`) | Repeat hunts on that castle |
| `.s sal` | OFF (plot) | Salvage on this plot |
| `.s hf` | ON (plot) | Heart Blood Essence auto-feed |
| `.s settings` | — | Show your toggles + this castle reserve |

## Server allows (`.sg`, admin)

Defaults ON after first boot (`AppliedSgAllOn`), except **convloop OFF**.

`.sg ss`, `.sg p`, `.sg cr`, `.sg asm`, `.sg rh` (repeat hunts, default OFF), `.sg rhmax` (repeat success cap, default 99), `.sg co`, `.sg convloop`, `.sg sal`, `.sg us`, `.sg bz`, `.sg nam`, `.sg trash`, `.sg rrg`, `.sg s` (show).

Admin also: `.s vq` work-queue depth, `.s catalog` item CSV, `.s peek [plot]` debug dump, `.s diag` snapshot mark.
