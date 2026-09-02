# Satisvampory help guide

Part 1 walks through every feature from the mod description with setup steps and a worked example. Part 2 is the full command reference. Type commands in game chat.

**The basics you need before anything else**

- **`.s`** is the player prefix. **`.sg`** is the server-admin prefix.
- Most castle features have **two switches**: a server allow (`.sg ...`, set once by an admin) and your own toggle (`.s ...`). Server allows default **ON** except repeat hunts and belt loops. Player toggles default **OFF** except scoop auto and `.s dpl`.
- `.s help` prints the short in-game summary. `.s settings` shows your toggles and the standing castle's reserve.
- Quote item names with spaces: `.pull "Iron Ore" 20`. If a name matches several items, pick one with `.s 2`.
- Aliases work anywhere an item name does: `.pull BE 100` pulls Blood Essence.

**Scopes**

| Scope | Meaning |
| --- | --- |
| **You** | Your Steam account. Follows you to every castle. |
| **Plot** | The castle under your feet. |
| **Castle** | Stored on that castle's heart owner. Stand on the plot to view or set. |
| **Clan** | Every non-excluded clan plot, treated as one island. |
| **Server** | Whole dedicated server. Admin only (`adminlist.txt` plus in-game `adminauth`). |

---

# Part 1: Features

## 1. Item pickup (scoop)

**What it does.** World drops within your radius go straight into your bags. It never touches death bags, soul shards, chests, or stations, and auto-scoop never takes items a player deliberately dropped. If two players are in range, the closest one gets the pile. Scoop is the one feature with no admin allow.

**Defaults.** Auto ON, filter `all`, radius 10, chat mode `manual`.

**Two filters.** `all` grabs every pile you walk past. `around` grabs only piles that spawned while you were already in range, so you can loot your own kills without vacuuming a clanmate's farm.

**Personal caps.** `.s bagcap` limits how many of an item scoop will collect. `0` means never pick it up, `-1` means unlimited. This is a scoop cap on your bags, not a castle production cap.

**Worked example: farm cotton, but stop at 200 and skip mushrooms**

```
.s radius 15
.s bagcap cotton 200
.s exclude mushrooms
.s auto all
```

Walk the field. When you hit 200 cotton, scoop leaves the rest on the ground. `.s exclude mushrooms` toggles the whole built-in group. Run `.s exclude` to see the skip list.

**Worked example: only my kills, quietly**

```
.s auto around
.s notify off
```

**Worked example: count clan chests toward my cap**

```
.s mode guild
.s bagcap "Iron Ore" 500
```

With Clan Share on, scoop stops iron ore once your bags plus clan chests hold 500.

---

## 2. Clan Share

**What it does.** Turns every clan plot into one logistics island. Stash, pull, craft-pull, find, tidy, conveyors, servant loot, and the starter kit all see every clan castle as one big storage. The vanilla treasury is also merged across clan plots. Items physically move between castles as needed.

**Scope.** Stored per clan. One member turns it on and it applies to everyone. Default OFF.

**Setup**

```
.s cs          (any clan member, standing on any clan plot)
```

**Keeping one castle private.** The heart owner stands on that plot and runs `.s cse`. While excluded, standing there is local-only, and other plots will not dump into it or pull from it. Run `.s cse` again to include it.

**Craft pulling.** Turn on `.s cr` (server allow `.sg cr`). Right-click a recipe that is missing ingredients and the mod pulls them from the island into your bags, honoring reserves.

**Pull commands.** `.pull <item> <n>` grabs items directly into your bags from anywhere on the island. Pull ignores reserves. Turn on `.s dpl` (default ON) so it never takes the last stack out of a chest. Pull is the one feature still flagged as a little buggy.

**Worked example: build at castle B with mats stored at castle A**

```
.s cs
.s cr
```

Stand at castle B, open the workbench, right-click a recipe. Missing planks come over from castle A. If reserves at A would drop below the floor, craft-pull leaves them.

**Worked example: grab 200 planks for a wall run**

```
.pull plank 200
```

Chat tells you which chests they came from unless `.s sp` (silent pull) is on.

**New castle on the island.** With Clan Share on, a fresh plot is already shared. Place the heart, lay foundations, place one chest. In a few seconds it fills with a starter kit (planks, stone bricks, gem dust, copper, iron, stone). Build a small treasury-floor room and place about three large chests so the covering buffer can land. Covering only runs while a clan member is standing on the plot. To dismantle a filled chest, empty it. It stays empty for 5 minutes.

---

## 3. Heart top-off

**What it does.** Keeps the castle heart's Blood Essence slots fed from clan chests so the heart never decays. Also seeds the next heart upgrade costs into treasury chests. Chests keep 500 Blood Essence as their own floor, separate from the heart.

**Scope.** Per plot. Default ON.

```
.s hf          (stand on the plot to toggle it off or on)
```

**Worked example.** You are away for a week. Your clanmate's castle has 3,000 BE in a chest named `blood`. Your heart is topped up from it as long as Clan Share is on and your plot is not excluded.

---

## 4. Improved conveyor belts

**What it does.** Name one chest `plank s1` (sender, line 1) and another `plank r1` (receiver, line 1). Items flow from sender to receiver on the same line number. Stations count as receivers too, so a sender full of planks feeds any workbench that wants planks. Unlike Kindred, belts honor **chest reserves** and **production caps**, and chest-to-chest lines work.

**Switches.** Player `.s co`. Server `.sg co` (default ON). Same-group loops, where a receiver is also a sender on the same line, need `.sg convloop` (default OFF, and it will vacuum a castle if misused).

**Priority.** Stations get fed first, then chest receivers. When several sinks want the same item they split fairly.

**Worked example: auto-feed a sawmill and stop at 500 planks**

1. Name the log chest `Lumber s1`.
2. Put the sawmill on line 1 by naming its output chest `plank r1` (any chest that takes planks works).
3. Run:

```
.s co
.s cap plank 500
.s reserve lumber 100
```

Logs flow to the sawmill until the island holds 500 planks. The lumber chest always keeps 100.

**Worked example: nothing is moving**

```
.s conv plank
```

Tells you the station, the line, and the reason: no sender, reserve reached, cap reached, or station full.

---

## 5. Item groups

**What it does.** Built-in groups such as `ore`, `mushrooms`, `flowers` let you set reserves, caps, scoop caps, and chest names by category instead of one item at a time. Groups are per castle and fully editable.

**Worked example: see what ore the clan has and where**

```
.s group ore
```

Prints each member's island total and a grand total. Add `full` to see reserve and cap per item.

**Worked example: a custom group for belt inputs**

```
.s group create belts
.s group belts add "Iron Ore" "Copper Ore" "Sulphur Ore"
.s reserve belts 50
```

Every item in `belts` now keeps 50 in chests. The first edit of a built-in group copies its default list. `.s group restore ore` puts it back.

---

## 6. Quick deposit (RR)

**What it does.** Dumps your whole inventory except the hotbar into the right chests. Trigger it with `.stash`, or double-tap R, or double-click the sort button once `.s ss` is on.

**Where it goes.** Standing on your plot: this castle. Clan Share on: the whole island. Standing off any castle: only if both `.s rrglobal` (you, default OFF) and `.sg rrg` (server, default ON) are on.

**Worked example: stash from the middle of a mining trip**

```
.s ss
.s rrglobal
```

Now double-tap R at the mine. Your ore goes home. Chat lists the chests unless `.s ssh` (silent stash) is on.

**Tidy.** `.s tidy` restacks what is already in chests onto better destinations using the same ranking, without touching belts, skip chests, or hearts. Run it after renaming chests.

---

## 7. Priority containers

**What it does.** Every deposit picks the best chest in a fixed order. Lower wins:

1. **Conveyor sender** (`s#`) that already holds the item.
2. **Exact name match** on the chest plate.
3. **Group or type match** (`ore`, `blood`, `jewel`, a custom group).
4. **Generic** chest (blank, `General`, `Empty`, `Everything Else`).
5. **Overflow** chest, last resort.

Ties go to a chest that already has the item, then treasury floor, then the plot you are standing on.

**Exclusions.** Put `NS` on a plate, or end it with two apostrophes `''`, and that chest is never a source or a destination.

**Built-in dest words match the group, not a substring.** A chest named `blood` takes Blood Essence, not Blood Jewel. Jewel Storage with a blank plate still counts as a jewel chest.

**Worked example chest layout**

| Chest plate | Gets |
| --- | --- |
| `Iron Ore s1` | Iron ore first, and feeds the furnace on line 1 |
| `ore` | Every other ore |
| `blood` | Blood Essence of all tiers |
| `Personal NS` | Nothing. Never touched. |
| `overflow` | Whatever had no better home |

---

## 8. Servant deposits

**What it does.** When servants return from a hunt, their loot is stashed using the same chest ranking as `.stash`. With Clan Share on, the destination is the island minus excluded plots. Prefers the servant's home plot for overflow.

**Switches.** Player `.s asm` (default OFF). Server `.sg asm` (default ON).

**Worked example**

```
.s asm
```

Send servants out. When they return, `Hunt returned (plot 86 L4) — Pools of Rebirth: Raven — 10 Oil.` appears and the oil is already in your `oil` chest.

---

## 9. Auto rerun hunts

**What it does.** When a hunt comes back, surviving and uninjured servants go straight back out on the same zone and mission. Dead or injured servants are skipped. Nobody has to sit the throne.

**Switches.** Server `.sg rh` (default OFF). Then per castle with `.s rh`. Turning either on captures hunts already in the field.

**Success cap.** `.sg rhmax <1-100>` is the intended max success percent for repeat sends (default 99). Right now the live roll is vanilla until a safe hook exists.

**Worked example: loop hunts on two of three clan castles**

```
.sg rh            (admin, once)
.s rh             (lists castles: 1 ON, 2 OFF, 3 OFF)
.s rh 2           (toggle row 2)
```

Or `.s rh all` for everything, `.s rh off` for the castle you stand on.

**Hunting from another castle's throne.** With Clan Share on, sit a throne and run `.s throne` to list clan plots, then `.s throne 2`. The next map click sends plot 2's servants. `.s hunt 1 3` picks servants 1 and 3 on that plot first.

---

## 10. Find item and find chest

**What it does.** `.fi <item>` lists which plot and chest holds an item and how many. `.fc <name>` lists chests by plate name. Both always show the plot you are standing on, and with Clan Share on they group by plot and heart level with `(here)` marking your location.

**Worked example**

```
.fi "Greater Blood Essence"
.fi GBE                 (same thing, alias)
.fc salvage
```

---

## 11. `.s need`

**What it does.** Lists the top 10 inputs your receiving stations are waiting for, with demand, stock, and reserve. Higher-tier stations first, then the item with the least stock after reserve. Use it to decide what to hunt or mine next.

```
.s need
```

Example output line: `Iron Ore: demand 120, total 40, reserve 10`.

---

## 12. Item aliases

**What it does.** Short names that work anywhere an item name does: find, pull, chest plates, reserve, cap, bagcap.

| Alias | Item |
| --- | --- |
| `BE` | Blood Essence |
| `GBE` | Greater Blood Essence |
| `PBE` | Primal Blood Essence |
| `ABE` | Ancestral Blood Essence |
| `GSS` | Greater Stygian Shard |
| `SGS` | Siege Golem Stone |
| `DSI` | Dark Silver Ingot |
| `OT` | Onyx Tear |

**Admin aliases.** Persist across restarts. Cannot reuse a built-in or a dest word like `blood`.

```
.sg alias add ing "Iron Ingot"
.pull ing 50
.sg alias del ing
```

---

## 13. Detailed hunt results

**What it does.** Every send and return is reported in chat.

- On **send**, the sender (or heart owner) sees who went, where, survival percent, and whether repeat is on. Example: `Hunt sent (plot 45 L2) — Haunted Iron Mine (99%): Stephen. Repeat ON (max 99%).`
- On **return**, connected clan members and the heart owner see the destination, who came back, loot stacks, and anyone who died or was injured. Example: `Hunt returned (plot 86 L4) — Pools of Rebirth: Raven — 10 Oil. Repeat ON — sending again.`
- If a repeat send fails, chat says `could not send` with the real reason instead of pretending they left.
- On **login** you get a servant roll-call per castle (home / hunt dest and time left / injured / dead) and the **haul** stashed since you last logged out, or the last 72 hours if you were gone longer. Example: `Servants (plot 86 L4): Corey hunt Ancient Village 1h 38m, Raven home` then `Haul since logout (14h): 1,200 Ghost Crystal, 80 Oil`.

---

## 14. Server admin toggles

**What they are.** Every castle feature has a server-wide allow under `.sg`. If the allow is off, the matching player toggle does nothing. Admins need their SteamID64 in `save-data\Settings\adminlist.txt` and must run `adminauth` in the console. `.sg s` shows the current state of every allow.

**Defaults.** Everything ON except `.sg rh` (repeat hunts) and `.sg convloop` (belt loops).

| Allow | Gates | Pairs with | Default | Why you would turn it off |
| --- | --- | --- | --- | --- |
| `.sg ss` | Double-tap R / sort-button stash | `.s ss` | ON | Force players to use `.stash` only |
| `.sg p` | `.pull` | none (player command) | ON | Stop players pulling past reserves |
| `.sg cr` | Craft-pull on recipe right-click | `.s cr` | ON | Stop remote crafting from clan stock |
| `.sg rrg` | Stash / RR while off any plot | `.s rrglobal` | ON | Make players walk home to deposit |
| `.sg asm` | Servant loot auto-stash | `.s asm` | ON | Keep servant loot in the coffin |
| `.sg rh` | Repeat hunts | `.s rh` | OFF | Default off; hunts loop forever when on |
| `.sg rhmax n` | Max success % for repeat sends | none | 99 | Lower to add risk to looped hunts |
| `.sg co` | Conveyor belts | `.s co` | ON | Debugging item movement |
| `.sg convloop` | Same-line chest loops | none | OFF | Loops can vacuum a castle |
| `.sg sal` | `salvage` chests feed the Devourer | `.s sal` | ON | Prevent accidental salvage |
| `.sg us` | `spawner` chests feed unit spawners | `.s us` | ON | |
| `.sg bz` | `brazier` chests feed braziers | `.s bz` | ON | |
| `.sg nam` | `night` / `prox` smart braziers | none | ON | Performance on very large castles |
| `.sg trash` | `trash` chests can be emptied | `.emptytrash` | ON | |
| `.sg alias` | Item alias list / add / del | none | | |

**Worked example: a fresh server**

```
adminauth            (console)
.sg s                (see everything)
.sg rh               (turn repeat hunts on)
.sg rhmax 90
.sg alias add ing "Iron Ingot"
```

**Worked example: lock down a PvP server**

```
.sg p
.sg cr
.sg rrg
```

Players can still stash and use belts on their own plot, but nothing leaves chests except by hand.

---

# Part 2: Command reference

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

## Quickest help

Point ChatGPT, Claude, or any LLM at https://github.com/Greeneye0/Satisvampory and ask. The `specs/` folder and this guide describe every behavior.
