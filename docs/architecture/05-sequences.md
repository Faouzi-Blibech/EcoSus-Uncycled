# 05 - Sequences

Message names and fields match [../protocol.md](../protocol.md). "Room" means the room actor
loop; "Engine" means `MatchEngine` inside it.

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
    C1->>WS: hello {proto, client, build}
    WS-->>C1: hello_ok {server_build}
    H->>C1: Create room
    C1->>WS: create {name, lang}
    WS->>RM: Create
    RM->>R: new Room(code), seat 0 = host
    R-->>C1: welcome {room, seat, token, host_seat, tuning, task_types, map_id, map_version}
    R-->>C1: lobby {host_seat, seats}
    J->>C2: Enter code ABCD
    C2->>WS: hello, then join {code, name}
    WS->>RM: TryJoin
    RM->>R: enqueue Join
    R-->>C2: welcome {room, seat, token, ...}
    R-->>C1: lobby {host_seat, seats}
    R-->>C2: lobby {host_seat, seats}
    H->>C1: Start
    C1->>WS: start
    R->>R: fill free seats with bots, assign roles, tasks, signatures
    R-->>C1: match_started {role, partner?, signature?, roster, tasks, spawn}
    R-->>C2: match_started {role, roster, tasks, spawn}
```

## 2. Movement and snapshots

```mermaid
sequenceDiagram
    participant C as Client
    participant R as Room
    participant E as Engine
    loop every 50 ms while moving
        C->>R: move {pos, seq}
    end
    loop every 50 ms (tick)
        R->>E: Apply(seat, Move) for each queued move, in order
        alt distance budget or wall check fails
            E-->>R: rejected
            R-->>C: correct {pos}
        end
        R->>E: Tick(dt)
        R->>E: ViewFor(seat)
        R-->>C: snapshot {tick, s, me, visible players, piles, meter, sabotage}
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
    R->>R: in range + line of sight, a task of this seat has its next step here
    R-->>C: use_ok {console, kind, task, type, step}
    C->>C: open mini-game
    P->>C: completes the step
    C->>R: task_step {task}
    R->>R: still in range, step order valid, minimum duration elapsed
    R-->>C: task_progress {task, steps_done}
    R->>R: win check (all task steps done?)
    Note over R: the task bar is only published at the next meeting
```

A polluter's fake task follows the same messages; the ledger simply does not count it.

## 4. Polluter hit (covert poison)

```mermaid
sequenceDiagram
    actor X as Polluter
    participant C as Polluter client
    participant R as Room / Engine
    participant V as Victim client
    X->>C: taps Hit near a citizen
    C->>R: hit {target}
    R->>R: hit validation (cooldown, range, line of sight, phase) + victim immunity
    alt rejected
        R-->>C: action_rejected {action, reason}
    else accepted
        R->>R: cooldown 25 s, victim immune 25 s, schedule the hit to land in 5 s
        R-->>C: hit_ok {cooldown}
        Note over V: nothing is shown yet, nobody gets a cue
        R->>R: 5 s later the hit lands, poison +34%
        R-->>V: poisoned {level}
        opt poison reaches 100%
            R->>R: victim Out, body pile created, unfinished tasks reassigned
            R-->>V: you_are_out {cause}
            R->>R: win check
        end
    end
```

## 5. Dump and witnesses

```mermaid
sequenceDiagram
    actor X as Polluter
    participant R as Room / Engine
    participant W as Seats that can see X
    X->>R: dump
    R->>R: polluter, cooldown ok, phase Playing
    R->>R: create pile {zone, MatchTime, signature}, meter +5%
    R-->>X: dump_ok {pile, cooldown}
    R-->>W: cue {kind, seat, pos}
    Note over W: humans see the dump animation, bots get the same cue in their SeatView
    R->>R: win check (meter)
```

## 5b. Cleaning a pile

```mermaid
sequenceDiagram
    actor P as Player
    participant C as Client
    participant R as Room / Engine
    P->>C: holds Clean near a dump pile
    C->>R: clean_start {pile}
    R->>R: active seat, dump pile, in range + line of sight, nobody else cleaning it
    R-->>C: clean_ok {pile, duration}
    alt player moves out of range, a meeting starts, the player is out or releases
        C->>R: clean_cancel {pile} (or the server detects it)
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
    R->>R: PileReport (fresh, range + line of sight, phase)
    R->>R: all fresh piles become Reported, fire cleared, clean and mini-games cancelled
    R-->>All: meeting_started {by, cause, clues, task_bar, active, discussion_s}
    B->>R: chat_phrase {id, args} (testimony, up to 2 per bot)
    All->>R: chat_phrase {id, args} (a human taps a player card to accuse or clear)
    R-->>All: chat {seat, id, args, text?}
    Note over B: bots add every phrase they hear to their suspicion scores
    Note over All: humans also talk out loud, up to 60 s
    All->>R: ready
    R-->>All: ready_cast {seat}
    Note over R: voting starts at 60 s, or once every active human is ready (10 s minimum)
    R-->>All: voting_started {voting_s}
    All->>R: vote {target} or vote {skip}
    B->>R: vote
    R-->>All: vote_cast {seat}
    R->>R: tally (timed-out votes count as skip)
    R-->>All: meeting_result {votes, ejected?, tie}
    opt someone ejected
        R-->>All: ejection {seat, was_polluter}
        R-->>All: you_are_out {cause} (to the ejected seat only)
    end
    R->>R: win check
    alt game continues
        R-->>All: round_resumed {spawn}
    else game over
        R-->>All: game_over {winner, reason, roles, timeline, fact}
    end
```

## 7. Sabotage: landfill fire

```mermaid
sequenceDiagram
    actor X as Polluter
    participant R as Room / Engine
    actor C1 as Player 1
    actor C2 as Player 2
    X->>R: sabotage {kind}
    R->>R: sabotage cooldown ok, fire starts (45 s)
    R-->>C1: sabotage_started {kind, countdown}
    R-->>C2: sabotage_started {kind, countdown}
    C1->>R: fix {console, holding} at console A
    C2->>R: fix {console, holding} at console B
    Note over R: every tick, holders are revalidated (range, line of sight, active)
    R->>R: both consoles held, fire cleared
    R-->>C1: sabotage_ended {kind, result}
    R-->>C2: sabotage_ended {kind, result}
    Note over R: if the countdown expires instead, the meter gets +20% and result is expired
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
    Note over R: the seat stays active for all rules
    C->>WS: hello, then join {code, name, token}
    WS->>R: enqueue Rejoin(token)
    R->>R: token matches the seat, close any old socket of that seat
    R->>Bot: detach
    R-->>C: welcome {seat, phase, state}
    Note over C: the client opens Round, Meeting or Results from state.phase
    R-->>C: snapshot (next tick)
```

## 9. AI request with fallback

```mermaid
sequenceDiagram
    participant R as Room loop
    participant S as AiService
    participant Rem as RemoteProvider
    participant Fb as OfflineProvider
    R->>S: Request(bot line for seat 5), fire and forget
    Note over R: the tick continues and the plain phrase is ready to show
    S->>Rem: HTTPS call (deadline per kind, 6 s for a bot line)
    alt success in time
        Rem-->>S: text
        S->>S: length cap, filter
    else timeout, error or AI_PROVIDER=offline
        S->>Fb: same request
        Fb-->>S: scripted phrase
    end
    S->>R: enqueue AiResult
    R->>R: next tick, the bot posts chat {seat, id, args, text?}
```

## 10. Game over and debrief

```mermaid
sequenceDiagram
    participant R as Room / Engine
    participant S as AiService
    participant All as All clients
    R->>R: win condition met
    R-->>All: game_over {winner, reason, roles, timeline, fact}
    All->>All: results scene shows who was who, dumps and hits on a timeline, the recycling fact
    opt AI debrief feature on
        R->>S: Request(debrief)
        S->>R: AiResult within 10 s
        R-->>All: debrief {text}
    end
    Note over R: 15 s later or on host request
    R-->>All: lobby {host_seat, seats}
```
