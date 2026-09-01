# Satisvampory

Server-only V Rising 1.1 BepInEx plugin. Castle logistics (named dest, ClanShare, conveyors, reserve/caps, treasury lend) plus ground scoop.

Current version **1.0.24**.

Satisvampory should be the only logistics/scoop plugin in `BepInEx/plugins`.

## Commands

Full list, scopes (you / plot / castle / clan / server), and examples: **[COMMANDS.md](COMMANDS.md)**.

In game: **`.s help`**. Prefix **`.s`**. Admin **`.sg`**. Chest floors are **`.s reserve`** (there is no leftover command). `.s cap` is castle production; `.s bagcap` is personal scoop.

## New castle (ClanShare)

With **`.s cs`** on, a brand-new plot is already on the clan island. `.stash` and RR (double-tap R / `.s ss`) work as soon as the heart is placed — they dump to clan chests, not only this plot.

Stand on the plot while you build. Covering only runs while a **clan member** is standing there (castle guests do not trigger clan covering or pull from other clan plots). If clan **reserves** are still met at the other castles, the kit **stays** when you leave.

1. Place the **heart**.
2. Lay **foundations**.
3. Place **one chest**. After a few seconds it fills with starter mats (planks, stone bricks, gem dust, copper, iron, stone) plus enough to enclose a small castle: **walls** and **treasury floor**.
4. Build that small enclosed room on treasury floor.
5. Place about **three large chests** on the treasury floor so the clan covering buffer can land (enough to place about 3 of whichever **unlocked** piece is hungriest per material, capped at 200 of each mat so a 1200-stone wall does not drain the warehouse). **One** clan member standing: leftover-bypass so a new castle can start (kit, covering 1x, heart upgrade, heart fuel seed). **Two or more** occupied clan plots: those pulls honor reserve. Covering 3x always honors reserve. Unnamed chests (blank nameplate) are generic dumps even if the prefab is "Jewel Storage". Chests named Empty, General, or Everything Else are also generic dumps.

Unnamed chests are fine. Named `s#` / `r#` belts can wait until the room exists.

To **dismantle** a filled chest: empty it. Kit/covering will not restuff that chest for **5 minutes** (or leave the plot). A new chest is a new fill.

To keep this castle **out of ClanShare**: stand on it as the heart owner and run **`.s cse`**. Standing there is then this plot only. Run `.s cse` again to include it.

## Install

1. Stop `VRisingServer`.
2. Remove any older logistics or scoop plugin DLLs from `BepInEx/plugins`.
3. Copy `Satisvampory.dll` plus `HookDOTS.API.dll` and VampireCommandFramework.
4. Start the server.

Build from this repo (needs a V Rising dedicated-server interop tree; `Satisvampory.csproj` points at `C:\VRisingServer\BepInEx\` by default):

```
dotnet build Satisvampory.csproj -c Release
```

Stage the output as `BepInEx/plugins/Satisvampory.dll.next` while the server is running, then bounce to swap.

## License

AGPL-3.0. See [LICENSE](LICENSE).
