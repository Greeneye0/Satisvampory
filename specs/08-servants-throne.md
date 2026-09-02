# 08 — Servants and throne

## Servant auto-stash (`.s asm`)

When a servant returns from a mission, dump loot with the **same dest ranking** as `.stash` / RR (named dest, then overflow).

- Player `.s asm` default **OFF**. Server `.sg asm` default ON.
- ClanShare ON: dest is the clan island except `.s cse` excluded plots (`GetServantStashPlotIds`).
- Skip `NS` / special named chests as dests. Skip raided hearts.
- Overflow dests: prefer non-special overflow, prefer the servant’s home plot.
- Stash the **returning servants** only (`_TempServantList`). `MissionOwner` is the castle heart — never dump the heart (or a player) as if it were a servant.

Do not give servant loot a different dest order than stash.

## Repeat hunts (`.sg rh`)

Admin **`.sg rh` / `.sg repeathunt`**. Default **OFF**. Server-wide allow. Not turned on by `SgAllOn`.

Per castle (needs server ON): **`.s rh`** lists every ClanShare / standing castle and ON/OFF. Missing plot = **ON**. **`.s rh all`** enables all listed. **`.s rh off`** / **`.s rh on`** this standing castle. **`.s rh 2`** toggles that row (list TTL 2 minutes).

When a plot is ON: each time a hunt **returns**, surviving (not dead, not injured) servants are sent again on the **same** zone / mission / throne after a short delay. Dead or injured servants are skipped. If none can go, no resend. Raided hearts do not resend.

Turning **`.sg rh` ON**, **`.s rh on`**, or **`.s rh all`** captures hunts **already in the field** so they loop when they get back.

**`.sg rhmax [1-100]`** caps success chance on **repeat** sends only (vanilla first send unchanged). Default **99**. `.sg rhmax` with no number shows the current cap.

On every return (repeat on or off), the castle **heart owner** (if connected) gets a system chat line: destination, survival %, who came back, loot stacks, or who **died** / injured. Then auto-stash runs as today. Example: `Hunt returned (plot 86 L4) — Bandit Logging Camp (99%): Corey — 120 Plank. Repeat: sent again.`

Do not Harmony-patch Burst `GetResponseEntries`. Remember the hunt from `SendOnMissionEvent` (after any throne rewrite). Abort clears that memory.

## `.s throne` / `.s hunt`

Optional ClanShare helper to send another plot’s servants from this chair (vanilla map click). Not required for repeat hunts. No client overlay.
