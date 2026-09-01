# 02 — ClanShare

ClanShare makes **every non-excluded clan plot one logistics island**.

Toggle: `.s cs` / `.s gs` (same command). Stored **per clan** (`c{net}`), not per player. Default **OFF**. When a clan turns it ON, it applies to **all members and all clan plots**.

`.s cse` (heart **owner only**, standing on the plot): exclude or include **this** plot. Standing on an excluded plot is **local-only**. Other clan plots will not dump into it, and standing there will not pull from the island.

## Island vs plot

When ClanShare is **ON** and the player is a clan member on a non-excluded plot, dest/source for:

- `.stash` / RR
- `.pull` / craft-pull
- `.s tidy`
- `.fi` / `.fc`
- conveyors (`s#`/`r#`)
- covering / kit / heart-upgrade lend
- servant mission auto-stash

…is the **clan island** (all non-excluded clan plots).

When ClanShare is **OFF**, dest/source is the **standing plot only**.

## New castle

With `.s cs` on, a **brand-new plot is already on the clan island**. `.stash` and RR work as soon as the heart is placed — they dump to clan chests, not only this plot.

Typical build order (player-facing, also in README):

1. Place the heart.
2. Lay foundations.
3. Place **one chest**. After a few seconds it fills with starter kit (see [03-covering-lend.md](03-covering-lend.md)).
4. Enclose a small room on **treasury floor**.
5. Place about **three large chests** on treasury floor so covering can land.

Stand on the plot while you build. Covering only runs while a **clan member** is standing there.

## Occupied plots (covering / leftover-bypass)

A plot is **occupied** only if a **clan member** is standing on it (`IsSameClanAsHeart`).

**Castle guests never occupy.** They do not trigger clan covering, leftover-bypass, or pulls from other clan plots.

Leftover-bypass (covering 1×, kit chest-first, heart-fuel seed) is on when the clan has **≤ 1 unique occupied plot**. Two or more occupied clan plots: those pulls **honor reserve**.

If clan reserves are still met at the other castles, kit/covering **stays** when you leave.

## `.s cse`

- Owner of the standing heart only.
- Excluded plot: standing there = this plot only. Island commands from elsewhere skip it.
- Run `.s cse` again to include it.
- New plots are **not** excluded by default.

## `.s rrglobal` vs ClanShare

- `.s rrglobal` (player, default OFF) + `.sg rrg` (server allow, default ON): allow **off-plot** stash/RR when you are **not** standing on a clan island plot.
- It does **not** shrink on-plot ClanShare dest. ClanShare island dump still works with rrglobal OFF.

## Find / inspect labels

`.fi` and `.fc` always print the plot you are standing on. ClanShare ON: group by plot + heart level (`plot {id} L{level}`) and mark current as `(here)`.

## Vanilla treasury

ClanShare also shares **vanilla merged treasury** across clan plots (server patches). Logistics dest ranking is separate from vanilla merge.

## Raided hearts

A raided plot (`ActiveEvent >= Attacked`) is skipped as dest and source.
