# 10 - Testing

Tests catch real regressions; they do not exist to look thorough.

## 1. What gets tested where

| Layer | How | Examples |
|---|---|---|
| `Sim` rules (vendored) | the vendored xunit tests, trimmed to the vendored code; must stay green after renaming | vote tally ties, hit validation order, task assignment |
| `Sim.Uncycled` rules (new) | table-driven xunit, written before the code | poison 0 -> 34 -> 68 -> Out; a hit lands after the delay; per-victim immunity; dump cooldown; pile reported once; all fresh piles reported at meeting start; meter clamps 0-100; task reassignment keeps the total; win order table (section 2) |
| `Sim.World` | xunit with tiny hand-made grids | line of sight blocked by a wall; A* finds a path; swept move rejected through a wall, accepted around a corner |
| `Sim.Match` engine | scripted command sequences + `dt` | full round: tasks done -> citizens win; both polluters voted -> citizens win; meter full -> polluters win; timed-out votes count as skip; Ready ends the discussion early |
| Seat views (`ViewFor`) | xunit | a citizen's view never holds polluter ids; seats behind a wall or out of range are absent; poison only in the owner's view; an out seat's view has no positions |
| Server protocol | round-trip test per message type | decode(encode(m)) == m; unknown type -> `error`, connection kept |
| Bot soak | headless test: 8 bots, 20 matches, random seeds | no exception, every match ends before the safety cap, tick p95 < 10 ms |
| Client + mini-games | play it (desktop + real phone) | no unit tests for scenes or rendering |

## 2. Win order table (example of a table-driven test)

| Active polluters | Active citizens | Meter | Safety cap reached | Tasks done | Expected |
|---|---|---|---|---|---|
| 0 | 3 | 99 | no | no | Citizens (`polluters_voted_out`) |
| 0 | 3 | 100 | no | no | Citizens (`polluters_voted_out`): polluters-out is checked first |
| 1 | 3 | 100 | no | no | Polluters (`pollution_full`) |
| 1 | 3 | 50 | yes | no | Polluters (`deadline`) |
| 2 | 2 | 50 | no | no | Polluters (`parity`) |
| 1 | 3 | 50 | no | yes | Citizens (`tasks_done`) |
| 1 | 3 | 100 | no | yes | Polluters (`pollution_full`): the meter is checked before tasks |
| 1 | 3 | 50 | no | no | continue |

## 3. Commands

```bash
dotnet test server                                        # everything
dotnet test server --filter "FullyQualifiedName~Poison"   # while iterating
dotnet test server --filter "Category=Soak"               # bot soak (slower)
```

## 4. Playtest protocol (each phase from P2)

1. 3+ humans on mixed devices (Android browser, iPhone Safari, APK, Windows) + bots.
2. Play 3 rounds; note: round length, who won and why, confusing moments, bugs.
3. Read the `match_summary` log lines; adjust `tuning.json` only (no code) where possible.
4. Write findings in `PROGRESS.md` under Known issues / Decisions.

Questions every playtest should answer: Did the bots follow the humans' accusations? Did
polluters dare to use poison? Was anyone out and bored for long? How long was the round?
