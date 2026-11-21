# Wire Protocol v2

Single source of truth for client <-> server messages. Change this file, the server and the
client in the same PR, and bump `PROTOCOL_VERSION` on any breaking change.

- Transport: WebSocket, one JSON object per **message** (text; the server accumulates frames
  until end-of-message). `wss://` in production.
- Envelope: `{"t": "<type>", ...fields}`. Unknown fields are ignored; unknown `t` returns `error`.
- Units: positions in **tiles** (float, 1 tile = 32 px). Times in seconds (float).
- Ids: `seat` 0-9, `pile` int, `console`/`spot`/`room` string ids from content or the map (a spot id is its console id).
- Max message: 4 KB client -> server (whole message, all frames). Rate limit: 30 msg/s, burst 60.
- Coordinates must be finite numbers; NaN/Infinity closes the connection.

`PROTOCOL_VERSION = 2`

Changelog:
- 2: Cleaners vs Trashers rules (spots, bags, Recycling Machine, upgrades, re-pollute, virus, gas, machine jam), room size 6/8/10 and seat ids 0-9, skins, `net_report`. Code names stay `citizen`/`polluter` on the wire.
- 1: typed chat `chat_text`, `chat` event `text` and `ghost`, `my_text`, `ghost_chat`, reasons `too_long`, `text_limit`, `phrase_limit`.

"Out of play in v2" means: the v1 message stays in the schema but the server rejects it with `action_rejected` reason `not_allowed`, and the client hides its button. Server messages marked out of play are never sent.

## 1. Connection

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `hello` | `proto`, `client` ("web"/"windows"/"android"), `build` | first message |
| S->C | `hello_ok` | `server_build` | |
| S->C | `error` | `code`, `detail?` | `proto_mismatch` closes the socket |
| C->S | `ping` | `c` (client time) | every 2 s |
| S->C | `pong` | `c`, `s` (server time) | ping + clock offset |
| C->S | `net_report` | `net`, `client`, `rtt_p50`, `rtt_p95`, `jitter_ms`, `snap_gap_p95_ms`, `late_pct`, `resp_p50_ms`, `samples` | any phase, at most 1 per 5 s per seat (network benchmark) |

## 2. Lobby

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `create` | `name` | creates a room, sender is host |
| C->S | `join` | `code`, `name`, `token?` | `token` = rejoin a seat. A `name` equal (case-insensitive) to a name already in the room gets a suffix " 2", " 3", ... (cut to fit 16 chars on a text-element boundary, so an emoji is never split; the bots added at match start follow the same rule); the name actually used is the one in `welcome.seats` and `lobby.seats`, so clients show that one, not what they typed. A rejoin keeps its name |
| S->C | `welcome` | `room`, `seat`, `token`, `host_seat`, `size`, `sizes`, `tuning`, `tasks` (the `tasks` array of `content/tasks.json`: `[{type, category, steps, min_s, consoles}]`), `map_id`, `map_version`, `phase`, `seats`, `state?`, `ghost_chat?` | `seats: [{seat, name, color, skin, human, out}]` lists every seat, also in the lobby and on rejoin (`out` is false in the lobby); `state` only when rejoining mid-match, see 2.1 |
| S->C | `join_failed` | `reason` (`no_room`, `full`, `in_match`, `bad_name`) | `full` = the humans in the room reach `size` |
| C->S | `set_color` | `color` | lobby only |
| C->S | `set_size` | `size` | host, lobby; must be in `rooms.sizes` (`sizes`, default 8) and not below the humans in the room, else `action_rejected` `target_invalid`. Trashers per size: 6 -> 1, 8 -> 2, 10 -> 2 |
| C->S | `set_skin` | `skin` | lobby; id from `content/skins.json`, else `action_rejected` `target_invalid` |
| S->C | `lobby` | `seats: [{seat, name, color, skin, human, ready}]`, `host_seat`, `size`, `sizes` | sent on every lobby change; `host_seat` moves when the host leaves or drops, so a client takes it from every `lobby` message |
| C->S | `start` | - | host only, phase Lobby. Bots fill the free seats up to `size` |
| C->S | `leave` | - | |

### 2.1 `welcome.state` (rejoin)

```json
{
  "phase": "playing | meeting | ejection | ended",
  "match_t": 212.4, "clock_t": 98.0,
  "role": "citizen", "partner": null, "signature": null,
  "status": "active | out",
  "poison": null,
  "tasks": [],
  "cooldowns": { "repollute": 0, "virus": 0, "gas": 0, "sabotage": 0 },
  "emergencies_left": 1,
  "meeting": { "stage": "voting", "ends_in": 12.0, "clues": [], "voted": [1, 4], "my_vote": null, "my_chat": 2, "my_text": 3, "living": [0, 1, 2, 4] },
  "sabotage": null,
  "game_over": null
}
```

In `meeting`, `my_chat` is how many chat phrases this seat has sent in the current meeting (5 allowed) and `my_text` how many typed lines (5 allowed, counted separately), so a
rejoining client shows the right "lines left". `living` is the seat ids active at meeting start (same as in
`meeting_started`): the only seats a chat phrase may name.

`welcome.ghost_chat` (top level of `welcome`, next to `state`) is present only for a seat that is out: the newest ghost lines said so far, oldest first, each shaped like the `chat` event, `ghost: true` included. The server keeps `tuning.meeting.ghost_chat_keep` (100) lines, but a welcome carries at most `ghost_welcome_max_lines` (50) of them and at most `ghost_welcome_max_bytes` (16384) bytes of encoded JSON, so a welcome stays far below 64 KB. A living seat's welcome never has it.

`clock_t` is the deadline clock exactly as in the snapshot (4.1), so a rejoining client shows the right time left at once (`deadline_t` follows with the next snapshot; until then `tuning.match.deadline_s`).

v2: `poison` is out of play (always `null`), `tasks` is `[]`, `meeting.task_bar` is gone. A rejoin also carries the private `me` fields of 4.1 (`bags`, `cap`, `tool`, `heals`, `masks`, `infected_s`, `repollutes`, `ability`), each only for the seats that get it there; the next snapshot restates them anyway.

Fields that the seat may not know are `null` (e.g. `partner` for citizens). The next `snapshot`
follows on the next tick.

## 3. Match start

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| S->C | `match_started` | `role` ("citizen"/"polluter"), `partner?`, `signature?`, `tasks: [{id, type, steps, console}]`, `spawn`, `seats` | `partner`, `signature` only to polluters; `tasks` is empty in v2; `seats: [{seat, name, color, skin, human, out}]` as in `welcome`, so bot names are known once the match has filled the empty seats |

## 4. Play

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `move` | `pos: [x, y]`, `seq` | 10 Hz |
| S->C | `correct` | `pos` | move rejected, snap here |
| S->C | `snapshot` | see 4.1 | 20 Hz, filtered per seat |
| S->C | `phase_changed` | `phase` ("playing"/"meeting"/"ejection"/"ended"), `stage?` ("discussion"/"voting"/"results") | to everyone, once per phase or meeting-stage change; `stage` only in a meeting. Snapshots also carry `phase` |
| C->S | `use` | `console` | a spot id opens a clean session (Cleaner, dirty spot, free bag slot); `"machine"` delivers all carried bags |
| S->C | `use_ok` | `console`, `kind` ("task"/"emergency"/"fire_fix"/"smog_fix"/"spot"/"machine"), `task?`, `type?`, `step?` | to the sender; opens the minigame or action. `spot` comes with the minigame `type`. `task` and `fire_fix` are out of play in v2 |
| C->S | `task_step` | `task` | out of play in v2: rejected with `not_allowed` (replaced by `spot_done`) |
| C->S | `spot_done` | `spot` | Cleaner with an open session on that spot, after at least `min_s` of its task (times `spots.tool_time_mult` with the tool) |
| S->C | `spot_cleaned` | `spot`, `bags` | the cleaner; `bags` = bags carried now |
| S->C | `delivered` | `bags`, `meter` | the deliverer; `bags` = bags delivered, `meter` = new cleanliness |
| S->C | `upgrade_offer` | `items` | the deliverer; one open offer at a time |
| C->S | `pick_upgrade` | `item` ("backpack"/"tool"/"heal"/"mask") | only while an offer is open, else `no_item` |
| S->C | `upgrade_ok` | `item` | sender |
| S->C | `task_progress` | `task`, `steps_done` | out of play in v2, not sent |
| S->C | `task_added` | `task: {id, type, steps, steps_done, console}` | out of play in v2, not sent |
| C->S | `clean_start` | `pile` | out of play in v2: rejected with `not_allowed` |
| S->C | `clean_ok` | `pile`, `duration` | out of play in v2, not sent |
| C->S | `clean_cancel` | `pile` | out of play in v2: rejected with `not_allowed` |
| S->C | `clean_done` / `clean_cancelled` | `pile` | out of play in v2, not sent |
| S->C | `cue` | `kind` ("repollute"; "hit"/"dump" out of play), `seat`, `pos` | only to seats with vision + LOS on the actor |
| C->S | `hit` | `target` | out of play in v2: rejected with `not_allowed` |
| S->C | `hit_ok` | `cooldown` | out of play in v2, not sent |
| S->C | `poisoned` | `level` | out of play in v2, not sent (replaced by `infected`) |
| S->C | `you_are_out` | `cause` ("vote"/"virus"/"gas"; "poison" out of play) | the seat |
| C->S | `dump` | - | out of play in v2: rejected with `not_allowed` (replaced by `repollute`) |
| S->C | `dump_ok` | `pile`, `cooldown` | out of play in v2, not sent |
| C->S | `report` | `pile` | `piles` now only hold bodies |
| C->S | `call_meeting` | - | at the emergency console; also accepted anywhere from an infected Cleaner, once per infection (not counted in `emergencies_left`) |
| C->S | `sabotage` | `kind` ("machine"/"smog") | Trasher. `"machine"` jams the Recycling Machine; `"fire"` is out of play (`not_allowed`). Shared cooldown |
| C->S | `fix` | `console`, `holding` | `"machine"`: hold true/false for `machine.fix_s` (4 s); smog: `holding` ignored; fire consoles are out of play |
| S->C | `sabotage_started` | `kind` ("machine"/"smog"), `countdown?` | |
| S->C | `sabotage_ended` | `kind`, `result` ("fixed"/"expired"/"cleared") | |
| S->C | `action_rejected` | `action`, `reason` | sender only |
| C->S | `repollute` | `spot` | Trasher, clean spot in range, off cooldown |
| S->C | `repollute_ok` | `cooldown`, `count` | sender; `count` = re-pollutes so far |
| S->C | `ability_unlocked` | `choices` (`["virus", "gas"]`) | the Trasher, at `trasher.unlock_after` (3) re-pollutes |
| C->S | `pick_ability` | `ability` ("virus"/"gas") | only after `ability_unlocked`, once per match, else `not_unlocked` |
| S->C | `ability_ok` | `ability` | sender |
| C->S | `plant_virus` | `spot` | Trasher with `virus`, dirty spot in range, none of theirs already planted |
| S->C | `virus_planted` | `spot` | sender only; a planted virus is in no snapshot |
| S->C | `infected` | `countdown` | the victim only: seconds to out (cause `virus`) |
| C->S | `heal` | `target` | Cleaner holding a heal; target is self or within `virus.heal_range` (1.5) + line of sight, and infected |
| S->C | `healed` | `by` | the cured seat |
| S->C | `mask_used` | - | the masked seat |
| C->S | `gas` | `room` | Trasher with `gas`, room id from the map `gas_rooms`, off cooldown |
| S->C | `gas_warning` | `room`, `in_s` | everyone |
| S->C | `gas_done` | `room` | everyone |

### 4.1 `snapshot`

```json
{
  "t": "snapshot", "tick": 1234, "s": 61.70,
  "phase": "playing", "match_t": 132.5, "deadline_t": 600, "clock_t": 98.0,
  "meter": 30.0,
  "me": { "pos": [12.0, 8.5], "vision": 4.0, "emergencies_left": 2,
          "bags": 1, "cap": 1, "tool": false, "heals": 0, "masks": 1, "infected_s": 24.0,
          "repollutes": 2, "ability": null,
          "cd": { "repollute": 0, "virus": 0, "gas": 0, "sabotage": 0 } },
  "players": [ { "seat": 3, "pos": [13.1, 9.0], "color": 2, "out": false } ],
  "piles": [ { "id": 7, "pos": [20.0, 4.0], "kind": "body", "state": "fresh" } ],
  "spots": [ { "id": "market_bins", "state": "dirty" } ],
  "machine": { "jammed": false, "repair_t": 1.5 },
  "gas": { "room": "gas_market", "in_s": 2.1 },
  "sabotage": { "kind": "smog", "countdown": 31.0, "smog_level": 0.6 }
}
```

`match_t` is match time (meetings included). `clock_t` is the deadline clock: active play seconds,
frozen during meetings and ejections. The match ends with reason `deadline` when `clock_t` reaches
`deadline_t`, so clients show `deadline_t - clock_t` as time left (never `deadline_t - match_t`).

v2 hidden fields, and who receives each:

- `meter` is the cleanliness 0..100 (0 at start), sent to every seat. It is not the v1 pollution meter.
- `spots` lists every spot for every seat (positions come from the map). `state` is "dirty" or "clean". A planted virus never appears in any snapshot.
- `me.bags`, `me.cap`, `me.tool`, `me.heals`, `me.masks`: Cleaners only. Bags are private: other seats never see them.
- `me.infected_s`: only the infected seat.
- `me.repollutes`, `me.ability` (`null`, "virus" or "gas"), `me.cd.repollute`, `me.cd.virus`, `me.cd.gas`, `me.cd.sabotage`: Trashers only. `cd.sabotage` starts at `trasher.start_cooldown_s` (10 s): a `sabotage` sent before it reaches 0 is rejected with `on_cooldown`, like the first repollute.
- `machine.repair_t`: only the seat repairing the machine (seconds of the repair counted so far); omitted for everyone else. `machine.jammed` goes to everyone.
- `gas` goes to everyone and is `null` when no warning runs.
- `piles` now only hold bodies (`kind` "body"). The v1 `me.poison`, `me.cd.hit` and `me.cd.dump` are out of play and absent. `sabotage.held` and the fire kind are out of play.

`me.vision` is the radius the server filters `players` and `piles` with: a citizen standing in a dark room (the map JSON `dark` rects) gets `tuning.vision.citizen * tuning.vision.dark_mult`, or the smog value if that is lower; polluters are not affected. The `sabotage` object also carries `smog_level` (0..1, smog easing) for smog. A smog repair takes `tuning.sabotage.smog_fix_s` at the panel. `me.smog_level` (0..1) is the smog strength vision follows; it stays above 0 for about two seconds after the smog ends while vision eases back (the `sabotage` object is already `null` then), so a client can keep its light radius in step. `sabotage.repair_t` (smog only) is the seconds of the repair counted so far, sent only to the seat that is repairing and omitted for everyone else (nobody learns who is at the panel); it vanishes when the server drops the repair (the repairer left range). A second `fix` tap while repairing is accepted and does not restart the count.
`emergencies_left` is the match-wide count. `players` and `piles` hold only what is within
vision **and** line of sight (a seat that just left sight keeps its last visible position for
0.3 s, never updated). Spectators (out) receive snapshots **without** `players` and `piles`.
`sabotage` is `null` when none (smog only; a machine jam is reported in `machine.jammed`).

## 5. Meeting

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| S->C | `meeting_started` | `by`, `cause` ("report"/"emergency"), `clues: [{pile, zone, t, waste?, victim?}]`, `discussion_s`, `living` (seat ids active at meeting start) | `clues` = every pile that became reported at this meeting start. `task_bar` is out of play in v2 (not sent). `cause` "emergency" also covers an infected Cleaner's call |
| C->S | `chat_phrase` | `id`, `args` | `id` must be in `content/phrases.json`; `args` match its slots in order (seat ids, zone ids of living seats and map zones), else `target_invalid`; max 5 per seat per meeting (`phrase_limit`); a living seat only in the discussion stage, else `wrong_phase`; at least 2 s between two chat lines of a seat, else `rate_limited`. An out seat may send one at any time as a ghost line (no per-meeting cap) |
| C->S | `chat_text` | `text` | a typed line. The server trims, collapses whitespace and strips control and invisible characters, then rejects an empty line (`target_invalid`), one over 120 characters (`too_long`, never truncated) or a 6th line in the meeting (`text_limit`); discussion stage only, else `wrong_phase`. At least 2 s between two chat lines (typed or phrase) of a seat, else `rate_limited`. Raw `text` over 1024 characters or not a string: `error` `bad_field`. Listed words (`content/chat_filter.json`) are replaced by `***`. An out seat may send one at any time as a ghost line (no per-meeting cap) |
| S->C | `chat` | `seat`, then `id`, `args` (a phrase) or `text` (a typed human line, filtered, plain text: never parse markup), `ghost?` | `ghost: true` marks a ghost line: sent only to the seats that are out when it is said, never to a living seat. Bots do not type, so `text` is always a human line |
| S->C | `voting_started` | `voting_s` | |
| C->S | `vote` | `target` (seat) or `skip: true` | |
| S->C | `vote_cast` | `seat` | who voted, not for whom |
| S->C | `meeting_result` | `votes: {seat: target or "skip"}`, `ejected?`, `tie` | a timed-out vote is reported and counted as `"skip"` |
| S->C | `ejection` | `seat`, `was_polluter` | confirm ejects on |
| S->C | `round_resumed` | `spawn` | |

## 6. End

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| S->C | `game_over` | `winner` ("citizens"/"polluters"), `reason` (see 7), `roles: {seat: role}`, `timeline: [{t, kind, seat?, zone?}]`, `fact`, `debrief_text?` | `debrief_text` only if the AI debrief feature is on |
| S->C | `lobby` | as in section 2 | back to lobby |

## 7. Reasons (enums)

- `action_rejected.reason`: `on_cooldown`, `out_of_range`, `wall_between`, `wrong_phase`,
  `not_allowed`, `target_invalid`, `target_immune`, `already_reported`, `already_cleaning`,
  `fire_active` (out of play), `no_emergencies_left`, `rate_limited`, `too_long` (typed line over 120 characters), `text_limit` (5 typed lines used up), `phrase_limit` (5 phrases used up; `not_allowed` stays for an out seat in a meeting-only action and for every out-of-play v1 message).
- New in 2: `bags_full` (clean with a full bag slot), `no_bags` (deliver with none), `machine_jammed` (delivery while jammed), `spot_clean` (clean or plant on a clean spot), `spot_dirty` (repollute a dirty spot), `no_item` (no open offer, item not offered, or no heal), `not_unlocked` (ability not unlocked or already picked).
- `game_over.reason`: `polluters_voted_out`, `meter_full` (cleanliness reached 100), `deadline`, `parity`. `tasks_done` and `pollution_full` are gone.
- `you_are_out.cause`: `vote`, `virus`, `gas` (`poison` out of play).
- `use_ok.kind`: `task`, `emergency`, `fire_fix`, `smog_fix`, `spot`, `machine`.

## 8. Map JSON additions

A `special` of kind `machine` (id `machine`); `gas_rooms: [{id, rect: [x, y, w, h]}]` (4 small rooms); 10 `spawns`; the `fire_fix` specials removed. The client gets spot and machine positions from the map, not from snapshots.
