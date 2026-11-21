# 05 - Sequences

Message names match [../protocol.md](../protocol.md). "Room" means the room actor loop; "Engine"
means `MatchEngine` inside it.

## 1. Create, join, start

```mermaid
sequenceDiagram
    actor H as Host player
    actor J as Joining player
    participant C1 as Host client
    participant C2 as Join client
    participant WS as Server /ws
    participant RM as RoomManager
    participant R as Room
    C1->>WS: hello {proto, client}
    WS-->>C1: hello_ok {server_version}
    H->>C1: Create room
    C1->>WS: create {name}
    WS->>RM: Create
    RM->>R: new Room(code), seat 0 = host
    R-->>C1: welcome {room, seat:0, token, tuning, lobby}
    J->>C2: Enter code ABCD
    C2->>WS: join {code, name}
    WS->>RM: TryJoin
    RM->>R: enqueue Join
    R-->>C2: welcome {room, seat:1, token, ...}
    R-->>C1: lobby {seats}
    H->>C1: Start
    C1->>WS: start
    R->>R: fill free seats with bots, assign roles, tasks, signatures
    R-->>C1: match_started {role, partner?, signature?, tasks, spawn}
    R-->>C2: match_started {role, tasks, spawn}
```

## 2. Movement and snapshots

```mermaid
sequenceDiagram
    participant C as Client
    participant R as Room
    participant E as Engine
    loop every 100 ms
        C->>R: move {pos, seq}
    end
    loop every 50 ms (tick)
        R->>E: Apply(seat, Move)
        alt speed or walkable check fails
            E-->>R: rejected
            R-->>C: correct {pos}
        end
        R->>E: Tick(dt)
        R-->>C: snapshot {tick, t, visible players, piles, meter, timers}
    end
```

## 3. Doing a task

```mermaid
sequenceDiagram
    actor P as Citizen
    participant C as Client
    participant R as Room / Engine
    P->>C: walks to console, taps Use
    C->>R: use {console}
    R->>R: in range + LOS + task assigned at this console?
    R-->>C: use_ok {console, kind: task, task, type, step}
    C->>C: open minigame
    P->>C: completes minigame
    C->>R: task_step {task}
    R->>R: still in range, step order valid, min duration elapsed
    R-->>C: task_progress {task, steps_done}
    R->>R: win check (all task steps?)
    Note over R: task bar is only published at the next meeting (MeetingsOnly)
```

## 4. Polluter hit (poison)

```mermaid
sequenceDiagram
    actor X as Polluter
    participant C as Polluter client
    participant R as Room / Engine
    participant V as Victim client
    X->>C: taps Hit near a citizen
    C->>R: hit {target}
    R->>R: KillValidator (cooldown, range, LOS, phases) + victim immunity window
    alt rejected
        R-->>C: action_rejected {action: hit, reason}
    else accepted
        R->>R: poison +34%, cooldown 25 s, victim immune 25 s, record witnesses
        R-->>V: poisoned {level}
        R-->>C: hit_ok {cooldown}
        R-->>V: cue {kind: hit, seat, pos} (and to every seat with vision + LOS)
        opt poison reaches 100%
            R->>R: victim Out, body pile created, unfinished tasks reassigned
            R-->>V: you_are_out {cause: poison}
            R->>R: win check
        end
    end
```

## 5. Dump and witnesses

```mermaid
sequenceDiagram
    actor X as Polluter
    participant R as Room / Engine
    participant B as Bot brains
    X->>R: dump
    R->>R: cooldown ok, not in meeting
    R->>R: create pile {zone, MatchTime, signature}, meter +5%
    R->>R: witnesses = active seats with vision + LOS on X
    R->>B: observation (same rule as the cue humans get)
    Note over R: seats with vision + LOS get cue {kind: dump, seat, pos} and see the pile in snapshots
    R->>R: win check (meter)
```

## 5b. Cleaning a pile

```mermaid
sequenceDiagram
    actor P as Citizen
    participant C as Client
    participant R as Room / Engine
    P->>C: holds Clean near a dump pile
    C->>R: clean_start {pile}
    R->>R: active citizen, pile is a dump pile, in range + LOS, nobody else cleaning it
    R-->>C: clean_ok {pile, duration: 3}
    alt player moves out of range / meeting / out / releases
        C->>R: clean_cancel {pile} (or server detects it)
        R-->>C: clean_cancelled {pile}
    else 3 s pass, still valid
        R->>R: pile Cleaned, meter -2.5%
        R-->>C: clean_done {pile}
    end
```

## 6. Report, meeting, vote, ejection

```mermaid
sequenceDiagram
    actor A as Reporter
    participant R as Room / Engine
    participant All as All clients
    participant B as Bot brains
    A->>R: report {pile}
    R->>R: PileReport (fresh, range + LOS, phase)
    R->>R: all fresh piles -> Reported, fire cleared, clean/minigames cancelled
    R-->>All: meeting_started {by, cause, clues: [zone, t, waste?, victim?], task_bar}
    R->>B: meeting context
    B->>R: chat_phrase {phrase_id, args} (testimony, 0-2 per bot)
    R-->>All: chat {seat, phrase_id, args}
    Note over All: humans talk out loud, 60 s
    R-->>All: voting_started (30 s)
    All->>R: vote {target or skip}
    B->>R: vote {target or skip}
    R-->>All: vote_cast {seat} (who voted, not for whom)
    R->>R: MeetingSession.Tally (timed-out votes count as skip)
    R-->>All: meeting_result {votes, ejected?, was_polluter?}
    alt someone ejected
        R-->>All: ejection {seat, was_polluter}
    end
    R->>R: win check
    alt game continues
        R-->>All: round_resumed {spawns}
    else game over
        R-->>All: game_over {winner, reason, roles, timeline}
    end
```

## 7. Sabotage: landfill fire

```mermaid
sequenceDiagram
    actor X as Polluter
    participant R as Room / Engine
    actor C1 as Citizen 1
    actor C2 as Citizen 2
    X->>R: sabotage {fire}
    R->>R: SabotageCooldown ok (30 s since last sabotage ended), HoldMeltdown.Start (45 s)
    Note over R: every tick: holders revalidated (range, LOS, active), released if invalid
    R-->>C1: sabotage_started {fire, countdown}
    R-->>C2: sabotage_started {fire, countdown}
    C1->>R: fix {console: A, holding: true}
    C2->>R: fix {console: B, holding: true}
    R->>R: both held -> Clear()
    R-->>C1: sabotage_ended {fire, fixed}
    R-->>C2: sabotage_ended {fire, fixed}
    Note over R: if countdown expires: meter +20%, sabotage_ended {expired}
```

## 8. Disconnect, bot takeover, reconnect

```mermaid
sequenceDiagram
    participant C as Client
    participant WS as Connection
    participant R as Room
    participant Bot as BotBrain
    C--xWS: socket closes (Wi-Fi drop)
    WS->>R: enqueue Disconnected(seat)
    R->>Bot: attach to seat (BotHoldingForHuman)
    Note over R: seat stays active for all rules
    C->>WS: hello + join {code, token}
    WS->>R: enqueue Rejoin(token)
    R->>R: token matches seat, close any old socket of that seat
    R->>Bot: detach
    R-->>C: welcome {seat, state: phase, role, partner?, tasks, status, poison?, cooldowns, meeting?, sabotage?}
    Note over C: client opens Round, Meeting or Results from state.phase
    R-->>C: snapshot (next tick)
```

## 9. AI request with fallback

```mermaid
sequenceDiagram
    participant R as Room loop
    participant S as AiService
    participant Rem as RemoteProvider
    participant Fb as OfflineProvider
    R->>S: Request(testimony for bot 5) (fire and forget)
    Note over R: tick continues, bot shows "thinking..."
    S->>Rem: HTTPS call (timeout 3 s)
    alt success in time
        Rem-->>S: text
        S->>S: length cap, phrase filter
    else timeout / error / AI_PROVIDER=offline
        S->>Fb: same request
        Fb-->>S: scripted phrase
    end
    S->>R: enqueue AiResult
    R->>R: next tick: bot posts the line
```

## 10. Game over and debrief

```mermaid
sequenceDiagram
    participant R as Room / Engine
    participant All as All clients
    R->>R: win condition met
    R-->>All: game_over {winner, reason, roles, timeline, fact_id}
    All->>All: results scene: who was who, dumps and hits on a timeline, recycling fact
    Note over R: 15 s later or host request
    R-->>All: lobby {seats} (bots removed, humans keep seats)
```
