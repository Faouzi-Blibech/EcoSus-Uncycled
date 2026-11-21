# Gameplay client plan

Owner: gameplay dev (`client/`). Status as of 2026-11-18. This page says what the client work
covers, in what order, and what it changes outside `client/`, so the rest of the team can plan
around it. Design background: `docs/architecture/03-client.md`, protocol: `docs/protocol.md`.

## Goal

Take the client from its join-and-move shell to a full playable round of the full design against
the existing server: role reveal, tasks with 6 mini-games, dump, poison, report, emergency
meeting, discussion, vote, ejection, spectator view, results, back to lobby, and rejoin into the
right phase. This is the MUST tier; the landfill fire and smog UI, clue polish, cleaning piles and
the art and sound pass come after.

## Order of work

Each step ends playable and is pushed on its own `gameplay/<name>` branch for a PR into `main`.

| Step | Work | Status |
|---|---|---|
| 1. Builds | Export presets (Web with threads off, Windows, Android with `INTERNET`), Android texture setting, `strings.csv` shipped as a plain file, 12-colour palette, headless test runner, art handoff page, `CREDITS.md`, one server test fix | Done on `gameplay/builds`; phone check pending |
| 2. World | Map toolkit (markers, tileset, export tool), greybox map `greybox_01` as a Godot scene, player and pile scenes, follow camera, fog that stops at walls | Toolkit in progress; map layout shown to the team before it goes to the server |
| 3. Match | Typed protocol signals and match state, HUD (task list, meter, timer, use/report/hit/dump buttons with cooldowns), role reveal, toasts, spectator view, 6 mini-games | Protocol signals and match state done on `gameplay/net-session` (in review) |
| 4. Meetings and end | `welcome.seats` on the server, meeting scene (clues, task bar, voting, results, ejection), results screen, reconnect routing, full-round tests | `welcome.seats` done on `gameplay/welcome-seats` |

## Mini-games and task types

Each citizen gets 1 common, 2 short and 1 long task (as `content/tuning.json` already asks).

| Mini-game | Category | Steps | What the player does | Task types and places |
|---|---|---|---|---|
| `sort_waste` | short | 1 | drag items into the right bins | `sort_waste` (market), `sort_waste_depot` (depot) |
| `turn_compost` | long | 2 | turn the heap, then water it | `turn_compost` (garden heap, then garden tap) |
| `check_meter` | common | 1 | read the street's pollution sensor and key in the number | `check_meter` (depot) |
| `crush_cans` | short | 1 | tap cans flat before the metal bin | `crush_cans` (market), `crush_cans_garden` (garden) |
| `rinse_bottles` | short | 1 | hold to rinse, release in the green band | `rinse_bottles` (depot), `rinse_bottles_market` (market) |
| `deliver_glass` | long | 2 | lift a glass crate, drop the bottles at the bottle bank | `deliver_glass` (depot, then market) |

The server fixes one console per step for a task type, so each place is its own task type; several
types share one mini-game scene.

## Changes outside `client/` (heads-up)

| File | Change | When |
|---|---|---|
| `content/maps/greybox_01.json` | Becomes an **export** of `client/world/maps/greybox_01.tscn`. After that, change the map in the scene and re-export; do not hand-edit the JSON. It keeps every current id (`market_bins`, `garden_compost`, `garden_tap`, `meeting_button`, `fire_a`, `fire_b`, `smog_panel`, zones `market`, `garden`, `depot`) and adds the task consoles below | Step 2 |
| `content/tasks.json` | The 9 task types above, on consoles `market_bins`, `depot_bins`, `garden_compost`, `garden_tap`, `depot_meter`, `market_cans`, `garden_cans`, `depot_sink`, `market_sink`, `depot_glass`, `market_bottle_bank`. This also gives the `common` category that `tuning.json` asks for | Step 3 |
| `server/Uncycled.Server/Rooms/Wire.cs`, `Room.cs`, `docs/protocol.md` | `welcome.seats: [{seat, name, color, human, out}]` for every seat, so a client restarted mid-match can draw vote cards. Additive, `PROTOCOL_VERSION` stays 0 | Done |
| `server/Sim.Tests/EngineSystemsTests.cs` | The wall test now waits past the 0.3 s lost-sight grace; all 118 server tests pass | Done |

Map export, from the repo root:
`godot --headless --path client --script res://tools/export_map.gd -- --map greybox_01`
(`--check` fails if the committed JSON is out of date; `--preview <png>` draws the layout).

## For the designers

- Art goes in `client/assets/` following the table in `client/assets/README.md` (tiles 32x32,
  player 24x32 tinted by seat colour, 16x16 seat icons, piles and consoles 32x32, HUD buttons
  48x48, PNG, `snake_case` names). The game runs with coloured placeholders until art arrives.
- The map's art can be painted over the greybox in the Godot editor as long as walls and markers
  stay where they are.
- Every outside asset gets a line in `CREDITS.md` with its licence (event rules Art. 5).

## Open points for the team

- **Bots have no owner.** Today's bots are idle: they never do tasks or vote, so citizens cannot
  win by tasks in a round with bots.
- **Hosting:** no `wss://` server yet; until then phones test against a laptop server on the LAN.
- Pass on to the server side, not blocking: the report range (3 tiles) lives only in
  `MatchConfig.cs`, not in `tuning.json`; no server test loads `content/`.

## Testing the client

- Unit tests (headless, exit code 0 = pass):
  `godot --headless --path client res://tests/test_runner.tscn`. Every test is
  `func test_<name>() -> bool:` ending with `return true`; a crashed, empty or missing test fails the run.
- Builds: `godot --headless --path client --export-release "Web" ../build/web/index.html`, and the
  same with `"Windows Desktop"` and `"Android"` (`AGENTS.md` section 9).
- A scripted test client (`client/tools/probe/`, step 2) plays rounds against the real server
  in Docker: join and move, tasks, a full round to a citizens' and a polluters' win, rejoin mid-meeting.
