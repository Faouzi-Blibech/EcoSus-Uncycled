# 08 - Security, Hidden Information, Fairness

A social-deduction game is broken the moment a client can learn who the polluters are. The
server is the only place secrets live.

Scope: this is an event build. The players are judges and teammates, not attackers, so the
priorities are (1) never leak a secret by accident, (2) never punish an honest player on a bad
connection, (3) cheap protection against the obvious cheats.

## 1. What each viewer receives

All of this is decided in one place, `MatchEngine.ViewFor(seat)` (02 section 7). Bots get the
same view as humans.

| Information | Active seat | Out seat (spectator) |
|---|---|---|
| Own role | yes | yes |
| Who the polluters are | polluters only (their partner) | never, until game over |
| Positions of others | within vision radius + 0.5 tile **and** line of sight; last visible position kept 0.3 s, never updated | none until game over |
| Own poison level | yes (citizens) | - |
| Others' poison level | never | never |
| A hit | never shown to anyone; the victim learns 5 s later through its own bar | - |
| A dump | `cue` only if the polluter is within vision + line of sight | none |
| Piles | within vision + line of sight | none |
| Own tasks + progress | yes | - |
| Task bar (total %) | only at meeting start | same |
| Pollution meter | yes | yes |
| Own cooldowns | yes (polluters) | - |
| Pile clues (zone, time, waste type or victim) | at meeting start, to everyone | same |
| Phrases said in meetings | everyone | everyone |
| Typed chat lines in meetings (filtered, 120 characters) | everyone | everyone |
| Ghost chat (lines typed or phrases said by out seats, any time of the match) | never | yes (out seats only; the newest kept lines also come in an out seat's rejoin welcome) |
| Vote targets during voting | only "has voted" | same |
| All roles + timeline (dumps, hits) | at `game_over`, to everyone | same |

Spectators get no map view at all mid-round: judges share a room, so anything a spectator sees
could be said out loud.

## 2. Validation per command (server)

| Command | Checks (all must pass) |
|---|---|
| `move` | phase allows moving; seat active; finite numbers; `seq` newer than the last accepted; **distance budget**: refilled at `speed x 1.2` tiles per second of server time, capped at 3 tiles, each move spends its length (over budget = `correct`); the segment from the last accepted position is swept cell by cell on the walk grid (no crossing walls). Moves are applied in order, never merged |
| `use` | active; console exists; in console range + line of sight; for a task console: a task of this seat has its next step here |
| `task_step` | an open `use` session for that task; still in range; minimum duration elapsed; step order |
| `clean_start` | active; dump pile; in range + line of sight; nobody else cleaning it; phase `Playing`; completion re-checked by the server after 3 s |
| `hit` | hit validation (polluter, active, cooldown, target is an active citizen, range, line of sight, phase `Playing`) + target outside its immunity window |
| `dump` | polluter, active, cooldown, phase `Playing` |
| `report` | active; pile fresh; in range + line of sight; phase `Playing` (allowed during fire; the meeting clears it) |
| `call_meeting` | active; at the emergency console; match-wide emergencies left; 15 s since the last meeting; no fire |
| `chat_phrase` | discussion stage (out seats: any time, as ghost chat); phrase id exists; args are seat ids / zone ids only; 5 per meeting per seat (`phrase_limit`); 2 s between chat lines (`rate_limited`) |
| `chat_text` | raw text at most 1024 characters, else `bad_field`; discussion stage (out seats: any time, as ghost chat); normalised, 120 characters at most (`too_long`), 5 per meeting per seat (`text_limit`), 2 s between chat lines (`rate_limited`); listed words replaced by `***` |
| `ready` | discussion stage; seat active |
| `vote` | voting stage; seat active; not voted yet; target active or skip |
| `sabotage` | polluter, active, sabotage cooldown, phase `Playing`, no sabotage already active |
| `fix` | active (any role); at the right console + line of sight; that sabotage is active; fire holders revalidated every tick |

Every rejection returns `action_rejected {action, reason}` to the sender only.

The movement budget is deliberately generous (3 tiles of slack): a phone on a bad connection
sends its moves in bursts, and an honest player must never be snapped back. The wall sweep is
what actually matters, because it keeps the server's idea of "who can see whom" correct.

## 3. Threat model (event scope)

| Threat | Mitigation |
|---|---|
| Modified client reads hidden roles | Roles never sent (section 1) |
| Wallhack / ESP | Positions outside vision or behind walls not sent |
| Speed hack / teleport / wall clipping | Distance budget + swept segment check; `correct {pos}` |
| Action spam / flooding | 40 messages/s per connection, burst 80; 4 KB max per message (all frames); bounded room inbox; slow readers disconnected |
| Room guessing | 4-letter codes from 20 unambiguous letters (160,000 codes); rooms close when empty |
| Seat hijack | Reconnect token = random 128-bit GUID per seat, only sent to that seat |
| Offensive names | 12 chars, `[A-Za-z0-9 _-]`, simple banned-word list; typed chat is length-limited, rate-limited and word-filtered |
| API key leak | Keys only in server env / `.env`; never in client builds or logs |
| Prompt injection via names | Names sanitised before prompts; AI output filtered and capped |
| Server crash loses rounds | Accepted for the event; the container restarts automatically |

## 4. Privacy

- No accounts, no email, no analytics SDK. A display name and a random token, nothing else.
- Logs contain room codes, seat ids and errors only, never names.
- Camera images (only if the scanner feature is chosen) are sent to the AI provider and never
  stored.
