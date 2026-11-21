# 08 - Security, Hidden Information, Fairness

A social-deduction game is broken the moment a client can learn who the polluters are. The
server is the only place secrets live.

## 1. What each viewer receives

| Information | Own seat | Polluter partner | Other seats | Spectator (Out) |
|---|---|---|---|---|
| Own role | yes | - | - | yes |
| Who the polluters are | polluters only | yes | **never** | **never** (until game over) |
| Positions of others | within vision radius **and** line of sight (last visible position kept 0.3 s, never updated) | same rule | same rule | **none** until game over |
| Own poison level | yes | - | - | - |
| Others' poison level | never | never | never | never |
| Own tasks + progress | yes | - | - | - |
| Task bar (total %) | only at meeting start | same | same | same |
| Pollution meter | yes | yes | yes | yes |
| Piles | within vision + LOS | same | same | **none** |
| Hit / dump cue | only if the actor is within vision + LOS | same | same | none |
| Pile clues (zone, time, waste type or victim) | at meeting start, to everyone | | | |
| Witness records | **never sent** (bots only) | | | |
| Cooldowns | own only | own only | - | - |
| Vote targets during voting | only "has voted" | | | |
| All roles + timeline | at `game_over`, to everyone | | | |

Spectators get no map view at all mid-round: judges share a room, so anything a spectator sees
could be said out loud. They see meter, phase and meeting events, and everything at game over.

## 2. Validation per command (server)

| Command | Checks (all must pass) |
|---|---|
| `move` | phase allows moving; seat active; finite numbers; `seq` newer than the last accepted; **distance budget**: each seat has a budget refilled at `speed x 1.2` tiles per second of server time, capped at 1.5 tiles, and each move spends its length (over budget = `correct`); the segment from the last accepted position is swept cell by cell on the walk grid (no crossing walls) |
| `use` | active; console exists; in console range + LOS; for tasks: assigned to this seat and not done |
| `task_step` | an open `use` session for that task; still in range; min duration elapsed; step order |
| `hit` | `KillValidator.Validate` (polluter, active, cooldown, target active citizen, range, LOS, no meeting) + target outside its 25 s immunity |
| `dump` | polluter, active, cooldown, phase `Playing` |
| `clean_start` | citizen, active, dump pile, in range + LOS, nobody else cleaning it, phase `Playing`; completion re-checked server-side after 3 s |
| `report` | active; pile fresh; in range + LOS (`PileReport`); phase `Playing` (allowed during fire; the meeting clears it) |
| `call_meeting` | active; at emergency console; match-wide emergencies left; 15 s since last meeting; no fire |
| `vote` | phase `Voting`; seat active; not voted yet; target active or skip (`MeetingSession.CastVote`) |
| `sabotage` | polluter, active, shared cooldown (30 s since the last sabotage ended), phase `Playing` |
| `fix` | citizen, active, at the right console + LOS, sabotage active; fire holders revalidated every tick |
| `chat_phrase` | phase is meeting; phrase id exists; args are seat ids / zone ids only; 3 per meeting per seat |

Every rejection returns `action_rejected {reason}` to the sender only.

## 3. Threat model (event scope)

| Threat | Mitigation |
|---|---|
| Modified client reads hidden roles | Roles never sent (section 1) |
| Wallhack / ESP | Positions outside vision or behind walls not sent |
| Speed hack / teleport / wall clipping | Distance budget + swept segment check; `correct {pos}` |
| Action spam / flooding | 30 messages/s per connection, burst 60; 4 KB max per message (all frames); bounded room inbox; slow readers disconnected |
| Room guessing | 4-letter codes from 20 unambiguous letters (160k codes); rooms close when empty |
| Seat hijack | Reconnect token = random 128-bit GUID per seat, only sent to that seat |
| Offensive names | 12 chars, `[A-Za-z0-9 _-]`, simple banned-word list; no free-text chat at all |
| API key leak | Keys only in server env / `.env`; never in client builds or logs |
| Prompt injection via names | Names sanitised before prompts; AI output filtered and capped |
| Server crash loses rounds | Accepted for the event; container restarts automatically |

## 4. Privacy

- No accounts, no email, no analytics SDK. A display name and a random token, nothing else.
- Logs contain room codes, seat ids and errors only, never names.
- Camera images (only if the scanner feature is chosen) are sent to the AI provider and never
  stored.
