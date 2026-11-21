# Cleaners vs Trashers: game design (v2)

Status: current design, set on Sat 21 Nov 2026. Where it conflicts with `docs/architecture/04-game-rules.md`,
`docs/CONTEXT.md` or the 2026-11-13 system design, **this file wins**. Every number below is a starting value
in `content/tuning.json`; the team tunes them while playtesting. Who builds what: `docs/team/README.md`.

## 1. The game in six lines

- Online social deduction on a recycling theme, 6 to 10 players, bots fill empty seats.
- **Cleaners** (majority) clean dirty spots by playing the spot's recycling mini-game. Each clean spot gives a
  trash bag. They carry bags to the **Recycling Machine** at the map edge: the **cleanliness meter** goes up and
  they pick an upgrade.
- **Trashers** (1 or 2, hidden) pretend to clean, re-pollute clean spots and jam the machine. After 3 re-pollutes
  they unlock a lethal ability: **Virus** (planted in a spot) or **Toxic Gas** (fills a room).
- Meetings and votes unmask Trashers, as before.
- Cleaners win at 100% cleanliness or when every Trasher is voted out. Trashers win when the timer ends or they
  reach parity.
- Outside the match: scanning a real recyclable item with the camera gives points and a daily streak that
  unlocks skins.

## 2. Names: screen vs code

The code keeps its old names, to save hours of renaming. Only player-facing text (`client/i18n/strings.csv`)
uses the new names.

| On screen | In code (C#, GDScript) | On the wire |
|---|---|---|
| Cleaner | `Role.Citizen`, `IsCitizen` | `"citizen"` |
| Trasher (name may change) | `Role.Polluter`, `IsPolluter` | `"polluter"` |
| Cleanliness meter | `Meter` (a `PollutionMeter` instance, value 0..100 = cleanliness) | `meter` |
| Cleaning spot | a task console (`ConsoleKind.Task`) | `spot` = the console id |

Never write the name of the game that inspired the genre in code, docs or the game (AGENTS.md hard rule 1,
event rule Art. 12).

## 3. What we keep and what it becomes

Nothing built gets thrown away; old systems take new jobs.

| Already built | Job in v2 |
|---|---|
| 6 mini-games + task consoles (`client/minigames`, `content/tasks.json`) | Cleaning a spot = playing that console's mini-game |
| Polluter fake tasks (client side) | Trasher fake clean: opens the mini-game locally, sends nothing |
| Dump action + dump cooldown + `cue` to witnesses | Re-pollute a clean spot |
| Poison (delayed out, body pile, report) | Virus (countdown, then out, body pile, report) |
| Fire sabotage (fix by holding a console) | Machine jam (fix by holding the machine) |
| Smog sabotage | Stays as is (Trasher sabotage that cuts vision) |
| Meetings, votes, chat, bots, reconnect, spectators | Unchanged |

Out of play in v2 (code may stay, server rejects with `not_allowed`, client hides the buttons): `hit`, `dump`
piles and `clean_start`/`clean_cancel`, per-player tasks and `task_step`, fire sabotage (its consoles leave the
map), `me.poison`, the meeting `task_bar`.

## 4. Rooms and roles

**Locked (decided Sat 21 Nov, evening): every game has 8 seats and 2 Trashers.** Bots fill the free
seats at start. A room is `full` at 8 humans.

- The server still supports 6/8/10 (`rooms.sizes`, `set_size`, built in PR #10), but no client sends
  `set_size` and the lobby has no size picker, so every room stays at its default of 8 with 2 Trashers.
- The map keeps its 10 spawns (harmless; seats 0-7 use the first 8).

## 5. Cleaners

**Spots.** Every task console on the map is a spot (11 on `greybox_01`). All start **dirty**. Each spot plays the
mini-game its console already has in `content/tasks.json` (for example `market_bins` plays `sort_waste`).

**Cleaning.** A Cleaner next to a dirty spot with a free bag slot sends `use`. The server opens a session and
answers `use_ok` (kind `spot`, with the mini-game `type`). The client plays the mini-game, then sends
`spot_done`. The server checks range and that at least `min_s` (from `tasks.json`, times
`spots.tool_time_mult` with the Cleaning Tool) has passed, then marks the spot **clean** and gives one **bag**.

**Bags.** Capacity 1 (`bags.cap`); 3 with the Backpack (`bags.backpack_cap`). A full Cleaner cannot clean.
Bags are private: only the carrier sees them (a visible bag would prove who is a Cleaner).

**Recycling Machine.** A `special` of kind `machine` at the map edge. `use` on it with bags delivers all of
them: meter `+ spots.bag_meter_add` per bag (5: 20 deliveries to win). Then the server offers an upgrade (`upgrade_offer`) and the
Cleaner picks one (`pick_upgrade`). One open offer at a time; a delivery while an offer is open adds meter but
no second offer.

**Upgrades.**

| Item | Effect |
|---|---|
| `backpack` | bag capacity 3 (does not stack; not offered once owned) |
| `tool` | clean time x 0.5 (does not stack; not offered once owned) |
| `heal` | +1 heal (max 1 held): cure yourself or an infected player within 1.5 tiles |
| `mask` | +1 mask (max 1 held): survive one gas; used up when it saves you |

**Machine jammed.** While jammed, deliveries are rejected (`machine_jammed`). A Cleaner fixes it by holding
`fix` at the machine for `machine.fix_s` (4 s).

**Litter comes back.** A spot left clean for `spots.relitter_s` (60 s) turns dirty again: Cleaners always
have work, and 20 deliveries need the spots to cycle.

**Balance (21 Nov, night).** 20 simulated matches with 8 scripted bots each: with 10 per bag and no litter the
Cleaners won 17/20 in about 4.4 min; with 5 per bag, 5 per re-pollute and litter after 60 s they win 11/20
(Trashers 9/20) in about 6.6 min of the 7. Humans clean slower than bots: retune `relitter_s` after a real match.

**Dash (every seat).** One charge: the server moves the seat up to `dash.tiles` (6 tiles, about 10 m at the
game's walking pace) along the chosen direction and stops it before a wall. Ready `dash.cooldown_s` (20 s) after
the match start and after each dash; a Cleaner who cleans `dash.cleans_refill` (5) spots gets it back at once.
The client leaves fading afterimages along the dash, also for the dashes of others it can see.

## 6. Trashers

- **Fake clean.** A Trasher can open any spot's mini-game; the client plays it locally and sends nothing. The
  spot stays dirty: that is a clue for Cleaners.
- **Re-pollute.** `repollute` on a clean spot in range: instant, the spot turns dirty, meter
  `- spots.repollute_meter_sub` (5), cooldown `trasher.repollute_cooldown_s` (20 s). Seats with vision and line
  of sight get a `cue` of kind `repollute`.
- **Sabotage.** `sabotage` kind `machine` (jam) or `smog` (vision). Shared cooldown, as today.
- **Unlock.** At `trasher.unlock_after` re-pollutes (3), the server sends `ability_unlocked` with the choices
  `virus` and `gas`. The Trasher picks one for the rest of the match (`pick_ability`).

**Virus.** `plant_virus` on a dirty spot in range. One planted virus per Trasher at a time, cooldown
`trasher.virus_cooldown_s` (30 s) after it triggers. Nobody else can see it. The next Cleaner who completes
`spot_done` on that spot is **infected** and gets a countdown:

```
age       = active time since planting
countdown = lerp(virus.start_s, virus.fatal_s, clamp(age / virus.grow_s, 0, 1))
          = 60 s when triggered at once, 35 s at 45 s old, 10 s at 90 s old or more
```

The countdown runs on active time (frozen in meetings). At 0 the Cleaner is out (cause `virus`) and leaves a
body pile that anyone can report. Only the infected player sees the countdown. While infected they may call
**one** meeting from anywhere (not counted in `emergencies_left`). A `heal` cures them.

**Toxic Gas.** `gas` with a room id from the map's `gas_rooms`. Everyone gets `gas_warning` (room, 3 s). When
it ends, every active Cleaner inside that room's rect is out (cause `gas`), unless they hold a mask (the mask
is used up instead). Trashers are immune. Cooldown `trasher.gas_cooldown_s` (90 s). No door locking for now.

## 7. Meter, timer, win

- Meter: cleanliness 0..100, starts at 0, no passive change (`meter.passive_per_s` = 0).
- Timer: `match.deadline_s` = 420 s of active time (frozen in meetings and ejections), as `clock_t` today.
- Cleaners win: meter reaches 100 (`meter_full`) or all Trashers voted out (`polluters_voted_out`).
- Trashers win: timer ends (`deadline`) or active Trashers >= active Cleaners (`parity`, which also covers
  "all Cleaners eliminated").

## 8. Meetings and bodies

Unchanged: report a body or call an emergency at the emergency console; discussion, vote, ejection. New: an
infected Cleaner's meeting call (section 6).

## 9. Camera scan, points, streak, skins

- From the main menu, a player scans a real recyclable item with the camera. The photo service (`app.py`)
  checks it (object detection + duplicate-photo check) and answers with the item and CO2 saved.
- A verified scan gives `points` (10 per scan, at most 3 scans a day) and counts for the **daily streak**
  (consecutive days with at least one scan, days in Tunis time).
- Skins unlock at streak thresholds listed in `content/skins.json` (first skin at streak 1 so the demo shows an
  unlock). The player picks an unlocked skin in the lobby; other players see it in the match.
- Platforms: Android app and web on phones (iPhone included) open the camera through a web page with
  `<input type="file" accept="image/*" capture="environment">`. The Windows game shows a QR code; the player
  scans it with a phone and takes the photo there.
- Anti-cheat: the scan link is single use and expires after 2 minutes (replaces the EXIF time check, which
  fails on browser photos and across time zones). The duplicate-photo hash check stays.

## 10. Network benchmark (mystery challenge)

Goal: show, with measured numbers, how the organisers' networks (Ooredoo, Tunisie Telecom 5G, Tunisie Telecom
5G SA) affect gameplay latency and response time.

- The player sets their network in Settings: `ooredoo`, `tt_5g`, `tt_5g_sa`, `wifi`, `other`.
- The client measures: round trip time (ping/pong every 2 s), jitter (mean change between consecutive round
  trips), snapshot gaps (expected 50 ms at 20 Hz; a gap over 100 ms counts as late), and action response time
  (an action sent until its `*_ok` or `action_rejected`).
- Every 10 s the client sends `net_report`. The server keeps them in memory and serves `GET /bench` (JSON) and
  `GET /bench.html` (one table and bar chart per network) for the pitch.
- Fair test: same phone, same server, same time window, 3 runs of 3 minutes per network, in a real match with
  bots. The server location is written on the results page. We show the numbers we measure.

## 11. Tunables (new or changed keys in `content/tuning.json`)

```json
"rooms":   { "sizes": [6, 8, 10], "default_size": 8, "trashers": { "6": 1, "8": 2, "10": 2 } },
"spots":   { "bag_meter_add": 5, "repollute_meter_sub": 5, "tool_time_mult": 0.5, "relitter_s": 60 },
"dash":    { "tiles": 6, "cooldown_s": 20, "cleans_refill": 5 },
"bags":    { "cap": 1, "backpack_cap": 3 },
"trasher": { "repollute_cooldown_s": 20, "unlock_after": 3, "virus_cooldown_s": 30,
             "gas_cooldown_s": 90, "gas_warning_s": 3, "start_cooldown_s": 10 },
"virus":   { "start_s": 60, "fatal_s": 10, "grow_s": 90, "heal_range": 1.5 },
"machine": { "fix_s": 4 },
"match":   { "deadline_s": 420, "return_to_lobby_s": 15 },
"meter":   { "passive_per_s": 0 },
"tasks":   { "common": 0, "short": 0, "long": 0 },
"scan":    { "points_per_scan": 10, "max_scans_per_day": 3, "token_ttl_s": 120 }
```

`seats` and `polluters` become the defaults for an 8-seat room; the room size decides them at match start.

## 12. Wire contract, protocol 2

`docs/protocol.md` is the source of truth; Hedi updates it from this table in the first server PR and bumps
`PROTOCOL_VERSION` to 2 (client `net.gd` bumps in the same merge). Every message not listed is unchanged.

**Client to server, new or changed**

| `t` | Fields | Rule |
|---|---|---|
| `set_size` | `size` | host, lobby; in `rooms.sizes`, not below the humans in the room, else `target_invalid`. Not used: seats are locked at 8 |
| `set_skin` | `skin` | lobby; id from `content/skins.json`, else `target_invalid` |
| `use` | `console` | a spot id opens a clean session; `"machine"` delivers bags |
| `spot_done` | `spot` | Cleaner with an open session on that spot, after `min_s` |
| `pick_upgrade` | `item` | `backpack`/`tool`/`heal`/`mask`, only while an offer is open |
| `repollute` | `spot` | Trasher, clean spot in range, off cooldown |
| `pick_ability` | `ability` | `virus`/`gas`, only after `ability_unlocked`, once per match |
| `plant_virus` | `spot` | Trasher with `virus`, dirty spot in range, none of theirs planted |
| `gas` | `room` | Trasher with `gas`, room id from map `gas_rooms`, off cooldown |
| `heal` | `target` | Cleaner holding a heal; target is self or within `virus.heal_range` + line of sight, and infected |
| `sabotage` | `kind` | adds `"machine"`; `"fire"` is gone |
| `fix` | `console`, `holding` | `"machine"`: hold for `machine.fix_s` |
| `call_meeting` | - | also accepted anywhere from an infected Cleaner, once per infection |
| `net_report` | `net`, `client`, `rtt_p50`, `rtt_p95`, `jitter_ms`, `snap_gap_p95_ms`, `late_pct`, `resp_p50_ms`, `samples` | any phase, at most 1 per 5 s per seat |

**Server to client, new or changed**

| `t` | Fields | To |
|---|---|---|
| `lobby`, `welcome` | add `size`, `sizes`; `seats[]` add `skin` | all |
| `match_started` | `seats[]` add `skin`; `tasks` is empty | each seat |
| `use_ok` | `kind` adds `"spot"` (with `type`) and `"machine"` | sender |
| `spot_cleaned` | `spot`, `bags` | the cleaner |
| `delivered` | `bags`, `meter` | the deliverer |
| `upgrade_offer` | `items` | the deliverer |
| `upgrade_ok` | `item` | sender |
| `infected` | `countdown` | the victim |
| `healed` | `by` | the cured seat |
| `mask_used` | - | the masked seat |
| `repollute_ok` | `cooldown`, `count` | sender |
| `ability_unlocked` | `choices` | the Trasher |
| `ability_ok` | `ability` | sender |
| `virus_planted` | `spot` | sender |
| `gas_warning` | `room`, `in_s` | all |
| `gas_done` | `room` | all |
| `cue` | `kind` adds `"repollute"` | witnesses |
| `you_are_out` | `cause` adds `"virus"`, `"gas"` | the seat |
| `game_over` | `reason` adds `"meter_full"`; `"tasks_done"`, `"pollution_full"` gone | all |

**Snapshot additions**

```json
"meter": 30.0,
"spots": [ { "id": "market_bins", "state": "dirty" } ],
"machine": { "jammed": false, "repair_t": 1.5 },
"gas": { "room": "gas_market", "in_s": 2.1 },
"me": { "bags": 1, "cap": 1, "tool": false, "heals": 0, "masks": 1, "infected_s": 24.0,
        "repollutes": 2, "ability": null,
        "cd": { "repollute": 0, "virus": 0, "gas": 0, "sabotage": 0 } }
```

- `spots` lists every spot for every seat (positions come from the map). A planted virus never appears in any
  snapshot.
- `machine.repair_t` only to the seat repairing. `gas` is `null` when no warning runs.
- `me.infected_s` only to the infected seat. `repollutes`, `ability`, `cd.repollute/virus/gas` only to Trashers.
  `bags`, `cap`, `tool`, `heals`, `masks` only to Cleaners.
- `piles` now only holds bodies.

**New reject reasons:** `bags_full`, `no_bags`, `machine_jammed`, `spot_clean`, `spot_dirty`, `no_item`,
`not_unlocked`.

**Map JSON additions:** a `special` of kind `machine` (id `machine`); `gas_rooms: [{id, rect: [x, y, w, h]}]`
(4 small rooms); 10 `spawns`; the `fire_fix` specials removed.

## 13. Open questions (owner: Faouzi)

1. Final names: game title, the Trasher role, the gas ability.
2. Gas locks the room's doors or not (not built for now).
3. Streak thresholds and points per scan (start values above).
4. Keep parity as a Trasher win, or only "all Cleaners out".
