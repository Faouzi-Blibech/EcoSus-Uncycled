# 10 - Testing

Tests catch real regressions; they do not exist to look thorough.

## 1. What gets tested where

| Layer | How | Examples |
|---|---|---|
| `Sim` rules (vendored) | existing xunit, must stay green after renaming | vote tally ties, kill validation order, task assignment |
| `Sim.Uncycled` rules (new) | table-driven xunit, written before the code | poison 0->25->...->Out; dump cooldown; pile reported once; meter clamps 0-100; win order table (section 2) |
| `Sim.World` | xunit with tiny hand-made grids | LOS blocked by wall; A* finds path; walkable check |
| `Sim.Match` engine | scripted command sequences + `dt` | full round: roles, tasks done -> citizens win; 2 polluters voted -> citizens win; meter -> polluters win |
| Server protocol | round-trip test per message type | decode(encode(m)) == m; unknown type -> error, connection kept |
| Snapshot filter | xunit | citizen never receives polluter ids; out-of-vision seats absent; poison only to owner |
| Bot soak | headless test: 8 bots, 20 matches, random seeds | no exception, every match ends, tick p95 < 10 ms |
| Client + mini-games | play it (desktop + real phone) | no unit tests for scenes or rendering |

## 2. Win order table (example of a table-driven test)

| Active polluters | Active citizens | Meter | Tasks done | Expected |
|---|---|---|---|---|
| 0 | 3 | 99 | no | Citizens (vote) |
| 0 | 3 | 100 | no | Citizens (vote), polluters-out wins ties |
| 1 | 3 | 100 | no | Polluters (pollution) |
| 2 | 2 | 50 | no | Polluters (parity) |
| 1 | 3 | 50 | yes | Citizens (tasks) |
| 1 | 3 | 50 | no | continue |

## 3. Commands

```bash
dotnet test server                                   # everything
dotnet test server --filter "FullyQualifiedName~Poison"   # while iterating
dotnet test server --filter "Category=Soak"          # bot soak (slower)
```

## 4. Playtest protocol (each phase from P2)

1. 3+ humans on mixed devices (Android browser, iPhone Safari, APK, Windows) + bots.
2. Play 3 rounds; note: round length, who won and why, confusing moments, bugs.
3. Read the `match_summary` log lines; adjust `tuning.json` only (no code) where possible.
4. Write findings in `PROGRESS.md` under Known issues / Decisions.
