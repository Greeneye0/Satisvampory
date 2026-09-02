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

When a plot is ON: each time a hunt **returns**, surviving (not dead, not injured) servants are sent again on the **same** zone / mission / throne after they are idle. Dead or injured servants are skipped. If vanilla does not accept the send, chat **could not send** with the real reason (missing zone, servants not ready, vanilla rejected) — do not claim they left. Raided hearts do not resend. Auto-send bypasses “must be sitting the throne”.

The send event needs a **MapZoneId** and a **duration slot** (`MissionDataID` 0–4 from `ServantMissionSetting`). That slot is not the mission prefab hash — stuffing `ServantMissionAsset.GuidHash` into `MissionDataID` makes vanilla drop the send. If the zone was not remembered (server bounce while they were out, or an empty auto-send overwriting the last hunt), look up the zone by destination name / mission prefab before injecting `SendOnMissionEvent`. Keep a valid slot 0. Send **coffin** NetworkIds (what the throne UI sends), not the servant unit. Point the sender’s Interactor at the throne for the auto-send tick. Do not overwrite a remembered dest with an empty auto-send event. Capture in-flight hunts on **boot** when `.sg rh` is already ON, not only when someone toggles it.

Turning **`.sg rh` ON**, **`.s rh on`**, or **`.s rh all`** captures hunts **already in the field** so they loop when they get back.

**`.sg rhmax [1-100]`** is the intended max success % (default **99**) for plots with repeat ON. **Do not Harmony-patch `GetMissionSuccessChanceForServant_Server` or `_Client`** — those take `FixedList4096Bytes<Entry>` and the IL2CPP trampoline NREs, which makes the native roll return **0% survive** (servants die). Leave the vanilla roll until a safe hook exists. Chat still shows Repeat ON/OFF and the configured max.

On every **send**, the sender (else heart owner) gets: who went, destination, survival % (after cap), and Repeat ON/OFF with the max. Example: `Hunt sent (plot 45 L2) — Haunted Iron Mine (99%): Stephen. Repeat ON (max 99%).`

On every **return** (repeat on or off), connected **clan members** (and the heart owner) get destination, who came back, loot stacks, or who **died** / injured. Example: `Hunt returned (plot 86 L4) — Pools of Rebirth: Raven — 10 Oil. Repeat ON — sending again.` After vanilla accepts: `Repeat: sent Raven to Pools of Rebirth.`

Do not cap the throne UI via HuntClock on `GetMissionSuccessChanceForServant_Client` (same trampoline crash).

Do not Harmony-patch Burst `GetResponseEntries`. Remember the hunt from `SendOnMissionEvent` (after any throne rewrite). Abort clears that memory.

## `.s throne` / `.s hunt`

Optional ClanShare helper to send another plot’s servants from this chair (vanilla map click). Not required for repeat hunts. No client overlay.
