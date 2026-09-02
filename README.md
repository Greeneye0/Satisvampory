# Satisvampory

> **Attribution:** Satisvampory is a modified fork of **[KindredLogistics](https://github.com/Odjit/KindredLogistics)** by **Odjit** and **Zfolmt**, used under the AGPL-3.0 license. It was forked from KindredLogistics 1.6.1 and modified by Greeneye from August 2026 onward; this is not the original project and is not maintained or endorsed by its authors. Full notice: [NOTICE.md](https://github.com/Greeneye0/Satisvampory/blob/main/NOTICE.md). Thank you Dj and Mitch for the foundation this is built on.

Current version **1.0.85**. Server-only V Rising 1.1 BepInEx plugin for dedicated servers.

Satisvampory is a server-side quality-of-life addon. It was built so playing the game stops feeling like a chore: less running between chests, less babysitting castle hearts and servants, and more actually playing. Nothing to install on the client.

## Main features

- **Item pickup (scoop)** - Pick up items around you with a configurable radius (1-50). Choose between grabbing everything you walk past or only items that drop near you. Per-item caps and an exclude list so your bags don't fill with cotton.
- **Clan Share** - Treat every clan plot as one island. If the clan has the mats, you can build and craft with them from any castle (craft-pull honors reserves). Includes **.pull** commands to grab items straight into your bags (still a little buggy).
- **Heart top-off** - Keeps castle hearts fed with Blood Essence automatically.
- **Improved conveyor belts** - Name chests **s#** (sender) and **r#** (receiver) and items flow between them. Like Kindred's belts, but with production caps, chest reserves, and box-to-box support.
- **Item groups** - Predefined groups (ore, mushrooms, and more), fully customizable per castle. **.s group ore** lists every member's stock and where it is.
- **Quick deposit (RR)** - Double-tap R or **.stash** to dump your inventory into the right chests. Works on the local castle, across all clan castles with Clan Share on, or from anywhere with **.s rrglobal**.
- **Priority containers** - Deposits go to the best chest in order: conveyors, exact name match, similar group or type name, then everything else. Chests can be excluded with **NS** or a trailing **''** in the name.
- **Servant deposits** - Servants returning from hunts stash their loot automatically using the same chest rules as you.
- **Auto rerun hunts** - Servants go straight back out on the same hunt when they get home. Toggle per castle.
- **Find item / find chest** - **.fi "Blood Essence"** and **.fc salvage** tell you which plot and chest, with **(here)** marking where you stand.
- **.s need** - Shows the top 10 things your stations are hungriest for, so you know what to hunt or farm next.
- **Item aliases** - Out of the box: BE (Blood Essence), GBE (Greater), PBE (Primal), ABE (Ancestral), GSS (Greater Stygian Shard), SGS (Siege Golem Stone), DSI (Dark Silver Ingot), OT (Onyx Tear). Admins can add their own with **.sg alias add**.
- **Detailed hunt results** - Loot and losses from each servant hunt reported in chat to the owner.
- **Server admin toggles** - Every feature has a server-wide allow under the **.sg** prefix (pull, craft-pull, conveyors, salvage, repeat hunts, and more), so admins decide what players can turn on.

## Getting started

Type **.s help** in game. Player commands use the **.s** prefix, admin toggles use **.sg**.

**Full help guide with every command and an example for each:** [GUIDE.md](https://github.com/Greeneye0/Satisvampory/blob/main/GUIDE.md) (also included in the zip). Quick reference table: [COMMANDS.md](https://github.com/Greeneye0/Satisvampory/blob/main/COMMANDS.md).

## Diagnostics

- `.s conv <item>` - Why a conveyor is not moving that item (station, line, reason).
- **.s need** - Station demand, total stock, and reserve for the top 10 inputs.
- **.s diag** - Drops a marker in the server's rolling log for dupes, missing items, or lag. Tell your admin the time.
- **.s settings** - Your toggles plus the reserve on the castle you're standing on.
- **.s vq** - Work-queue depth (admin).

## Install

- Stop the dedicated server.
- Remove any other logistics or scoop plugin DLLs from BepInEx\plugins. Satisvampory should be the only one.
- Copy everything from the zip's plugins folder (Satisvampory.dll, HookDOTS.API.dll, VampireCommandFramework.dll) into BepInEx\plugins.
- Start the server.

## Quickest way to get help

Point ChatGPT, Claude, or any LLM at the GitHub repo and ask it your question. The specs folder and COMMANDS.md describe every behavior, so it will usually give you a better answer than I can at 2am.

Source (AGPL-3.0): [github.com/Greeneye0/Satisvampory](https://github.com/Greeneye0/Satisvampory)

## Specs

Product contract (dest ranking, tidy, covering, ClanShare, belts, scoop, servants): **[specs/](specs/README.md)**. Behavior changes update the matching spec in the same commit.

## Commands

Quick table: **[COMMANDS.md](COMMANDS.md)**. Full guide with an example per command: **[GUIDE.md](GUIDE.md)**.

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

## Build from source

Build from this repo (needs a V Rising dedicated-server interop tree; `Satisvampory.csproj` points at `C:\VRisingServer\BepInEx\` by default):

```
dotnet build Satisvampory.csproj -c Release
```

Stage the output as `BepInEx/plugins/Satisvampory.dll.next` while the server is running, then bounce to swap.

# Chat commands overview

## Scoop (you)

| Command | What it does | Example |
| --- | --- | --- |
| `.s` / `.scoop` / `.sc` | Scoop now in your radius | `.s` |
| `.s auto` | Toggle auto-scoop | `.s auto` |
| `.s auto around\|all` | Auto ON and set the filter | `.s auto around` |
| `.s filter` / `.s filter around\|all` | Show or set the filter | `.s filter all` |
| `.s radius` / `.s radius <1-50>` | Show or set radius | `.s radius 15` |
| `.s exclude` / `.s exclude <item\|group>` | Show or toggle the skip list | `.s exclude cotton` |
| `.s bagcap` / `.s bagcap <item>` / `.s bagcap <item> <n>` | Show or set scoop caps | `.s bagcap cotton 200` |
| `.s bagcapclear` | Clear every scoop cap | `.s bagcapclear` |
| `.s mode bags\|guild` | Count bags only, or bags plus clan stashes | `.s mode guild` |
| `.s notify` / `.s notify off\|manual\|on` | Pickup chat mode | `.s notify on` |
| `.s last` | Reprint last scoop line | `.s last` |

## Stash, pull, find (you)

| Command | What it does | Example |
| --- | --- | --- |
| `.stash` | Dump inventory except hotbar | `.stash` |
| `.s ss` | Enable RR (double-tap R / sort button) | `.s ss` |
| `.s ssh` | Silent stash | `.s ssh` |
| `.s rrglobal` / `.s rrg` | Allow off-plot stash | `.s rrglobal` |
| `.pull <item> <n>` | Pull into bags, ignores reserve | `.pull plank 50` |
| `.s sp` | Silent pull | `.s sp` |
| `.s dpl` | Never pull the last stack (default ON) | `.s dpl` |
| `.s cr` | Craft-pull, honors reserve | `.s cr` |
| `.fi <item>` | Find item | `.fi BE` |
| `.fc <name>` | Find chests by name | `.fc overflow` |
| `.s tidy` | Restack chests onto better dests | `.s tidy` |
| `.s <n>` / `.s pick <n>` | Choose from an ambiguous search | `.s 2` |

## Castle floors (heart owner, stand on the plot)

| Command | What it does | Example |
| --- | --- | --- |
| `.s reserve` / `.s reserve <n>` | Show or set default reserve (default 10, `0` off) | `.s reserve 20` |
| `.s reserve <item\|group>` / `... <n>` | Show or set one reserve | `.s reserve plank 50` |
| `.s rsvc <item>` or `.s reserve <item> -1` | Clear an override | `.s rsvc plank` |
| `.s cap` / `.s cap <item\|group>` / `... <n>` | Production caps. `0` none, `-1` unlimited | `.s cap plank 200` |
| `.s capclear <item>` | Clear a cap | `.s capclear plank` |
| `.s group` / `.s group <name>` / `.s group <name> full` | List groups, totals, or totals with reserve and cap | `.s group ore full` |
| `.s group create\|delete\|restore <name>` | Manage groups | `.s group restore mushrooms` |
| `.s group <name> add\|remove <item> ...` | Edit members | `.s group ore add "Iron Ore"` |

## Belts, clan, plot

| Command | Scope | What it does | Example |
| --- | --- | --- | --- |
| `.s co` | You | Toggle conveyors | `.s co` |
| `.s conv <item>` | Plot / island | Why a belt is not moving that item | `.s conv plank` |
| `.s need` | Plot / island | Top 10 station inputs wanted | `.s need` |
| `.s cs` / `.s gs` | Clan | Clan Share on or off | `.s cs` |
| `.s cse` | Plot, owner | Exclude or include this plot | `.s cse` |
| `.s hf` | Plot | Heart Blood Essence auto-feed | `.s hf` |
| `.s sal` | Plot | Salvage chests into the Devourer | `.s sal` |
| `.s us` | You | Spawner chests | `.s us` |
| `.s bz` | You | Brazier chests | `.s bz` |
| `.s settings` / `.s s` | You + castle | Show toggles and reserve | `.s settings` |

## Servants and hunts

| Command | Scope | What it does | Example |
| --- | --- | --- | --- |
| `.s asm` | You | Servant loot auto-stash | `.s asm` |
| `.s rh` / `.s rh all` / `.s rh off` / `.s rh <n>` | Clan / plot | Repeat-hunt status and toggles | `.s rh 2` |
| `.s throne` / `.s throne <n>` / `.s throne here` | You | Which clan plot this throne hunts from | `.s throne 2` |
| `.s hunt [1] [2] [3]` | You | Pick servants, then click a map zone | `.s hunt 1 2` |

## Diagnostics

| Command | What it does | Example |
| --- | --- | --- |
| `.s conv <item>` | Belt troubleshooting | `.s conv "Iron Ingot"` |
| `.s need` | Station demand, stock, reserve | `.s need` |
| `.s diag` | Mark the server log for dupes, missing items, or lag | `.s diag` |
| `.s settings` | Your toggles and the castle reserve | `.s settings` |
| `.s vq` | Work-queue depth (admin) | `.s vq` |
| `.s peek [plot]` | Dump a plot to `BepInEx/config/Satisvampory/debug/res.json` (admin) | `.s peek` |
| `.s catalog` | Dump the item catalog to `BepInEx\Log\item-catalog.csv` (admin) | `.s catalog` |

## Server admin (`.sg`)

| Command | What it does | Example |
| --- | --- | --- |
| `.sg s` | Show all allows | `.sg s` |
| `.sg ss` / `.sg p` / `.sg cr` / `.sg rrg` / `.sg asm` | Allow RR, pull, craft-pull, off-plot stash, servant stash | `.sg p` |
| `.sg rh` / `.sg repeathunt` | Allow repeat hunts (default OFF) | `.sg rh` |
| `.sg rhmax [1-100]` | Repeat-hunt success cap (default 99) | `.sg rhmax 90` |
| `.sg co` / `.sg convloop` | Allow belts, allow belt loops (default OFF) | `.sg co` |
| `.sg sal` / `.sg us` / `.sg bz` / `.sg nam` / `.sg trash` | Allow salvage, spawner, brazier, smart braziers, trash | `.sg sal` |
| `.sg alias` / `.sg alias add <a> <item>` / `.sg alias del <a>` | Item aliases | `.sg alias add ing "Iron Ingot"` |
| `.emptytrash` | Empty `trash` chests on this plot | `.emptytrash` |
| `.adminstash <item> <n>` | Spawn items into this plot's chests | `.adminstash plank 100` |

## Chest plate cheat sheet

| Plate | Meaning |
| --- | --- |
| `plank s1` / `plank r1` | Belt sender / receiver, line 1 |
| `overflow` | Last-resort dump |
| `salvage`, `spoils`, `trash`, `spawner`, `brazier`, `night`, `prox` | Special-purpose chests |
| `NS` or trailing `''` | Skip entirely |
| blank, `General`, `Empty`, `Everything Else` | Generic dump |

## Credits and license

Based on [KindredLogistics](https://github.com/Odjit/KindredLogistics) by Odjit and Zfolmt (AGPL-3.0). Modified by Greeneye, 2026. See [NOTICE.md](NOTICE.md) for the full attribution and modification notice.

Satisvampory is AGPL-3.0. See [LICENSE](LICENSE). Source: https://github.com/Greeneye0/Satisvampory
