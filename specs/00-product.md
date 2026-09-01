# 00 — Product

Satisvampory is a **dedicated-server-only** V Rising 1.1 BepInEx plugin. Castle logistics (named dest, ClanShare, conveyors, reserve/caps, treasury lend) plus ground scoop.

It MUST refuse to load on a client (`VRising` product). Chat prefix **`.s`**. Admin server toggles **`.sg`**. `.l help` redirects to `.s help`.

Satisvampory MUST be the only logistics/scoop plugin in `BepInEx/plugins`.

License: AGPL-3.0.

## Player model

| Scope | Meaning |
| --- | --- |
| **You** | This Steam account. Follows the player to every castle. |
| **Plot** | Castle **under the player's feet**, not "home heart" if they own more than one. |
| **Castle** | Stored on that castle’s **heart owner**. Stand on the plot to view or set. |
| **Clan** | Every non-excluded clan plot as **one island**. New plots join automatically when ClanShare is on. |
| **Server** | Whole dedicated server. Admin only (`adminlist.txt` + in-game `adminauth`). |

Most logistics features need the **server allow (`.sg`) on** and the **player toggle (`.s`) on**. Scoop does not use `.sg`.

## Defaults (1.0.49)

Player toggles start **OFF** except:

- `.s dpl` (don't pull last) **ON**
- Scoop auto **ON**, filter **all**, radius **10**, notify **manual**, mode **bags**
- Heart Blood Essence auto-feed **ON** per plot until turned off
- Default reserve **10**
- Production cap **unlimited**

Server `.sg` allows start **ON** (applied once via `AppliedSgAllOn`), except:

- `.sg convloop` **OFF** (chest→chest loops)
- Player `.s rrglobal` still **OFF** even though `.sg rrg` allow is ON

ClanShare (`.s cs`) default **OFF** for a clan (a live clan may already have it ON).

Plot salvage (`.s sal`) default **OFF**.

## What it does

- Dump / restack / pull items using **named dest ranking** ([01-dest-ranking.md](01-dest-ranking.md)).
- Optional **ClanShare island**: stash, RR, tidy, find, conveyors, covering, servant loot across clan plots ([02-clan-share.md](02-clan-share.md)).
- **Treasury lend**: starter kit, covering buffer, next-level heart-upgrade costs, heart fuel ([03-covering-lend.md](03-covering-lend.md)).
- Named **`s#` / `r#` conveyors**, salvage / spawner / brazier / trash chests ([06-conveyors-stations.md](06-conveyors-stations.md)).
- **Ground scoop** into bags ([07-scoop.md](07-scoop.md)).
- Servant mission auto-stash and ClanShare **throne hunt picker** ([08-servants-throne.md](08-servants-throne.md)).
- Per-castle **reserve**, **production cap**, and **item groups** ([09-settings.md](09-settings.md)).

## Non-goals

- Client DLL. Do not load or hot-swap on the game client.
- A leftover / "keep N in *this chest*" command. Floors are castle **reserve**.
- Dual-loading another logistics plugin.
- Moving items while the castle is raided (`CastleHeart.ActiveEvent >= Attacked`).
- Pulling from `NS` or skip-quotes (`''`) chests, or depositing into them.
- Covering or clan pulls triggered by **castle guests**. Only clan members occupy a plot.
- Draining castle hearts for tidy / stash / covering dest.
- Chat HUD spam for covering (chat push is off).
- Giving / spawning items through the debug mailbox (read/inspect only, except documented `apply` covering sim and `gotothrone`, which moves a player).

## Player action gates

`.stash`, `.pull`, tidy, empty-trash (and similar) refuse when:

- downed, dead, or in PvP combat
- bat form (except `.stash` / RR, which allow bat form)
- not allied with the standing heart
- standing off-plot with ClanShare off (or no clan plots when ClanShare on)
- the heart is raided

## Work

Castle conveyor / salvage / spawner / brazier work is a **FIFO plot queue** (`WorkQueueService`). Covering/lend ticks every **5 seconds** independently. Do not replace the FIFO with a stack.

## Version

Plugin version lives in `Satisvampory.csproj` (`Version` / `AssemblyVersion` / `FileVersion`). Shipping a version bump MUST commit and `git push origin main` in the same turn. Bouncing the dedicated server is not shipping.
