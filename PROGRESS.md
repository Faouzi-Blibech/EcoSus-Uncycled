# PROGRESS

Read this first when resuming. Keep it short: done, next, known issues, decisions.

## Done
- 2026-11-13: system design drafted (`docs/superpowers/specs/2026-11-13-uncycled-system-design.md`),
  context files written (`AGENTS.md`, `CLAUDE.md`, `docs/CONTEXT.md`), full architecture views
  (`docs/architecture/`), wire protocol v0 (`docs/protocol.md`), adversarial design review done
  and resolved (design doc section 13). Team review of all docs still pending.
- 2026-11-16: organiser rules, schedule and jury grid written up (`docs/HACKATHON.md`); whole
  repo reviewed against them (`docs/reviews/2026-11-16-jury-fit-review.md`).
- 2026-11-17: first code written by agents: `server/Sim` rules engine + tests,
  `server/Uncycled.Server` host + tests, `content/` (tuning, tasks, greybox_01 map), `client/`
  join-and-move shell. Written fresh, no `reference` code. Must be declared to the jury as
  pre-existing (rules Art. 8).
- 2026-11-18: first build check (Godot 4.7.2, .NET 10 SDK in Docker): server compiles with no
  warnings; 117 of 118 tests pass (one test bug, fix below); the server image builds and serves
  `/health`; the client imports and every scene runs with no script error; two scripted clients
  create, join and start a room on the real server, get roles and 20 Hz snapshots, and see each
  other move. Web, Windows and Android (signed debug APK) exports proven on a test project.
- 2026-11-19: `ContentTests` (server) load the real `content/` and check task consoles, spawns and
  task counts per category; `phase_changed` and `dump_ok` documented in `docs/protocol.md`.

## Next
1. Gameplay: the client's MUST tier in 4 steps: builds, world (map scene + export), match
   (HUD, 6 mini-games), meetings and results.
2. Server: report range in `tuning.json`, then the deadline on `ActiveTime`.
3. Bots: still no owner (the round works with idle bots, but they never do tasks or vote).
4. Hosting: pick a host that gives `wss://`; until then phones test against a laptop server on LAN.
5. AI: computer-vision waste scanner (AI dev).
6. Event: decide how pre-existing work is declared to the jury (rules Art. 8).

## Known issues
- Server has 12 colours, client palette has 8 (colours 8-11 draw white). Fixed in gameplay step 1.
- `client/project.godot` lacks `rendering/textures/vram_compression/import_etc2_astc=true`:
  Android export fails until it is set. Fixed in gameplay step 1.
- **Event rules:** Art. 8 wants a project conceived during the hackathon and all pre-existing
  code, design and content declared (`docs/HACKATHON.md` s3). The team builds the full game
  beforehand on purpose, so the declaration must be clear and honest.
- **Scope vs time:** about 23 build hours at the event plus the days before it, and a 10% mystery
  challenge announced on the day.
- Rule bug in `04-game-rules.md`: the 10:00 deadline runs on `MatchTime`, so polluters can win by
  reporting their own piles and sitting in meetings (review s4.2). Fix: run it on `ActiveTime`.
- `reference` is derived from decompiled code of the reference game (its `docs/logic/*.md`
  say so in their first lines). Use it only to understand how behaviour works; never copy its
  code, data or text into this repo (rules Art. 5 and Art. 12, `AGENTS.md` hard rule 9).
- `ReportRange` (3 tiles) lives only in `MatchConfig.cs`, not in `tuning.json`.
- The server dev's PC has the .NET 10 SDK (10.0.404) but no Godot; it has JDK 21 (Godot recommends 17).

## Decisions
| Decision | Status |
|---|---|
| Multiplayer, judges in one round, bots fill seats | Decided 2026-11-13 |
| Godot 4 GDScript client + .NET 10 (LTS) server | Decided 2026-11-13 |
| Platforms: Web, Windows, Android | Decided 2026-11-13 |
| `reference` stays a separate repo, used as read-only reference | Decided 2026-11-13 |
| Vote kick-out + poison hits (34%/hit = 3 hits, 25 s per-victim immunity, out at 100%, becomes a reportable pollution pile) | Decided 2026-11-13 |
| Free dump + witness; one report per pile; clue = zone, time, waste type | Decided 2026-11-13 |
| Humans chat by phrases or typed text (120 characters, 5 typed lines per meeting, word filter, 2 s gap); out players have a ghost chat living players never receive; bots use quick-chat testimony, polluter bots lie | Decided 2026-11-13, free text added 2026-11-21 |
| 8 seats, 2 polluters, ~6 min round, 60 s talk + 30 s vote | Decided 2026-11-13 |
| Server on .NET 10 LTS (not 9) | Decided 2026-11-13 |
| **Event scope: the full design, built as a complete game.** The 28-hour cut (review s5) is dropped | Decided 2026-11-18 |
| No lead: the team works and decides together; anyone may change any folder, telling its owner | Decided 2026-11-18 |
| Roles: server dev (server, rooms), gameplay dev (`client/`), AI dev (computer-vision scanner), 2 designers (art) | Decided 2026-11-18 |
| Vendor nothing from `reference` (decompiled-derived; `server/Sim` is already our own fresh code) | Proposed 2026-11-18, team to confirm |
| Godot 4.7.2 standard edition for everyone | Decided 2026-11-18 |
| More task types than the first 2 (the genre has many); exact list in the gameplay design | Decided 2026-11-18 |
| Balance numbers, deadline, emergency cap, spectator blackout, task reassignment (04-game-rules) | Proposed 2026-11-13, team to confirm |
| How pre-existing work is declared to the jury (rules Art. 8) | Open |
| Pitch owner; owners for sound, playtests, submission, bots | Open |
| Stretch AI feature | Open |
| Map setting (look); greybox layout comes with the gameplay design | Open |
| Default language FR / EN | Open |
| Server host (needs `wss://`) and web host | Open |
