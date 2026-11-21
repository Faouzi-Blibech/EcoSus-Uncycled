# Uncycled: System Design

Status: **draft v2 for team approval** (2026-11-14). Owner: lead developer.
Supersedes the old Python / pygame-ce CLAUDE.md. The timeline in that file no longer applies.

This doc explains **what we build and why**. It does not repeat details that have their own
source of truth:

| Topic | Source of truth |
|---|---|
| Rules and every number | `docs/architecture/04-game-rules.md` (values live in `content/tuning.json`) |
| Messages and fields | `docs/protocol.md` |
| Diagrams of every part | `docs/architecture/` |
| What is decided, what is open, what is next | `PROGRESS.md` |

---

## 1. Intent

**What:** an original multiplayer social-deduction game with a recycling theme, built for the
Green Hackathon (Swedish-Tunisian Games and Innovation Days, El Ghazala, Nov 21-22, 2026).
Theme: recycling and waste management.

**Why:** win the hackathon with a game the judges play **together, several people in the same
round**, on their own devices, plus an AI angle that makes the team stand out.

**The game in one line:** citizens clean up a neighbourhood with eco mini-games while two
hidden **polluters** secretly dump waste, poison citizens and sabotage; citizens find clues,
call meetings and vote the polluters out before the pollution meter fills.

**Success at the event:**
1. 4-8 judges join one room from their phones and laptops (room code) and finish a full round.
2. Empty seats are filled by AI players so a round works with any number of humans (1 to 8).
3. The same game runs as a **web game**, a **Windows app** and an **Android app**.
4. At least one AI feature works live, with an offline fallback when Wi-Fi fails.

## 2. Goals, non-goals, anti-goals

**Goals**
- Real-time multiplayer, 1-8 humans per room, AI bots fill the rest.
- One client codebase exported to Web, Windows and Android.
- Reuse the tested rule components of `reference/sim` instead of rewriting them.
- Data-driven content (tasks, maps, strings, tuning) so designers and the AI developer add content without code changes.

**Non-goals (for the event)**
- Accounts, friends, matchmaking lists, cosmetics store, ads, iOS app (iPhones use the web build).
- More than one polished map.
- Voice chat. (Typed chat was added later: 120 characters, 5 lines per meeting, word filter.)

**Anti-goals (failure even if it "works")**
- A round that breaks when the venue Wi-Fi drops (must have a plan B, see section 9).
- Anything recognisable from the reference game: names, map layouts, art, sounds, text. Rules and pacing only.
- Three separate codebases.
- AI that blocks the game while waiting for a model.
- Bots that ignore what the humans say: bots usually hold most of the votes, so a round where
  they cannot be convinced is not a game.
- A judge who is out early and has nothing to do for minutes (open decision, section 11).

## 3. Approaches considered

| # | Approach | Trade-off | Verdict |
|---|---|---|---|
| A | **Godot 4 client (GDScript) + .NET server built on the C# `sim` rule components** | Godot exports Web + Windows + Android from one project; the server reuses tested rules. Two languages (GDScript, C#) | **Picked** |
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
   +-----------------------------------------------+
   | Uncycled.Server  (ASP.NET Core, .NET 10 LTS)  |
   |  RoomManager -> Room (one per room code)      |
   |    tick loop 20 Hz, one input queue           |
   |    MatchEngine (Sim, engine-free C# rules)    |
   |    Bots       (AI seats, same commands)       |
   |    AI service (LLM provider + offline)        |
   |  one per-seat view for humans and bots        |
   +-----------------------------------------------+
            |  HTTPS (keys in env vars, server only)
            v
        LLM / vision provider (swappable)
```

### 4.1 Authority model
- **Movement: client-authoritative with sanity checks.** The client moves its own character with
  Godot physics and sends its position 20 times per second. The server applies a generous
  distance budget and sweeps each move against the map grid, so nobody passes through walls.
  It is lenient on purpose: an honest player on a bad connection must never be snapped back.
- **Interactions need range and line of sight.** The server checks distance *and* line of sight
  on the map grid, so nobody can use, report, hit or clean through a wall.
- **One unit scale everywhere:** 1 world unit = 1 tile = 32 px. Godot converts at the edge
  (`Net`); the server and `Sim` only see tile units.
- **Everything else: server-authoritative.** Clients send *requests*; the server validates with
  `Sim` (range, cooldown, phase, status) and sends *results*.
- **Hidden information stays on the server.** One function, `MatchEngine.ViewFor(seat)`, decides
  what a seat may know. Humans receive it as a snapshot; bots read it directly. Only polluters
  learn who the polluters are.

### 4.2 Seats and bots
A room has 8 **seats**. Each seat is controlled by a human connection or by a **bot brain**.
Bots produce the same commands as humans, go through the same validation and see the same
per-seat view, so the rules never know the difference. Seats are always filled to 8, so role
assignment always has enough players.

Disconnects: when a human drops, a bot takes over the seat **and the seat stays active**. Do not
apply `Sim`'s `Disconnected` state (it removes the player from living counts and votes). The
client keeps a reconnect token (`user://`); reconnecting during the match gives the human the
seat back, in whatever phase the match is in.

### 4.3 Server modules (`server/`)
| Module | Job |
|---|---|
| `Sim/` (vendored part) | The rule components we reuse from `reference/sim`, renamed to our theme. Only about a third of that library is vendored; the list is in `docs/architecture/02-server.md` section 1.1 |
| `Sim/Match`, `Sim/Uncycled`, `Sim/World` | **New engine-free code:** `MatchEngine` (phase flow, command dispatch, seat views), poison, piles, clues, pollution meter, task ledger, win order, map grid + line of sight + A*. Kept inside `Sim` so the whole game runs in unit tests and bot soak tests without a server |
| `Sim.Tests/` | Vendored xunit tests (trimmed) + tests for every new rule |
| `Uncycled.Server/Net` | WebSocket endpoint, message (de)serialisation, rate limit |
| `Uncycled.Server/Rooms` | Room codes, lobby, the room loop, seats, snapshot writing, event routing |
| `Uncycled.Server/Bots` | Bot brains (citizen, polluter), memory, testimony, vote policy |
| `Uncycled.Server/Ai` | `IAiProvider` with `OfflineProvider` + `RemoteProvider`, deadlines per request kind, never awaited inside the tick |
| `content/` (repo root) | JSON read by the server: tuning, task catalog, maps, phrases, facts. The server sends the client what it needs in `welcome`, so both sides never disagree |

### 4.4 Client modules (`client/`, Godot 4.7 standard edition, GDScript)
| Folder | Job |
|---|---|
| `autoload/` | `Net` (WebSocket + message signals), `Session` (local mirror of server state), `Settings`, `Assets` |
| `scenes/` | boot, menu, lobby, round, meeting, results |
| `world/` | map scenes, player scene, camera, vision, interpolation of remote players (100 ms buffer) |
| `ui/` | HUD, virtual joystick, action buttons, task list, pollution meter |
| `minigames/` | one scene per mini-game type; a finished step sends `task_step` |
| `assets/` | designers' art and audio (placeholders until real art) |
| `i18n/` | Godot CSV translations (en, fr); no player-facing text in code |

Screen: landscape 16:9, logical 640x360, stretch mode `canvas_items`, aspect `expand`.
Touch first: on-screen joystick + buttons, min 48 px targets. Keyboard for desktop.

### 4.5 Protocol
JSON over WebSocket, one object per message: `{"t": "<type>", ...}`. `docs/protocol.md` is the
single source both sides follow; the protocol version travels in `hello`, and a mismatch shows
"please update". Binary encoding is a later optimisation, not needed for 8 players.

### 4.6 Maps
The first real map is designed later. Contract now: a map is a Godot scene (source of truth)
plus an exported `content/maps/<id>.json` (walk grid, zones, consoles, spawns) that the server
loads for range checks, line of sight and bot path-finding. Collision in the scene must fill
whole tiles so the client and the server agree on where walls are. A headless Godot tool script
writes the JSON. Map ideas are original; reference-game data is used only for scale (number of
rooms and consoles, walk times).

## 5. Rule mapping (reference rules -> Uncycled)

The exact rules are in `docs/architecture/04-game-rules.md`. This table only shows where each
one comes from.

| Reference concept | Uncycled | Code |
|---|---|---|
| Two teams, hidden roles | Citizen / Polluter (2 of 8) | `RoleAssigner` (reused) |
| Tasks, win by tasks | Eco mini-games, task bar shown at meetings | `TaskAssigner` (reused) + new `TaskLedger` |
| Kill | **Poison:** a covert hit adds 34%; three hits and the citizen is out and leaves a body pile | `KillValidator`, `KillTimerRules` (reused, renamed) + new `PoisonRule` |
| (new) | **Dump:** raises the pollution meter and leaves a pile with a clue; visible to anyone looking | new |
| (new) | **Pollution meter:** the round's clock; cleaning piles lowers it | new |
| (new) | **Clues and signatures:** zone, time, and a waste type that links piles to one polluter | new |
| Report / emergency meeting / vote | Same; most votes kicks a player out; timed-out votes count as skip | `MeetingSession` (reused, two edits) + new `PileReport` |
| Meeting talk | Humans chat by phrases or typed text; bots post phrases; humans tap phrases to tell the bots | new `Bots/Testimony`, `Bots/VotePolicy` |
| Critical sabotage | Landfill fire: two players hold two consoles | `HoldMeltdown`, `SabotageCooldown` (reused) |
| Lights sabotage | Smog: citizen vision shrinks | new `SmogSystem` |
| Win checks | Polluters out -> meter full -> parity -> tasks done | new `UncycledWin` |

What `Sim` really gives us: tested rule **components** (role and task assignment, hit
validation, cooldowns, meeting and vote tally, the two-console sabotage). What it does **not**
give us: a match loop, command dispatch, task-completion validation, movement validation,
seat views, bots, dumps, the meter and clues. Those are new code in `Sim/Match`, `Sim/Uncycled`,
`Sim/World` and `Uncycled.Server/Bots`.

Originality rule: when vendoring, rename every reference-game term. Most of those names live in
files we do not vendor at all.

## 6. AI features

The team is still choosing. The architecture supports any of them because all AI runs **on the
server** (keys never reach a client) behind `IAiProvider`:
- An offline version always exists (rule-based or scripted JSON) and is ready first; an AI
  result only replaces it if it arrives before that request kind's deadline.
- Calls are async; the room tick never waits.
- Vendor-swappable (no vendor types outside `Ai/`).

The bots are the main AI story for the pitch: they see only what their seat sees, remember,
lie when they are polluters, and listen to what people tell them. On top of that the cheapest
visible LLM features are natural-language bot lines and an eco-coach debrief. Details and the
other candidates: `docs/architecture/06-bots-and-ai.md`.

## 7. Repo layout

```
EcoSus-Uncycled/
├── AGENTS.md            # rules for every coding agent
├── CLAUDE.md            # imports AGENTS.md + Claude-only notes
├── PROGRESS.md          # done / next / known issues / decisions
├── README.md
├── docs/
│   ├── CONTEXT.md       # one-file brief to paste into any chatbot
│   ├── protocol.md      # message schema (source of truth)
│   ├── architecture/    # diagrams and rules (04 = rules source of truth)
│   └── superpowers/specs/  # this design doc
├── content/             # JSON read by the server: tuning, tasks, maps, phrases, facts
├── server/
│   ├── Uncycled.sln
│   ├── Sim/  Sim.Tests/  Uncycled.Server/  Uncycled.Server.Tests/
│   └── Dockerfile
├── client/              # Godot project (project.godot, export_presets.cfg)
└── reference/        # separate repo, cloned here, gitignored, read-only reference
```

## 8. Phases

No dates (the team plans those). Each phase ends with a demo that proves it. Risky things first.

| Phase | Goal | Done when |
|---|---|---|
| **P0 Foundations + spikes** | Prove the 3 platforms can talk to a hosted server | Event rules checked (pre-existing code, AI-made assets); Godot 4.7 + export templates and the .NET 10 SDK installed; repo layout in place; the needed `sim` files vendored and renamed, `dotnet test` green; a hello client built for **Web, Windows and Android** connects via `wss://` to the deployed echo endpoint, tested on a **real Android phone browser, an iPhone Safari and the APK**; the plan-B LAN setup tried once (section 9) |
| **P1 Rooms + movement** | Several people in one room | Create/join by room code; 4+ devices (phone browser, APK, Windows) see each other move smoothly on a **greybox map at final scale**; map JSON export + server walk grid + line of sight; reconnect gives the seat back |
| **P2 First playable round** | A full round end to end, every MUST rule | Roles, 2 mini-game types with server validation, dump + meter + cleaning, poison, report, meeting with Ready and vote, ejection, wins, results screen; bots fill empty seats, play, vote and follow accusations |
| **P3 Recycling identity** | It is *our* game | Clues + signatures, landfill fire, bot testimony phrases, debrief with a real recycling fact |
| **P4 AI feature** | The AI angle | Chosen AI feature live with offline fallback. **The AI dev spikes it from P1 on**, so P4 only integrates it |
| **P5 Content + polish** | Looks and feels finished | Real art on the greybox layout, sound, EN/FR strings, joystick feel, 30 FPS on a mid-range Android browser |
| **P6 Event ready** | Nothing breaks on stage | Web build hosted, APK + Windows zip downloadable, server deployed + plan B rehearsed, demo script, 3 full playtests |
| **Event** | Bug fixes only | Feature freeze holds |

### 8.1 Who builds what in parallel

Two contracts let three developers work at the same time without waiting for each other:
`docs/protocol.md` (client <-> server) and the `MatchEngine` API (`Apply`, `Tick`, `ViewFor`).

| Track | Owner | P0-P1 | P2 |
|---|---|---|---|
| Server shell | Lead | `Net`, `Rooms`, deploy with `wss://`, exports of the hello client | Wire `MatchEngine` into the room loop, snapshots, reconnect |
| Rules engine | AI dev | Vendor `sim`, `Sim/World` (grid, line of sight, A*), `MatchEngine` skeleton, all in unit tests | Rules, then bots (they only need the engine, not the client) |
| Client | Gameplay dev | `Net` autoload, menu, lobby, movement + interpolation, joystick | HUD, 2 mini-games, meeting screen, results |
| Art | Designers | Greybox map layout at final scale, UI wireframes | Characters, tiles, UI kit, in the asset folders |

### 8.2 The cut rule

Scope tiers (MUST / SHOULD / COULD) are in `docs/architecture/04-game-rules.md` section 11.
Checkpoints, as a share of the time left before the event:
- by **25%**: P0 and P1 done (devices in one room, moving);
- by **60%**: P2 done (a full round with bots);
- at **85%**: feature freeze; only bug fixes, playtests and the demo rehearsal after that.

If a checkpoint is missed, drop COULD, then SHOULD from the bottom up. Never cut a MUST to keep
a SHOULD, and never cut a platform test to add a feature.

## 9. Deployment and demo risks

| Risk | Plan |
|---|---|
| Web build on `https` cannot open plain `ws://` (mixed content) | The server must have TLS: host on a platform that gives `wss://` (Fly.io / Render / Railway / VPS + Caddy). Decide in P0 |
| Rooms live in memory | Run **exactly one server instance** (no autoscaling; some hosts route new connections to other instances). The client pings every 2 s and reconnects automatically with its token |
| Free hosting sleeps | Use an always-on plan; wake the server before the demo; `/health` endpoint |
| Venue Wi-Fi fails | **Plan B1:** devices switch to mobile data / a phone hotspot and keep using the cloud server (no change). **Plan B2:** server on a laptop, APK and Windows app connect to `ws://<laptop-ip>`; a new round starts (no state transfer). Web over a LAN `http` address may hit Godot's secure-context limits: test it in P0, and if it fails the B2 web fallback is dropped |
| Godot web on phones | Export **without threads** (no special headers needed). Web needs WebAssembly + WebGL 2 and the Compatibility renderer; Safari has known caveats. Test Android Chrome and iPhone Safari every phase. Bring 2-3 spare Android phones with the APK so judges are never blocked |
| Android export toolchain | Android SDK and JDK 21 exist on the lead's PC; Godot recommends 17 but supports newer, confirm with Godot 4.7 in P0. Debug keystore only; never commit keystores. Enable the `INTERNET` permission |
| Missing data in builds | Godot does not pack `.json` by default: add `*.json` to each preset's non-resource export filter |
| Audio on web | Start audio only after the first tap |
| Event rules | Check that pre-existing code (our `sim` library) and AI-made assets are allowed, and say so openly in the pitch |

## 10. Testing

- `server/Sim.Tests`: table-driven xunit, one test per rule and a regression test per bug.
  Vendored tests must stay green. New rules get tests first.
- Seat views: tests that no view ever contains what its seat may not know.
- Protocol: round-trip test per message type on the server.
- Bots: a headless soak test (8 bots, 20 matches, no exception, every match ends).
- Client and mini-games: verified by playing on a phone, not unit tests.

Details: `docs/architecture/10-testing.md`.

## 11. Decisions

Decided by the owner (2026-11-13): multiplayer with judges in one round; Godot + .NET; Web,
Windows, Android; 8 seats, 2 polluters; vote kick-out; poison hits; free dump + witness; humans
chat by phrases or typed text and bots use phrases; 60 s talk + 30 s vote.

Open, most important first:
1. **What an out player does.** Today: a waiting screen until the round ends (the owner said an
   out player "can't play"). Recommended alternative: out players keep doing tasks and cleaning
   as invisible volunteers. It keeps judges playing and removes task reassignment.
2. **Round length.** With 60 s + 30 s meetings a round lasts 6 to 10 minutes. For about 6
   minutes in total, shorten meetings or speed up the meter.
3. Confirm the proposed rules in `04-game-rules.md` (covert poison, meter as the clock, Ready
   button, bot vote weights, emergency cap, task bar at meetings only).
4. Core and stretch AI feature.
5. Map setting and layout (campus, souk, beach, neighbourhood...).
6. Default language (EN or FR).
7. Server host (Fly.io, Render, Railway, VPS) and web host (itch.io or GitHub Pages).
8. Should at least one polluter always be a human when two or more humans play? (Default: random.)

## 12. Calls made in this design (check these)

- Client-authoritative movement with a lenient server check.
- JSON over WebSocket, not binary or UDP, for the event.
- AI runs inside the game server, not a separate proxy.
- Only the needed parts of `sim` are vendored (copied) into this repo, so teammates clone one
  repo and rename less.
- The match engine and the per-seat view live in `Sim` (engine-free), not in the server project.
- Bots replace disconnected humans without marking the seat disconnected; humans can reclaim it.
- 1 world unit = 1 tile = 32 px on both sides; map collision fills whole tiles.
- One server instance, in-memory rooms; plan B2 starts a fresh round instead of moving state.
- Godot **4.7** (latest stable line), standard edition, pinned for the whole team.
- Server on **.NET 10 LTS** (.NET 9 support ended on Nov 10, 2026).
- Bots are shown as bots in the player list.
- Joining is by a typed 4-letter code; a single QR code points to the web build.

## 13. Review 1: challenges and resolutions (2026-11-13)

An adversarial review of the first draft raised 25 issues. The main ones and how they ended:

| Issue | Resolution |
|---|---|
| The meter filled in about 2.5 min | Rebalanced (see review 2 for the final numbers) |
| Meetings could chain | All fresh piles are reported at meeting start; emergencies capped at 2 per match |
| Two polluters could stack poison | 34% per hit (3 hits), 25 s hit cooldown, 25 s per-victim immunity |
| Movement check allowed about 8 tiles/s and wall clipping | Distance budget refilled by server time + swept segment on the grid |
| Snapshots leaked players through walls | The server filters by vision **and** line of sight; last visible position kept 0.3 s |
| Cleaning had no protocol | `clean_start` / `clean_cancel` / `clean_done`, server-timed |
| Reconnect state undefined | `welcome.state` covers every phase; reconnect is a MUST |
| Ejecting an idle citizen could win by tasks | Task totals fixed in steps; orphaned tasks reassigned |
| Timed-out votes let 2 polluters decide | Timed-out votes count as skip |
| The task bar exposed fake tasks | Task bar published only at meetings |
| Spectators could scout and talk | Spectators receive no positions until game over |
| `Sim`'s round clock resets at meetings | Three clocks: MatchTime, ActiveTime, SinceMeeting |
| Fire holders, sabotage cooldown semantics | Holders revalidated each tick; cooldown counts from sabotage end |
| Queues unbounded, frames vs messages | Latest-snapshot slot + capped event queue; bounded inbox; 4 KB per whole message |
| .NET 9 out of support | .NET 10 LTS |
| Scope too big | MUST / SHOULD / COULD tiers |

## 14. Review 2: a full re-read of every file (2026-11-14)

A second pass read all the docs against each other and against the `sim` source. It found
contradictions between docs, claims about `sim` that the code does not support, and design
holes the first review missed.

| Finding | Fix |
|---|---|
| A 10-minute deadline counted meetings, so honest play (three meetings) often lost on time, and the meter could not fill before it. Cleaning piles became pointless | The **meter is the clock**: it rises by itself only during play. A 15-minute cap remains as a safety net |
| A hit told the victim who the attacker was, so every hit meant an accusation; polluters would never use poison | **Covert poison:** the hit shows nothing and lands 5 s later; the victim only knows who was nearby |
| Bots hold most votes but could not hear humans, and heard evidence could never reach their vote threshold. Three humans who saw a dump could not get the polluter ejected | Humans tap accusation phrases; bots score them; one eyewitness phrase is enough to move a bot |
| Remote players would move in a stop-go pattern (moves at 10 Hz, snapshots at 20 Hz) | Moves at 20 Hz |
| Merging queued moves after a lag spike would cut corners and snap honest players back | Moves applied in order; a more generous distance budget |
| Hit range of 1.0 tile was too tight with network delay | 1.8 tiles on the server (the reference default), button enabled at 1.5 on the client |
| Bots had a separate "perception" and a server-side witness list: two filters to keep in sync | One per-seat view (`MatchEngine.ViewFor`) for humans and bots; bot memory replaces the witness list |
| "Reuse `sim` as-is": the task assigner de-duplicates by type (2 mini-games would give 2 tasks), `ReportValidator` has no line-of-sight check, `WinChecker` was listed as reused and not reused | Task catalog of **entries**; `PileReport`; `UncycledWin`; an explicit vendor list (about a third of `sim`) |
| Design spec still said 15 moves/s, ping every 10 s, `task_complete`, witness names in clues, engine in the server project | Spec rewritten; it now points to the sources of truth instead of repeating them |
| Protocol gaps: no roster, no host migration, no Ready, the hit cue named the attacker, mismatched field names between docs | Protocol updated; sequences use its exact names |
| 60 s discussions with nothing to say | Ready button ends the discussion early |
| Only citizens could hold fire consoles (a tell for polluters) | Any active player may hold |
| AI lines "in the player's language" although one line is shown to all; a 3 s timeout would drop most LLM answers | Room language; deadlines per request kind, offline line always ready first |
| Map collision could differ between Godot and the server grid | Collision must fill whole tiles; the export tool reads the same layer |
| The freshness check for map JSON relied on file dates | SHA-1 of the scene stored in the JSON |
| No one checked the event's rules on pre-existing code | Added to P0 |
| Poison (a MUST) was missing from every phase; cleaning was optional although it is the theme | Both are in P2 and in the MUST tier |
| Nothing said who builds what first | Parallel tracks and a cut rule (section 8) |
