# 05 — Chest tidy (`.s tidy`)

Player-triggered restack: move items **between existing chests** onto **better dests**. Same dest rank as `.stash` / RR (`RankDeposit`). Does **not** honor reserve.

ClanShare ON: whole clan island. OFF: standing plot only.

Needs an allied heart (same gates as stash, including raided skip). One tidy at a time.

## Why this spec exists

Tidy went off the rails when it skipped **all** `s#` chests as dests (Ghost Crystal never left a generic “stuff” chest for `Crystal Stone S1`) and when it treated treasury floor as forbidden. Those are frozen below.

## Collect chests

Include plot chests with an external inventory, except:

- refinement stations
- **castle hearts** (`stash == heart` or `Has<CastleHeart>`)
- `NS` / skip-quotes (`''`)
- name contains `salvage`, `trash`, `brazier`, or `spawner`

Need at least **two** eligible chests.

## Rank (`TidyRank`)

Start from `RankDeposit` ([01-dest-ranking.md](01-dest-ranking.md)). Then:

| Chest | Source | Dest |
| --- | --- | --- |
| Heart / skip plate | no | no |
| Overflow or spoils | **yes** | **no** |
| `s#` / `r#` | **no** | **yes** if dest class **≤ 2** (matching seeded s# / exact / category) |
| Other (including **treasury floor**) | **yes** | **yes** if dest class **≤ 4** |

So:

- Matching `Crystal Stone S1` is a dest (class 0/1/2). Tidy MUST move Ghost Crystal out of a generic chest into it.
- `s#` / `r#` are **never sources**. Tidy never drains a belt.
- Treasury-floor chests **are sources**.
- Overflow/spoils empty toward better dests; nothing tidies **into** them.
- Empty custom (class 6) is not a dest.
- Does not honor reserve. Empty-hold chests (kit unbuild window) are not dests.

Move only when the dest is **strictly better** (`Class` lower, or same class with higher `Spec`). Same-class same-spec does not shuffle.

## Chat

`Tidied {n} items ({k} kinds) across {c} chests on the {plot|clan island}.` or `No better dests. Looked at {c} chests…`

## Frozen

- Dest rank = `.stash` / RR. Matching `s#` first.
- Never drain `s#`/`r#`, `NS`, `''`, salvage/trash/spoils/spawner/brazier, **hearts**.
- Treasury floor **is** a source.
- Overflow/spoils source only.
