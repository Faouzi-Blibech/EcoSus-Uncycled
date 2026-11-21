# 02 - Server

ASP.NET Core minimal API on .NET 10 (LTS). One process, one instance, rooms in memory.

## 1. Project layout

```
server/
├── Uncycled.sln
├── Sim/                         # netstandard2.1, C# 9, engine-free (vendored + extended)
│   ├── Core/                    # GameState, PlayerState, Vec2, IRandom, enums (renamed)
│   ├── Rules/                   # Kill (used for poison hits), Meeting, Win, Assign
│   ├── Systems/                 # Lights (smog), CriticalSabotage (fire), cooldowns
│   ├── Movement/                # speed formula, quantisation (later)
│   ├── World/                   # NEW: MapData, WalkGrid, GridLineOfSight, AStar
│   ├── Uncycled/                # NEW: PollutionMeter, Poison, Piles, Clues, Witnesses, UncycledWin
│   └── Match/                   # NEW: MatchEngine, commands, events, MatchConfig
├── Sim.Tests/                   # xunit, table-driven (vendored + new)
├── Uncycled.Server/             # net10.0
│   ├── Program.cs               # DI, config, endpoints
│   ├── Net/                     # WebSocket endpoint, Connection, JSON codec, rate limit
│   ├── Rooms/                   # RoomManager, Room (actor), Seat, SnapshotFilter, EventRouter
│   ├── Bots/                    # BotBrain, CitizenBot, PolluterBot, Perception, Testimony, VotePolicy
│   ├── Ai/                      # IAiProvider, OfflineProvider, RemoteProvider, AiService
│   └── Content/                 # ContentLoader (reads ../content or /app/content)
├── Uncycled.Server.Tests/       # protocol round-trips, filter tests, bot soak
└── Dockerfile
```

## 2. Components

```mermaid
flowchart LR
    ws["WsEndpoint<br/>/ws"] --> conn["Connection<br/>receive loop + send queue"]
    conn -- "parsed ClientMessage" --> rm["RoomManager<br/>codes, create/join, cleanup"]
    rm --> room["Room (actor)<br/>Channel&lt;RoomInput&gt;, tick loop"]
    room --> engine["MatchEngine (Sim)"]
    room --> bots["BotDirector<br/>one brain per bot seat"]
    room --> filter["SnapshotFilter<br/>per-seat view"]
    room --> router["EventRouter<br/>who gets which event"]
    room --> aisvc["AiService"]
    aisvc -- "AiResult as RoomInput" --> room
    filter --> conn
    router --> conn
    loader["ContentLoader"] --> rm
```

## 3. Threading model: one actor per room

All game state of a room is touched by **one loop only**. Everything else talks to it through a
queue. This removes locks and race conditions.

```mermaid
flowchart TB
    subgraph io["I/O threads (ASP.NET)"]
        r1["Connection A receive loop"]
        r2["Connection B receive loop"]
        ai["AI HTTP call completes"]
    end
    q[["Room inbox<br/>Channel&lt;RoomInput&gt;"]]
    subgraph loop["Room loop (single task, PeriodicTimer 50 ms)"]
        drain["1. drain inbox -> commands"]
        botsx["2. bots think -> commands"]
        tick["3. engine.Tick(dt) -> events"]
        route["4. route events + build snapshots"]
        enqueue["5. push to each connection's send queue"]
    end
    s1[["Send queue A"]]
    s2[["Send queue B"]]
    w1["Connection A send loop<br/>(one SendAsync at a time)"]
    w2["Connection B send loop"]

    r1 --> q
    r2 --> q
    ai --> q
    q --> drain --> botsx --> tick --> route --> enqueue
    enqueue --> s1 --> w1
    enqueue --> s2 --> w2
```

Rules:
- Network handlers **only enqueue**. They never read or write `GameState`.
- `WebSocket.SendAsync` must not run concurrently on one socket: each connection has its own
  send loop. Outbound has two lanes: a **latest-snapshot slot** (a new snapshot replaces an
  unsent one) and a **reliable event queue** capped at 256; a client whose event backlog passes
  the cap is disconnected (its seat goes to a bot and it can reconnect).
- Inbound is bounded: the room inbox holds at most 1,024 inputs; each tick drains at most 256,
  and `move` inputs are coalesced (only the latest per seat is applied).
- One JSON object per WebSocket **message** (frames are accumulated until `EndOfMessage`);
  total message size capped at 4 KB, a message taking longer than 5 s to arrive is dropped.
- AI calls run on the thread pool; their result comes back as a `RoomInput.AiResult`.
- A room loop that throws logs the error, ends the match with a "server error" event and closes
  the room. It never takes down other rooms.

## 4. Room lifecycle

```mermaid
stateDiagram-v2
    [*] --> Open: create (host joins)
    Open --> Open: join / leave / ready
    Open --> InMatch: host starts (>=1 human)
    InMatch --> Open: match ended, back to lobby
    Open --> Closed: no humans for 60 s
    InMatch --> Closed: no humans for 120 s
    Closed --> [*]
```

## 5. Class model (server side)

```mermaid
classDiagram
    class RoomManager {
        +Create(hostName) Room
        +TryJoin(code, name, token) JoinResult
        -Dictionary~string, Room~ rooms
    }
    class Room {
        +string Code
        +Seat[8] Seats
        +MatchEngine? Match
        +ChannelWriter~RoomInput~ Inbox
        -RunLoop()
    }
    class Seat {
        +byte Id
        +string Name
        +byte Color
        +SeatControl Control
        +Connection? Human
        +BotBrain? Bot
        +Guid ReconnectToken
    }
    class SeatControl {
        <<enumeration>>
        Empty
        Human
        Bot
        BotHoldingForHuman
    }
    class Connection {
        +Guid Id
        +SendQueue Out
        +Enqueue(ServerMessage)
    }
    class MatchEngine {
        +MatchPhase Phase
        +Apply(byte seat, Command) CommandResult
        +Tick(float dt) IReadOnlyList~GameEvent~
        +ViewFor(byte seat) SeatView
    }
    class BotBrain {
        <<abstract>>
        +Think(Perception p, float dt) Command[]
        +OnEvent(GameEvent e)
    }
    class SnapshotFilter {
        +Build(MatchEngine m, byte seat) Snapshot
    }
    class AiService {
        +Request(AiRequest r, CancellationToken ct)
    }
    RoomManager "1" o-- "*" Room
    Room "1" *-- "8" Seat
    Seat --> SeatControl
    Seat "0..1" --> Connection
    Seat "0..1" --> BotBrain
    Room "1" *-- "0..1" MatchEngine
    Room --> SnapshotFilter
    Room --> AiService
```

## 6. Match engine (inside `Sim`)

The engine is the whole game, engine-free. The server and the tests drive it the same way.

```mermaid
classDiagram
    class MatchEngine {
        +MatchConfig Config
        +GameState State
        +PollutionMeter Meter
        +PileRegistry Piles
        +WitnessLog Witnesses
        +EnvSystems Env
        +MeetingSession? Meeting
        +Apply(byte seat, Command c) CommandResult
        +Tick(float dt) List~GameEvent~
    }
    class Command {
        <<interface>>
    }
    class Move { Vec2 Pos; ushort Seq }
    class UseConsole { int ConsoleId }
    class CompleteTask { int TaskId }
    class Hit { byte Target }
    class Dump { }
    class Report { int PileId }
    class CallMeeting { }
    class CastVote { byte? Target }
    class Sabotage { SabotageKind Kind }
    class FixConsole { int ConsoleId; bool Holding }
    class CleanStart { int PileId }
    class GameEvent {
        <<interface>>
    }
    Command <|.. Move
    Command <|.. UseConsole
    Command <|.. CompleteTask
    Command <|.. Hit
    Command <|.. Dump
    Command <|.. Report
    Command <|.. CallMeeting
    Command <|.. CastVote
    Command <|.. Sabotage
    Command <|.. FixConsole
    Command <|.. CleanStart
    MatchEngine ..> Command : validates
    MatchEngine ..> GameEvent : emits
```

Mapping to reused `Sim` code (renamed on vendoring):

| Engine step | Reused | New |
|---|---|---|
| Assign roles (2 polluters of 8) | `RoleAssigner.Assign` | waste-type signature per polluter |
| Assign tasks | `TaskAssigner.Assign` | `TaskLedger` (step-based totals, reassignment of orphaned tasks; `WinChecker.CountTasks` not reused) |
| Validate `Hit` | `KillValidator.Validate` (range, LOS, cooldown, meeting) | `PoisonRule.Apply` (+34%, out at 100%, 25 s per-victim immunity) |
| Cooldowns | `KillTimerRules` (hit cooldown, reset after meeting) | dump cooldown (same pattern) |
| Report pile / emergency | `ReportValidator` (distance, emergency rules) | `PileReport` adapter: freshness, LOS, phase; match-wide emergency cap |
| Meeting + vote | `MeetingSession` (discussion, voting, results, tally) | `Missed` counted as skip in `Tally`; spectators get `Dead` vote kind |
| Smog sabotage | `LightsSystem.VisionRadius` formula only | `SmogSystem` with timed repair |
| Landfill fire | `HoldMeltdown` (2 consoles held) | per-tick holder revalidation; expiry feeds the pollution meter (see 04) |
| Clocks | - | `MatchTime`, `ActiveTime`, `SinceMeeting` (04 section 0) |
| Win check | `WinChecker.CountTasks`, parity logic | `UncycledWin` order with the pollution meter |

## 7. Snapshot pipeline (per tick, per seat)

```mermaid
flowchart LR
    state["Engine state"] --> vis["Visibility: seats and piles within<br/>vision radius AND grid line of sight;<br/>lost sight: last visible position kept 0.3 s, never updated"]
    vis --> role["Role mask: polluter ids<br/>only to polluters"]
    role --> mine["Private: my tasks, my poison,<br/>my cooldowns"]
    mine --> shared["Shared: meter, phase, timers,<br/>visible piles, sabotage state"]
    shared --> json["Serialize snapshot"]
    json --> q["Seat's send queue"]
```

Details of what each seat may see are in [08-security-and-fairness.md](08-security-and-fairness.md).

## 8. Configuration

| Key | Source | Default |
|---|---|---|
| `ASPNETCORE_URLS` | env | `http://0.0.0.0:8080` (container), `http://0.0.0.0:5000` (local) |
| `UNCYCLED_CONTENT_DIR` | env | `./content` |
| `UNCYCLED_MAX_ROOMS` | env | 20 |
| `AI_PROVIDER` | env | `offline` (`remote` to enable) |
| `AI_API_KEY`, `AI_MODEL`, `AI_BASE_URL` | env / `.env` (gitignored) | unset |
| `UNCYCLED_LOG_LEVEL` | env | `Information` |

## 9. Endpoints

| Path | Use |
|---|---|
| `GET /health` | `200 ok` + version; hosting probe and demo warm-up |
| `GET /stats` | rooms, seats, connections, tick time p95 (no personal data) |
| `GET /ws` | WebSocket upgrade; protocol in [../protocol.md](../protocol.md) |
