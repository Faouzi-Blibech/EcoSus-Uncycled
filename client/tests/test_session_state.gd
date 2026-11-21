extends "res://tests/test_case.gd"
## Session match state, driven by calling the Net handlers directly.

const TEST_SAVE: String = "user://test_session.cfg"
const TEST_EJECTIONS: String = "user://test_ejections.cfg"


func _session() -> Object:
	var s: Object = track(load("res://autoload/session.gd").new())
	s.save_path = TEST_SAVE
	s.ejections_path = TEST_EJECTIONS
	return s


func _cleanup_save() -> void:
	for path in [TEST_SAVE, TEST_EJECTIONS]:
		if FileAccess.file_exists(path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(path))


## A running match for room ABCD and token tok, with the tuning the welcome carries.
func _in_match(s: Object) -> void:
	s._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2}})
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})


func test_polluters_left_counts_ejections() -> bool:
	var s: Object = _session()
	_in_match(s)
	assert_eq(s.polluters_left(), 2, "none ejected")
	s._on_event({"t": "ejection", "seat": 4, "was_polluter": false})
	assert_eq(s.polluters_left(), 2, "a citizen does not count")
	s._on_event({"t": "ejection", "seat": 5, "was_polluter": true})
	assert_eq(s.polluters_left(), 1, "one polluter out")
	s._on_event({"t": "ejection", "seat": 6, "was_polluter": null})
	assert_eq(s.polluters_left(), -1, "an unrevealed role makes the count unknown: hidden, not stale")
	s._on_event({"t": "ejection", "seat": 3, "was_polluter": true, "polluters_left": 0})
	assert_eq(s.polluters_left(), 0, "the server count is preferred")
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	assert_eq(s.polluters_left(), 2, "a new match starts again")
	_cleanup_save()
	return true


func test_ejection_count_survives_rejoin() -> bool:
	var a: Object = _session()
	_in_match(a)
	a._on_event({"t": "ejection", "seat": 5, "was_polluter": true})
	var b: Object = _session()
	b._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2},
		"state": {"phase": "playing", "status": "active", "tasks": []}})
	assert_eq(b.polluters_left(), 1, "same room and token: restored from the file")
	var c: Object = _session()
	c._on_welcome({"seat": 1, "room": "ABCD", "token": "other", "tuning": {"polluters": 2},
		"state": {"phase": "playing", "status": "active", "tasks": []}})
	assert_eq(c.polluters_left(), -1, "another token: not this seat's count")
	_cleanup_save()
	return true


func test_ejection_missed_while_away_hides_the_count() -> bool:
	var a: Object = _session()
	_in_match(a)
	a._on_event({"t": "ejection", "seat": 5, "was_polluter": true})
	var state: Dictionary = {"phase": "playing", "status": "active", "tasks": []}
	var same: Object = _session()
	same._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2}, "state": state,
		"seats": [{"seat": 5, "name": "E", "color": 0, "out": true}, {"seat": 6, "name": "F", "color": 1, "out": false}]})
	assert_eq(same.polluters_left(), 1, "the same seats are out as when it was saved")
	var missed: Object = _session()
	missed._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2}, "state": state,
		"seats": [{"seat": 5, "name": "E", "color": 0, "out": true}, {"seat": 6, "name": "F", "color": 1, "out": true}]})
	assert_eq(missed.polluters_left(), -1, "seat 6 went out while this client was away: unknown")
	_cleanup_save()
	return true


func test_a_seat_out_by_poison_is_saved_too() -> bool:
	var a: Object = _session()
	_in_match(a)
	a._on_snapshot({"s": 1.0, "players": [{"seat": 4, "pos": Vector2.ZERO, "color": 0, "out": true}]})
	var b: Object = _session()
	b._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2},
		"state": {"phase": "playing", "status": "active", "tasks": []},
		"seats": [{"seat": 4, "name": "D", "color": 0, "out": true}]})
	assert_eq(b.polluters_left(), 2, "a poisoned seat seen live is not a missed ejection")
	_cleanup_save()
	return true


func test_a_new_match_in_the_same_room_resets_the_saved_count() -> bool:
	var a: Object = _session()
	_in_match(a)
	a._on_event({"t": "ejection", "seat": 5, "was_polluter": true})
	a._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	var b: Object = _session()
	b._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2},
		"state": {"phase": "playing", "status": "active", "tasks": []}})
	assert_eq(b.polluters_left(), 2, "the file holds the new match's 0, not the old 1")
	_cleanup_save()
	return true


func test_the_ejections_file_keeps_only_the_current_room() -> bool:
	var a: Object = _session()
	a._on_welcome({"seat": 1, "room": "OLD1", "token": "t1", "tuning": {"polluters": 2}})
	a._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	var b: Object = _session()
	b._on_welcome({"seat": 1, "room": "NEW2", "token": "t2", "tuning": {"polluters": 2}})
	b._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	var cfg: ConfigFile = ConfigFile.new()
	assert_eq(cfg.load(TEST_EJECTIONS), OK, "file written")
	var keys: PackedStringArray = cfg.get_section_keys("ejections")
	assert_true(not keys.has("OLD1:t1"), "the old room is pruned: " + str(keys))
	assert_true(keys.has("NEW2:t2"), "the current room is kept: " + str(keys))
	_cleanup_save()
	return true


func test_polluters_left_unknown_on_new_device() -> bool:
	var s: Object = _session()
	_cleanup_save()
	s._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tuning": {"polluters": 2},
		"state": {"phase": "playing", "status": "active", "tasks": []}})
	assert_eq(s.polluters_left(), -1, "nothing saved: unknown, so the UI hides it")
	s._on_event({"t": "ejection", "seat": 5, "was_polluter": true})
	assert_eq(s.polluters_left(), -1, "still unknown after one more ejection")
	s._on_event({"t": "ejection", "seat": 6, "was_polluter": true, "polluters_left": 0})
	assert_eq(s.polluters_left(), 0, "unless the server says it")
	_cleanup_save()
	return true


func test_task_progress_moves_to_next_console() -> bool:
	var s: Object = _session()
	s.task_types = {"turn_compost": {"consoles": ["garden_compost", "garden_tap"]}}
	s.my_tasks = [{"id": 5, "type": "turn_compost", "steps": 2, "steps_done": 0, "console": "garden_compost"}]
	s._on_event({"t": "task_progress", "task": 5, "steps_done": 1})
	assert_eq(str(s.task_by_id(5).get("console")), "garden_tap", "console advanced")
	assert_eq(int(s.my_task_at_console("garden_tap").get("id", -1)), 5, "found at new console")
	assert_true(s.my_task_at_console("garden_compost").is_empty(), "old console empty")
	return true


func test_welcome_state_restores_meeting() -> bool:
	var s: Object = _session()
	s._on_welcome({
		"seat": 1, "room": "ABCD", "token": "tok", "map_version": "v3", "phase": "meeting",
		"tasks": [{"type": "sort_waste", "category": "x", "steps": 1, "min_s": 4.0, "consoles": ["market_bins"]}],
		"state": {"phase": "meeting", "role": "citizen", "status": "active", "poison": 34, "tasks": [],
			"cooldowns": {"hit": 0, "dump": 7, "sabotage": 0}, "emergencies_left": 1,
			"meeting": {"stage": "voting", "ends_in": 12.0, "clues": [], "voted": [1, 4], "my_vote": null, "task_bar": 0.42}},
	})
	assert_eq(str(s.meeting.get("stage")), "voting", "stage")
	assert_eq(s.poison, 34, "poison")
	assert_eq(s.is_out, false, "not out")
	assert_eq(s.map_version, "v3", "map_version")
	assert_true(s.task_types.has("sort_waste"), "task types keyed by type")
	assert_near(s.min_s_for("sort_waste"), 4.0, 0.001, "min_s")
	assert_eq(s.emergencies_left, 1, "emergencies")
	assert_eq(s.meeting.get("voted"), [1, 4], "voted restored")
	assert_near(float(s.meeting.get("ends_at", 0.0)) - Net.local_time(), 12.0, 0.5, "ends_in became a local deadline")
	assert_true(not s.meeting.has("ends_in"), "ends_in dropped")
	assert_eq(s.cooldowns.get("dump"), 7, "cooldowns restored")
	_cleanup_save()
	return true


func test_welcome_seats_set_names_and_out() -> bool:
	var s: Object = _session()
	s._on_welcome({
		"seat": 0, "room": "ABCD", "token": "tok", "phase": "lobby",
		"seats": [{"seat": 0, "name": "Ann", "color": 1, "out": false}, {"seat": 4, "name": "Bo", "color": 2, "out": true}],
	})
	assert_eq(s.seat_name(0), "Ann", "name")
	assert_eq(s.seat_out(4), true, "bo out")
	assert_eq(s.seat_out(0), false, "ann in")
	_cleanup_save()
	return true


func test_saved_seats_survive_reload() -> bool:
	var a: Object = _session()
	a.room_code = "ABCD"
	a.reconnect_token = "tok"
	a._on_lobby({"seats": [{"seat": 0, "name": "Ann", "color": 1, "out": false}, {"seat": 4, "name": "Bo", "color": 2, "out": true}]})
	var b: Object = _session()
	assert_true(b.load_saved(), "load_saved")
	b._on_welcome({"seat": 0, "room": "ABCD", "token": "tok", "phase": "playing", "state": {"phase": "playing", "status": "active", "tasks": []}})
	assert_eq(b.seat_name(4), "Bo", "name survives")
	assert_eq(b.seat_out(4), true, "out survives")
	assert_eq(b.seat_out(0), false, "ann in")
	_cleanup_save()
	return true


func test_snapshot_players_out_fill_out_seats() -> bool:
	var s: Object = _session()
	s._on_snapshot({"s": 1.0, "players": [{"seat": 2, "pos": Vector2.ZERO, "color": 0, "out": true}]})
	assert_eq(s.seat_out(2), true, "snapshot out")
	return true


func test_ejection_marks_out() -> bool:
	var s: Object = _session()
	s._on_event({"t": "ejection", "seat": 6, "was_polluter": true})
	assert_eq(s.seat_out(6), true, "ejected out")
	return true


func test_match_started_seats_replace_the_seat_list() -> bool:
	var s: Object = _session()
	s.seats = [{"seat": 0, "name": "Ana", "color": 0, "human": true}]
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO, "seats": [
		{"seat": 0, "name": "Ana", "color": 0, "human": true, "out": false},
		{"seat": 1, "name": "Bot 2", "color": 3, "human": false, "out": false}]})
	assert_eq(s.seat_name(1), "Bot 2", "bot name known")
	_cleanup_save()
	return true


func test_match_started_without_seats_keeps_the_seat_list() -> bool:
	var s: Object = _session()
	s.seats = [{"seat": 0, "name": "Ana", "color": 0, "human": true}]
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	assert_eq(s.seat_name(0), "Ana", "seat list kept")
	_cleanup_save()
	return true


func test_welcome_tasks_array_fills_task_types() -> bool:
	var s: Object = _session()
	var defs: Array = [{"type": "sort_waste", "steps": 1, "min_s": 4.0, "consoles": ["market_bins"]}]
	s._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tasks": defs})
	assert_eq(s.min_s_for("sort_waste"), 4.0, "array shape, as the server sends it")
	s._on_welcome({"seat": 1, "room": "ABCD", "token": "tok", "tasks": {"tasks": defs}})
	assert_eq(s.console_for_step("sort_waste", 0), "market_bins", "object shape, as content/tasks.json")
	return true


func test_out_cause_is_kept_for_the_next_scene_and_reset_with_the_match() -> bool:
	var s: Object = _session()
	assert_eq(s.out_cause, "", "none at the start")
	s._on_event({"t": "you_are_out", "cause": "poison"})
	assert_eq(s.out_cause, "poison", "you_are_out cause")
	s._on_event({"t": "meeting_started", "cause": "report"})
	assert_eq(s.out_cause, "poison", "kept across a meeting")
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	assert_eq(s.out_cause, "", "reset with the match state")
	_cleanup_save()
	return true


func test_own_ejection_sets_the_vote_cause() -> bool:
	var s: Object = _session()
	s.seat_id = 3
	s._on_event({"t": "ejection", "seat": 5, "was_polluter": false})
	assert_eq(s.out_cause, "", "someone else ejected")
	s._on_event({"t": "ejection", "seat": 3, "was_polluter": false})
	assert_eq(s.out_cause, "vote", "my own ejection")
	assert_eq(s.is_out, true, "out")
	return true


func test_move_seq_counts_up_across_meetings_and_resets_only_on_welcome_or_match_start() -> bool:
	var s: Object = _session()
	assert_eq([s.next_move_seq(), s.next_move_seq(), s.next_move_seq()], [0, 1, 2], "counts from 0")
	s._on_event({"t": "meeting_started", "cause": "report", "by": 1})
	s._on_event({"t": "round_resumed", "spawn": Vector2.ZERO})
	assert_eq(s.next_move_seq(), 3, "a meeting and its resume keep the counter (the server keeps its last seq)")
	s._on_event({"t": "meeting_started", "cause": "emergency", "by": 2})
	s._on_event({"t": "round_resumed", "spawn": Vector2.ZERO})
	assert_eq(s.next_move_seq(), 4, "several meetings in one match: still counting up")
	s._on_welcome({"seat": 0, "room": "ABCD", "token": "t"})
	assert_eq(s.next_move_seq(), 0, "welcome restarts it: the server resets the seat on rejoin")
	s.next_move_seq()
	s._on_match_started({"role": "citizen"})
	assert_eq(s.next_move_seq(), 0, "a new match restarts it: the server built a new engine")
	_cleanup_save()
	return true


func test_lobby_message_moves_the_host() -> bool:
	var s: Object = _session()
	s.seat_id = 3
	s.host_seat = 0
	assert_true(not s.is_host(), "seat 3 is not host at first")
	s._on_lobby({"seats": [], "host_seat": 3})
	assert_eq(s.host_seat, 3, "the host from the lobby message")
	assert_true(s.is_host(), "I am host now and see Start")
	s._on_lobby({"seats": []})
	assert_eq(s.host_seat, 3, "a lobby without the field keeps the host")
	_cleanup_save()
	return true


func test_meeting_deadline_ignores_the_server_clock_offset() -> bool:
	# A rejoin right after start: the first pong or snapshot has not set the offset yet and it
	# jumps later, so the deadline must not depend on it.
	var s: Object = _session()
	var saved: float = Net._offset_pong
	Net._offset_pong = 5000.0
	s._on_welcome({"seat": 1, "room": "ABCD", "token": "t", "state": {"phase": "meeting", "status": "active",
		"meeting": {"stage": "discussion", "ends_in": 40.0}}})
	assert_near(float(s.meeting["ends_at"]) - Net.local_time(), 40.0, 0.5, "welcome")
	s._on_event({"t": "meeting_started", "discussion_s": 60.0})
	assert_near(float(s.meeting["ends_at"]) - Net.local_time(), 60.0, 0.5, "meeting_started")
	s._on_event({"t": "voting_started", "voting_s": 30.0})
	assert_near(float(s.meeting["ends_at"]) - Net.local_time(), 30.0, 0.5, "voting_started")
	Net._offset_pong = saved
	_cleanup_save()
	return true


func test_lost_seat_notice_only_when_a_match_was_running() -> bool:
	var s: Object = _session()
	s.room_code = "ABCD"
	s.reconnect_token = "tok"
	s.phase = "playing"
	s._save_saved()
	var fresh: Object = _session()
	assert_true(fresh.load_saved(), "saved file loads")
	assert_eq(fresh.lost_seat_notice(), "MENU_SESSION_EXPIRED", "a match was running: say the game ended")
	s.phase = "lobby"
	s._save_saved()
	var lobby_only: Object = _session()
	lobby_only.load_saved()
	assert_eq(lobby_only.lost_seat_notice(), "", "the room was only a lobby: no scary notice")
	s.phase = "ended"
	s._save_saved()
	var after: Object = _session()
	after.load_saved()
	assert_eq(after.lost_seat_notice(), "", "the match had ended")
	_cleanup_save()
	return true


func test_banner_from_snapshot() -> bool:
	var s: Object = _session()
	s._on_snapshot({"s": 1.0, "sabotage": {"kind": "fire", "countdown": 31.0, "held": ["fire_a"]}})
	assert_eq(str(s.sabotage.get("kind")), "fire", "kind from the snapshot")
	assert_near(float(s.sabotage.get("countdown")), 31.0, 0.001, "countdown")
	assert_eq(s.sabotage.get("held"), ["fire_a"], "held consoles")
	s._on_snapshot({"s": 2.0, "sabotage": {"kind": "fire", "countdown": 30.0, "held": []}})
	assert_near(float(s.sabotage.get("countdown")), 30.0, 0.001, "the next snapshot replaces it")
	s._on_event({"t": "sabotage_ended", "kind": "fire", "result": "fixed"})
	assert_true(s.sabotage.is_empty(), "cleared on sabotage_ended")
	s._on_snapshot({"s": 3.0, "sabotage": {"kind": "smog", "smog_level": 0.5}})
	s._on_snapshot({"s": 4.0, "sabotage": null})
	assert_true(s.sabotage.is_empty(), "a null sabotage clears it")
	return true


func test_sabotage_from_started_event_and_rejoin_welcome() -> bool:
	var s: Object = _session()
	s._on_event({"t": "sabotage_started", "kind": "fire", "countdown": 45.0})
	assert_eq(str(s.sabotage.get("kind")), "fire", "started event shows the banner before the next snapshot")
	assert_near(float(s.sabotage.get("countdown")), 45.0, 0.001, "countdown from the event")
	var r: Object = _session()
	r._on_welcome({
		"seat": 1, "room": "ABCD", "token": "tok", "phase": "playing",
		"state": {"phase": "playing", "role": "citizen", "status": "active", "tasks": [],
			"sabotage": {"kind": "fire", "countdown": 22.0, "held": ["fire_b"]}},
	})
	assert_eq(str(r.sabotage.get("kind")), "fire", "a rejoin restores the sabotage from welcome.state")
	assert_eq(r.sabotage.get("held"), ["fire_b"], "held")
	r._on_match_started({"role": "citizen"})
	assert_true(r.sabotage.is_empty(), "a new match starts clean")
	_cleanup_save()
	return true


## A rejoining client's timer is right at once: welcome.state carries the deadline clock like a snapshot,
## and the deadline falls back to the tuning until the first snapshot sends it.
func test_welcome_state_restores_the_deadline_clock() -> bool:
	var s: Object = _session()
	s._on_welcome({
		"seat": 0, "room": "ABCD", "token": "tok", "phase": "playing",
		"tuning": {"match": {"deadline_s": 600}},
		"state": {"phase": "playing", "status": "active", "tasks": [], "match_t": 212.4, "clock_t": 98.5},
	})
	assert_eq(s.clock_t, 98.5, "clock_t from the state")
	assert_eq(s.match_t, 212.4, "match_t as before")
	assert_eq(s.deadline_t, 600.0, "deadline from tuning until a snapshot")
	s._on_snapshot({"match_t": 213.0, "clock_t": 99.0, "deadline_t": 300.0})
	assert_eq(s.deadline_t, 300.0, "a snapshot's deadline wins")
	_cleanup_save()
	return true


func test_snapshot_smog_level_is_kept_and_resets() -> bool:
	var s: Object = _session()
	s._on_snapshot({"s": 1.0, "me": {"pos": Vector2.ZERO, "vision": 2.0, "smog_level": 0.4}})
	assert_eq(s.smog_level, 0.4, "from me.smog_level")
	s._on_snapshot({"s": 1.1, "me": {"pos": Vector2.ZERO, "vision": 4.0}})
	assert_eq(s.smog_level, 0.0, "absent means clear air")
	return true
