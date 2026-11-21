# Team plan: Cleaners vs Trashers (Green Hackathon, 21-22 Nov 2026)

Read this first, then your own plan. Design (what the game is): `docs/design/2026-11-21-cleaners-vs-trashers.md`.

**Deposit: Sun 22 Nov, 14:30** (build, deck, backup video). **Pitch: Sun 15:30**, all five of us on site.

## Who does what

| Who | Role | Owns | Plan |
|---|---|---|---|
| Faouzi | Team lead, Godot client gameplay, deck and pitch | `client/` gameplay: `round.gd`, `hud.gd`, `session.gd`, `action_rules.gd`, map scene, results | `docs/superpowers/plans/2026-11-21-faouzi-client-gameplay.md` |
| Hedi | Server rules, protocol, bots | `server/Sim`, `server/Uncycled.Server`, `bots/`, `docs/protocol.md`, `content/tuning.json` | `docs/superpowers/plans/2026-11-21-hedi-server-rules-bots.md` |
| Louay | Camera scan, network benchmark, builds, hosting | `app.py`, `scan_profile.py`, `client/` menu, lobby, settings, `Profile`, `NetStats`; `/bench` on the server | `docs/superpowers/plans/2026-11-21-louay-scan-bench-builds.md` |
| Yasmin | Digital artist | characters, skins, machine, spots, deck art | `docs/team/art-list.md` |
| Khammar | Graphic designer, art integration | icons, UI, effects, putting art into `client/assets/` | `docs/team/art-list.md` |

## Schedule (now -> deposit)

| When | Hedi | Faouzi | Louay | Yasmin + Khammar |
|---|---|---|---|---|
| Sat 18:30-19:30 | Task 1: `protocol.md` v2 (PR by 19:15) | F0: install Godot, map (machine, gas rooms, 10 spawns) | L1: scan service | Read art list, start P1 |
| Sat 19:30-23:30 | Tasks 2-4: room size, spots, machine | F1-F2: protocol 2, HUD, spots | L1-L2: scan service + menu | P1 art |
| **Sat 23:30** | **Checkpoint 1: on `main`, a match where Cleaners clean spots and deliver bags** | | | |
| Sat 23:30-02:30 | Tasks 5-6: Trasher kit, virus | F3-F4: machine, Trasher kit | L3-L4: lobby skins, benchmark; first web build on an iPhone | P1 done, start P2 |
| 02:30-07:30 | Sleep in shifts, at least 4 h each | | | |
| Sun 08:00-11:00 | Task 7 gas, Task 8 bots | F5-F6: virus, heal, gas, end screens, art in | L5: hosting, web + APK + Windows | P2, deck art |
| **Sun 09:00** | **Cut check: if gas has not started, cut it (virus stays)** | | | |
| **Sun 11:00** | **Feature freeze. Full playtest: 5 humans + bots on the hosted server** | | L6: benchmark runs | |
| Sun 11:00-13:30 | Bug fixes | Deck, backup video (recorded by 12:30), Art. 8 declaration | Benchmark slide data | Deck art, final UI |
| Sun 13:30-14:30 | Bug fixes only, final builds | Deposit at 14:30 | Final builds | - |

## Handoffs (the moments we depend on each other)

1. **Sat 19:15** Hedi's `protocol.md` v2 PR: everyone reads it; it is the contract between server and client.
2. **Sat 19:30** Faouzi's new map JSON goes to Hedi: the server must load `machine` and `gas_rooms`.
3. **Protocol 2 merge:** Hedi's Task 2 and Faouzi's F1 merge together (both bump `PROTOCOL_VERSION` to 2).
4. **Louay <-> Faouzi:** one line `NetStats.mark_sent("<action>")` next to each action `Net.send`; one
   `set_skin(id)` hook in `world/player/avatar.gd`.
5. **Khammar -> Faouzi:** say when a P1 art group is in; file names must match the art list exactly.

## Cut order if we are late

1. Toxic gas (keep the virus).
2. Windows build (web + Android stay).
3. Skins in the match (keep points and streak in the menu).
4. Bots using abilities (bots still clean, deliver and re-pollute).

Never cut: the core loop (clean, deliver, meter, win), meetings and votes, the hosted web build, the benchmark.

## How we work

- Branch per feature (`server/...`, `gameplay/...`, `scan/...`, `bench/...`, `build/...`, `art/...`), PR into
  `main`, someone else glances at it, merge. `main` must run at every merge.
- Pull `main` often. Small PRs merge faster and conflict less.
- Stay in your folders. If you must touch someone else's file, do it in its own commit and tell them.
- Before a merge: `dotnet test server` (server changes), `dotnet test bots` (bot changes), the Godot test runner
  `godot --headless --path client res://tests/test_runner.tscn` (client changes), `pytest -q tests` (photo
  service).
- No player-facing text in code: `client/i18n/strings.csv`, FR + EN.
- Never commit `.env`, keystores, `build/`, `reference/`, the generated JSON stores.
- Do not write the name of the game that inspired the genre anywhere (event rule Art. 12).

## Using Claude Code (or any agent) on your plan

In the repo, start the agent and say:

```
Read docs/team/README.md and docs/design/2026-11-21-cleaners-vs-trashers.md, then execute Task <N> of
docs/superpowers/plans/<your plan>.md. Stop after that task and show me the test output.
```

One task at a time; check the test output yourself before the next task.

## Deliverables at 14:30 (owner)

| Deliverable | Owner |
|---|---|
| Hosted web link (works on iPhone and Android) | Louay |
| Android APK, Windows zip | Louay |
| Pitch deck (problem, concept, features, demo, impact, benchmark results) | Faouzi, art from Yasmin |
| Backup demo video (2-3 min) | Faouzi + Khammar |
| Art. 8 declaration: what existed before the event (repo started 2026-11-13: server, rules engine, bots, mini-games, client shell, photo checker) and what we built here (v2 rules, scan in game, benchmark, art) | Faouzi |
| Benchmark results (`/bench.html` screenshot + CSV) | Louay |

## Links (fill in as they exist)

- Game (web): 
- Server health: 
- Photo service: 
- Benchmark page: 
- APK / Windows zip: 

## Open decisions (owner: Faouzi)

1. Final names: game title, the Trasher role, the gas ability.
2. Gas locks doors or not (not built).
3. Numbers: tuned while playtesting, in `content/tuning.json` only.
