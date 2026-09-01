# 08 — Servants and throne

## Servant auto-stash (`.s asm`)

When a servant returns from a mission, dump loot with the **same dest ranking** as `.stash` / RR (named dest, then overflow).

- Player `.s asm` default **OFF**. Server `.sg asm` default ON.
- ClanShare ON: dest is the clan island except `.s cse` excluded plots (`GetServantStashPlotIds`).
- Skip `NS` / special named chests as dests. Skip raided hearts.
- Overflow dests: prefer non-special overflow, prefer the servant’s home plot.

Do not give servant loot a different dest order than stash.

## `.s throne` (ClanShare hunt picker)

ClanShare ON: numbered picker listing each plot’s living servants, then **teleports you to that castle’s throne** so the vanilla hunt UI is that plot.

- Default is the castle you are sitting on / standing on.
- `.s throne` lists plots and servant names; pick with `.s 2` (pending pick TTL **2 minutes**).
- Hunt UI loads **on sit** (the map opens from the InteractBuff). Teleporting while seated keeps the old throne bind; standing up snaps you home.
- Picking another plot **unseats** (destroy `InteractBuff`, `StopInteractingWithObjectEvent`, clear `Interactor.Target`), then `TeleportUtilityServer.Teleport` (and `Translation` / `LastTranslation`) to that throne (save return position), then casts that throne’s interact ability so the hunt map reloads for that plot.
- `.s throne here` unseats and teleports back.
- Do **not** Harmony-patch `GetResponseEntries` / extra servant entries (Burst abort on sit).
- Vanilla listing stays Burst-safe (no extra entries). ServantInfo / hunt events are retargeted at that plot’s real throne.
- Never treat a **castle heart** as a throne (`ActiveServantMission` lives on the heart). Learn the real sit-target from `Request.Throne`, then `UseThrone` / prefab name containing `Throne`. Patch `Request.Throne` in-place. Also retarget `Interactor.Target` for that update; restore next tick. Do **not** Harmony-patch `GetResponseEntries` (Burst abort on sit).
- Debug mailbox `thrones` lists throne positions and connected players. `gotothrone` with `plot:N` actually moves a connected player onto that throne (does **not** set the `.s throne` pick — sit there for a vanilla hunt-UI test). `name":"here"` returns. `thrones` also records last response names and whether the cached entity is a heart.
- ClanShare off or plot excluded: sit **this** throne to manage its servants. Picker is a no-op / explains that.

May manage from plot A only if A and the target throne are both on the character’s ClanShare island.

Castle guests do not get clan-wide throne management.
