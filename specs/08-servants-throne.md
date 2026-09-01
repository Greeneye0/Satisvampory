# 08 — Servants and throne

## Servant auto-stash (`.s asm`)

When a servant returns from a mission, dump loot with the **same dest ranking** as `.stash` / RR (named dest, then overflow).

- Player `.s asm` default **OFF**. Server `.sg asm` default ON.
- ClanShare ON: dest is the clan island except `.s cse` excluded plots (`GetServantStashPlotIds`).
- Skip `NS` / special named chests as dests. Skip raided hearts.
- Overflow dests: prefer non-special overflow, prefer the servant’s home plot.

Do not give servant loot a different dest order than stash.

## `.s throne` / `.s hunt` (ClanShare hunt from this chair)

Do **not** clone the hunt map. Fog of war, undiscovered zones, and zone clicks stay the **vanilla** throne map (same interaction). Satisvampory is server-only. **HuntClock 1.2** draws a castle-switch popup on that map (not a second map) and arms `.s hunt` / mailbox `hunt` for the next vanilla click. Snapshot: `debug/thrones-client.json`.

ClanShare ON: sit **this** castle’s throne. Pick another clan plot’s servants. Click a **discovered** zone on this map. The server rewrites `SendOnMissionEvent` to that plot’s real throne + those servants (`MissionDataID` / `MapZoneId` stay the vanilla click).

- `.s throne` lists each plot’s living servants. Pick with `.s 2` (pending pick TTL **2 minutes**). Default is this castle.
- `.s hunt` lists idle servants on the managed plot. `.s hunt 1 2` (max 3) arms the next map click. `.s hunt` with no numbers lists. `.s throne here` back to vanilla this-throne send.
- Do **not** Harmony-patch `GetResponseEntries` / extra servant entries (Burst abort on sit). Do not teleport-as-product (sit bind stays on this chair; standing snaps home).
- Never treat a **castle heart** as a throne (`ActiveServantMission` lives on the heart).
- Debug mailbox `thrones` / `hunt` (`plot:N`, `name:"1 2"`) arms the same rewrite. `gotothrone` is debug-only movement.
- ClanShare off or plot excluded: sit **this** throne; picker is a no-op.

May manage from plot A only if A and the target throne are both on the character’s ClanShare island.

Castle guests do not get clan-wide throne management.
