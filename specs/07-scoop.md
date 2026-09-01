# 07 — Ground scoop

World piles into **the player’s bags**. Scoop does **not** use `.sg`.

Commands: `.s` / `.scoop` / `.sc` scoop now. `.l help` is the same help as `.s help`.

## Never scoop

- Death bags
- Soul shards
- Chests
- Stations
- Player inventory-drops (auto never takes these)

Closest eligible player wins a contested pile.

## Auto

- Default **ON**, filter **all**, radius **10** (1–50), notify **manual** (`.s` chat only; auto silent).
- `.s auto` toggles. `.s auto around|all` turns ON and sets filter.
- `.s filter around|all` sets filter without toggling auto.
- `around` = only piles that spawned while you were in radius.

## Exclude / bagcap / mode

- `.s exclude [item|group]`: skip list for auto.
- `.s bagcap`: **personal scoop caps**, not castle production caps. `0` = scoop none, `-1` = unlimited.
- `.s bagcapclear`: clear all your scoop caps.
- `.s mode bags|guild`: count bags only, or bags + ClanShare stashes. Still **your** cap.

`.s cap cotton 200` stops **conveyors** when the **castle/clan** already has 200. `.s bagcap cotton 200` stops **scoop** at 200 in bags.

## Notify

`.s notify off|manual|on`. Default **manual**. `.s last` reprints the last scoop line.
