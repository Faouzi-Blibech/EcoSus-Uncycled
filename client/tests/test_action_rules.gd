extends "res://tests/test_case.gd"
## ActionRules.button_states: which action buttons show and whether HIT / DUMP are ready.

const RULES_PATH: String = "res://ui/hud/action_rules.gd"


## A playing citizen with nothing nearby; each test overrides what it needs.
func _ctx(over: Dictionary = {}) -> Dictionary:
	var ctx: Dictionary = {
		"phase": "playing", "role": "citizen", "is_out": false,
		"near_console": {}, "has_task_here": false, "fresh_pile": -1, "hit_target": -1,
		"cd": {"hit": 0.0, "dump": 0.0}, "emergencies_left": 2,
	}
	ctx.merge(over, true)
	return ctx


func _states(over: Dictionary = {}) -> Dictionary:
	return load(RULES_PATH).button_states(_ctx(over))


func test_citizen_at_own_task_console() -> bool:
	var s: Dictionary = _states({"near_console": {"id": "market_bins", "kind": "task"}, "has_task_here": true})
	assert_eq(s["use"], true, "use")
	assert_eq(s["hit"]["visible"], false, "hit hidden")
	assert_eq(s["dump"]["visible"], false, "dump hidden")
	return true


func test_citizen_at_console_without_task() -> bool:
	var s: Dictionary = _states({"near_console": {"id": "market_bins", "kind": "task"}})
	assert_eq(s["use"], false, "no task here")
	return true


func test_polluter_at_task_console() -> bool:
	var s: Dictionary = _states({"role": "polluter", "near_console": {"id": "market_bins", "kind": "task"}})
	assert_eq(s["use"], true, "use for the fake")
	assert_eq(s["hit"]["visible"], true, "hit visible")
	assert_eq(s["hit"]["ready"], false, "no target")
	assert_eq(s["dump"]["visible"], true, "dump visible")
	assert_eq(s["dump"]["ready"], true, "dump ready")
	return true


func test_polluter_hit_on_cooldown_with_target() -> bool:
	var s: Dictionary = _states({"role": "polluter", "hit_target": 3, "cd": {"hit": 12.5, "dump": 0.0}})
	assert_eq(s["hit"]["visible"], true, "visible")
	assert_eq(s["hit"]["ready"], false, "cooling down")
	assert_eq(s["hit"]["cooldown"], 12.5, "cooldown value")
	return true


func test_emergency_console_without_emergencies_left() -> bool:
	var near: Dictionary = {"id": "meeting_btn", "kind": "emergency"}
	assert_eq(_states({"near_console": near, "emergencies_left": 0})["use"], false, "none left")
	assert_eq(_states({"near_console": near, "emergencies_left": 1})["use"], true, "one left")
	return true


func test_meeting_phase_hides_everything() -> bool:
	var s: Dictionary = _states({
		"phase": "meeting", "role": "polluter", "near_console": {"id": "market_bins", "kind": "task"},
		"fresh_pile": 7, "hit_target": 2})
	assert_eq(s["use"], false, "use")
	assert_eq(s["report"], false, "report")
	assert_eq(s["hit"]["visible"], false, "hit")
	assert_eq(s["dump"]["visible"], false, "dump")
	return true


func test_out_player_hides_everything() -> bool:
	var s: Dictionary = _states({
		"is_out": true, "role": "polluter", "near_console": {"id": "market_bins", "kind": "task"},
		"fresh_pile": 7, "hit_target": 2})
	assert_eq(s["use"], false, "use")
	assert_eq(s["report"], false, "report")
	assert_eq(s["hit"]["visible"], false, "hit")
	assert_eq(s["dump"]["visible"], false, "dump")
	return true


func test_fresh_pile_enables_report() -> bool:
	assert_eq(_states({"fresh_pile": 7})["report"], true, "report")
	assert_eq(_states()["report"], false, "no pile")
	return true


const FIRE: Dictionary = {"kind": "fire", "countdown": 30.0, "held": []}
const SMOG: Dictionary = {"kind": "smog"}


func test_polluter_sabotage_ready_when_cd_zero() -> bool:
	var s: Dictionary = _states({"role": "polluter", "cd": {"hit": 0.0, "dump": 0.0, "sabotage": 0.0}})
	assert_eq(s["sabotage"]["visible"], true, "visible")
	assert_eq(s["sabotage"]["ready"], true, "ready")
	var cooling: Dictionary = _states({"role": "polluter", "cd": {"hit": 0.0, "dump": 0.0, "sabotage": 12.0}})
	assert_eq(cooling["sabotage"]["ready"], false, "cooling down")
	assert_eq(cooling["sabotage"]["cooldown"], 12.0, "cooldown value")
	assert_eq(_states()["sabotage"]["visible"], false, "a citizen has no sabotage button")
	return true


func test_polluter_sabotage_hidden_during_active_sabotage() -> bool:
	var s: Dictionary = _states({"role": "polluter", "sabotage": FIRE})
	assert_eq(s["sabotage"]["visible"], false, "hidden while a sabotage runs")
	assert_eq(s["sabotage"]["ready"], false, "and not ready")
	return true


func test_citizen_fix_fire_at_fire_console_during_fire() -> bool:
	var s: Dictionary = _states({"near_console": {"id": "fire_a", "kind": "fire_fix"}, "sabotage": FIRE})
	assert_eq(s["fix"], {"visible": true, "kind": "fire"}, "fix offered")
	assert_eq(s["use"], false, "use stays off at a fire console")
	return true


func test_polluter_no_fix_at_fire_console() -> bool:
	var s: Dictionary = _states({"role": "polluter", "near_console": {"id": "fire_a", "kind": "fire_fix"}, "sabotage": FIRE})
	assert_eq(s["fix"]["visible"], false, "a polluter cannot fix")
	assert_eq(s["use"], false, "use stays off")
	return true


func test_citizen_no_fix_without_sabotage() -> bool:
	var s: Dictionary = _states({"near_console": {"id": "fire_a", "kind": "fire_fix"}})
	assert_eq(s["fix"]["visible"], false, "no fire, no fix")
	var panel: Dictionary = _states({"near_console": {"id": "smog_panel", "kind": "smog_fix"}, "sabotage": FIRE})
	assert_eq(panel["fix"]["visible"], false, "a fire does not make the smog panel usable")
	var fire_panel: Dictionary = _states({"near_console": {"id": "fire_a", "kind": "fire_fix"}, "sabotage": SMOG})
	assert_eq(fire_panel["fix"]["visible"], false, "smog does not make a fire console usable")
	return true


func test_citizen_fix_smog_at_panel() -> bool:
	var s: Dictionary = _states({"near_console": {"id": "smog_panel", "kind": "smog_fix"}, "sabotage": SMOG})
	assert_eq(s["fix"], {"visible": true, "kind": "smog"}, "smog fix offered")
	return true


func test_out_player_has_no_sabotage_or_fix() -> bool:
	var s: Dictionary = _states({"is_out": true, "near_console": {"id": "fire_a", "kind": "fire_fix"}, "sabotage": FIRE})
	assert_eq(s["fix"]["visible"], false, "fix")
	assert_eq(s["sabotage"]["visible"], false, "sabotage")
	return true


func test_citizen_clean_near_dump_pile() -> bool:
	assert_eq(_states({"clean_pile": 4})["clean"], true, "clean offered")
	assert_eq(_states({"clean_pile": 0})["clean"], true, "pile id 0 counts")
	assert_eq(_states()["clean"], false, "no pile in range")
	return true


func test_polluter_no_clean() -> bool:
	assert_eq(_states({"role": "polluter", "clean_pile": 4})["clean"], false, "a polluter cannot clean")
	return true


func test_out_no_clean() -> bool:
	assert_eq(_states({"is_out": true, "clean_pile": 4})["clean"], false, "out")
	assert_eq(_states({"phase": "meeting", "clean_pile": 4})["clean"], false, "meeting")
	return true


func test_citizen_no_fix_at_fire_console_held_by_another_seat() -> bool:
	var near: Dictionary = {"id": "fire_a", "kind": "fire_fix"}
	var fire: Dictionary = {"kind": "fire", "countdown": 30.0, "held": ["fire_a"]}
	assert_eq(_states({"near_console": near, "sabotage": fire})["fix"]["visible"], false, "someone else holds it: no HOLD")
	assert_eq(_states({"near_console": near, "sabotage": fire, "held_console": "fire_a"})["fix"]["visible"], true, "my own hold keeps the button")
	var other: Dictionary = {"id": "fire_b", "kind": "fire_fix"}
	assert_eq(_states({"near_console": other, "sabotage": fire})["fix"]["visible"], true, "the free console is offered")
	return true
