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
| Trailing `''` (two apostrophes) | Skip-quotes. Treated like NS **everywhere**. Empty name is not skip. |

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

## Deposit classes (`RankDeposit`)

Lower class wins. Then higher **specificity**, then **seeded** (already has the item), then **treasury-floor**, then **local** (standing plot).

| Class | Label | Meaning |
| --- | --- | --- |
| **0** | `s#` | Seeded matching sender: nameplate is `s#` **and** the chest already has this item **and** (unnamed/generic **or** exact **or** category match). Overflow never class 0. Named `s#` MUST NOT take unmatched items. |
| **1** | name-match | Exact item-name match on the remaining name (after stripping `s#`/`r#`/overflow/generic filler). |
| **2** | category | Dest-group / ItemCategory match (built-in dest words, custom groups, `+` AND / space OR). |
| **3** | generic | Unnamed / generic plate. Treasury-floor generic slightly preferred (`Spec = 1`). |
| **4** | custom-last | Named custom plate that already has this item but did not exact/category match. |
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
- Exact item aliases match **that item only** (find / pull / dest names): `be` (Blood Essence), `gbe` / `pbe` / `abe` (Greater / Primal / Ancestral), `gss` (Greater Stygian Shard), `sgs` (Siege Golem Stone), `dsi` (Dark Silver Ingot), `ot` (Onyx Tear). Admin can add more with `.sg alias add <alias> <item>`. Cannot overwrite dest-group words (`blood`, `stone`, …) or built-ins. `blood` as a dest word stays the Blood Essence dest **group**; `BE` is the exact item.
- Spelling fold: fiber/fibre, sulfur/sulphur, armor/armour, gray/grey, jewelry/jewellery, etc.

## Source pass (lend / `.pull` sources)

Used when **taking** from chests (covering, kit, heart fuel, `.pull`), not when ranking dests.

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
