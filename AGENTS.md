# AGENTS.md: Uncycled (Green Hackathon game)

Rules for every coding agent (Claude Code, Codex, Cursor, Copilot, Gemini...). Read this whole
file before writing code. If a request conflicts with it, ask the human.

- **Current design (v2, 2026-11-21), wins over every older doc below:** `docs/design/2026-11-21-cleaners-vs-trashers.md`.
  Team, owners, schedule: `docs/team/README.md`. Section 1 and the owners in section 3 describe v1.
- What the game is and why: `docs/CONTEXT.md`
- Event rules, schedule and the jury grid we are scored on: `docs/HACKATHON.md`
- Full system design: `docs/superpowers/specs/2026-11-13-uncycled-system-design.md`
- Architecture views (diagrams, state machines, sequences, data, security, ops): `docs/architecture/README.md`
- Wire protocol: `docs/protocol.md`
- Gameplay client plan (scope, order, status, changes outside `client/`): `docs/gameplay-plan.md`
- Current state, next steps, decisions: `PROGRESS.md` (read first when resuming)

## 1. The project in 5 lines

- Original multiplayer social-deduction game, recycling theme. Citizens do eco mini-games; hidden
  polluters dump waste and sabotage; meetings and votes unmask them.
- Judges play **together in one round** on their own devices. AI bots fill empty seats.
- One Godot client exported to **Web, Windows and Android**.
- One authoritative **.NET 10 (LTS) server** that runs our C# rules library (`server/Sim`).
- Golden rule: one polished, complete round beats a big broken world. When in doubt, cut scope.

## 2. Stack

| Part | Choice |
|---|---|
| Client | **Godot 4.x standard edition, GDScript** (not the .NET edition: it cannot export to web). Exact version in `PROGRESS.md` |
| Server | ASP.NET Core minimal API, .NET 10 (LTS), WebSockets |
| Rules | `server/Sim` (C#, netstandard2.1), vendored from `reference/sim` |
| Protocol | JSON over WebSocket (`wss://` in production). Schema: `docs/protocol.md` |
| Tests | xunit for `Sim` and server logic. Client verified by running it |
| Content | JSON in `content/` (tuning, tasks, maps, phrases), read by both sides |

## 3. Repo map

| Path | What | Owner |
|---|---|---|
| `client/` | Godot project: `autoload/`, `scenes/`, `world/`, `ui/`, `minigames/`, `i18n/` | Gameplay dev |
| `client/assets/` | Art and audio (placeholders until real art) | Designers |
| `server/Sim`, `server/Sim.Tests` | Rules, `Sim/Match` engine, `Sim/World` grid, and their tests | Server dev + AI dev |
| `server/Uncycled.Server/Net`, `Rooms` | WebSocket, rooms, tick loop, snapshot filter | Server dev |
| `server/Uncycled.Server/Bots`, `Ai` | Bot brains, AI providers | AI dev |
| `content/` | Shared data | AI dev (content), server dev (tuning) |
| `docs/` | Context, design, protocol | Whole team |
| `reference/` | Separate repo, gitignored, **read-only reference** | nobody edits it from here |

Stay inside your role's folders by default. There is no lead: when a change needs another role's folder, make it in its own commit and tell that folder's owner.
When a human starts a session and has not said their role, ask.

## 4. Reuse before you write (saves time and tokens)

Before implementing any rule, check in this order:
1. `server/Sim` (once vendored in P0; tested).
2. `reference/sim/src/Sim` (code not yet vendored). Copy the file, then rename to our theme.
3. `reference/docs/logic/*.md` (exact rules: timers, formulas, check order).
4. `reference/docs/data/gameplay/*.json` (tuning numbers as starting defaults).
5. `reference/AGENTS.md` routes to everything else. Open only the doc you need.

Only write a rule from scratch when none of these covers it (pollution meter, dumps, clues).

## 5. Hard rules

1. **Original work only.** From `reference` we take rules, timings and structure. Never copy
   names, map layouts, art, audio, text or trademarks of the reference game. When vendoring code,
   rename every reference-game term (maps, roles, systems) to Uncycled terms.
2. **Server-authoritative actions.** Clients send requests; the server validates with `Sim`
   (range **and** line of sight on the map grid) and broadcasts results. Movement is
   client-authoritative with server speed, teleport and walkable-cell checks.
   Units: 1 world unit = 1 tile = 32 px; only the client's `Net` layer converts to pixels.
3. **Never leak hidden info.** Each client gets a filtered snapshot. Only polluters learn who the
   polluters are.
4. **`Sim` stays engine-free.** No ASP.NET, no Godot, no real clocks (time comes in as `dt`),
   randomness only through `Sim.Core.IRandom`. Rules get table-driven xunit tests.
5. **No API keys in any client.** AI calls happen only on the server; keys come from environment
   variables / `.env` (gitignored).
6. **AI never blocks the game.** Every AI call is async, has a 3 s timeout and an
   `OfflineProvider` fallback. The room tick never awaits an AI call.
7. **Bots and humans use the same commands.** No rule may check "is this a bot". A bot taking
   over a dropped human keeps the seat active (do not set `Sim`'s `Disconnected`); the human can
   reclaim it with their reconnect token.
8. **Data-driven.** Tunables in `content/tuning.json`, tasks and phrases in `content/`, player text
   in `client/i18n/` (FR + EN). No player-facing text or magic numbers in code.
9. **Never commit** keystores, `.env`, build output, `reference/`, or anything decompiled.

## 6. Client rules (Godot)

- GDScript with static types (`var speed: float = 80.0`). One script per scene; logic in scripts,
  not in huge scenes. `.tscn` files are text: keep them small so agents and git can merge them.
- Autoloads: `Net` (socket + message signals), `Session` (local mirror of server state),
  `Settings`. Scenes listen to `Net` signals; they never open sockets themselves.
- Screen: landscape 16:9, logical 640x360, stretch `canvas_items`, aspect `expand`.
- Touch first: virtual joystick + buttons, targets at least 48 px. Keyboard WASD/E/Space for desktop.
- Remote players are drawn from interpolated snapshots (~100 ms buffer), never snapped.
- Web export: **threads off**, Compatibility renderer. Start audio only after the first tap. OGG audio.
- Every export preset adds `*.json` to the non-resource filter; Android preset enables `INTERNET`.
- Missing art must never crash: load through one helper that falls back to a coloured placeholder.
- Commit `*.import` files; never commit `.godot/`.

## 7. Server rules (.NET)

- One `Room` per room code, one tick loop (20 Hz) per room, a single command queue. All state
  changes happen on the tick; network handlers only enqueue.
- Snapshots 10-20 Hz, filtered per seat. Events (meeting, vote result, game over) are reliable
  messages sent once.
- Protocol changes: update `docs/protocol.md` **and** the client in the same change, and bump the
  protocol version.
- Health endpoint `/health` for hosting and demo warm-up.

## 8. How to work

- Keep `main` runnable at every commit. One feature per branch: `role/short-name`
  (e.g. `gameplay/sorting-task`). PR into `main`; the team merges (there is no lead).
- Agents never commit, push or open PRs unless the human asks in the current message. No AI
  co-author lines in commits.
- Small changes, plain code, type hints/static types, short docs. No frameworks or abstractions
  that were not asked for.
- Tests: rules and server logic only, table-driven, one regression test per bug. Do not test
  rendering. Run the narrowest test while iterating: `dotnet test server --filter <Name>`.

### Before saying a task is done
1. `dotnet test server` passes (if you touched `server/`).
2. Run the server and the Godot client and do the thing you changed.
3. For client changes: say how to test it on a phone (web export or APK).
4. Report what changed, the command you ran and its result, and open issues.

## 9. Commands (fill in as they become real in P0)

```bash
dotnet test server                               # rules + server tests
dotnet run --project server/Uncycled.Server      # local server on http://localhost:5000
godot --path client                              # open the client
godot --headless --path client --export-release "Web" ../build/web/index.html
godot --headless --path client --export-release "Windows Desktop" ../build/win/Uncycled.exe
godot --headless --path client --export-debug "Android" ../build/android/uncycled.apk
```

## 10. Open decisions

Listed in `PROGRESS.md` under "Decisions". Do not build on an open decision; ask the team.
