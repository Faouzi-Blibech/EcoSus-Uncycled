# Wire Protocol v1 (draft)

Single source of truth for client <-> server messages. Change this file, the server and the
client in the same PR, and bump `PROTOCOL_VERSION` on any breaking change.

- Transport: WebSocket, one JSON object per **message** (text; the server accumulates frames
  until end-of-message). `wss://` in production.
- Envelope: `{"t": "<type>", ...fields}`. Unknown fields are ignored; unknown `t` returns `error`.
- Units: positions in **tiles** (float, 1 tile = 32 px). Times in seconds (float).
- Ids: `seat` 0-7, `pile`/`task`/`console` ints or string ids from content.
- Max message: 4 KB client -> server (whole message, all frames). Rate limit: 30 msg/s, burst 60.
- Coordinates must be finite numbers; NaN/Infinity closes the connection.

`PROTOCOL_VERSION = 1` (1: typed chat `chat_text`, `chat` event `text` and `ghost`, `my_text`, `ghost_chat`, reasons `too_long`, `text_limit`, `phrase_limit`)

## 1. Connection

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `hello` | `proto`, `client` ("web"/"windows"/"android"), `build` | first message |
| S->C | `hello_ok` | `server_build` | |
| S->C | `error` | `code`, `detail?` | `proto_mismatch` closes the socket |
| C->S | `ping` | `c` (client time) | every 2 s |
| S->C | `pong` | `c`, `s` (server time) | ping + clock offset |

## 2. Lobby

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `create` | `name` | creates a room, sender is host |
| C->S | `join` | `code`, `name`, `token?` | `token` = rejoin a seat. A `name` equal (case-insensitive) to a name already in the room gets a suffix " 2", " 3", ... (cut to fit 16 chars on a text-element boundary, so an emoji is never split; the bots added at match start follow the same rule); the name actually used is the one in `welcome.seats` and `lobby.seats`, so clients show that one, not what they typed. A rejoin keeps its name |
| S->C | `welcome` | `room`, `seat`, `token`, `host_seat`, `tuning`, `tasks` (the `tasks` array of `content/tasks.json`: `[{type, category, steps, min_s, consoles}]`), `map_id`, `map_version`, `phase`, `seats`, `state?`, `ghost_chat?` | `seats: [{seat, name, color, human, out}]` lists every seat, also in the lobby and on rejoin (`out` is false in the lobby); `state` only when rejoining mid-match, see 2.1 |
| S->C | `join_failed` | `reason` (`no_room`, `full`, `in_match`, `bad_name`) | |
| C->S | `set_color` | `color` | lobby only |
| S->C | `lobby` | `seats: [{seat, name, color, human, ready}]`, `host_seat` | sent on every lobby change; `host_seat` moves when the host leaves or drops, so a client takes it from every `lobby` message |
| C->S | `start` | - | host only, phase Lobby |
| C->S | `leave` | - | |

### 2.1 `welcome.state` (rejoin)

```json
{
  "phase": "playing | meeting | ejection | ended",
  "match_t": 212.4, "clock_t": 98.0,
  "role": "citizen", "partner": null, "signature": null,
  "status": "active | out",
  "poison": 34,
  "tasks": [ { "id": 3, "type": "sort_waste", "steps": 1, "steps_done": 0, "console": "market_bins" } ],
  "cooldowns": { "hit": 0, "dump": 0, "sabotage": 0 },
  "emergencies_left": 1,
  "meeting": { "stage": "voting", "ends_in": 12.0, "clues": [], "voted": [1, 4], "my_vote": null, "my_chat": 2, "my_text": 3, "living": [0, 1, 2, 4], "task_bar": 0.42 },
  "sabotage": null,
  "game_over": null
}
```

In `meeting`, `my_chat` is how many chat phrases this seat has sent in the current meeting (5 allowed) and `my_text` how many typed lines (5 allowed, counted separately), so a
rejoining client shows the right "lines left". `living` is the seat ids active at meeting start (same as in
`meeting_started`): the only seats a chat phrase may name.

`welcome.ghost_chat` (top level of `welcome`, next to `state`) is present only for a seat that is out: the newest ghost lines said so far, oldest first, each shaped like the `chat` event, `ghost: true` included. The server keeps `tuning.meeting.ghost_chat_keep` (100) lines, but a welcome carries at most `ghost_welcome_max_lines` (50) of them and at most `ghost_welcome_max_bytes` (16384) bytes of encoded JSON, so a welcome stays far below 64 KB. A living seat's welcome never has it.

`clock_t` is the deadline clock exactly as in the snapshot (4.1), so a rejoining client shows the right time left at once (`deadline_t` follows with the next snapshot; until then `tuning.match.deadline_s`).

Fields that the seat may not know are `null` (e.g. `partner` for citizens). The next `snapshot`
follows on the next tick.

## 3. Match start

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| S->C | `match_started` | `role` ("citizen"/"polluter"), `partner?`, `signature?`, `tasks: [{id, type, steps, console}]`, `spawn`, `seats` | `partner`, `signature` only to polluters; `seats: [{seat, name, color, human, out}]` as in `welcome`, so bot names are known once the match has filled the empty seats |

## 4. Play

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| C->S | `move` | `pos: [x, y]`, `seq` | 10 Hz |
| S->C | `correct` | `pos` | move rejected, snap here |
| S->C | `snapshot` | see 4.1 | 20 Hz, filtered per seat |
| S->C | `phase_changed` | `phase` ("playing"/"meeting"/"ejection"/"ended"), `stage?` ("discussion"/"voting"/"results") | to everyone, once per phase or meeting-stage change; `stage` only in a meeting. Snapshots also carry `phase` |
| C->S | `use` | `console` | |
| S->C | `use_ok` | `console`, `kind` ("task"/"emergency"/"fire_fix"/"smog_fix"), `task?`, `type?`, `step?` | opens the minigame or action |
| C->S | `task_step` | `task` | after the minigame step |
| S->C | `task_progress` | `task`, `steps_done` | |
| S->C | `task_added` | `task: {id, type, steps, steps_done, console}` | orphaned task reassigned to you |
| C->S | `clean_start` | `pile` | citizen, dump pile in range |
| S->C | `clean_ok` | `pile`, `duration` | |
| C->S | `clean_cancel` | `pile` | released early |
| S->C | `clean_done` / `clean_cancelled` | `pile` | |
| S->C | `cue` | `kind` ("hit"/"dump"), `seat`, `pos` | only to seats with vision + LOS on the actor |
| C->S | `hit` | `target` | polluter |
| S->C | `hit_ok` | `cooldown` | |
| S->C | `poisoned` | `level` | victim only |
| S->C | `you_are_out` | `cause` ("poison"/"vote") | |
| C->S | `dump` | - | polluter |
| S->C | `dump_ok` | `pile`, `cooldown` | dumper only; `pile` is the new pile's id |
| C->S | `report` | `pile` | |
| C->S | `call_meeting` | - | at emergency console |
| C->S | `sabotage` | `kind` ("smog"/"fire") | polluter |
| C->S | `fix` | `console`, `holding` | fire consoles: hold true/false; smog: `holding` ignored |
| S->C | `sabotage_started` | `kind`, `countdown?` | |
| S->C | `sabotage_ended` | `kind`, `result` ("fixed"/"expired"/"cleared") | |
| S->C | `action_rejected` | `action`, `reason` | sender only |

### 4.1 `snapshot`

```json
{
  "t": "snapshot", "tick": 1234, "s": 61.70,
  "phase": "playing", "match_t": 132.5, "deadline_t": 600, "clock_t": 98.0,
  "meter": 37.5,
  "me": { "pos": [12.0, 8.5], "vision": 4.0, "poison": 34,
          "cd": { "hit": 0, "dump": 12.3, "sabotage": 0 }, "emergencies_left": 2 },
  "players": [ { "seat": 3, "pos": [13.1, 9.0], "color": 2, "out": false } ],
  "piles": [ { "id": 7, "pos": [20.0, 4.0], "kind": "dump", "state": "fresh" } ],
  "sabotage": { "kind": "fire", "countdown": 31.0, "held": ["fire_a"] }
}
```

`match_t` is match time (meetings included). `clock_t` is the deadline clock: active play seconds,
frozen during meetings and ejections. The match ends with reason `deadline` when `clock_t` reaches
`deadline_t`, so clients show `deadline_t - clock_t` as time left (never `deadline_t - match_t`).

`me.poison` exists only for citizens. `me.cd.hit/dump/sabotage` only for polluters. `cd.sabotage` starts at `tuning.sabotage.start_cooldown_s` (10 s): a `sabotage` sent before it reaches 0 is rejected with `on_cooldown`, like the first hit and dump.

`me.vision` is the radius the server filters `players` and `piles` with: a citizen standing in a dark room (the map JSON `dark` rects) gets `tuning.vision.citizen * tuning.vision.dark_mult`, or the smog value if that is lower; polluters are not affected. The `sabotage` object also carries `smog_level` (0..1, smog easing) for smog. A smog repair takes `tuning.sabotage.smog_fix_s` at the panel. `me.smog_level` (0..1) is the smog strength vision follows; it stays above 0 for about two seconds after the smog ends while vision eases back (the `sabotage` object is already `null` then), so a client can keep its light radius in step. `sabotage.repair_t` (smog only) is the seconds of the repair counted so far, sent only to the seat that is repairing and omitted for everyone else (nobody learns who is at the panel); it vanishes when the server drops the repair (the repairer left range). A second `fix` tap while repairing is accepted and does not restart the count.
`emergencies_left` is the match-wide count. `players` and `piles` hold only what is within
vision **and** line of sight (a seat that just left sight keeps its last visible position for
0.3 s, never updated). Spectators (out) receive snapshots **without** `players` and `piles`.
The task bar is not in snapshots (published only in `meeting_started`). `sabotage` is `null`
when none.

## 5. Meeting

| Dir | `t` | Fields | Notes |
|---|---|---|---|
| S->C | `meeting_started` | `by`, `cause` ("report"/"emergency"), `clues: [{pile, zone, t, waste?, victim?}]`, `task_bar`, `discussion_s`, `living` (seat ids active at meeting start) | `clues` = every pile that became reported at this meeting start |
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
| S->C | `game_over` | `winner` ("citizens"/"polluters"), `reason`, `roles: {seat: role}`, `timeline: [{t, kind, seat?, zone?}]`, `fact`, `debrief_text?` | `debrief_text` only if the AI debrief feature is on |
| S->C | `lobby` | as in section 2 | back to lobby |

## 7. Reasons (enums)

- `action_rejected.reason`: `on_cooldown`, `out_of_range`, `wall_between`, `wrong_phase`,
  `not_allowed`, `target_invalid`, `target_immune`, `already_reported`, `already_cleaning`,
  `fire_active`, `no_emergencies_left`, `rate_limited`, `too_long` (typed line over 120 characters), `text_limit` (5 typed lines used up), `phrase_limit` (5 phrases used up; `not_allowed` stays for an out seat in a meeting-only action).
- `game_over.reason`: `polluters_voted_out`, `tasks_done`, `pollution_full`, `deadline`, `parity`.
