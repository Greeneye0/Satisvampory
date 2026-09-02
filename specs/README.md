# Satisvampory product specs

These files are the **product contract**. Code implements them. `COMMANDS.md` is the player-facing command table. If a future change would contradict a spec, **update the spec in the same commit** or do not ship the change.

Pinned to shipped behavior in **1.0.49**.

## How to use this as the rail

1. Read the matching spec **before** changing dest ranking, tidy, covering, ClanShare, belts, scoop, servants, or settings.
2. A behavior change MUST update the spec in the **same commit** as the code.
3. Update `COMMANDS.md` in the same commit if the player-visible command, default, or scope changed.
4. If code and spec disagree, the spec is the intended product. Fix the code, or explicitly amend the spec with why.
5. Do not "just code" dest / tidy / covering / ClanShare.

## Index

| Spec | Covers |
| --- | --- |
| [00-product.md](00-product.md) | What the plugin is, scopes, defaults, non-goals |
| [01-dest-ranking.md](01-dest-ranking.md) | **Shared dest rank** used by `.stash`, RR, tidy, servant loot, covering park |
| [02-clan-share.md](02-clan-share.md) | Clan island vs plot, `.s cse`, guests, new-castle join |
| [03-covering-lend.md](03-covering-lend.md) | Starter kit, covering 1×/3×, heart upgrade, heart fuel, leftover-bypass |
| [04-stash-pull-find.md](04-stash-pull-find.md) | `.stash`, RR, `.pull`, craft-pull, `.fi` / `.fc` |
| [05-tidy.md](05-tidy.md) | `.s tidy` sources, dests, skip list |
| [06-conveyors-stations.md](06-conveyors-stations.md) | `s#`/`r#`, convloop, salvage, spawner, brazier, trash |
| [07-scoop.md](07-scoop.md) | Ground scoop, auto, bagcap, exclude |
| [08-servants-throne.md](08-servants-throne.md) | Servant auto-stash, `.s throne` hunt picker |
| [09-settings.md](09-settings.md) | Reserve, production cap, item groups, player/server toggles |
| [10-ops.md](10-ops.md) | Install, bounce, debug mailbox, versioning |

## Frozen rules

Do not "simplify" these away. They exist because earlier updates went off the rails.

- Dest classes **0–6, 90, 99** stay the shared rank. Matching `s#` is class **0**.
- `.stash`, RR, tidy, and servant loot **share** that rank. Do not invent a second dest order.
- Tidy **never** drains `s#`/`r#`, `NS`, skip-quotes (`''`), salvage/trash/spoils/spawner/brazier, or **castle hearts**.
- Treasury-floor chests **are** tidy sources.
- Overflow and spoils are tidy **sources only**.
- Covering **1× leftover-bypass** when ≤1 occupied clan plot, then **3× honor reserve**. Cap **200** per mat at 1×. Blood Essence in **chests** is **500**, not counting heart fuel, not capped at 200, not tripled on 3×.
- Covering pull budget **12 per plot / 120 per tick**. Missing-on-plot first; a new castle (not all kit types yet) fills kit before other mats.
- `.sg convloop` default **OFF**. Chest→chest without a same-group cycle still works.
- Castle **guests** never occupy a plot for covering / leftover-bypass / clan pulls.
- Blood Essence covering parks **Blood / Alchemy / generic** dests (including named treasury-floor). Vanilla treasury chests reject regular Blood Essence.
- Heart fuel fills **every unlocked HUD pad** (level 2 → two stacks of 500). Do not treat the second pad as locked because `GetLevelData()` with no index says `ItemSlots=1`.
- There is **no** `.leftover` command. Chest floors are `.s reserve`.
