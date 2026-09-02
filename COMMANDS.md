# Satisvampory command guide

Chat commands. Prefix **`.s`**. Admin server toggles are **`.sg`**. `.l help` redirects here.

There is no `.leftover` command. Chest floors are **`.s reserve`**.

Product contract (dest ranking, tidy, covering, ClanShare, and the rest): **[specs/](specs/README.md)**. This file is the player-facing command table; the specs are the rail for updates.

Most logistics features need the **server allow (`.sg`) on** and your **player toggle (`.s`) on**. Scoop does not use `.sg`.

## Who it applies to

| Scope | Meaning |
| --- | --- |
| **You** | This Steam account only. Follows you to every castle. |
| **Plot** | The castle **under your feet** (not your home heart if you own more than one). |
| **Castle** | Stored on that castle’s **heart owner**. Stand on the plot to view or set. With ClanShare on, viewing can work off-plot. |
| **Clan** | Every non-excluded clan plot, as one island. New plots join automatically. |
| **Server** | Whole dedicated server. **Admin only** (`adminlist.txt` + in-game `adminauth`). |

---

## Scoop — **you**

World piles into **your bags**. Never death bags, soul shards, chests, or stations.

| Command | What |
| --- | --- |
| `.s` / `.scoop` / `.sc` | Scoop now in your radius |
| `.s auto` | Toggle auto-scoop (default **ON**, filter **all**) |
| `.s auto around` / `.s auto all` | Auto ON and set filter |
| `.s filter` / `.s filter around\|all` | Show or set filter without toggling auto |
| `.s radius` / `.s radius 15` | Show or set radius (1–50, default **10**) |
| `.s exclude` | List skip list |
| `.s exclude cotton` | Toggle exclude for an item or group |
| `.s bagcap` | List your scoop caps (not castle production caps) |
| `.s bagcap cotton` / `.s bagcap cotton 200` | Show or set. `0` = scoop none. `-1` = unlimited |
| `.s bagcapclear` | Clear all your scoop caps |
| `.s mode bags` / `.s mode guild` | Count bags only, or bags + ClanShare stashes. Still **your** cap |
| `.s notify` / `.s notify off\|manual\|on` | Pickup chat. Default **manual** (`.s` only; auto silent) |
| `.s last` | Reprint last scoop line |

`around` = only piles that spawned while you were in radius. Auto never takes player inventory-drops. Closest eligible player wins a contested pile.

---

## Stash, pull, find — **you**, dest is **plot** or **clan**

If ClanShare is **ON**, dest/source is the **clan island**. If **OFF**, standing plot only. Off-plot stash also needs **`.s rrglobal`** (and **`.sg rrg`** allow).

| Command | Scope | What |
| --- | --- | --- |
| `.stash` | You; dest plot or clan | Dump inventory except hotbar into named/matching chests |
| `.s tidy` | Plot, or clan island if `.s cs` on | Restack existing chests onto better dests (same rank as `.stash` / RR: matching `s#` first). Never drains `s#`/`r#`, `NS`, skip-quotes (`''`), salvage/trash, or castle hearts. Treasury-floor chests are sources. Overflow and spoils are sources only. Does not honor reserve. |
| `.s ss` | You | Allow double-click sort / double-tap R to stash |
| `.pull plank 50` | You; source plot or clan | Take items into your bags. **Does not honor reserve** |
| `.s cr` | You | Right-click recipe craft-pull. **Honors reserve** |
| `.s dpl` | You | Don’t pull the last stack (default **ON**) |
| `.fi "Blood Essence"` / `.s fi …` | You; search plot or clan | Find item. Always shows the plot you are standing on. ClanShare ON: groups by plot + heart level and marks current as `(here)` |
| `.fc sal` | You | Find chests by name. Same plot / `(here)` labels as `.fi` when ClanShare is ON |
| `.s sp` | You | Silent pull (no “from where” chat) |
| `.s ssh` | You | Silent stash |
| `.s rrglobal` | You | Allow **off-plot** stash/RR. Default **OFF**. Does not shrink on-plot ClanShare dest |
| `.s conv plank` | Plot / clan island | Why a conveyor is not moving that product |
| `.s need` | Plot / clan island | Top 10 station inputs the conveyor wants. Shows demand, total, reserve. Higher tier first, then lowest stock after reserve |

Chest **names**: `s#` sender, `r#` receiver, `overflow`, `salvage`, `spoils`, `trash`, `NS` or trailing `''` skip, braziers `night` / `prox`. Built-in dest words (`blood`, `stone`, `jewel`, …) match that dest group, not a substring of the item name (Blood Jewel → jewel, not blood). Blank-plate dest furniture (Jewel Storage) still counts as that dest; blank Small Chest / cabinet / bureau do not.

---

## Castle floors — **castle** (heart owner)

Stand on the plot. Default reserve **10**. Default production cap **unlimited**.

| Command | What |
| --- | --- |
| `.s reserve` | Show default reserve |
| `.s reserve 10` | Set default for every item. `0` disables reserve |
| `.s reserve plank` | Show one item or group |
| `.s reserve plank 50` | Leave 50 of that item in chests |
| `.s reserve plank -1` or `.s rsvc plank` | Clear override (back to default) |
| `.s cap` | List production caps (conveyors stop at this many **on the island**) |
| `.s cap plank` / `.s cap plank 200` | Show or set. `0` = make none. `-1` = unlimited |
| `.s capclear plank` | Clear one cap |
| `.s group` | List built-in and custom groups on this castle |
| `.s group ore` | List members |
| `.s group create belts` / `.s group delete belts` | Custom group |
| `.s group restore` / `.s group restore mushrooms` | Restore built-ins (Hell’s Clarion is mushrooms, not flowers) |
| `.s group ore add "Iron Ore"` | First edit of a built-in copies the default list |

Quote names with spaces. If a name is ambiguous, pick with **`.s 2`** or **`.s pick 2`**.

---

## Clan and plot — **clan** / **plot**

| Command | Scope | What |
| --- | --- | --- |
| `.s cs` / `.s gs` | **Clan** | ClanShare on/off for **all members, all plots**. Default **OFF** (this clan may already be ON). Items move between clan castles |
| `.s throne` then `.s 2` | **You** | ClanShare ON: pick which clan plot to hunt from **this** throne. Stay seated. `.s throne here` is this castle again |
| `.s hunt` then `.s hunt 1 2` | **You** | Pick up to 3 servants on the managed plot, then **click a discovered zone on this vanilla hunt map** (fog and undiscovered stay vanilla). Server sends that plot’s servants |
| `.s rh` | **Clan** / **plot** | List each castle’s repeat-hunt status. `.s rh all` ON for all listed. `.s rh off` this castle. `.s rh 2` toggle that row. Needs **`.sg rh`** |
| `.s cse` | **Plot**, heart **owner only** | Exclude or include **this** plot from ClanShare. Standing on an excluded plot is local-only |
| `.s sal` | **Plot** (clanmate of heart owner) | Feed chests named `salvage` into the devourer. Default **OFF** per plot. Needs **`.sg sal`** allow |
| `.s hf` | **Plot** | Heart Blood Essence auto-feed. Default **ON** until you turn it off |
| `.s asm` | You | Servants auto-stash mission loot with the same dest rules as `.stash` / RR (named dest ranking, then overflow). ClanShare ON: clan island except `.s cse`. Player default **OFF**; `.sg asm` must also be on |
| `.s co` | You | Conveyors (`s#` / `r#`). Chest `s#` fills seeded `r#` chests (honor reserve). Same-group `s#r#` cycles need **`.sg convloop`** |
| `.s us` | You | Chests named `spawner` |
| `.s bz` | You | Chests named `brazier` |
| `.s settings` | You + standing castle | Show your toggles and this castle’s reserve |

---

## Server — **server**, admin only

Needs `save-data\Settings\adminlist.txt` SteamID64 **and** console `adminauth`.

`.sg` defaults **ON** (Satisvampory 1.0.1+). Player toggles still start **OFF** except `.s dpl` and scoop auto.

| Command | What |
| --- | --- |
| `.sg s` | Show global allows |
| `.sg ss` | Allow sort-stash |
| `.sg p` | Allow `.pull` |
| `.sg cr` | Allow craft-pull |
| `.sg asm` | Allow servant auto-stash |
| `.sg rh` / `.sg repeathunt` | Repeat hunts: servants go back out on the same hunt when they return. Default **OFF**. Chat the owner loot or death. Turning ON captures hunts already out |
| `.sg rhmax [n]` | Intended max success **%** when repeat is ON (default **99**). The live roll is **vanilla** until a safe cap hook exists; do not patch `GetMissionSuccessChanceForServant_*` |
| `.sg co` | Allow conveyors |
| `.sg convloop` / `.sg cloop` | Allow s# chest → r# chest **loops** (dest is also s# on the same group). Default **OFF**. Chest→chest without a cycle works even when this is off |
| `.sg sal` | Allow devourer salvage |
| `.sg us` | Allow `spawner` chests |
| `.sg bz` | Allow `brazier` chests |
| `.sg nam` | Allow night/prox braziers |
| `.sg trash` | Allow emptying `trash` chests |
| `.sg rrg` | Allow off-plot RR/stash (player `.s rrglobal` still defaults OFF) |
| `.emptytrash` | Empty `trash` chests on the standing plot |
| `.adminstash plank 100` | Spawn into standing-plot stash |
| `.s vq` | Work-queue depth (admin) |
| `.s catalog` | Dump item CSV to `BepInEx\Log\item-catalog.csv` |

---

## Examples

**1. Vacuum cotton, but stop at 200 in your bags**

```
.s bagcap cotton 200
.s auto all
.s
```

You: scoop caps and auto. Not a castle cap. `.s cap cotton 200` would stop **conveyors** when the **castle/clan** already has 200.

**2. Keep 50 plank in chests; still pull 200 into your bags for a build**

```
.s reserve plank 50
.pull plank 200
```

Reserve is **castle**. `.pull` ignores it. Craft-pull and conveyors leave the 50.

**3. Clan dumps as one island; this plot stays local**

Stand on any clan plot:

```
.s cs
```

Walk to the plot you own and do **not** want shared:

```
.s cse
```

`.stash` from another clan castle will not use the excluded plot. Standing **on** the excluded plot is local-only.

**4. Named conveyor, salvage on this plot**

Name chests `plank s1` and `plank r1`. Then:

```
.s co
.s sal
```

`.s co` is **you**. `.s sal` is **this plot**. Server must have `.sg co` and `.sg sal` on (they are, by default after 1.0.1).
