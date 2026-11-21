# 01 - System Context, Containers, Deployment

## 1. System context

Who and what talks to Uncycled.

```mermaid
flowchart LR
    judge(["Judge / player<br/>phone, laptop"])
    team(["Dev team<br/>3 developers"])
    designers(["Designers<br/>art, UI, audio"])

    subgraph uncycled["Uncycled system"]
        client["Game client<br/>Godot 4: Web, Windows, Android"]
        server["Game server<br/>.NET 10 (LTS), authoritative"]
    end

    llm["LLM / vision provider<br/>swappable, optional"]
    itch["itch.io / GitHub Pages<br/>hosts the web build"]
    gh["GitHub<br/>repo, releases, CI"]

    judge -- "plays (touch / keyboard)" --> client
    client -- "wss:// JSON" --> server
    server -- "HTTPS, keys server-side only" --> llm
    itch -- "serves web build" --> judge
    gh -- "APK + Windows zip downloads" --> judge
    team -- "code, builds" --> gh
    designers -- "assets via PR" --> gh
```

## 2. Containers

```mermaid
flowchart TB
    subgraph clients["Godot client (one project, 3 exports)"]
        web["Web export<br/>HTML5 + WASM, threads off"]
        win["Windows export<br/>.exe + .pck"]
        apk["Android export<br/>.apk (debug-signed)"]
    end

    subgraph srv["Game server process (single instance)"]
        api["Uncycled.Server<br/>ASP.NET Core: /ws, /health, /stats"]
        rooms["Rooms + Bots + AI service"]
        sim["Sim library (netstandard2.1)<br/>rules + match engine"]
        content[("content/*.json<br/>tuning, tasks, maps, phrases, facts")]
    end

    ai["LLM / vision provider"]

    web -- "wss" --> api
    win -- "wss (ws on LAN plan B2)" --> api
    apk -- "wss (ws on LAN plan B2)" --> api
    api --> rooms
    rooms --> sim
    rooms -. "loads at start" .-> content
    rooms -- "async HTTPS, 3 s timeout" --> ai
```

Key properties:
- The **server is the only authority** for roles, tasks, poison, piles, meetings, votes and wins.
- The client is presentation + input + mini-games. It never decides an outcome.
- Content JSON is read by the server; the client receives what it needs in `welcome`.
- No database: rooms live in memory and die with the process (acceptable for the event).

## 3. Code modules and dependencies

Arrows mean "depends on". Nothing may depend upward.

```mermaid
flowchart BT
    simcore["Sim.Core<br/>GameState, PlayerState, Vec2, IRandom"]
    simrules["Sim.Rules + Sim.Systems<br/>kill/poison, report, meeting, win, tasks, sabotage"]
    simuc["Sim.Uncycled<br/>pollution meter, piles, clues, witnesses, win order"]
    simmatch["Sim.Match<br/>MatchEngine: commands in, events out"]
    simnav["Sim.World<br/>MapData, walkable grid, line of sight, A*"]
    bots["Uncycled.Server/Bots<br/>bot brains, testimony"]
    aisvc["Uncycled.Server/Ai<br/>IAiProvider, offline + remote"]
    roomsm["Uncycled.Server/Rooms<br/>RoomManager, Room actor, SnapshotFilter"]
    net["Uncycled.Server/Net<br/>WebSocket endpoint, codec, Connection"]
    host["Uncycled.Server/Program.cs<br/>DI, config, endpoints"]

    simrules --> simcore
    simnav --> simcore
    simuc --> simrules
    simmatch --> simuc
    simmatch --> simnav
    bots --> simmatch
    bots --> aisvc
    roomsm --> simmatch
    roomsm --> bots
    roomsm --> aisvc
    net --> roomsm
    host --> net
```

Rules:
- Everything under `Sim.*` is engine-free and framework-free (no ASP.NET, no Godot, no clocks,
  no `System.Random`; only `IRandom`). It is fully unit-testable.
- `Bots` only call the same `MatchEngine` command API that humans reach through `Net`.
- `Ai` has no dependency on rooms; it returns results that the room feeds back as commands.

## 4. Deployment

### 4.1 Normal setup (event day)

```mermaid
flowchart LR
    subgraph venue["Venue"]
        phones["Judges' phones<br/>browser or APK"]
        laptops["Laptops<br/>browser or Windows app"]
        spare["2-3 spare Android phones<br/>APK preinstalled"]
    end
    subgraph cloud["Cloud host (one region close to Tunisia, e.g. Frankfurt/Paris)"]
        tls["TLS edge (platform-managed)"]
        app["Container: Uncycled.Server<br/>1 instance, always on"]
    end
    itch["itch.io web build"]
    llm["LLM provider"]

    phones -- "HTTPS (load game)" --> itch
    laptops -- "HTTPS (load game)" --> itch
    phones -- "wss://" --> tls
    laptops -- "wss://" --> tls
    spare -- "wss://" --> tls
    tls --> app
    app -- "HTTPS" --> llm
```

### 4.2 Plan B (venue Wi-Fi fails)

| Plan | Setup | What changes |
|---|---|---|
| B1 | Devices switch to mobile data or a phone hotspot | Nothing: same cloud server, same builds |
| B2 | Server runs on a laptop (`dotnet run`), all devices on the laptop's hotspot | APK and Windows app enter `ws://<laptop-ip>:5000` in Settings; a new round starts. Web over LAN `http` only if the P0 test passed |

```mermaid
flowchart LR
    hotspot["Phone or laptop hotspot"]
    laptop["Lead laptop<br/>Uncycled.Server :5000<br/>offline AI provider"]
    apk2["Spare phones (APK)"]
    win2["Laptops (Windows app)"]
    apk2 -- "ws://laptop-ip:5000" --> laptop
    win2 -- "ws://laptop-ip:5000" --> laptop
    hotspot --- laptop
```

## 5. Runtime numbers (defaults, tunable)

| Thing | Value | Why |
|---|---|---|
| Seats per room | 8 (2 polluters) | Decided |
| Server tick | 20 Hz (50 ms) | Enough for 8 players, cheap |
| Snapshot rate | 20 Hz, filtered per seat | Smooth with a 100 ms interpolation buffer |
| Client position send | 10 Hz (`Sim` `MovementSender.SendInterval = 0.1`) | Matches the reference pacing |
| Walk speed | 2.5 tiles/s (`MovementSpeed.BaseSpeed`) = 80 px/s | Reference value, playtest later |
| Max rooms per instance | 20 | Event needs 1-3 |
| Message size cap | 4 KB | No free text exists |
