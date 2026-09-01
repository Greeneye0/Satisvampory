# Satisvampory

Server-only V Rising 1.1 BepInEx plugin. Castle logistics (named dest, ClanShare, conveyors, reserve/caps, treasury lend) plus ground scoop.

Current version **1.0.20**.

Satisvampory should be the only logistics/scoop plugin in `BepInEx/plugins`.

## Commands

Full list, scopes (you / plot / castle / clan / server), and examples: **[COMMANDS.md](COMMANDS.md)**.

In game: **`.s help`**. Prefix **`.s`**. Admin **`.sg`**. Chest floors are **`.s reserve`** (there is no leftover command). `.s cap` is castle production; `.s bagcap` is personal scoop.

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
