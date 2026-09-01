# Satisvampory

## Product specs

`specs/` is the product contract. Read the matching spec before changing dest ranking, tidy, covering, ClanShare, belts, scoop, servants, or settings. A behavior change updates that spec in the same commit. If code and spec disagree, the spec is the intended product.

## Ship versions to origin

When you bump `Satisvampory.csproj` `Version` / `AssemblyVersion` / `FileVersion`:

1. Commit that version with the work it ships (author: Satisvampory).
2. `git push origin main` in the same turn. Origin is the ship. Do not leave a new version only local.
3. Do not wait to be asked.

Bouncing the dedicated server is not shipping. Copy the DLL and restart only when the user is out.

## Live dedicated

- Source: this repo. Live DLL: `C:\VRisingServer\BepInEx\plugins\Satisvampory.dll`. Stage `Satisvampory.dll.next` while the server is running.
- Copy the plugin only after `VRisingServer.exe` has exited. Never dual-load Kindred.
- Start with WMI `Win32_Process.Create` of `cmd.exe /c start_server.bat` in `C:\VRisingServer`. Do not use Start-Process (Job Object kills the server). Never assign `$PID`.
- World: `Moo World` / `world1`. Harmony `OnUpdate` patches take `__instance`, not `system`.
