# Architecture

All architecture views for Uncycled. The design rationale and decisions live in
`../superpowers/specs/2026-11-13-uncycled-system-design.md`; this folder shows the system from
every angle a builder needs. Diagrams are Mermaid (render on GitHub and in most IDEs).

Status: **draft v1 (2026-11-14), reviewed twice, to be confirmed by the team.**

When two docs disagree: `04-game-rules.md` wins for rules and numbers, `../protocol.md` wins for
messages, the design spec wins for why. Fix the other doc.

## Reading order

| # | File | Views |
|---|---|---|
| 01 | [01-system-context.md](01-system-context.md) | System context, containers, module dependencies, deployment |
| 02 | [02-server.md](02-server.md) | What to vendor from `sim`, server components, room actor and threading, class model, seat views |
| 03 | [03-client.md](03-client.md) | Godot project layout, autoloads, scene flow, net layer, interpolation, input, platforms |
| 04 | [04-game-rules.md](04-game-rules.md) | **Source of truth for rules and numbers.** State machines: match, seat, player, poison, pile, meeting, sabotage; meter; win evaluation; scope tiers |
| 05 | [05-sequences.md](05-sequences.md) | Sequence diagrams for every gameplay and network flow |
| 06 | [06-bots-and-ai.md](06-bots-and-ai.md) | Bot brains, testimony, how bots listen to humans and vote; AI provider architecture |
| 07 | [07-data-and-content.md](07-data-and-content.md) | Runtime data model, content JSON schemas, map pipeline, i18n |
| 08 | [08-security-and-fairness.md](08-security-and-fairness.md) | Hidden-information matrix, validation per command, threat model, limits |
| 09 | [09-deployment-and-ops.md](09-deployment-and-ops.md) | Hosting, builds, CI, configuration, observability, demo-day runbook |
| 10 | [10-testing.md](10-testing.md) | Test strategy per layer, soak test, playtest protocol |
| -- | [../protocol.md](../protocol.md) | **Source of truth for messages.** Wire protocol v1: every message and field |

## Glossary

| Term | Meaning |
|---|---|
| Seat | One of the 8 player slots in a room. Controlled by a human connection or a bot brain |
| Citizen | Good team. Does eco tasks, reports piles, votes |
| Polluter | Hidden bad team (2 per room). Dumps, poisons, sabotages, fakes tasks |
| Poison | Per-citizen 0-100%. Each polluter hit adds 34% (3 hits) and is covert: it lands 5 s later and only the victim learns of it. At 100% the citizen is out (spectator) |
| Pile | Waste left by a dump or by a poisoned-out citizen. Reportable once |
| Clue | Facts shown at a meeting for each reported pile: zone, match time, waste type (dump) or victim (body) |
| Pollution meter | Shared 0-100%, the round's clock. It rises by itself during play; dumps and an expired fire raise it; cleaning lowers it; full = polluters win |
| Safety cap | 15 min of match time; a backstop against stalling (polluters win) |
| Seat view | Everything one seat may know this tick. Built by `MatchEngine.ViewFor(seat)`; humans get it as a snapshot, bots read it directly |
| Task entry | One mini-game type at one place; the unit the task assigner hands out |
| Smog | Sabotage that shrinks citizen vision; one player holds a panel 3 s to clear it |
| Landfill fire | Critical sabotage; two players hold two consoles to stop it (built on `HoldMeltdown`) |
| Match engine | Engine-free game logic in `server/Sim/Match`: takes commands + `dt`, emits events and seat views |
| Snapshot | Per-seat filtered view of the world sent ~20 times per second |
| Tile unit | World unit used by server and `Sim`: 1 unit = 1 tile = 32 px |
