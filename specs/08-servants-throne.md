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
- Find thrones by `ActiveServantMission` buffer first, then ECS `UseThrone` tag (not authoring `UseThroneComponent`). Skip player entities. Patch `Request.Throne` NetworkId in-place (do not Marshal-write the whole event).
- ClanShare off or plot excluded: sit **this** throne to manage its servants. Picker is a no-op / explains that.

May manage from plot A only if A and the target throne are both on the character’s ClanShare island.

Castle guests do not get clan-wide throne management.
