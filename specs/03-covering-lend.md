# 03 — Covering, kit, heart upgrade, heart fuel

Server-side **lend**: move items from other clan (or same-plot treasury) chests onto the occupied dest plot. Interval **5 seconds**. Chat push **off**.

Never dump “16 of every item”. Only move:

1. Next-level **heart upgrade costs** onto treasury dests.
2. **Starter kit** into the first non-overflow dest (chest recipe first) with spend-refill until treasury.
3. **Covering buffer** while a clan member stands on a treasury plot.
4. **Heart fuel** into heart Blood Essence slots (not a chest).

No dest chest = fuel only. Kit is **OFF** when dest mode is treasury-only with no chest dests in allShared sense — kit still first-fills if the plot does not yet have all kit types.

Skip raided hearts. Return unused **ledgered** kit/upgrade leftovers to the **source chest NetworkId**. Do not return heart fuel seed.

## Tick budget

- `MaxLendPullsPerTick` = **120**
- `MaxLendPullsPerPlot` = **12**

Do not drop the per-plot cap. A 4-pull cap starved covering (Grave Dust / Amethyst sitting in source while dests were empty).

Zero connected players: no lends; return leftovers.

## Occupied / leftover-bypass

See [02-clan-share.md](02-clan-share.md). Bypass when unique occupied clan plots **≤ 1**. Guests never occupy.

- Covering **1×**: leftover-bypass (raw stack count, do not subtract reserve) when bypass is on.
- Covering **3×**: **always honor reserve**.
- Kit chest-first amounts may leftover-bypass; remaining kit honors reserve.
- Heart fuel seed uses the same bypass flag.

## Starter kit (first fill / spend-refill)

| Item | Amount |
| --- | --- |
| Plank | 288 (72 chest-first leftover-bypass, remaining 216 honor reserve) |
| Stone Brick | 456 |
| Gem Dust | 32 |
| Copper Ingot | 24 (chest-first leftover-bypass) |
| Iron Ingot | 24 |
| Stone | 192 |

Chest-first leftover-bypass also uses stack-size copper/iron/gemdust/brick/stone as documented in code comments.

Park into the first **non-overflow** dest (prefer a chest that can hold the chest recipe). Never park kit into overflow if another dest exists.

**Seed / opt-out is dest-chest NetworkId** (`n{net}`) only. Ignore legacy `t{plot}` starter-kit keys.

- Empty a seeded chest: that NetworkId **opts out** for **5 minutes** (`EmptyHoldSeconds`) so the player can unbuild. Kit/covering/self-sort/tidy MUST NOT restuff that chest during the hold. Leave the plot or wait; a **new** chest NetworkId first-fills again.
- Partial kit below targets = spend-refill until treasury.

## Covering buffer

Enough to place **3 copies** of whichever **unlocked** castle blueprint is hungriest per material (1 copy if leftover-bypass).

- 1× amount is the max cost among unlocked (or start) blueprints plus station recipe costs on the plot, then **`Covering1xCap = 200`** per material. A 1200-stone wall MUST NOT dump 1200.
- **Blood Essence in chests is 500**, standalone from heart fuel. Do not apply the 200 cap. Do not triple it on covering 3×. Do not count BE sitting in the castle heart toward that 500.
- 3× = 1× × `BuildCoverCopies` (3), honor reserve (except chest BE stays 500).
- Park into vanilla-visible dests (generic / matching), not overflow, class cap **3** during covering park. Skip castle hearts as dests and as covering stock.

### Covering order (`OrderedCoveringTargets`)

Materials needed on the dest plot, compared to **stock already on that plot**:

1. **Kit-zero** (none of that kit mat on the plot): plank, stone brick, gem dust, copper, iron, stone, Blood Essence.
2. Then:
   - **New castle** (`!PlotHasAllKitTypes`): remaining **kit-more**, then other-zero (rotate), then other-more (rotate).
   - **Established castle**: **other-zero** (rotate by plot cursor), then kit-more, then other-more (rotate).

Missing-on-plot **first**. Do not top up planks that are already present while Grave Dust / Amethyst on the dest is still 0.

## Heart upgrade costs

Move the **next heart level** upgrade costs onto treasury dests on the occupied plot. Uses dest ranking. Blood Essence uses Blood/Alchemy/generic dests (treasury chests reject regular BE). Greater Blood Essence may sit in vanilla treasury.

## Heart fuel

- Fills the heart’s Blood Essence **fuel slots**, cap **500** per stack (`HeartFuelStack`).
- Fill **every unlocked HUD pad**, not only top off stacks that already have BE. A level-2 heart with 500 + empty must get a second 500. Locked pads stay empty.
- Unlocked count is the **current heart level** blob (`GetLevelData(level)`), at least `heart.Level` (L2 → 2 slots). Do not use no-index `GetLevelData()` — that blob is wrong on L2 (empty upgrade costs / ItemSlots=1).
- Move BE **into the heart fuel inventory**, not through chest dest ranking.
- Default **ON** (`.s hf`). Missing per-plot key = ON.
- Seed / opt-out is **heart NetworkId** (`n{net}`) only. `t{plot}` MUST NOT stick to a replacement heart.
- Dump-opt-out only sticks while auto-feed is **OFF** (so unbuild/empty is possible). Turning `.s hf` ON clears opt-out.
- Heart fuel may pull from the **same plot** (`allowSamePlot`).
- Do not return heart fuel seed to the source chest.

## Named last-resort

Kit / upgrade / covering may borrow from named (including conveyor) chests as **pass 1 / 2** last-resort. Those moves are **ledgered** to that chest NetworkId and unused leftovers return there.

Clan conveyors MUST NOT drain overflow on occupied allShared plots (kit park).

## Dest mode

Covering dests use RankDeposit (never NS; seeded `s#` / generic / exact / category / overflow / custom-last). Covering park itself caps at class **3** (generic) so it does not dump covering mats into overflow.

## Blood Essence covering

Vanilla treasury rejects regular Blood Essence. Park BE on Blood / Alchemy / generic dests, **including named treasury-floor**. If those dests exist, do not report “no blood dest”.
