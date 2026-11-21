# Godot client gameplay for Cleaners vs Trashers: implementation plan (Faouzi)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the Godot client plays v2: map with machine and gas rooms, cleanliness HUD, spots cleaned through
mini-games, machine and upgrades, Trasher kit, virus, heal, gas, new end screens.

**Architecture:** reuse the round scene, HUD, action buttons and mini-game layer. `Session` parses the protocol 2
snapshot; `action_rules.gd` (pure function, tested) decides which buttons show; new small UI scripts for pickers
and the virus timer. Faouzi owns `round.gd`, `hud.gd`, `action_rules.gd`, `session.gd`, the map scene and the
results scene. Louay owns `menu.gd`, `lobby.gd`, `settings_panel.gd` and the new `Profile`/`NetStats` autoloads.

**Tech Stack:** Godot 4.7.2 standard, GDScript with static types, Compatibility renderer, 640x360.

**Spec:** `docs/design/2026-11-21-cleaners-vs-trashers.md` (sections 3-8 and 12). Wire truth:
`docs/protocol.md` v2 (Hedi, Task 1). Team plan: `docs/team/README.md`.

## Global Constraints

- Static types everywhere (`var bags: int = 0`). Scenes listen to `Net` signals, never open sockets.
- No player-facing text in code: add keys to `client/i18n/strings.csv` in FR and EN. Faouzi appends at the
  **end** of the file; Louay adds his keys right after the header line (avoids merge conflicts).
- Screen-facing names: Cleaner, Trasher, Cleanliness. Code and wire keep `citizen`/`polluter`.
- Touch targets at least 48 px; keyboard: WASD/arrows move, E use, Space action, R report.
- Every texture goes through `Assets.texture(path)` (placeholder when missing): art lands without code changes.
  Paths are fixed in `docs/team/art-list.md`.
- Tests: `godot --headless --path client res://tests/test_runner.tscn` (extend `client/tests`, base class
  `test_case.gd`). Test logic (rules, parsing, formatting), not rendering.
- Branches `gameplay/<short-name>`, PR into `main`. Agents commit only when Faouzi asks.

## Review Focus

1. Snapshot fields missing for a role (Cleaners get no `repollutes`, Trashers get no `bags`): the HUD must treat
   a missing key as "not mine", never crash or show 0/0. Test in Task F1.
2. A meeting or `you_are_out` while a mini-game, upgrade picker or ability picker is open: close it and clear
   holds, so nothing is left on screen in the meeting. Test in Task F3/F5 by sending the events in order.
3. Rejoin (`welcome.state`) mid-match with bags, an open offer or an infection: the HUD rebuilds from it. Check by
   killing the client mid-match and reconnecting.
4. A Trasher's fake clean must send nothing to the server (a `spot_done` from a Trasher is `not_allowed` and would
   reveal them through a toast). Test in Task F2.
5. Web on iPhone Safari: tiny screen and safe area; the 4-card upgrade picker and the room picker must fit 640x360
   with 48 px buttons. Check on a phone at the end of F3 and F4.

---

### Task F0: Godot setup and map changes (45 min, needed by Hedi's Task 4 and 7)

**Files:**
- Modify: `client/world/maps/map_marker.gd` (kinds `machine`, `gas_room`), `client/tools/map_json.gd` (write the
  `machine` special and `gas_rooms` like `dark`), `client/world/maps/greybox_01.tscn`,
  `content/maps/greybox_01.json` (regenerated), `client/tests/test_map_json.gd`

- [ ] **Step 1:** Install Godot 4.7.2 standard (not .NET) and its export templates. Check: `godot --version`.
- [ ] **Step 2: Write the failing test** in `test_map_json.gd`: the exported map has one special
  `{id: "machine", kind: "machine"}` on a walkable tile touching the map border, 4 `gas_rooms` with ids and rects,
  10 `spawns`. (Done: fire consoles stay until Hedi's Task 5, bot tests depend on them.)
- [ ] **Step 3:** Run the test runner. Expected: FAIL.
- [ ] **Step 4:** Add the marker kinds and export, then in `greybox_01.tscn`: a machine marker at the map edge,
  4 small gas-room rects (ids `gas_market`, `gas_garden`, `gas_depot`, `gas_dark`), 2 more spawns. Regenerate: `godot --headless --path client --script res://tools/export_map.gd -- --map greybox_01`.
- [ ] **Step 5:** Run the test runner. Expected: PASS. Send the new JSON to Hedi (his `ContentLoader` must accept
  `machine` and `gas_rooms` before this merges). Commit on `gameplay/map-v2`.

### Task F1: protocol 2, Session, HUD basics (1 h)

**Files:**
- Modify: `client/autoload/net.gd` (`PROTOCOL_VERSION = 2`), `client/autoload/session.gd` (`_on_snapshot`:
  `spots`, `machine`, `gas`, `me.*`), `client/ui/hud/hud.gd` (cleanliness bar replaces the pollution bar, bag
  counter), `client/i18n/strings.csv`, `client/tools/probe/probe_client.gd` (protocol 2)
- Test: `client/tests/test_session_v2.gd` (new), `client/tests/test_chat_text.gd` (protocol 2)
- Done on `gameplay/hud-v2`. Hit, dump and pile clean are **not** hidden here: hiding them breaks 11 round
  tests of those flows. F2 replaces pile clean with spot clean, F4 replaces hit/dump with the Trasher kit, and
  each rewrites its tests then.

**Interfaces:**
- Produces on `Session`: `spots: Dictionary` (id -> `"dirty"`/`"clean"`), `machine_jammed: bool`,
  `gas_room: String` (empty when none), `gas_in_s: float`, `bags: int`, `bag_cap: int`, `has_tool: bool`,
  `heals: int`, `masks: int`, `infected_s: float` (-1 when not infected), `repollutes: int`,
  `ability: String` (`""`, `"virus"`, `"gas"`); Trasher cooldowns stay in the existing `cooldowns`.

- [ ] **Step 1: Write the failing tests** in `test_session_v2.gd`: feed a Cleaner snapshot and a Trasher snapshot
  (copy the JSON shape of spec section 12) into `Session._on_snapshot` and assert every field above; a snapshot
  without `me.infected_s` gives `infected_s == -1` (Review Focus 1).
- [ ] **Step 2:** Run the test runner. Expected: FAIL.
- [ ] **Step 3:** Implement the parsing and HUD: meter bar labelled `HUD_METER` (now "Cleanliness"), time left
  `deadline_t - clock_t` (unchanged), bag counter `HUD_BAGS` "1/1" for Cleaners only.
- [ ] **Step 4:** Run the test runner. Expected: PASS. Run the local server and client: the HUD shows 0% and
  7:00. Commit on `gameplay/hud-v2`; merge together with Hedi's Task 2 (protocol 2 on both sides).

### Task F2: spots and cleaning through mini-games (1 h 30)

Done on `gameplay/spots`, simpler than planned: the existing USE button cleans (no `clean_spot` action);
`SpotView` is a script-only Sprite2D (`client/world/spot/spot_view.gd`, no .tscn); the mini-game layer opens
on `use_ok` kind `spot` and sends `spot_done` (`Net.send_spot_done`); `Session.clean_min_s` applies the tool
multiplier; `MinigameRegistry.base_type` names the spot art. The original plan follows for reference.

**Files:**
- Create: `client/world/spot/spot.gd`, `client/world/spot/spot.tscn` (sprite dirty/clean, from map consoles of
  kind `task`)
- Modify: `client/scenes/round/round.gd` (`interaction_context()`, `_use_nearest_console()`, `use_ok` kind `spot`
  opens the mini-game, mini-game success sends `spot_done`), `action_rules.gd` (`clean_spot` button),
  `strings.csv`
- Test: `client/tests/test_action_rules.gd`

**Interfaces:**
- Consumes: `Session.spots`, `Session.bags`, `Session.bag_cap`; `MinigameRegistry.scene_for(type)`.
- Produces: action `clean_spot` in `action_rules.button_states(ctx)`; `ctx` gains `near_spot: String`,
  `near_spot_state: String`, `bags_full: bool`, `is_polluter: bool`.

- [ ] **Step 1: Write the failing tests** (table in `test_action_rules.gd`):
  - Cleaner near a dirty spot with room -> `clean_spot` enabled.
  - Bags full -> `clean_spot` disabled.
  - Near a clean spot -> `clean_spot` hidden.
  - Trasher near a dirty spot -> `clean_spot` enabled (fake clean).
- [ ] **Step 2:** Run the test runner. Expected: FAIL.
- [ ] **Step 3:** Implement:
  - Spots draw from `Session.spots` with `res://assets/spots/<type>_dirty.png` and `_clean.png`.
  - A Cleaner's `clean_spot` sends `use {console: spot}`. On `use_ok` kind `spot`, the client opens the existing
    mini-game for `type`; on success it sends `spot_done {spot}`; `spot_cleaned` shows a toast and updates bags.
  - A Trasher's `clean_spot` opens the same mini-game locally and sends nothing (Review Focus 4; add a test that
    the Trasher path calls no `Net.send`).
- [ ] **Step 4:** Run the test runner, then play locally: clean a spot, the bag counter shows 1/1. Commit on
  `gameplay/spots`.

### Task F3: machine, delivery, upgrades, jam (1 h)

Done on `gameplay/machine` (stacked on F2). USE at the machine delivers; FIX at a jammed machine is a
hold (`fix machine true/false`, ring from `machine.repair_t`); the jam banner comes from
`Session.machine_jammed` (snapshots carry only smog in `sabotage`); `Session.upgrade_offer` +
`UpgradePicker` (built in parallel by a worker) -> `RoundScene.pick_upgrade` -> `pick_upgrade`; owned items
line under the bag counter; toasts for `delivered` and `upgrade_ok`. A Trasher is offered nothing at the
machine (no fake delivery). The original plan follows for reference.

**Files:**
- Create: `client/ui/hud/upgrade_picker.gd` (4 cards, 48 px min, icons `res://assets/icons/upgrade_<item>.png`)
- Modify: `round.gd` (`deliver`, `fix_machine` hold reuses the fire-hold code), `action_rules.gd`, `hud.gd`,
  `strings.csv`
- Test: `test_action_rules.gd`, `client/tests/test_upgrade_picker.gd` (new)

**Interfaces:**
- Consumes: `delivered {bags, meter}`, `upgrade_offer {items}`, `upgrade_ok {item}`, `Session.machine_jammed`.
- Produces: actions `deliver`, `fix_machine`; `UpgradePicker.open(items: Array[String])`, signal `picked(item: String)`.

- [ ] **Step 1: Write the failing tests:**
  - `action_rules` table: near the machine with bags -> `deliver` enabled.
  - Jammed -> `deliver` hidden and `fix_machine` shown.
  - Trasher near the machine -> `deliver` enabled (fake) but sends nothing.
  - `test_upgrade_picker.gd`: `open(["heal","mask"])` shows 2 cards; `picked` emits the item; `phase_changed` to
    meeting closes it (Review Focus 2).
- [ ] **Step 2:** Run the test runner. Expected: FAIL.
- [ ] **Step 3:** Implement. `pick_upgrade {item}` on `picked`; HUD shows owned items (tool, backpack, heal count,
  mask count) as small icons.
- [ ] **Step 4:** Run the test runner; play locally: deliver, pick backpack, counter shows 0/3. Commit on
  `gameplay/machine`.

### Task F4: Trasher kit (1 h 30)

Done on `gameplay/trasher-kit` (stacked on F3). The v1 HIT and DUMP buttons became RE-POLLUTE (key of
HIT) and the ability button (VIRUS or GAS, key of DUMP); `hit`/`dump` code and tests are gone. Pickers share
a `CardPicker` base (worker, in parallel): `UpgradePicker`, `AbilityPicker`, `RoomPicker` (cancelable). The
ability choice comes from `ability_unlocked`, and after a rejoin from the snapshot (no ability, re-pollutes >=
`trasher.unlock_after`), since the server does not re-send it. Sabotage choices are machine and smog; a
witnessed re-pollute plays the old dump splat. The original plan follows for reference.

**Files:**
- Create: `client/ui/hud/ability_picker.gd` (2 cards), `client/ui/hud/room_picker.gd` (gas room list from map
  `gas_rooms`, names `GAS_ROOM_<ID>`)
- Modify: `round.gd`, `action_rules.gd`, `hud.gd` (cooldown rings for `repollute`, `virus`, `gas`, `sabotage`),
  `client/ui/hud/sabotage_banner.gd` (machine jam text, no fire), `strings.csv`
- Test: `test_action_rules.gd`

**Interfaces:**
- Consumes: `repollute_ok`, `ability_unlocked`, `ability_ok`, `virus_planted`, `Session.cd`, `Session.ability`.
- Produces: actions `repollute`, `plant_virus`, `gas`, `sabotage_machine`, `sabotage_smog`.

- [ ] **Step 1: Write the failing tests** (`action_rules` table):
  - Trasher near a clean spot, cooldown 0 -> `repollute` enabled; cooldown 5 -> disabled with time.
  - `ability == "virus"` near a dirty spot -> `plant_virus`; `ability == ""` -> hidden.
  - `ability == "gas"` -> `gas` anywhere.
  - Cleaner -> all Trasher actions hidden.
- [ ] **Step 2:** Run the test runner. Expected: FAIL.
- [ ] **Step 3:** Implement. `ability_unlocked` opens the ability picker (`pick_ability`). `gas` opens the room
  picker (`gas {room}`). Sabotage button opens a 2-choice menu (machine, smog). Witnesses get `cue` kind
  `repollute`: reuse `cue_effect.gd` with its own icon.
- [ ] **Step 4:** Run the test runner; play as a Trasher against bots: start the local server with
  `UNCYCLED_DEV_HOST_ROLE=polluter` (Hedi's Task 2 dev switch) so the host is always the Trasher. Commit on
  `gameplay/trasher-kit`.

### Task F5: virus, heal, gas, death, end screens (1 h)

Done on `gameplay/virus-gas` (stacked on F4), with languages: `VirusTimer` (red countdown, pulses under 10 s)
and `GasOverlay` (green room tint growing to the landing) built by a parallel worker; a Cleaner's HEAL and ALERT
take the Trasher slots and keys (heal self when infected, else the nearest player in `virus.heal_range`; one
ALERT meeting per infection); toasts for healed, mask used, gas warning; spectator and results texts for virus,
gas and `meter_full`. Languages: English by default (no longer the computer's language), French and Arabic in
Settings; `strings.csv` has an `ar` column (worker), Noto Sans Arabic is a fallback of the default font. The
original plan follows for reference.

**Files:**
- Create: `client/ui/hud/virus_timer.gd` (big red countdown, only when `Session.infected_s >= 0`)
- Modify: `round.gd` (`heal` button: self, or the nearest player within 1.5 tiles; infected `call_meeting`
  button anywhere), `hud.gd` (gas warning banner `gas_warning`, room tint over the gas rect for `in_s`, toast
  `mask_used`), `client/ui/hud/spectator_overlay.gd` (causes `virus`, `gas`),
  `client/scenes/results/results.gd` (reasons `meter_full`, `deadline`, `parity`, `polluters_voted_out`; winners
  shown as Cleaners/Trashers), `strings.csv`
- Test: `client/tests/test_virus_timer.gd` (new), `test_action_rules.gd`

**Interfaces:**
- Consumes: `infected`, `healed`, `mask_used`, `gas_warning`, `gas_done`, `you_are_out`, `game_over`.

- [ ] **Step 1: Write the failing tests:**
  - `virus_timer` formats 24.3 as "24" and shows at < 0 nothing.
  - `action_rules` table: infected Cleaner -> `call_meeting` enabled anywhere until used once.
  - Holding a heal near an infected player -> `heal` enabled; no heal -> hidden.
  - `you_are_out` with an open picker closes it (Review Focus 2).
- [ ] **Step 2:** Run the test runner. Expected: FAIL.
- [ ] **Step 3:** Implement. Results screen texts use new keys (`RESULT_METER_FULL`, `RESULT_DEADLINE`, ...).
- [ ] **Step 4:** Run the test runner; play a full match vs bots to `game_over`. Commit on `gameplay/virus-gas`.

### Task F6: art in, demo polish (rest of the time)

- [ ] Drop art files from Khammar into the paths of `docs/team/art-list.md`; no code change needed when paths match.
- [ ] Check on a phone (web build from Louay) at 640x360: every button reachable, texts fit.

---

## Done means

- `godot --headless --path client res://tests/test_runner.tscn` passes.
- On a phone (web) and on Windows: lobby -> match -> clean, deliver, upgrade; as Trasher: re-pollute, unlock,
  virus or gas -> meeting -> `game_over` screen with the right reason.
