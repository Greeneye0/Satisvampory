# 08 — Servants and throne

## Servant auto-stash (`.s asm`)

When a servant returns from a mission, dump loot with the **same dest ranking** as `.stash` / RR (named dest, then overflow).

- Player `.s asm` default **OFF**. Server `.sg asm` default ON.
- ClanShare ON: dest is the clan island except `.s cse` excluded plots (`GetServantStashPlotIds`).
- Skip `NS` / special named chests as dests. Skip raided hearts.
- Overflow dests: prefer non-special overflow, prefer the servant’s home plot.

Do not give servant loot a different dest order than stash.

## `.s throne` (ClanShare hunt picker)

ClanShare ON: numbered picker for which clan plot the **throne hunt UI** manages.

- Default is the castle you are sitting on / standing on.
- `.s throne` lists plots; pick with `.s 2` (pending pick TTL **2 minutes**).
- `.s throne here` resets to this castle.
- Reopen the hunt panel after picking.
- Vanilla listing stays Burst-safe (no extra entries). ServantInfo / hunt events are retargeted at that plot’s real throne.
- Never treat a **castle heart** as a throne (`ActiveServantMission` lives on the heart). Learn the real sit-target from `Request.Throne`, then `UseThrone` / prefab name containing `Throne`. Patch `Request.Throne` in-place. Also retarget `Interactor.Target` for that update; restore next tick. Do **not** Harmony-patch `GetResponseEntries` (Burst abort on sit). Debug mailbox `thrones` records last response names and whether the cached entity is a heart.
- ClanShare off or plot excluded: sit **this** throne to manage its servants. Picker is a no-op / explains that.

May manage from plot A only if A and the target throne are both on the character’s ClanShare island.

Castle guests do not get clan-wide throne management.
