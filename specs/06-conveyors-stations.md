# 06 — Conveyors, salvage, spawner, brazier, trash

Per-plot work runs from the FIFO **work queue**. ClanShare ON: one island drain per generation (sibling plots marked consumed so the island is not processed N times).

Player `.s co` + server `.sg co` to run conveyors.

## Belts (`s#` / `r#`)

Nameplate tokens: `s(\d+)` sender, `r(\d+)` receiver. Group number is the digit.

- **Stations first.** Chest senders fill receiving stations (`r#`) that want the item, then chest receivers.
- Honor **reserve** and **production cap** (`.s cap`) counted on the island.
- Overflow chests ignore groups; senders stay on their `s#`/`r#` group.
- Fair-share split when several sinks want the same item.
- Never drain `s#`/`r#` as generic covering/tidy sources (see dest ranking + tidy).

### Chest → chest and convloop

Chest senders fill **stations first**. Chest → chest is a **second pass**.

- Chest → `r#` that is **not** also `s#` on the same group: allowed even when convloop is off.
- Chest → `r#` that **is** also `s#` on the same group (a loop): requires **`.sg convloop` / `.sg cloop`**, default **OFF**.

Do not turn convloop on by default. Loops will vacuum a castle.

`.s conv <item>`: why that product is not moving (station, line, cap, reserve).
`.s need`: top 10 station inputs. Higher tier first, then lowest stock after reserve. Shows demand, total, reserve.

Station feed multiplier **5**. Clan island item counts are snapshotted per drain generation.

## Salvage (`.s sal`)

Feed chests named **`salvage`** into the Devourer on **this plot**.

- Plot toggle (clanmate of the heart owner). Default **OFF**.
- Needs `.sg sal` allow (default ON).
- Not a tidy dest/source.

## Unit spawners (`.s us` / `.s sp` player; `.sg us` server)

Chests named **`spawner`** fill unit stations. Spawner feed multiplier **2**.

## Braziers (`.s bz`; `.sg bz`)

Chests named **`brazier`** fill braziers. Keep at least **10** in the chest (`BrazierMin`).

`.sg nam`: allow **night** / **prox** named braziers. Prox range **20**. Tick **2.5s**.

## Trash (`.sg trash`)

Chests named **`trash`**. `.emptytrash` wipes them on the standing plot (admin). Player empty uses the trash service when allowed.

Not a tidy dest. Tidy may treat overflow/spoils as sources; trash plates are skipped entirely.

## Special chests vs dest ranking

`salvage` / `spoils` / `brazier` / `spawner` / `trash` are dest class **90**. They are not normal `.stash` dumps. Conveyor names are never inferred from prefab names.
