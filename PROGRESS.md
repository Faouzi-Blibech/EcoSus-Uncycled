# PROGRESS

Read this first when resuming. Keep it short: done, next, known issues, decisions.

**Since Sat 2026-11-21 evening the game is v2, Cleaners vs Trashers.** Design:
`docs/design/2026-11-21-cleaners-vs-trashers.md` (wins over older docs). Who does what and when:
`docs/team/README.md`. Per-person plans: `docs/superpowers/plans/2026-11-21-*.md`.

## Done
- 2026-11-13: system design drafted (`docs/superpowers/specs/2026-11-13-uncycled-system-design.md`),
  context files written (`AGENTS.md`, `CLAUDE.md`, `docs/CONTEXT.md`), full architecture views
  (`docs/architecture/`), wire protocol v0 (`docs/protocol.md`), adversarial design review done
  and resolved (design doc section 13). Team review of all docs still pending.
- 2026-11-16: organiser rules, schedule and jury grid written up (`docs/HACKATHON.md`); whole
  repo reviewed against them (`docs/reviews/2026-11-16-jury-fit-review.md`).
- 2026-11-17: first code written by agents: `server/Sim` rules engine + tests,
  `server/Uncycled.Server` host + tests, `content/` (tuning, tasks, greybox_01 map), `client/`
  join-and-move shell. Written fresh, no `reference` code. Must be declared to the jury as
  pre-existing (rules Art. 8).
- 2026-11-18: first build check (Godot 4.7.2, .NET 10 SDK in Docker): server compiles with no
  warnings; 117 of 118 tests pass (one test bug, fix below); the server image builds and serves
  `/health`; the client imports and every scene runs with no script error; two scripted clients
  create, join and start a room on the real server, get roles and 20 Hz snapshots, and see each
  other move. Web, Windows and Android (signed debug APK) exports proven on a test project.
- 2026-11-19: `ContentTests` (server) load the real `content/` and check task consoles, spawns and
  task counts per category; `phase_changed` and `dump_ok` documented in `docs/protocol.md`.
- 2026-11-21 (event day): scripted bots with meeting talk (PR #1), photo checker app with YOLOE and
  duplicate-photo check (PR #2, EXIF bypass server-only in PR #4), typed meeting chat and ghost chat
  end to end, protocol 1 (PR #3, #5). Tests: server 134, Sim 186, bots 236 (+1 skipped, needs
  Cloudflare keys), all green. Mystery challenge announced: online game + network benchmark on the
  organisers' networks. Design pivot to v2 (see top).
- 2026-11-22 (art + HUD, PRs #31, #34, #35):
  - **Map:** TBS map with the Figma rooms (cafeteria, library, Art Factory, Enactus) and the Gemini
    recycling room (machine on its pad).
  - **Players:** animated lobby characters in the round instead of discs; speed 3.0 tiles/s.
  - **HUD (Screens reference look, Caroni font):**
    - painted round action buttons, labels on a pill under the disc;
    - pollution bar;
    - player card, note with the timer, mini map, painted joystick.
  - **Campus ground:** brick, plaster walls, stone corridors.
  - **Gemini assets** via the asset skill file (`EcoSus_Asset_Skill.md`), always scaled uniformly.

## Next
The v2 plan and schedule: `docs/team/README.md`.

### HUD parity with the server (Cleaners and Trashers)

**Context.** The server already implements the v2 Cleaner loop (design s5): clean a dirty spot through its
mini-game and get one bag (`bags.cap` 1, 3 with the Backpack); a full Cleaner cannot clean; `use` on the
machine delivers all bags (+6 meter each) and opens an upgrade offer. It also implements the Trasher loop
(s6): re-pollute 3 times, then pick virus or gas. The client handles every server message (audit 2026-11-22),
but the screen does not show all of it:

| Server feature | Client today | Gap |
|---|---|---|
| Bags: 1 slot (3 with Backpack); full Cleaners cannot clean | "Bags: 1/3" text in the note | No visual bag gauge, no "bag full, go recycle" prompt |
| `use` cleans a spot, recycles at the machine, calls a meeting at the emergency table (fake clean for a Trasher) | One "Use" hand button | The button does not say what it will do |
| Items: backpack, tool, heal, mask | One line of item names | No item icons |
| Trasher unlocks virus/gas after 3 re-pollutes, then keeps one | Nothing until the choice cards | No unlock progress, no "your ability" display |
| Basic vs special actions | All buttons the same size | Size difference requested by the art lead |

These already work and need no change: upgrade cards after a delivery, jammed-machine banner and Fix,
virus countdown and Alert, Heal, gas warning, mask-used toast, and the machine art (idle, working, jammed).
No server or protocol change is needed.

**Plan** (client only: `client/ui/hud/hud.gd`, `client/ui/hud/action_button.gd`, `client/i18n/strings.csv`,
`client/assets/ui/hud/`, one new test file):

1. **Art** (done, branch `art/bag-recycle-art`). Files in `client/assets/ui/hud/`:
   - `bag_full.png`: the Gemini trash bag;
   - `bag_empty.png`: the same bag, faded;
   - `btn_recycle*`: the green recycle disc, normal, pressed and disabled;
   - `btn_sabotage_machine*`: the "jam machine" choice now uses the purple bag disc, so the recycle icon
     stays a Cleaner icon.
2. **Two button sizes** (done 2026-11-22, branch `hud/parity-steps-2-3`; also removed the coloured
   squares beside player names).
   - **Basic (52 px disc):** Use, Report, Dash and Fix, plus the two sabotage choices.
   - **Special (64 px disc):** Clean, Heal and Alert for Cleaners; Re-pollute, Virus/Gas and Sabotage
     for Trashers.
   - The label pill stays under the disc; the grid and the Dash position adapt.
3. **Context Use button** (done 2026-11-22, same branch: `ActionRules` `use_look`, `Hud.use_look_face`).
   It changes with the console the player is next to:
   - dirty spot: Clean (sponge);
   - machine with bags: Recycle (n) (recycle disc);
   - emergency table: Meeting (bell);
   - anything else: Use (hand).

   A Trasher at a spot also sees Clean, so the fake clean looks the same. New strings `ACT_RECYCLE` and
   `ACT_MEETING` (EN/FR/AR).
4. **Bag gauge on the player card** (Cleaners only, since bags are private).
   - One slot per bag of capacity (`bag_full` / `bag_empty`).
   - When the last slot fills, the slots bounce and the toast reads "Bag full! Take it to the Recycling
     Machine" (`HUD_BAGS_FULL`).
   - The slots empty with a short animation on `delivered`.
5. **Trasher progress on the player card.** Re-pollute pips (x/`trasher.unlock_after`) until the unlock;
   after the pick, the ability's icon and name.
6. **Items as icons** in the top-right note (backpack, tool, heal, mask, only while owned). This replaces
   the "Bags: x/y" line and the item-name line.
7. **Tests and checks.**
   - Table tests for the pure rules: the Use button look per context, the bag slots, the unlock progress.
   - Full client, Sim and server test runs.
   - A rendered screenshot per role, then a play test.

Open points:
- Confirm the basic/special split in step 2.
- Bell for "Meeting"? For now it borrows the Alert bell (`btn_alert`); a separate bell art can replace it.

## Known issues
- Server has 12 colours, client palette has 8 (colours 8-11 draw white). Fixed in gameplay step 1.
- `client/project.godot` lacks `rendering/textures/vram_compression/import_etc2_astc=true`:
  Android export fails until it is set. Fixed in gameplay step 1.
- **Event rules:** Art. 8 wants a project conceived during the hackathon and all pre-existing
  code, design and content declared (`docs/HACKATHON.md` s3). The team builds the full game
  beforehand on purpose, so the declaration must be clear and honest.
- **Scope vs time:** about 23 build hours at the event plus the days before it, and a 10% mystery
  challenge announced on the day.
- Rule bug in `04-game-rules.md`: the 10:00 deadline runs on `MatchTime`, so polluters can win by
  reporting their own piles and sitting in meetings (review s4.2). Fix: run it on `ActiveTime`.
- `reference` is derived from decompiled code of the reference game (its `docs/logic/*.md`
  say so in their first lines). Use it only to understand how behaviour works; never copy its
  code, data or text into this repo (rules Art. 5 and Art. 12, `AGENTS.md` hard rule 9).
- `ReportRange` (3 tiles) lives only in `MatchConfig.cs`, not in `tuning.json`.
- The server dev's PC has the .NET 10 SDK (10.0.404) but no Godot; it has JDK 21 (Godot recommends 17).

## Decisions
| Decision | Status |
|---|---|
| Multiplayer, judges in one round, bots fill seats | Decided 2026-11-13 |
| Godot 4 GDScript client + .NET 10 (LTS) server | Decided 2026-11-13 |
| Platforms: Web, Windows, Android | Decided 2026-11-13 |
| `reference` stays a separate repo, used as read-only reference | Decided 2026-11-13 |
| Vote kick-out + poison hits (34%/hit = 3 hits, 25 s per-victim immunity, out at 100%, becomes a reportable pollution pile) | Decided 2026-11-13 |
| Free dump + witness; one report per pile; clue = zone, time, waste type | Decided 2026-11-13 |
| Humans chat by phrases or typed text (120 characters, 5 typed lines per meeting, word filter, 2 s gap); out players have a ghost chat living players never receive; bots use quick-chat testimony, polluter bots lie | Decided 2026-11-13, free text added 2026-11-21 |
| 8 seats, 2 polluters, ~6 min round, 60 s talk + 30 s vote | Decided 2026-11-13 |
| Server on .NET 10 LTS (not 9) | Decided 2026-11-13 |
| **Event scope: the full design, built as a complete game.** The 28-hour cut (review s5) is dropped | Decided 2026-11-18 |
| No lead: the team works and decides together; anyone may change any folder, telling its owner | Decided 2026-11-18 |
| Roles: server dev (server, rooms), gameplay dev (`client/`), AI dev (computer-vision scanner), 2 designers (art) | Decided 2026-11-18 |
| Vendor nothing from `reference` (decompiled-derived; `server/Sim` is already our own fresh code) | Proposed 2026-11-18, team to confirm |
| Godot 4.7.2 standard edition for everyone | Decided 2026-11-18 |
| More task types than the first 2 (the genre has many); exact list in the gameplay design | Decided 2026-11-18 |
| Balance numbers, deadline, emergency cap, spectator blackout, task reassignment (04-game-rules) | Proposed 2026-11-13, team to confirm |
| How pre-existing work is declared to the jury (rules Art. 8) | Open |
| Pitch owner; owners for sound, playtests, submission, bots | Open |
| Stretch AI feature | Open |
| Map setting (look); greybox layout comes with the gameplay design | Open |
| Default language FR / EN | Open |
| Server host (needs `wss://`) and web host | Open (Louay proposes in L5) |
| **v2 design: Cleaners vs Trashers** (spots via mini-games, bags, machine, upgrades, re-pollute, virus, gas). Replaces the poison, dump, per-player task and 8-seat rows above | Decided 2026-11-21 |
| Room sizes 6/8/10 with 1/2/2 Trashers, host picks | Replaced 2026-11-21 evening: **locked at 8 seats, 2 Trashers** (server keeps 6/10 support, unused) |
| Platforms: Android APK, web (also for iPhone), Windows; Windows scans through a QR code on a phone | Decided 2026-11-21 |
| Camera scan gives points and a daily streak that unlocks skins | Decided 2026-11-21 |
| Benchmark networks: Ooredoo, Tunisie Telecom 5G, Tunisie Telecom 5G SA (Wi-Fi as reference) | Decided 2026-11-21 |
| Faouzi is team lead; roles: Faouzi client, Hedi server + bots, Louay scan + benchmark + builds, Yasmin + Khammar art. Replaces "No lead" and the 2026-11-18 roles | Decided 2026-11-21 |
| Code keeps `Citizen`/`Polluter` names; only screen text says Cleaner/Trasher | Proposed 2026-11-21 |
