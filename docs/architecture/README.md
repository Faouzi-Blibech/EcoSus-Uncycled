# Architecture

All architecture views for Uncycled. The design rationale and decisions live in
`../superpowers/specs/2026-11-13-uncycled-system-design.md`; this folder shows the system from
every angle a builder needs. Diagrams are Mermaid (render on GitHub and in most IDEs).

Status: **draft v0 (2026-11-13), to be reviewed by the team.**

## Reading order

| # | File | Views |
|---|---|---|
| 01 | [01-system-context.md](01-system-context.md) | System context, containers, module dependencies, deployment |
| 02 | [02-server.md](02-server.md) | Server components, room actor and threading, class model, snapshot pipeline |
| 03 | [03-client.md](03-client.md) | Godot project layout, autoloads, scene flow, net layer, interpolation, input, platforms |
| 04 | [04-game-rules.md](04-game-rules.md) | State machines: match, seat, player, poison, pile, meeting, sabotage; win evaluation |
| 05 | [05-sequences.md](05-sequences.md) | Sequence diagrams for every gameplay and network flow |
| 06 | [06-bots-and-ai.md](06-bots-and-ai.md) | Bot brains, perception, testimony, voting; AI provider architecture |
| 07 | [07-data-and-content.md](07-data-and-content.md) | Runtime data model, content JSON schemas, map pipeline, i18n |
| 08 | [08-security-and-fairness.md](08-security-and-fairness.md) | Hidden-information matrix, validation per command, threat model, limits |
| 09 | [09-deployment-and-ops.md](09-deployment-and-ops.md) | Hosting, builds, CI, configuration, observability, demo-day runbook |
| 10 | [10-testing.md](10-testing.md) | Test strategy per layer, soak test, playtest protocol |
| -- | [../protocol.md](../protocol.md) | Wire protocol v0: every message and field |

## Glossary

| Term | Meaning |
|---|---|
| Seat | One of the 8 player slots in a room. Controlled by a human connection or a bot brain |
| Citizen | Good team. Does eco tasks, reports piles, votes |
| Polluter | Hidden bad team (2 per room). Dumps, poisons, sabotages, fakes tasks |
| Poison | Per-citizen 0-100%. Each polluter hit adds 34% (3 hits). At 100% the citizen is out (spectator) |
| Pile | Waste left by a dump or by a poisoned-out citizen. Reportable once |
| Clue | Facts shown at a meeting for each reported pile: zone, match time, waste type (dump) or victim (body) |
| Pollution meter | Shared 0-100%. Dumps, a slow passive rise and an expired fire raise it; cleaning lowers it; full = polluters win |
| Deadline | 10:00 of match time; reaching it = polluters win |
| Smog | Sabotage that shrinks citizen vision; one citizen holds a panel 3 s to clear it |
| Landfill fire | Critical sabotage; two citizens hold two consoles to stop it (built on `HoldMeltdown`) |
| Match engine | Engine-free game logic in `server/Sim/Match`: takes commands + `dt`, emits events |
| Snapshot | Per-seat filtered view of the world sent ~20 times per second |
| Tile unit | World unit used by server and `Sim`: 1 unit = 1 tile = 32 px |
