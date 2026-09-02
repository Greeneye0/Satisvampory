# Satisvampory help guide

Every command, what it does, and an example. Type commands in game chat.

- **`.s`** is the player prefix. **`.sg`** is the server-admin prefix.
- **`.s help`** prints the short in-game summary. This file is the long version.
- Most castle features need **two switches**: the server allow (`.sg ...`, admin) and your own toggle (`.s ...`). Server allows default **ON**. Player toggles default **OFF** except scoop auto and `.s dpl`.
- Quote item names with spaces: `.pull "Iron Ore" 20`. If a name matches several items, pick one with `.s 2` (or `.s pick 2`).

## Scopes

| Scope | Meaning |
| --- | --- |
| **You** | Your Steam account. Follows you to every castle. |
| **Plot** | The castle under your feet. |
| **Castle** | Stored on that castle's heart owner. Stand on the plot to view or set. |
| **Clan** | Every non-excluded clan plot, treated as one island. |
| **Server** | Whole dedicated server. Admin only (`adminlist.txt` plus in-game `adminauth`). |

---

## 1. Scoop (item pickup) — scope: you

Picks up world drops into your bags. Never death bags, soul shards, chests, or stations. Does not need `.sg`.

| Command | What it does | Example |
| --- | --- | --- |
| `.s` / `.scoop` / `.sc` | Scoop everything in your radius right now. | `.s` |
| `.s auto` | Toggle auto-scoop on or off. Default ON, filter `all`. | `.s auto` |
| `.s auto around` | Auto ON, only piles that spawned while you were nearby. | `.s auto around` |
| `.s auto all` | Auto ON, every pile you walk past. | `.s auto all` |
| `.s filter` | Show your auto filter. | `.s filter` |
| `.s filter around` / `all` | Change the filter without toggling auto. | `.s filter around` |
| `.s radius` | Show your scoop radius. | `.s radius` |
| `.s radius <1-50>` | Set radius. Default 10. | `.s radius 15` |
| `.s exclude` | List items auto-scoop skips. | `.s exclude` |
| `.s exclude <item\|group>` | Toggle an item or group on the skip list. | `.s exclude cotton` |
| `.s bagcap` | List your personal scoop caps. | `.s bagcap` |
| `.s bagcap <item>` | Show one cap and your current count. | `.s bagcap cotton` |
| `.s bagcap <item> <n>` | Stop scooping that item at n. `0` = never scoop it, `-1` = unlimited. | `.s bagcap cotton 200` |
| `.s bagcapclear` | Clear every scoop cap. | `.s bagcapclear` |
| `.s mode bags` / `guild` | Count only your bags, or bags plus Clan Share stashes, toward caps. | `.s mode guild` |
| `.s notify` | Show pickup chat mode. | `.s notify` |
| `.s notify off\|manual\|on` | `manual` (default) chats only for `.s`. `on` chats for auto too. | `.s notify on` |
| `.s last` | Reprint your last scoop line. | `.s last` |

**Example: vacuum everything but stop hoarding cotton**

```
.s bagcap cotton 200
.s auto all
```

---

## 2. Stash, pull, find — scope: you; destination is plot or clan

With Clan Share ON the destination or source is the whole clan island. OFF means the plot you stand on.

| Command | What it does | Example |
| --- | --- | --- |
| `.stash` | Dump your inventory (not hotbar) into matching chests. | `.stash` |
| `.s ss` | Enable double-click sort or double-tap R to stash (RR). | `.s ss` |
| `.s ssh` | Silent stash: no "went to chest X" chat. | `.s ssh` |
| `.s rrglobal` / `.s rrg` | Allow stash and RR while off your plot. Default OFF. Needs `.sg rrg`. | `.s rrglobal` |
| `.pull <item> <n>` | Pull items into your bags. Ignores reserve. Needs `.sg p`. | `.pull plank 50` |
| `.s sp` | Silent pull. | `.s sp` |
| `.s dpl` | Don't pull the last stack from a chest. Default ON. | `.s dpl` |
| `.s cr` | Craft-pull: right-click a recipe to pull missing ingredients. Honors reserve. Needs `.sg cr`. | `.s cr` |
| `.fi <item>` / `.s fi <item>` | Find an item. Lists plot, chest, and count. `(here)` marks your plot. | `.fi "Blood Essence"` |
| `.fc <name>` | Find chests by name. | `.fc salvage` |
| `.s tidy` | Restack existing chests onto better destinations. Never drains belts, skip chests, or hearts. Ignores reserve. | `.s tidy` |
| `.s <n>` / `.s pick <n>` | Pick from the last ambiguous name search. | `.s 2` |

**Example: grab 200 plank for a build while keeping 50 in chests**

```
.s reserve plank 50
.pull plank 200
```

Reserve is honored by craft-pull and conveyors but not by `.pull`.

---

## 3. Chest names (priority containers)

Deposits pick the best chest in this order: conveyor sender, exact name match, group or type name, then anything else.

| Name on the chest | Meaning |
| --- | --- |
| `plank s1` | Sender belt for plank, line 1. |
| `plank r1` | Receiver belt for plank, line 1. |
| `overflow` | Takes whatever has nowhere better. |
| `salvage` | Fed into the Devourer when `.s sal` is on. |
| `spoils` | Servant loot dump. Source only. |
| `trash` | Emptied by `.emptytrash` when `.sg trash` is on. |
| `spawner` | Fills unit spawner stations (`.s us`). |
| `brazier`, `night`, `prox` | Fills braziers, or night-only or proximity braziers (`.s bz`, `.sg nam`). |
| `NS` or a trailing `''` | Skip this chest entirely. |
| `Empty`, `General`, `Everything Else`, or blank | Generic dump. |

Built-in dest words like `blood`, `stone`, `jewel` match that group, not a substring of an item name. Blood Jewel goes to `jewel`.

---

## 4. Castle floors: reserve, cap, groups — scope: castle (heart owner)

Stand on the plot. Default reserve 10. Default cap unlimited.

| Command | What it does | Example |
| --- | --- | --- |
| `.s reserve` | Show the default reserve. | `.s reserve` |
| `.s reserve <n>` | Set the default for every item. `0` disables. | `.s reserve 10` |
| `.s reserve <item\|group>` | Show one item or group. | `.s reserve plank` |
| `.s reserve <item\|group> <n>` | Leave n of that item in chests. | `.s reserve plank 50` |
| `.s reserve <item> -1` / `.s rsvc <item>` | Clear the override. | `.s rsvc plank` |
| `.s cap` | List production caps (belts stop at this many on the island). | `.s cap` |
| `.s cap <item\|group>` | Show one cap. | `.s cap plank` |
| `.s cap <item\|group> <n>` | Set a cap. `0` = make none, `-1` = unlimited. | `.s cap plank 200` |
| `.s capclear <item>` | Clear one cap. | `.s capclear plank` |
| `.s group` | List built-in and custom groups. | `.s group` |
| `.s group <name>` | Each member's island total plus a grand total. | `.s group ore` |
| `.s group <name> full` | Same, plus reserve and cap per item. | `.s group ore full` |
| `.s group create <name>` | New custom group. | `.s group create belts` |
| `.s group delete <name>` | Delete a group. | `.s group delete belts` |
| `.s group restore [name]` | Restore built-in defaults. | `.s group restore mushrooms` |
| `.s group <name> add <item> ...` | Add items. First edit of a built-in copies its default list. | `.s group ore add "Iron Ore" "Copper Ore"` |
| `.s group <name> remove <item> ...` | Remove items. | `.s group ore remove "Copper Ore"` |

**Example: cotton cap vs cotton bagcap**

`.s cap cotton 200` stops *conveyors* when the castle already has 200. `.s bagcap cotton 200` stops *scoop* when your bags have 200.

---

## 5. Conveyors (belts) — scope: you, plus `.sg co`

Name a chest `plank s1` and another `plank r1`. Items flow from sender to receiver, honoring reserve and cap. Stations count as receivers.

| Command | What it does | Example |
| --- | --- | --- |
| `.s co` | Toggle your conveyors. | `.s co` |
| `.s conv <item>` | Why a belt is not moving that item: station, line, reason. | `.s conv plank` |
| `.s need` | Top 10 station inputs the belts want, with demand, stock, and reserve. | `.s need` |

Chest-to-chest loops on the same group (an `s#` that is also a dest) need `.sg convloop`.

---

## 6. Clan Share and plots — scope: clan / plot

| Command | Scope | What it does | Example |
| --- | --- | --- | --- |
| `.s cs` / `.s gs` | Clan | Clan Share on or off for all members and all plots. Default OFF. | `.s cs` |
| `.s cse` | Plot, owner only | Exclude or include this plot from Clan Share. | `.s cse` |
| `.s hf` | Plot | Heart Blood Essence auto-feed (heart top-off). Default ON. | `.s hf` |
| `.s sal` | Plot | Feed `salvage` chests into the Devourer. Needs `.sg sal`. | `.s sal` |
| `.s us` | You | Fill unit spawners from `spawner` chests. Needs `.sg us`. | `.s us` |
| `.s bz` | You | Fill braziers from `brazier` chests. Needs `.sg bz`. | `.s bz` |
| `.s settings` / `.s s` | You + castle | Your toggles and this castle's reserve. | `.s settings` |

**Example: share the clan but keep one castle private**

```
.s cs          (stand on any clan plot)
.s cse         (stand on the plot you want kept local, as its heart owner)
```

---

## 7. Servants and hunts

| Command | Scope | What it does | Example |
| --- | --- | --- | --- |
| `.s asm` | You | Servants stash mission loot with the same chest rules as `.stash`. Needs `.sg asm`. | `.s asm` |
| `.s rh` | Clan / plot | List each castle's repeat-hunt status. Needs `.sg rh`. | `.s rh` |
| `.s rh all` | Clan | Repeat hunts ON for every listed castle. | `.s rh all` |
| `.s rh off` | Plot | Repeat hunts OFF for this castle. | `.s rh off` |
| `.s rh <n>` | Clan | Toggle that row. | `.s rh 2` |
| `.s throne` / `.s throne <n>` / `.s throne here` | You | Clan Share: pick which clan plot the next hunt-map click sends from. Stay seated. | `.s throne 2` |
| `.s hunt [1] [2] [3]` | You | Pick up to 3 servants on the managed plot, then click a discovered zone on the map. | `.s hunt 1 2` |

Hunt results (loot, losses) are chatted to the owner when repeat hunts are on.

---

## 8. Diagnostics

| Command | What it does | Example |
| --- | --- | --- |
| `.s conv <item>` | Why a belt is not moving that item. | `.s conv "Iron Ingot"` |
| `.s need` | Station demand, stock, and reserve for the top 10 inputs. | `.s need` |
| `.s diag` | Drop a marker in the server's rolling log for dupes, missing items, or lag. Tell an admin the time. | `.s diag` |
| `.s settings` | Your toggles plus the standing castle's reserve. | `.s settings` |
| `.s vq` | Work-queue depth (admin). | `.s vq` |
| `.s peek [plot]` | Write a live plot dump to `BepInEx/config/Satisvampory/debug/res.json` (admin). | `.s peek` |
| `.s catalog` | Dump the item catalog to `BepInEx\Log\item-catalog.csv` (admin). | `.s catalog` |

---

## 9. Server admin toggles — scope: server

Needs your SteamID64 in `save-data\Settings\adminlist.txt` **and** console `adminauth`. All allows default ON except `.sg rh` and `.sg convloop`.

| Command | What it allows | Example |
| --- | --- | --- |
| `.sg s` | Show all server allows. | `.sg s` |
| `.sg ss` | Sort-stash (RR). | `.sg ss` |
| `.sg p` | `.pull`. | `.sg p` |
| `.sg cr` | Craft-pull. | `.sg cr` |
| `.sg rrg` | Off-plot RR and stash. | `.sg rrg` |
| `.sg asm` | Servant auto-stash. | `.sg asm` |
| `.sg rh` | Repeat hunts. Default OFF. Turning on captures hunts already out. | `.sg rh` |
| `.sg rhmax [1-100]` | Intended max success % for repeat hunts. Default 99. | `.sg rhmax 90` |
| `.sg co` | Conveyors. | `.sg co` |
| `.sg convloop` / `.sg cloop` | Belt loops on the same group. Default OFF. | `.sg convloop` |
| `.sg sal` | Devourer salvage. | `.sg sal` |
| `.sg us` | `spawner` chests. | `.sg us` |
| `.sg bz` | `brazier` chests. | `.sg bz` |
| `.sg nam` | `night` and `prox` braziers. | `.sg nam` |
| `.sg trash` | Emptying `trash` chests. | `.sg trash` |
| `.sg alias` | List item aliases. | `.sg alias` |
| `.sg alias add <alias> <item>` | Add an alias for find, pull, and chest names. Persists. | `.sg alias add ing "Iron Ingot"` |
| `.sg alias del <alias>` | Remove an admin alias. Built-ins stay. | `.sg alias del ing` |
| `.emptytrash` | Empty `trash` chests on the standing plot. | `.emptytrash` |
| `.adminstash <item> <n>` | Spawn items into the standing plot's chests. | `.adminstash plank 100` |

Built-in aliases: **BE** Blood Essence, **GBE** Greater, **PBE** Primal, **ABE** Ancestral, **GSS** Greater Stygian Shard, **SGS** Siege Golem Stone, **DSI** Dark Silver Ingot, **OT** Onyx Tear.

---

## 10. Setting up a new castle with Clan Share

1. Place the heart and lay foundations.
2. Place one chest. It fills with starter mats within a few seconds.
3. Build a small enclosed room on treasury floor.
4. Place about three large chests on the treasury floor for the covering buffer.
5. Name belts (`s#` / `r#`) later, once the room exists.

Covering only runs while a clan member stands on the plot. To dismantle a filled chest, empty it first. It will not be restuffed for 5 minutes.

## Quickest help

Point ChatGPT, Claude, or any LLM at https://github.com/Greeneye0/Satisvampory and ask. The `specs/` folder and this guide describe every behavior.
