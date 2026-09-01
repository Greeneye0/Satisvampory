# 10 — Install, bounce, debug, versioning

## Install

1. Stop `VRisingServer`.
2. Remove any older logistics or scoop plugin DLLs from `BepInEx/plugins`.
3. Copy `Satisvampory.dll` plus `HookDOTS.API.dll` and VampireCommandFramework.
4. Start the server.

Satisvampory MUST be the only logistics/scoop plugin in that folder.

Build:

```
dotnet build Satisvampory.csproj -c Release
```

Interop tree defaults to `C:\VRisingServer\BepInEx\`.

## Live dedicated (this world)

- Source: git origin `main`.
- Live DLL: `C:\VRisingServer\BepInEx\plugins\Satisvampory.dll`.
- While the server is running, stage **`Satisvampory.dll.next`**. Never overwrite `Satisvampory.dll` while `VRisingServer.exe` is up.
- Copy the plugin only after the process has **exited**.
- Start with WMI `Win32_Process.Create` of `cmd.exe /c start_server.bat` in `C:\VRisingServer`. Do not use `Start-Process` (Job Object kills the server). Never assign `$PID`.
- World: **Moo World** / `world1`.
- Harmony `OnUpdate` patches take `__instance`, not `system`.
- Bounce only when the user is out, or the debug mailbox `players` list is empty.

Bouncing is not shipping.

## Version ship

When you bump `Satisvampory.csproj` `Version` / `AssemblyVersion` / `FileVersion`:

1. Commit that version with the work it ships (author: Satisvampory `<satisvampory@users.noreply.github.com>`).
2. `git push origin main` in the same turn. Origin is the ship.
3. Do not wait to be asked.
4. Update the matching **spec** in the same commit if behavior changed. Update `COMMANDS.md` if the player-facing table changed.

GitHub: https://github.com/Greeneye0/Satisvampory (public AGPL).

## Debug mailbox

Local file mailbox. No network socket. No give/spawn (except covering `sim` with `apply:true`, which actually moves).

- Write `BepInEx/config/Satisvampory/debug/req.json`
- Read `res.json`
- Poll ~0.25s, main thread only

Ops include: `help`, `players`, `plots`, `plot`, `item`, `covering`, `upgrade`, `settings`, `dest`, `sim`, `fair`, `occupy`, `guest`, `cover`, `unstick`, `need`, `selftest`, `log`, `perf`, `logdump`, `servants`, `servantstash`, `thrones`.

`thrones` also dumps `.s throne` picks, last rewrite (sitting/selected/from/to), whether Request and Interactor were patched, and servant names on the last `ServantInfoEvent.Response`.

`players` is the bounce gate. `selftest` includes dest-ranking assertions (`StashRouting.SelfTestDest`).

`.s peek [plot]` writes a live plot dump to `res.json` (admin).
`.s diag` marks a snapshot in the rolling log.
`.s catalog` dumps `BepInEx\Log\item-catalog.csv`.

## Player gates (ops-relevant)

Do not run tidy / stash / covering dest into raided hearts. Empty-hold (5 minutes after a player empties a seeded kit chest) blocks restuff so unbuild works.
