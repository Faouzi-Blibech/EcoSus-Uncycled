# Uncycled: System Design

Status: **draft for team approval** (2026-11-13). Owner: lead developer.
Supersedes the old Python / pygame-ce CLAUDE.md. The timeline in that file no longer applies.

---

## 1. Intent

**What:** an original multiplayer social-deduction game with a recycling theme, built for the
Green Hackathon (Swedish-Tunisian Games and Innovation Days, El Ghazala, Nov 21-22, 2026).
Theme: recycling and waste management.

**Why:** win the hackathon with a game the judges play **together, several people in the same
round**, on their own devices, plus an AI angle that makes the team stand out.

**The game in one line:** citizens clean up a neighbourhood with eco mini-games while one or more
hidden **polluters** secretly dump waste and sabotage; citizens find clues, call meetings and vote
the polluter out before the pollution meter fills.

**Success at the event:**
1. 4-8 judges join one room from their phones and laptops (room code / QR) and finish a full round.
2. Empty seats are filled by AI players so a round works with any number of humans (1 to 8).
3. The same game runs as a **web game**, a **Windows app** and an **Android app**.
4. At least one AI feature works live, with an offline fallback when Wi-Fi fails.

## 2. Goals, non-goals, anti-goals

**Goals**
- Real-time multiplayer, 1-8 humans per room, AI bots fill the rest.
- One client codebase exported to Web, Windows and Android.
- Reuse the C# rules library from `reference/sim` instead of rewriting rules.
- Data-driven content (tasks, maps, strings, tuning) so designers and the AI developer add content without code changes.

**Non-goals (for the event)**
- Accounts, friends, matchmaking lists, cosmetics store, ads, iOS build.
- More than one polished map.
- Voice chat or free-text chat between humans (quick-chat phrases at most).

**Anti-goals (failure even if it "works")**
- A round that breaks when the venue Wi-Fi drops (must have a plan B, see section 9).
- Anything recognisable from the reference game: names, map layouts, art, sounds, text. Rules and pacing only.
- Three separate codebases.
- AI that blocks the game while waiting for a model.

## 3. Approaches considered

| # | Approach | Trade-off | Verdict |
|---|---|---|---|
| A | **Godot 4 client (GDScript) + .NET server running the existing C# `sim`** | Godot exports Web + Windows + Android from one project; server reuses `sim` as-is. Two languages (GDScript, C#) | **Picked** |
| B | Godot 4 .NET (C#) client sharing `sim` directly | One language, but Godot C# **cannot export to Web** (still unshipped in 2026) | Rejected: kills the web version |
| C | TypeScript + Phaser client + .NET server | Best mobile-browser footprint, but Windows/Android need extra wrappers (Electron, Capacitor) and the team chose Godot | Rejected |
| D | Unity (code-first) + `sim` DLL | Loads `sim` everywhere, but heavy WebGL on phones and the team wants to avoid the Unity editor | Rejected |

Why A: the server is authoritative, so the rules only need to run on the server. The client only
draws, reads input and runs mini-games. That removes the need for C# on the client, so GDScript
(which exports everywhere) is fine.

## 4. Architecture

```
  Web (browser)   Windows app   Android app        <- one Godot project, 3 export presets
        \             |             /
         \  WebSocket (wss://), JSON messages
          v           v           v
   +---------------------------------------------+
   | Uncycled.Server  (ASP.NET Core, .NET 10 (LTS))     |
   |  RoomManager -> Room (one per room code)    |
   |    tick loop 20 Hz, command queue           |
   |    GameState  <- Sim (vendored C# rules)    |
   |    Bots       (AI seats, same commands)     |
   |    AI service (LLM providers + offline)     |
   |  per-client snapshot filter (hidden info)   |
   +---------------------------------------------+
            |  HTTPS (keys in env vars, server only)
            v
        LLM / vision provider (swappable)
```

### 4.1 Authority model
- **Movement: client-authoritative with sanity checks.** The client moves its own character with
  Godot physics and sends its position ~15 times/s. The server rejects impossible speed or
  teleports (speed cap built from the formula in `sim/Movement/MovementSpeed`) and rejects
  positions on non-walkable cells of the map grid. This is what the reference game does and it
  avoids writing server-side physics.
- **Interactions need range and line of sight.** The server checks distance *and* line of sight
  on the map's walkable grid (implementing `Sim.Rules.Kill.ILineOfSight`), so nobody can use,
  report or dump through a wall.
- **One unit scale everywhere:** 1 world unit = 1 tile = 32 px. Godot converts at the edge
  (`Net`), server and `Sim` only see tile units.
- **Everything else: server-authoritative.** Clients send *requests* (use task, complete task,
  dump, report, call meeting, vote, sabotage). The server validates with `Sim` (range, cooldown,
  phase, alive) and broadcasts *results*.
- **Hidden information stays on the server.** Each client gets a filtered snapshot: only polluters
  learn who the polluters are. Never send a client a role it may not know.

### 4.2 Seats and bots
A room has N **seats**. Each seat is controlled by a human connection or by a **bot brain**.
Bots produce the same commands as humans and go through the same validation, so the rules never
know the difference. Seats are always filled to the room size, so role assignment always has
enough players.

Disconnects: when a human drops, a bot takes over the seat **and the seat stays active**. Do not
apply `Sim`'s `Disconnected` state (it removes the player from living counts, task totals and
votes); that state is only for seats nobody controls. The client keeps a reconnect token
(`user://`); reconnecting within the round gives the human their seat back.

### 4.3 Server modules (`server/`)
| Module | Job |
|---|---|
| `Sim/` | Vendored copy of `reference/sim/src/Sim` (netstandard2.1), renamed to our theme, plus our new rules (pollution meter, dumps, clues) |
| `Sim.Tests/` | Vendored xunit tests + tests for every new rule |
| `Uncycled.Server/Net` | WebSocket endpoint, message (de)serialisation, connection -> seat mapping |
| `Uncycled.Server/Rooms` | Room codes, lobby, start/end, tick loop, command queue, snapshot filter |
| `Uncycled.Server/Bots` | Bot brains: citizen bot, polluter bot (state machine below), grid path-finding |
| `Uncycled.Server/Ai` | `IAiProvider` with `OfflineProvider` + `RemoteProvider`, 3 s timeout, never awaited inside the tick |
| `Sim/Match`, `Sim/Uncycled`, `Sim/World` | **New engine-free code:** `MatchEngine` (phase flow, command dispatch, task-completion validation), poison, piles, clues, witnesses, pollution meter, win order, map grid + line of sight + A*. Kept inside `Sim` so the whole game runs in unit tests and bot soak tests without a server |
| `content/` (repo root) | JSON read by the server: tuning, tasks, maps, quick-chat phrase ids. The server sends the client what it needs (tuning, task list, phrase ids) in `welcome`, so both sides never disagree |

Polluter bot: `WANDER -> FAKE_TASK -> FIND_UNSEEN_SPOT -> DUMP -> LEAVE_AREA -> WANDER`.
A spot is unseen when no other character is within view radius (later: line of sight).
All tunables in `content/tuning.json`.

### 4.4 Client modules (`client/`, Godot 4 standard edition, GDScript)
| Folder | Job |
|---|---|
| `autoload/` | `Net` (WebSocket + message bus), `Session` (local mirror of server state), `Settings` |
| `scenes/` | boot, menu, lobby, round, meeting, results/debrief |
| `world/` | map scenes, player scene, camera, interpolation of remote players (~100 ms buffer) |
| `ui/` | HUD, virtual joystick, action buttons, task list, pollution meter, meeting/vote UI |
| `minigames/` | one scene per eco task; on success sends `task_complete` |
| `assets/` | designers' art and audio (placeholders until real art) |
| `i18n/` | Godot CSV translations (fr, en); no player-facing text in code |

Screen: landscape 16:9, logical 640x360, stretch mode `canvas_items`, aspect `expand`.
Touch first: on-screen joystick + buttons, min 48 px targets. Keyboard (WASD/E/Space) for desktop.

### 4.5 Protocol (first version)
JSON over WebSocket, one object per message: `{"t": "<type>", ...}`. Full schema lives in
`docs/protocol.md` (draft v0 written; finalised in phase 1) and is the single source both sides
follow. All architecture views (context, containers, components, state machines, sequences,
data, security, deployment, testing) are in `docs/architecture/`.
- Client -> server: `join`, `ready`, `move`, `use`, `task_complete`, `dump`, `report`, `call_meeting`, `vote`, `sabotage`, `chat_phrase`.
- Server -> client: `welcome` (seat id, room state), `snapshot` (10-20 Hz, filtered), `event` (meeting started, vote result, player out, game over), `error`.
- Every message carries a protocol version in `welcome`; mismatch shows "please update".
Binary encoding (the `PositionCodec` in `sim`) is a later optimisation, not needed for 8 players.

### 4.6 Maps
Maps are defined later. Contract now: a map is a Godot scene (source of truth) plus an exported
`content/maps/<id>.json` (walkable grid, spawn points, task consoles, rooms, vents if any) that
the server loads for range checks and bot path-finding. A headless Godot tool script writes the
JSON. Map ideas are original; reference-game data is used only for scale (rooms, consoles, walk times).

## 5. Rule mapping (reference game -> Uncycled)

Default proposal; the team confirms in "Open decisions".

| `sim` concept | Uncycled | Reuse |
|---|---|---|
| Crewmate / Impostor roles | Citizen / Polluter | `RoleAssigner`, `RoleDefinition` (renamed) |
| Tasks + task bar, crew wins by tasks | Eco mini-games, cleanup bar | `TaskAssigner`, task progress |
| Kill | **Poison (decided):** a polluter "hit" adds poison to a citizen (default 34% per hit = 3 hits, 25 s per-victim immunity, in `tuning.json`). At 100% the citizen **becomes pollution** and is out of the round (spectator). Each hit is validated like a kill (alive, not a polluter, in range + line of sight, cooldown, not in a meeting) | `KillValidator`, `KillTimerRules`, `TargetSelector` (renamed); new poison counter |
| (new) | Poisoned-out citizen leaves a **pollution pile** where they fell; it is reportable once, like a body | `ReportValidator` (as-is) |
| (new) | Witnesses: any citizen with line of sight when a hit or a dump happens is recorded as a witness (feeds clues and bot testimony) | new rule + tests |
| Sabotage systems | Smog (vision shrinks, like lights), overflowing bins / landfill fire (critical, 2 players fix) | `Lights`, `CriticalSabotage`, `SabotageCooldown` |
| (new) | **Dump (decided):** a polluter can dump any time, on a cooldown. It raises the pollution meter and leaves a pile. Each pile is reportable once (starts a meeting) and gives a clue: zone, time, waste type. Cleaning a pile is a short task | new rule + tests |
| (new) | Clues: zone, time and waste type from piles, plus witness names | new rule + tests |
| Report / emergency meeting / vote | Same. **Vote kick-out (decided)**: the most-voted player is removed; tie or skip removes nobody | `MeetingSession`, `ReportValidator` (as-is) |
| Meeting talk | **Decided:** humans talk out loud in the room. Bots post quick-chat lines built from what they really saw ("I saw Green near the river at 2:10"); polluter bots lie. An LLM may later rewrite bot lines (AI feature) | new `Bots/Testimony` |
| Win checks | Citizens: all tasks done, or every polluter voted out. Polluters: pollution meter full, or polluters >= citizens still in. Check order after each event: polluters all out -> meter full -> parity -> tasks done | `WinChecker` + pollution condition |

What `Sim` really gives us: rule components (role and task assignment, kill/report validation,
meeting and vote tally, win checks, sabotage systems, movement maths), all tested. What it does
**not** give us: a match loop, command dispatch, task-completion validation, movement
validation, bots, dumps and clues. Those are new code in `Uncycled.Server/Match` and `Bots`.
`ReportValidator` reports a body position; dumps need their own "reportable waste" rule.
`WinChecker` assumes elimination and parity; it gets a pollution-meter condition and an explicit
win order (decided in section 11).

Rename rule: strip every reference-game name (maps, roles, systems) when vendoring
(`MapProfile`, `OptionPresets`, `Enums`, `WinChecker` currently contain them).

## 6. AI features

The team is still choosing. Architecture supports any of them because all AI runs **on the
server** (keys never reach a client) behind `IAiProvider`:
- Offline version always exists (rule-based / scripted JSON); used on error or after 3 s.
- Calls are async; the room tick never waits. The client shows a "thinking..." state.
- Vendor-swappable (no SDK types outside `Ai/`).

Candidates from the earlier list that fit multiplayer best: smart bots (polluter/citizen AI),
AI-voiced bot answers in meetings, AI eco-coach debrief, fresh case per round, waste scanner
(camera, Android/web only, spike first).

## 7. Repo layout

```
EcoSus-Uncycled/
├── AGENTS.md            # rules for every coding agent (source of truth)
├── CLAUDE.md            # imports AGENTS.md + Claude-only notes
├── PROGRESS.md          # done / next / known issues / decisions
├── docs/
│   ├── CONTEXT.md       # one-file brief to paste into any chatbot
│   ├── protocol.md      # message schema (phase 1)
│   └── superpowers/specs/  # design docs (this file)
├── content/             # shared JSON: tuning, tasks, maps, phrases
├── server/
│   ├── Uncycled.sln
│   ├── Sim/  Sim.Tests/  Uncycled.Server/
│   └── Dockerfile
├── client/              # Godot project (project.godot, export_presets.cfg)
└── reference/        # separate repo, cloned here, gitignored, read-only reference
```

## 8. Phases

No dates (team plans those). Each phase ends with a demo that proves it. Do risky things first.

| Phase | Goal | Done when |
|---|---|---|
| **P0 Foundations + spikes** | Prove the 3 platforms can talk to a hosted server | Repo layout in place; `Sim` vendored, renamed, `dotnet test` green; Godot project opens; a hello client built for **Web, Windows and Android** connects via `wss://` to the deployed server echo endpoint, tested on a **real Android phone browser, an iPhone Safari and the APK**; the plan-B LAN setup tried once (section 9) |
| **P1 Rooms + movement** | Several people in one room | Create/join by room code; reconnect token; 4+ devices (mix of phone browser, APK, Windows) see each other move smoothly on a **greybox map at final scale** (room count and walk times close to the real map); map JSON export + server walkable grid; `docs/protocol.md` written |
| **P2 Core loop** | A full round end to end | Roles assigned, 2 mini-games with server task validation, task bar, **dumps + pollution meter + reporting a dump**, emergency meeting, vote, eject, win/lose in the agreed order, results screen; bots fill empty seats and can win |
| **P3 Recycling identity** | It is *our* game | Clues, one sabotage, bot meeting behaviour (quick-chat testimony, votes), debrief with a real recycling fact |
| **P4 AI feature** | The AI angle | Chosen core AI feature live with offline fallback; stretch feature if time. **The AI dev spikes the chosen feature from P1 on**, so P4 only integrates it |
| **P5 Content + polish** | Looks and feels finished | Real art on the greybox layout, sound, FR/EN strings, joystick feel, 30 FPS on a mid-range Android browser |
| **P6 Event ready** | Nothing breaks on stage | Web build hosted, APK + Windows zip downloadable, server deployed + LAN fallback rehearsed, demo script, 3 full playtests |
| **Event** | Bug fixes only | Feature freeze holds |

## 9. Deployment and demo risks

| Risk | Plan |
|---|---|
| Web build on `https` cannot open plain `ws://` (mixed content) | Server must have TLS: host on a platform that gives `wss://` (Fly.io / Render / Railway / VPS + Caddy). Decide in P0 |
| Rooms live in memory | Run **exactly one server instance** (no autoscaling; some hosts route new connections to other instances). WebSocket ping every 10 s; the client reconnects automatically with its token |
| Free hosting sleeps | Wake the server before the demo; `/health` endpoint |
| Venue Wi-Fi fails | **Plan B1:** devices switch to mobile data / a phone hotspot and keep using the cloud server (no change). **Plan B2:** server on a laptop, APK and Windows app connect to `ws://<laptop-ip>`; a new round starts (no state transfer). Web over a LAN `http` address may hit Godot's secure-context limits: test it in P0, and if it fails the B2 web fallback is dropped |
| Godot web on phones | Export **without threads** (no SharedArrayBuffer headers needed). Web needs WebAssembly + WebGL 2 and the Compatibility renderer; Safari has known caveats. Test Android Chrome and iPhone Safari every phase. Bring 2-3 spare Android phones with the APK so judges are never blocked |
| Android export toolchain | Android SDK and JDK 21 exist on the lead's PC; Godot recommends 17 but supports newer, confirm with the pinned version in P0. Debug keystore only; never commit keystores. Enable the `INTERNET` permission |
| Missing data in builds | Godot does not pack `.json` by default: add `*.json` to each preset's non-resource export filter |
| Audio on web | Start audio only after the first tap |

## 10. Testing

- `server/Sim.Tests`: table-driven xunit, one test per rule and a regression test per bug.
  Vendored tests must stay green. New rules (pollution, dump, clues) get tests first.
- Protocol: round-trip test per message type on the server.
- Bots: a headless soak test (8 bots, one full round, no exception).
- Client and mini-games: verified by playing on a phone, not unit tests.

## 11. Open decisions (team)

Decided 2026-11-13: vote kick-out + poison hits (section 5); free dump + witness; talk aloud +
bot phrases; **8 seats, 2 polluters**, ~6 min rounds, meetings 60 s talk + 30 s vote (all in
`tuning.json`). Exact rules and numbers: `docs/architecture/04-game-rules.md`.

Still open:
3. Core and stretch AI feature (if the event judges AI explicitly, decide by P1).
4. Map setting and layout (campus, souk, beach, neighbourhood...).
5. Default language (FR or EN).
6. Server host (Fly.io, Render, Railway, VPS) and web host (itch.io or GitHub Pages).
7. Exact Godot 4.x version to pin (whole team uses the same one).

## 12. Calls made by the designer of this doc (check these)

- Client-authoritative movement (simpler, matches the reference game).
- JSON over WebSocket, not binary or UDP, for the event.
- AI runs inside the game server, not a separate proxy.
- `Sim` is vendored (copied) into this repo, not referenced across repos, so teammates only clone one repo.
- Bots replace disconnected humans without marking the seat disconnected; humans can reclaim it.
- Interactions are checked for range and line of sight on the server's walkable grid.
- 1 world unit = 1 tile = 32 px on both sides.
- One server instance, in-memory rooms; plan B2 starts a fresh round instead of moving state.
- Dumps and reporting a dump move into P2 (a report needs something to report); greybox map at
  final scale in P1 so art in P5 does not change distances.
- Godot version pinned to 4.7 unless a newer stable 4.x is out when installing (the `gamedev`
  Godot skills target 4.7).
- Poison details: the victim sees their own poison bar; others do not. Poison does not heal.
  A poisoned-out citizen becomes a spectator and leaves a reportable pollution pile.
- Hits and dumps both create witnesses; win check order as in section 5.
- Each polluter has a secret "signature" waste type; piles carry it, so matching piles link to
  the same polluter.
- Cleaning a dump pile is an optional 3 s action that lowers the meter (-2.5%); the meter also
  rises slowly by itself (+0.08%/s of active play); a landfill fire that expires adds +20%
  instead of ending the round; a 10:00 match deadline means polluters win.
- Confirm ejects is on (the ejection screen says if the player was a polluter).
- Match engine lives in `Sim` (engine-free), not in the server project.
- Server targets **.NET 10 LTS** (.NET 9 support ended Nov 10, 2026); the lead PC only has the
  .NET 9 SDK, so installing the .NET 10 SDK is a P0 step.

## 13. Design review: challenges and resolutions (2026-11-13)

An adversarial review of this doc and `docs/architecture/` raised 25 issues. All are resolved in
the architecture docs; the main changes:

| Issue | Resolution |
|---|---|
| Meter filled in ~2.5 min | Rebalanced: dump +5% / 40 s, clean -2.5%, passive +0.08%/s: ~5 min of active play without cleaning, ~8 min with |
| Meetings could eat the whole demo and chain | 60/30 s kept (owner's choice for in-room talk); all fresh piles reported at meeting start; emergencies capped at 2 per match; 10:00 deadline |
| Two polluters could stack poison | 34% per hit (3 hits), 25 s hit cooldown, **25 s per-victim immunity** |
| Victim accusations | Kept on purpose: poison is private, so "X poisoned me" is an unverifiable claim |
| Humans could not see what bots witnessed | Hit and dump cues go to every seat with vision + LOS; bots use the same rule |
| Movement check allowed ~8 tiles/s and wall clipping | Distance budget refilled by server time + swept segment on the grid |
| Snapshots leaked players through walls | Server filters by vision **and** LOS; last visible position kept 0.3 s, never updated |
| Cleaning had no protocol | `clean_start` / `clean_cancel` / `clean_done`, server-timed |
| Reconnect state undefined | `welcome.state` schema covers every phase; client routes by phase; reconnect is MUST |
| Ejecting an idle citizen could win by tasks | Task totals fixed in steps; orphaned tasks reassigned (`task_added`) |
| Timed-out votes let 2 polluters decide | Timed-out votes count as skip |
| Task bar exposed fake tasks | Task bar published only at meetings |
| Spectators could scout and talk | Spectators receive no positions until game over |
| `Sim` round clock resets at meetings | Three clocks: MatchTime, ActiveTime, SinceMeeting |
| Smog switches, fire holders, sabotage cooldown semantics | Timed smog repair; holders revalidated each tick; cooldown counts from sabotage end |
| Send/receive queues unbounded, frames vs messages | Latest-snapshot slot + capped event queue; bounded inbox; 4 KB per whole message |
| .NET 9 out of support | .NET 10 LTS |
| Scope too big | MUST / SHOULD / COULD tiers in `docs/architecture/04-game-rules.md` section 11; all 3 exports stay MUST |
