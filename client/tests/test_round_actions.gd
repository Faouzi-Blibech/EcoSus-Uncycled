extends "res://tests/test_case.gd"
## The round's action routing and targeting on the real greybox_01 map with a faked Session:
## which console is near, who HIT may aim at, and what USE does for a polluter and a citizen.
## The senders seam records the sends; nothing reaches the network.

const CONSOLE: String = "market_bins"
const OTHER_CONSOLE: String = "depot_meter"
const EMERGENCY: String = "meeting_button"
const CONSOLE_RANGE_PX: float = 38.4
const HIT_RANGE_TILES: float = 1.0
const TILE_PX: float = 32.0

## Session fields the tests touch, restored after each test.
const SESSION_FIELDS: Array[String] = [
	"map_id", "map_version", "seat_id", "role", "phase", "partner_seat", "tuning", "task_types",
	"my_tasks", "piles", "out_seats", "is_out", "spawn", "spawn_pending", "has_me_pos", "seats",
	"cooldowns", "emergencies_left", "move_seq", "sabotage", "spots", "bags", "bag_cap",
]

var _saved: Dictionary = {}
var _sent: Array = []


func _remember() -> void:
	for f in SESSION_FIELDS:
		_saved[f] = Session.get(f).duplicate(true) if Session.get(f) is Array or Session.get(f) is Dictionary else Session.get(f)


func _restore() -> void:
	for f in SESSION_FIELDS:
		Session.set(f, _saved[f])


func _record(kind: String) -> Callable:
	return func(arg: Variant = null) -> void: _sent.append([kind, arg])


## A round on greybox_01 with the player standing `offset` px right of the marker `at`.
func _round(role: String, at: String, offset: float) -> RoundScene:
	_remember()
	_sent.clear()
	var meta: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://world/maps/greybox_01.meta.json"))
	Session.map_id = "greybox_01"
	Session.map_version = str(meta.get("version", ""))
	Session.seat_id = 0
	Session.role = role
	Session.phase = "playing"
	Session.partner_seat = -1
	Session.is_out = false
	Session.out_seats = {}
	Session.piles = []
	Session.sabotage = {}
	Session.spots = {CONSOLE: "dirty", OTHER_CONSOLE: "dirty"}
	Session.bags = 0
	Session.bag_cap = 1
	Session.cooldowns = {}
	Session.emergencies_left = 1
	Session.tuning = {"poison": {"hit_range": HIT_RANGE_TILES, "hit_cooldown_s": 25}, "dump": {"cooldown_s": 40}}
	Session.task_types = {"sort_waste": {"min_s": 4, "consoles": [CONSOLE]}}
	Session.my_tasks = [{"id": 1, "type": "sort_waste", "steps": 1, "steps_done": 0, "console": CONSOLE}]
	Session.has_me_pos = true
	Session.spawn_pending = false
	var r: RoundScene = load("res://scenes/round/round.tscn").instantiate() as RoundScene
	track(r)
	# The runner's tree is busy in _ready, so the scene is built by hand, outside the tree.
	var map: MapRoot = r._load_map()
	r.add_child(map)
	r._map = map
	r._piles_holder = Node2D.new()
	r.add_child(r._piles_holder)
	r._avatars_holder = Node2D.new()
	r.add_child(r._avatars_holder)
	r._player = load("res://world/player/local_player.tscn").instantiate() as LocalPlayer
	r.add_child(r._player)
	r._player.position = map.marker(at).position + Vector2(offset, 0.0)
	r._minigames = load("res://minigames/minigame_layer.tscn").instantiate() as MinigameLayer
	r.add_child(r._minigames)
	for kind in ["use", "report", "hit", "dump", "call_meeting", "sabotage", "clean_start", "clean_cancel"]:
		r.senders[kind] = _record(kind)
	r.senders["fix"] = func(console: String, holding: bool) -> void: _sent.append(["fix", console, holding])
	return r


func _remote(r: RoundScene, seat: int, pos: Vector2) -> void:
	var remote: Dictionary = {}
	for s in r._avatars:
		if (r._avatars[s] as Avatar).visible:
			remote[s] = {"pos": (r._avatars[s] as Avatar).position, "color": 0, "out": false}
	remote[seat] = {"pos": pos, "color": 0, "out": false}
	r._update_remote(remote)


func test_near_console_only_inside_its_range() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, CONSOLE_RANGE_PX - 2.0)
	var near: Dictionary = r.interaction_context()["near_console"]
	assert_eq(str(near.get("id", "")), CONSOLE, "inside the range")
	assert_eq(str(near.get("kind", "")), "task", "kind")
	r._player.position = r._map.marker(CONSOLE).position + Vector2(CONSOLE_RANGE_PX + 2.0, 0.0)
	assert_true((r.interaction_context()["near_console"] as Dictionary).is_empty(), "outside the range")
	_restore()
	return true


## v2: the near task console is a spot; its state and the bag load come from Session.
func test_near_spot_state_and_bags_full_come_from_session() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	assert_eq(r.interaction_context()["near_spot_state"], "dirty", "dirty spot")
	assert_true(not bool(r.interaction_context()["bags_full"]), "0 of 1 bag")
	Session.spots[CONSOLE] = "clean"
	Session.bags = 1
	assert_eq(r.interaction_context()["near_spot_state"], "clean", "clean spot")
	assert_true(bool(r.interaction_context()["bags_full"]), "1 of 1 bag")
	Session.bag_cap = 0
	assert_true(not bool(r.interaction_context()["bags_full"]), "no cap (a Trasher): never full")
	_restore()
	return true


func test_emergency_console_is_near_with_its_kind() -> bool:
	var r: RoundScene = _round("citizen", EMERGENCY, 0.0)
	var near: Dictionary = r.interaction_context()["near_console"]
	assert_eq(str(near.get("kind", "")), "emergency", "kind")
	assert_eq(r.interaction_context()["near_spot_state"], "", "the emergency console is no spot")
	_restore()
	return true


func test_hit_target_skips_partner_out_hidden_and_far_seats() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	var here: Vector2 = r._player.position
	Session.partner_seat = 2
	Session.out_seats = {3: true}
	_remote(r, 2, here + Vector2(4.0, 0.0))
	_remote(r, 3, here + Vector2(6.0, 0.0))
	_remote(r, 4, here + Vector2(HIT_RANGE_TILES * TILE_PX + 8.0, 0.0))
	assert_eq(r._nearest_hit_target(here), -1, "partner, out seat and far seat are no targets")
	_remote(r, 5, here + Vector2(20.0, 0.0))
	assert_eq(r._nearest_hit_target(here), 5, "the one valid seat in range")
	_remote(r, 6, here + Vector2(10.0, 0.0))
	assert_eq(r._nearest_hit_target(here), 6, "the nearest valid seat")
	r._update_remote({2: {"pos": here, "color": 0, "out": false}, 3: {"pos": here, "color": 0, "out": false}})
	assert_eq(r._nearest_hit_target(here), -1, "a hidden avatar is no target")
	_restore()
	return true


func test_polluter_use_opens_a_fake_and_sends_nothing() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	r.perform_action("use")
	assert_true(r._minigames.is_open(), "the fake is open")
	assert_eq(r._minigames._console_id, CONSOLE, "fake knows its console")
	assert_eq(_sent.size(), 0, "no use is sent")
	_restore()
	return true


func test_citizen_use_sends_use_for_the_console() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	r.perform_action("use")
	assert_eq(_sent, [["use", CONSOLE]], "one use with the console id")
	assert_true(not r._minigames.is_open(), "the game opens on use_ok, not here")
	_restore()
	return true


func test_cleaner_with_full_bags_cannot_use_a_spot() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.bags = 1
	r.perform_action("use")
	assert_eq(_sent.size(), 0, "no clean while carrying a full load")
	_restore()
	return true


func test_clean_spot_offers_no_use_to_anyone() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.spots[CONSOLE] = "clean"
	r.perform_action("use")
	assert_eq(_sent.size(), 0, "a Cleaner sends nothing at a clean spot")
	Session.role = "polluter"
	r.perform_action("use")
	assert_true(not r._minigames.is_open(), "a Trasher fakes only on dirty spots")
	_restore()
	return true


func test_use_at_the_emergency_console_calls_the_meeting() -> bool:
	var r: RoundScene = _round("citizen", EMERGENCY, 0.0)
	r.perform_action("use")
	assert_eq(_sent, [["call_meeting", null]], "meeting called")
	Session.emergencies_left = 0
	_sent.clear()
	r.perform_action("use")
	assert_eq(_sent.size(), 0, "no emergencies left")
	_restore()
	return true


func test_hit_sends_the_nearest_target_only_when_ready() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	_remote(r, 5, r._player.position + Vector2(10.0, 0.0))
	r.perform_action("hit")
	assert_eq(_sent, [["hit", 5]], "hit on the target")
	_sent.clear()
	Session.cooldowns = {"hit": 3.0}
	r.perform_action("hit")
	assert_eq(_sent.size(), 0, "no hit on cooldown")
	_restore()
	return true


func test_report_and_dump_send_their_requests() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	Session.piles = [
		{"id": 9, "pos": r._player.position + Vector2(40.0, 0.0), "kind": "dump", "state": "fresh"},
		{"id": 8, "pos": r._player.position + Vector2(10.0, 0.0), "kind": "dump", "state": "old"},
	]
	r.perform_action("report")
	r.perform_action("dump")
	assert_eq(_sent, [["report", 9], ["dump", null]], "nearest fresh pile, then a dump")
	_restore()
	return true


func test_report_hit_dump_do_nothing_while_a_game_is_open() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	_remote(r, 5, r._player.position + Vector2(10.0, 0.0))
	Session.piles = [{"id": 9, "pos": r._player.position + Vector2(10.0, 0.0), "kind": "dump", "state": "fresh"}]
	r.perform_action("use")
	assert_true(r._minigames.is_open(), "fake open")
	for action in ["report", "hit", "dump"]:
		r.perform_action(action)
	assert_eq(_sent.size(), 0, "keys Q, F and R are dead under the fake")
	r._minigames.close_all()
	r.perform_action("hit")
	assert_eq(_sent, [["hit", 5]], "hit works again once it is closed")
	_restore()
	return true


func _record_move() -> Callable:
	return func(pos: Vector2, seq: int) -> void: _sent.append(["move", seq, pos])


func test_a_new_round_scene_keeps_the_move_counter_going() -> bool:
	var first: RoundScene = _round("citizen", CONSOLE, 0.0)
	var saved_first: Dictionary = _saved.duplicate()
	first.senders["move"] = _record_move()
	first._send_move()
	first._send_move()
	var seqs: Array = []
	for s in _sent:
		seqs.append(s[1])
	var second: RoundScene = _round("citizen", CONSOLE, 0.0)
	second.senders["move"] = _record_move()
	second._send_move()
	seqs.append(_sent[0][1])
	assert_eq(seqs, [0, 1, 2], "the scene after a meeting does not restart at 0")
	_saved = saved_first
	_restore()
	return true


func test_walking_input_is_ignored_while_a_game_is_open() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	r._joystick = RoundScene.Joystick.new()
	track(r._joystick)
	r._joystick.value = Vector2(1.0, 0.0)
	assert_near(r._read_direction().x, 1.0, 0.001, "the stick walks when no game is open")
	r.perform_action("use")
	assert_true(r._minigames.is_open(), "fake open")
	assert_eq(r._read_direction(), Vector2.ZERO, "no walking under an open game")
	_restore()
	return true


func test_report_is_offered_only_for_a_pile_inside_the_report_range() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	var far_px: float = RoundScene.REPORT_RANGE_FALLBACK_TILES * TILE_PX + 40.0
	Session.piles = [{"id": 4, "pos": r._player.position + Vector2(far_px, 0.0), "kind": "dump", "state": "fresh"}]
	assert_true(not bool(r.action_states()["report"]), "a visible pile 1+ tiles beyond the range: no REPORT")
	r.perform_action("report")
	assert_eq(_sent.size(), 0, "and nothing is sent")
	Session.piles = [
		{"id": 4, "pos": r._player.position + Vector2(far_px, 0.0), "kind": "dump", "state": "fresh"},
		{"id": 5, "pos": r._player.position + Vector2(2.0 * TILE_PX, 0.0), "kind": "dump", "state": "fresh"},
	]
	assert_true(bool(r.action_states()["report"]), "a pile 2 tiles away")
	r.perform_action("report")
	assert_eq(_sent, [["report", 5]], "the one inside the range")
	_restore()
	return true


func test_report_range_comes_from_tuning_when_the_server_sends_it() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [{"id": 4, "pos": r._player.position + Vector2(2.0 * TILE_PX, 0.0), "kind": "dump", "state": "fresh"}]
	Session.tuning["report_range"] = 1.5
	assert_true(not bool(r.action_states()["report"]), "tuning shrinks the range below 2 tiles")
	_restore()
	return true


# v1 fire rule, out of play in v2: the fire consoles are gone from the map, so the fire-hold tests below are
# renamed skipped_v1_* (the runner only runs test_*). Kept for reference.
const FIRE_CONSOLE: String = "fire_a"
const SMOG_PANEL: String = "smog_panel"
const FIRE: Dictionary = {"kind": "fire", "countdown": 30.0, "held": []}


func skipped_v1_test_fire_hold_sends_true_then_false_on_release() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = FIRE.duplicate()
	assert_eq(r.action_states()["fix"], {"visible": true, "kind": "fire"}, "fix offered at the fire console")
	r.begin_hold("fix")
	assert_eq(_sent, [["fix", FIRE_CONSOLE, true]], "press holds the console")
	r.end_hold("fix")
	assert_eq(_sent, [["fix", FIRE_CONSOLE, true], ["fix", FIRE_CONSOLE, false]], "release lets go")
	r.end_hold("fix")
	assert_eq(_sent.size(), 2, "a second release sends nothing")
	_restore()
	return true


func skipped_v1_test_walking_out_of_range_releases_the_fire_console() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = FIRE.duplicate()
	r.begin_hold("fix")
	r._player.position += Vector2(400.0, 0.0)
	r._sync_holds()
	assert_eq(_sent[_sent.size() - 1], ["fix", FIRE_CONSOLE, false], "release sent when fix is no longer offered")
	_restore()
	return true


func skipped_v1_test_fire_ending_releases_the_hold() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = FIRE.duplicate()
	r.begin_hold("fix")
	Session.sabotage = {}
	r._sync_holds()
	assert_eq(_sent[_sent.size() - 1], ["fix", FIRE_CONSOLE, false], "release sent when the fire is over")
	_restore()
	return true


func test_smog_fix_is_a_tap_and_repairing_shows_until_it_ends() -> bool:
	var r: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	assert_eq(r.action_states()["fix"], {"visible": true, "kind": "smog"}, "smog fix offered")
	r.begin_hold("fix")
	assert_eq(_sent, [["fix", SMOG_PANEL, false]], "one tap, no hold")
	assert_true(r.is_repairing(), "repairing label on")
	r.end_hold("fix")
	assert_eq(_sent.size(), 1, "releasing the tap sends nothing")
	assert_true(r.is_repairing(), "still repairing after the finger lifts")
	r._player.position += Vector2(400.0, 0.0)
	r._sync_holds()
	assert_true(not r.is_repairing(), "walking away ends the repair (the server cancels it)")
	_restore()
	return true


func skipped_v1_test_hold_released_on_leave() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = FIRE.duplicate()
	r.begin_hold("fix")
	Session.phase = "meeting"
	r._on_event({"t": "meeting_started"})
	assert_eq(_sent[_sent.size() - 1], ["fix", FIRE_CONSOLE, false], "the release the server is owed goes out before the scene leaves")
	assert_true(r._leaving, "the round is leaving")
	_restore()
	return true


func test_sabotage_button_sends_the_kind_when_ready() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	r.perform_action("sabotage_fire")
	r.perform_action("sabotage_smog")
	assert_eq(_sent, [["sabotage", "fire"], ["sabotage", "smog"]], "kinds as sent")
	_sent.clear()
	Session.cooldowns = {"sabotage": 5.0}
	r.perform_action("sabotage_fire")
	assert_eq(_sent.size(), 0, "no sabotage on cooldown")
	Session.cooldowns = {}
	Session.sabotage = FIRE.duplicate()
	r.perform_action("sabotage_smog")
	assert_eq(_sent.size(), 0, "no second sabotage while one runs")
	_restore()
	return true


func test_citizen_cannot_send_sabotage() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	r.perform_action("sabotage_fire")
	assert_eq(_sent.size(), 0, "a citizen's sabotage is dropped")
	_restore()
	return true


func test_polluter_cannot_send_fix() -> bool:
	var r: RoundScene = _round("polluter", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	r.begin_hold("fix")
	assert_eq(_sent.size(), 0, "a polluter's fix is dropped")
	_restore()
	return true


func _pile(r: RoundScene, id: int, dx: float, kind: String = "dump", state: String = "fresh") -> Dictionary:
	return {"id": id, "pos": r._player.position + Vector2(dx, 0.0), "kind": kind, "state": state}


func test_clean_pile_is_the_nearest_cleanable_pile_in_range() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [
		_pile(r, 1, TILE_PX * 1.5),
		_pile(r, 2, 8.0, "body"),
		_pile(r, 3, 20.0, "dump", "old"),
		_pile(r, 4, 24.0, "dump", "reported"),
		_pile(r, 5, 12.0, "dump", "fresh"),
	]
	assert_eq(int(r.interaction_context()["clean_pile"]), 5, "the nearest fresh or reported dump pile in range")
	Session.piles = [_pile(r, 1, TILE_PX * 1.5), _pile(r, 2, 8.0, "body"), _pile(r, 3, 20.0, "dump", "old")]
	assert_eq(int(r.interaction_context()["clean_pile"]), -1, "out of range, a poison pile and a removed pile are not cleanable")
	Session.tuning["dump"]["clean_range"] = 2.0
	Session.piles = [_pile(r, 1, TILE_PX * 1.5)]
	assert_eq(int(r.interaction_context()["clean_pile"]), 1, "the range comes from tuning when the server sends it")
	_restore()
	return true


func test_report_and_clean_both_offered() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	var states: Dictionary = r.action_states()
	assert_true(bool(states["report"]), "report offered")
	assert_true(bool(states["clean"]), "clean offered")
	r.perform_action("report")
	assert_eq(_sent, [["report", 7]], "REPORT sends the report only")
	_sent.clear()
	r.begin_hold("clean")
	assert_eq(_sent, [["clean_start", 7]], "CLEAN sends the clean start only")
	_restore()
	return true


func test_clean_hold_cancels_on_release_unless_done() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	assert_true(r.clean_progress()["active"], "ring running after clean_ok")
	assert_near(float(r.clean_progress()["duration"]), 3.0, 0.001, "ring length from the server")
	r.end_hold("clean")
	assert_eq(_sent, [["clean_start", 7], ["clean_cancel", 7]], "release cancels")
	assert_true(not r.clean_progress()["active"], "ring gone")
	_sent.clear()
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	r._on_clean_done({"pile": 7})
	r.end_hold("clean")
	assert_eq(_sent, [["clean_start", 7]], "after clean_done the release owes nothing")
	_restore()
	return true


func test_clean_state_clears_on_cancel_and_rejection() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	r._on_clean_cancelled({"pile": 7})
	assert_true(not r.clean_progress()["active"], "server cancelled")
	_sent.clear()
	r.begin_hold("clean")
	r._on_action_rejected({"action": "clean_start", "reason": "already_cleaning"})
	r.end_hold("clean")
	assert_eq(_sent, [["clean_start", 7]], "a refused start leaves nothing to cancel")
	_restore()
	return true


func test_walking_out_of_range_cancels_the_clean() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	r._player.position += Vector2(200.0, 0.0)
	r._sync_holds()
	assert_eq(_sent[_sent.size() - 1], ["clean_cancel", 7], "cancel sent when the pile is out of reach")
	_restore()
	return true


func test_clean_cancelled_on_leave() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	Session.phase = "meeting"
	r._on_event({"t": "meeting_started"})
	assert_eq(_sent[_sent.size() - 1], ["clean_cancel", 7], "the cancel the server is owed goes out before the scene leaves")
	assert_true(r._leaving, "the round is leaving")
	_restore()
	return true


func test_polluter_cannot_clean() -> bool:
	var r: RoundScene = _round("polluter", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 10.0)]
	r.begin_hold("clean")
	assert_eq(_sent.size(), 0, "a polluter's clean is dropped")
	_restore()
	return true


func _fire_with_held(held: Array) -> Dictionary:
	return {"kind": "fire", "countdown": 30.0, "held": held}


func skipped_v1_test_fix_rejection_clears_the_hold_and_the_repair() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = _fire_with_held([])
	r.begin_hold("fix")
	r._on_action_rejected({"action": "fix", "reason": "not_allowed"})
	assert_true(not bool(r.interaction_context()["holding_fire"]), "a refused hold is not kept")
	var s: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	s.begin_hold("fix")
	s._on_action_rejected({"action": "fix", "reason": "out_of_range"})
	assert_true(not s.is_repairing(), "a refused smog tap is not repairing")
	_restore()
	return true


func skipped_v1_test_hold_missing_from_the_snapshot_is_resent_once_then_dropped() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = _fire_with_held([])
	r.begin_hold("fix")
	r._sync_holds(RoundScene.HOLD_RECONCILE_S * 0.5)
	assert_eq(_sent, [["fix", FIRE_CONSOLE, true]], "inside the grace time nothing is re-sent")
	r._sync_holds(RoundScene.HOLD_RECONCILE_S)
	assert_eq(_sent, [["fix", FIRE_CONSOLE, true], ["fix", FIRE_CONSOLE, true]], "re-sent once when the server list lacks it")
	assert_true(bool(r.interaction_context()["holding_fire"]), "still holding while the re-send is in flight")
	r._sync_holds(RoundScene.HOLD_RECONCILE_S * 1.5)
	assert_eq(_sent.size(), 2, "not re-sent a second time")
	assert_true(not bool(r.interaction_context()["holding_fire"]), "dropped when it is still missing")
	_restore()
	return true


func skipped_v1_test_hold_listed_by_the_server_is_kept() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = _fire_with_held([])
	r.begin_hold("fix")
	Session.sabotage = _fire_with_held([FIRE_CONSOLE])
	for i in 5:
		r._sync_holds(RoundScene.HOLD_RECONCILE_S)
	assert_eq(_sent, [["fix", FIRE_CONSOLE, true]], "nothing re-sent")
	assert_true(bool(r.interaction_context()["holding_fire"]), "still holding")
	_restore()
	return true


func test_smog_repair_line_ends_when_the_repair_time_passes_without_an_end() -> bool:
	var r: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	r.begin_hold("fix")
	r._sync_holds(RoundScene.SMOG_FIX_FALLBACK_S * 0.5)
	assert_true(r.is_repairing(), "still repairing inside the repair time")
	r._sync_holds(RoundScene.SMOG_FIX_FALLBACK_S + RoundScene.SMOG_REPAIR_GRACE_S)
	assert_true(not r.is_repairing(), "the server stopped repairing: the line goes")
	r.begin_hold("fix")
	assert_true(r.is_repairing(), "and a new tap starts again")
	_restore()
	return true


func test_clean_keeps_going_when_a_nearer_pile_appears() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 12.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	Session.piles = [_pile(r, 7, 12.0), _pile(r, 8, 4.0)]
	r._sync_holds()
	assert_eq(_sent, [["clean_start", 7]], "the server keeps cleaning pile 7, so no cancel")
	assert_true(r.clean_progress()["active"], "ring goes on")
	_restore()
	return true


func test_a_just_cleaned_pile_is_not_offered_again() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	Session.piles = [_pile(r, 7, 12.0)]
	r.begin_hold("clean")
	r._on_clean_ok({"pile": 7, "duration": 3.0})
	r._on_clean_done({"pile": 7})
	assert_eq(int(r.interaction_context()["clean_pile"]), -1, "pile 7 is gone until the snapshot says so")
	_restore()
	return true


func skipped_v1_test_lobby_and_update_leave_release_the_holds() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = _fire_with_held([])
	r.begin_hold("fix")
	r._on_lobby({})
	assert_eq(_sent[_sent.size() - 1], ["fix", FIRE_CONSOLE, false], "the lobby path releases too")
	_restore()
	return true


func skipped_v1_test_window_focus_loss_ends_the_key_holds() -> bool:
	var r: RoundScene = _round("citizen", FIRE_CONSOLE, 0.0)
	Session.sabotage = _fire_with_held([])
	var hud: Hud = track(Hud.new()) as Hud
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(hud)
	hud.bind(r)
	r.begin_hold("fix")
	hud._notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
	assert_eq(_sent[_sent.size() - 1], ["fix", FIRE_CONSOLE, false], "focus loss lets go of the console")
	_restore()
	return true


## The server may have renamed us on join (a name already in the room gets " 2"): the round shows the
## name the server sent, not the one typed in the menu.
func test_own_name_is_the_one_the_server_sent() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	var typed: String = Settings.player_name
	Settings.player_name = "Sam"
	Session.seats = [{"seat": 0, "name": "Sam 2", "color": 0, "human": true, "out": false}]
	var shown: String = r._own_name()
	Settings.player_name = typed
	_restore()
	assert_eq(shown, "Sam 2", "the server's name wins")
	return true


## The ring and the percent follow the server's own count (snapshot sabotage.repair_t, sent to the
## repairer only), never a local timer: a repair the server dropped must not keep filling.
func test_smog_repair_progress_follows_the_servers_count() -> bool:
	var r: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	Session.tuning["sabotage"] = {"smog_fix_s": 4.0}
	r.begin_hold("fix")
	assert_true(r.is_repairing(), "the tap shows the repairing line at once")
	assert_eq(r.repair_progress(), {"active": false, "elapsed": 0.0, "duration": 4.0}, "but no progress before the server counts")
	r._sync_holds(1.0)  # local time passing changes nothing
	assert_eq(r.repair_progress()["elapsed"], 0.0, "no local timer")
	Session.sabotage = {"kind": "smog", "repair_t": 1.5}
	r._sync_holds(0.05)
	assert_eq(r.repair_progress(), {"active": true, "elapsed": 1.5, "duration": 4.0}, "the server's 1.5 s of 4")
	Session.sabotage = {"kind": "smog", "repair_t": 4.2}
	r._sync_holds(0.05)
	assert_eq(r.repair_progress()["elapsed"], 4.0, "never beyond the duration")
	_restore()
	return true


func test_smog_repair_the_server_dropped_stops_showing_progress() -> bool:
	var r: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	r.begin_hold("fix")
	Session.sabotage = {"kind": "smog", "repair_t": 2.0}
	r._sync_holds(0.05)
	assert_true(bool(r.repair_progress()["active"]), "counting")
	Session.sabotage = {"kind": "smog"}  # the server's position left the panel range: no repair_t any more
	r._sync_holds(0.05)
	assert_true(not r.is_repairing(), "the line goes at once")
	assert_true(not bool(r.repair_progress()["active"]), "and so does the ring")
	_restore()
	return true


func test_a_repeat_tap_does_not_restart_the_smog_repair() -> bool:
	var r: RoundScene = _round("citizen", SMOG_PANEL, 0.0)
	Session.sabotage = {"kind": "smog"}
	r.begin_hold("fix")
	Session.sabotage = {"kind": "smog", "repair_t": 1.5}
	r._sync_holds(0.05)
	r.begin_hold("fix")
	assert_eq(_sent, [["fix", SMOG_PANEL, false]], "the second tap sends nothing: the server keeps its timer")
	assert_eq(r.repair_progress()["elapsed"], 1.5, "and the progress stays")
	_restore()
	return true


## One SpotView per task console of the map (11 on greybox_01), each following Session.spots.
func test_spot_views_follow_the_session_state() -> bool:
	var r: RoundScene = _round("citizen", CONSOLE, 0.0)
	r._build_spots()
	assert_eq(r._spot_views.size(), 11, "one per task console")
	var v: SpotView = r._spot_views[CONSOLE] as SpotView
	assert_eq(v.state, "dirty", "starts dirty")
	assert_eq(v.art_type, "sort_waste", "art named after the mini-game")
	Session.spots[CONSOLE] = "clean"
	r._update_spots()
	assert_eq(v.state, "clean", "follows the snapshot")
	assert_eq(v.modulate, Color.WHITE, "no dirt tint once clean")
	_restore()
	return true
