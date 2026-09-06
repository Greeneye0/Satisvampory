# 01 — Dest ranking

**This is the shared dest contract.** `.stash`, RR (double-tap R / `.s ss`), `.s tidy`, servant mission auto-stash, covering park, and heart-upgrade chest dests all use `StashRouting.RankDeposit` (or tidy’s wrapper around it). Do not invent a second order. Conveyor hopper leftovers return to **source** `s#` / overflow, not dest ranking.

Ranking decides **WHERE** an already-decided amount goes. It does not decide **HOW MUCH**. Amounts come from reserve, covering targets, pull quantity, etc.

## Nameplate vs dest name

- **Nameplate** (`RawName`): what the player typed on the chest.
- **Dest name** (`DestName`): nameplate if set, else the **prefab / EntityName**. Never `entity.ToString()`.
- Blank plate on dest-quality furniture (**Jewel Storage**, **Woodworking Storage**, …) still **category-matches** that dest via `RankMatchName`.
- Blank **Small Chest / cabinet / bureau / Small Storage** stay **generic** so covering can park there.
- Player-typed generic words (`Empty`, `General`, `Everything Else`, …) are generic even if the prefab is Jewel Storage.

`s#` / `r#` live on the **nameplate only**. Never treat a vanilla prefab name as a conveyor.

Special identity (`salvage`, `spoils`, `brazier`, `spawner`, `trash`) is the **nameplate**. A blank plate does not inherit a prefab "salvage" token.

## Skip (never source, never dest)

| Token | Rule |
| --- | --- |
| `NS` as a word (`\bns\b`) | No-share. Class **99**. |
| `''` (two apostrophes) at the **start or end** of the plate, space between them allowed (`Lock Box''`, `' ' Lock Box`) | Skip-quotes. Treated like NS **everywhere**. Empty name is not skip. (1.0.107: leading and spaced forms; before, only a trailing `''` counted and `' ' Lock Box` was tidied.) |

## Generic nameplates

Blank, or (case-insensitive, trimmed): `Chest`, `Container`, `Empty`, `General`, `Misc`, `Miscellaneous`, `Everything`, `Everything Else`, `All`, `Other`, `Others`, `Extra`, `Dump`, `Stuff`.

Furniture-only names (`Small Chest`, size + `chest`/`storage` with no dest word) are also unnamed/generic.

## Conveyor tokens

- Sender: nameplate matches `s(\d+)` (case-insensitive).
- Receiver: nameplate matches `r(\d+)`.
- A chest can be both (`s1 r1`).

## Overflow / special

- **Overflow**: name contains `overflow`. Dest class **5** last-resort. Never seeds as class 0.
- **Special**: nameplate contains `salvage`, `spoils`, `brazier`, `spawner`, or `trash`. Dest class **90** (not a normal dump dest). Overflow names are not special for ranking.

## Priority `+` (1.0.108)

One or more `+` at the **end** of the nameplate is a priority boost, not part of the name (`Stone Brick R1S1++` is the name `Stone Brick R1S1` with priority 2). A `+` **inside** the name is still the AND separator (`Wood + Stone`).

- A boosted chest outranks **everything**, `s#` included, for any item it **matches or already holds** (base class 0-3). More `+` = higher: class becomes `-N`.
- Among equal boosts the base class and specificity still decide.
- `+` on an empty generic, unmatched custom, overflow, special, or NS chest does nothing.
- Applies wherever `RankDeposit` is used: stash, RR, tidy, servant auto-stash, covering park, belt mutual links, `.pull` order (a `+` chest is drained last).

## Twins: split across same-name `+` chests (1.0.112)

Two or more chests anywhere on the island (ClanShare on) with the **same clean name** and the **same number of `+`** (at least one) that tie on rank are **twins**.

- `.stash` / RR: a stack is **split** across the twins so their counts of that item end as even as possible (lift the emptiest first, then share the rest).
- Tidy, covering park, push-back, servant stash: every deposit goes to the twin holding the **least** of the item, so they balance over successive moves.
- Twins that are also on the same belt line tie on rank and stay put; the balance comes from deposits, not from belts shuffling between them.
- Different `+` counts are not twins: the higher one simply wins.

## Restricted furniture (1.0.119)

Vanilla restricted storage (Consumables, Jewel / Coin / Fish storage, soul-shard rules) rejects other categories at `TryAddItem`. A chest whose furniture cannot hold the item is class **97 `restricted`**: never a dest whatever the plate says, so a plate name can no longer make the stash pick a container that then silently refuses. `.s why` reports the restriction and what the name alone would have ranked.

## Exclusions `--word` (1.0.110)

A token starting with `--` on the nameplate is an **exclusion**, not part of the name. `Weapons --copper --iron+` is the name `Weapons`, priority 1, and never takes anything `copper` or `iron` matches.

- An exclusion word matches **broadly**: admin / essence alias (`--gd`), exact item name, built-in or custom group word (`--alchemy`), ItemCategory word, or a 3+ letter name fragment. The fragment rule applies to **equipment too** here (`--copper` catches Copper Sword), unlike dest matching.
- A matching exclusion makes the chest class **98 `excluded`** for that item: never a dest for stash, RR, tidy, servant stash, covering, push-back, or belt links. Sources are unaffected.
- Exclusions are stripped before every other rule, so `s#`/`r#`, `+`, group and exact matching see the clean name. Plates are 20 characters: use aliases (`.sg alias add`) to keep exclusions short.

## Deposit classes (`RankDeposit`)

Lower class wins. Then higher **specificity**, then **seeded** (already has the item), then **treasury-floor**, then **local** (standing plot).

**Category specificity is tiered** (1.0.96): the best matching token decides the tier, then matched token count, then matched length.

| Tier | Token kind | Example |
| --- | --- | --- |
| 4 | **Custom group** name (`.s group create …`) | `AlchTest` for its members |
| 3 | **Built-in group word** (`alchemy`, `bone`, `blood`, …), essence alias | `Alchemy` for Grave Dust (alchemy group member) |
| 2 | **ItemCategory word** (vanilla category flag) | `Consumable` |
| 1 | **Partial item name** (3+ chars, not all digits) | `Grave` for Grave Dust |

Custom group beats built-in group beats ItemCategory word beats partial name, regardless of token count. Exact item name is still its own class above all of these. Group names match **exactly** after normalization (plural / spelling fold only), never as a substring. A token that is a custom group's name and whose item is not a member does **not** fall back to a built-in or partial match on that word (1.0.102). To make a chest win by group, put the item in that group (`.s group bone add "Grave Dust"`); to win outright, name the chest the exact item (`Grave Dust R1S1`).

| Class | Label | Meaning |
| --- | --- | --- |
| **0** | `s#` | Sender: nameplate is `s#` **and** (the chest **already holds this item**, **or** exact / category match on the remaining name). Seed **or** name, either is enough (1.0.99): `Alch S5` holding Grave Dust keeps taking Grave Dust; an empty `Blood Essence S5R5` beats a `Blood` chest. Overflow never class 0. |
| **1** | name-match | Exact item-name match on the remaining name (after stripping `s#`/`r#`/overflow/generic filler). |
| **2** | category | Dest-group / ItemCategory match (built-in dest words, custom groups, `+` AND / space OR). |
| **3** | seeded | Any chest that **already holds this item** but did not exact/category match: generic plate or custom name. Seeded beats empty (1.0.99). Generic seeded slightly preferred over custom seeded, then treasury-floor. |
| **4** | generic | Unnamed / generic plate, **empty** of this item. Treasury-floor generic slightly preferred (`Spec = 1`). |
| **5** | overflow | Overflow last-resort. |
| **6** | empty-custom | Named custom plate that does **not** have the item and did not match. **Not a usable dump dest** (`IsDepositUsable` is class ≤ 5). |
| **90** | special | salvage/spoils/brazier/spawner/trash. |
| **99** | skip-NS / skip-quotes | Never. |

Usable dump dest: **class ≤ 5**.

## Name match

After stripping conveyor tokens and overflow/generic filler:

- `+` separates **AND** clauses. Spaces inside a clause are **OR**.
- No `+`: type-word AND fallback when a token is weapon/armor/material (materials map to Mineral).
- Built-in dest words (`blood`, `stone`, `bone`, `jewel`, …) match **dest-group membership only** — not ItemCategory flags and not a substring of the item name.
  - **Blood Jewel** → jewels, not blood.
  - **Miststone** → not stone.
- `blood` as a dest word is **Blood Essence**, not Greater/Primal/Ancestral. Ranks above Alchemy category.
- An unrecognised word on a plate (`Alch`) matches nothing. Before 1.0.99 such a chest was class 4 even when seeded and an empty generic chest won; now a seeded chest is class 3 (class 0 if `s#`) and beats empty generic. Check with `.s why <item> <container>`.
- Exact item aliases match **that item only** (find / pull / dest names): `be` (Blood Essence), `gbe` / `pbe` / `abe` (Greater / Primal / Ancestral), `gss` (Greater Stygian Shard), `sgs` (Siege Golem Stone), `dsi` (Dark Silver Ingot), `ot` (Onyx Tear). Admin can add more with `.sg alias add <alias> <item>`. Cannot overwrite dest-group words (`blood`, `stone`, …) or built-ins. `blood` as a dest word stays the Blood Essence dest **group**; `BE` is the exact item.
- Spelling fold: fiber/fibre, sulfur/sulphur, armor/armour, gray/grey, jewelry/jewellery, etc.
- **Partial item-name tokens must be 3+ characters and not all digits** (1.0.101). `Alch 1 R1S1` must not match `Ancestral Whip Shards Tier 1 Shattered` through the `1`. Numbers on a plate are labels, not match words.
- **Shattered shards are the `shattered` group, not `weapons`** (1.0.107). A `Shattered` plate takes shards by group word; a `Weapons` plate never does. A plate `Shattered Weapons` is OR and takes both — use `Shattered` alone.
- **Equipment never matches by a name fragment** (1.0.104). Items whose ItemCategory has Weapon, Armor, or Magic skip the partial rule entirely: `Copper Iron` takes Copper Ingot (partial) but never Copper Sword or a legendary whose prefab label is `Merciless Iron Crossbow`. Equipment matches only by exact name, group word (`Weapons`, `Armor`), ItemCategory word, or a custom group. Legendaries carry their base prefab label, not the display name.

## Source pass (lend sources)

Used when **taking** from chests for covering, kit, and heart fuel, not when ranking dests. `.pull` and craft-pull no longer use passes: they drain in the **inverse of dest ranking** for the item (`OrderPullSources`, see [04-stash-pull-find.md](04-stash-pull-find.md)) and never touch castle hearts.

| Pass | Chests |
| --- | --- |
| **-1** | NS / skip-quotes — never |
| **0** | unnamed, generic, overflow |
| **1** | named (including named treasury-floor dests) |
| **2** | `s#` / `r#` last-resort |

Named treasury dests are **not** pass 0.

## Same-plot self-sort (`RankSort`)

Covering/lend also self-sorts surplus **on the dest plot** using a stricter rank:

- Dest: exact (0) or category (1) only.
- Never dest: `s#`/`r#`, special, overflow.
- Never drain `s#`/`r#`.
- Generic/custom are sources only (class 3).
- Never self-sort **into** overflow.

Tidy does **not** use `RankSort`. Tidy uses `RankDeposit` with extra source/dest gates — see [05-tidy.md](05-tidy.md).

## Blood Essence dests

Vanilla **treasury chests reject regular Blood Essence**. Covering / upgrade park for BE MUST use Blood / Alchemy / generic dests (blank or named), including **named treasury-floor** chests that match. Do not skip those dests or HUD/stock stays 0.

Greater Blood Essence is accepted by vanilla treasury.

## What MUST NOT change without a spec amendment

- Matching seeded `s#` is class **0** (ahead of exact name).
- Named `s#` that does not match the item is **not** a dest for that item.
- Overflow never outranks exact / category / named custom.
- Blank Jewel Storage is a jewel dest; blank Small Chest is generic.
- `NS` and trailing `''` are never source or dest.
- Dest words match groups, not item-name substrings.
