extends "res://tests/test_case.gd"
## ActionRules.button_states: which action buttons show and whether HIT / DUMP are ready.

const RULES_PATH: String = "res://ui/hud/action_rules.gd"


## A playing citizen with nothing nearby; each test overrides what it needs.
func _ctx(over: Dictionary = {}) -> Dictionary:
	var ctx: Dictionary = {
		"phase": "playing", "role": "citizen", "is_out": false,
		"near_console": {}, "fresh_pile": -1,
		"cd": {"repollute": 0.0, "virus": 0.0, "gas": 0.0, "sabotage": 0.0}, "emergencies_left": 2,
	}
	ctx.merge(over, true)
	return ctx


func _states(over: Dictionary = {}) -> Dictionary:
	return load(RULES_PATH).button_states(_ctx(over))


const SPOT: Dictionary = {"id": "market_bins", "kind": "task"}


## v2: a task console is a cleaning spot; USE cleans it when it is dirty and a bag slot is free.
func test_cleaner_at_dirty_spot_with_a_free_bag_slot() -> bool:
	var s: Dictionary = _states({"near_console": SPOT, "near_spot_state": "dirty", "bags_full": false})
	assert_eq(s["use"], true, "use")
	assert_eq(s["repollute"]["visible"], false, "a Cleaner has no re-pollute")
	assert_eq(s["ability"]["visible"], false, "nor an ability")
	return true


func test_cleaner_spot_use_table() -> bool:
	assert_eq(_states({"near_console": SPOT, "near_spot_state": "dirty", "bags_full": true})["use"], false, "bags full")
	assert_eq(_states({"near_console": SPOT, "near_spot_state": "clean"})["use"], false, "already clean")
	assert_eq(_states({"near_console": SPOT})["use"], false, "state unknown before the first snapshot")
	assert_eq(_states({"role": "polluter", "near_console": SPOT, "near_spot_state": "clean"})["use"], false, "a Trasher fakes only dirty spots")
	return true


func test_polluter_at_task_console() -> bool:
	var s: Dictionary = _states({"role": "polluter", "near_console": SPOT, "near_spot_state": "dirty", "bags_full": true})
	assert_eq(s["use"], true, "use for the fake")
	assert_eq(s["repollute"]["visible"], true, "re-pollute shown to a Trasher")
	assert_eq(s["repollute"]["ready"], false, "but not at a dirty spot")
	assert_eq(s["ability"]["visible"], false, "no ability before the unlock")
	assert_eq(s.has("hit") or s.has("dump"), false, "v1 hit and dump are gone")
	return true


## v2 Trasher kit: RE-POLLUTE at a clean spot; the unlocked ability (virus at a dirty spot, gas anywhere).
func test_trasher_kit_table() -> bool:
	var clean: Dictionary = {"role": "polluter", "near_console": SPOT, "near_spot_state": "clean"}
	assert_eq(_states(clean)["repollute"], {"visible": true, "ready": true, "cooldown": 0.0}, "re-pollute a clean spot")
	var cooling: Dictionary = clean.duplicate()
	cooling["cd"] = {"repollute": 12.5}
	assert_eq(_states(cooling)["repollute"], {"visible": true, "ready": false, "cooldown": 12.5}, "on cooldown")
	var virus: Dictionary = {"role": "polluter", "ability": "virus", "near_console": SPOT, "near_spot_state": "dirty"}
	assert_eq(_states(virus)["ability"], {"visible": true, "ready": true, "cooldown": 0.0, "kind": "virus"}, "plant at a dirty spot")
	virus["near_spot_state"] = "clean"
	assert_eq(_states(virus)["ability"]["ready"], false, "no virus on a clean spot")
	var gas: Dictionary = {"role": "polluter", "ability": "gas", "cd": {"gas": 30.0}}
	assert_eq(_states(gas)["ability"], {"visible": true, "ready": false, "cooldown": 30.0, "kind": "gas"}, "gas anywhere, on cooldown")
	gas["cd"] = {"gas": 0.0}
	assert_eq(_states(gas)["ability"]["ready"], true, "gas ready")
	var cleaner: Dictionary = _states({"near_console": SPOT, "near_spot_state": "clean", "ability": "gas"})
	assert_eq(cleaner["repollute"]["visible"], false, "a Cleaner has no re-pollute")
	assert_eq(cleaner["ability"]["visible"], false, "nor an ability")
	return true


func test_sabotage_hidden_while_the_machine_is_jammed() -> bool:
	assert_eq(_states({"role": "polluter", "machine_jammed": true})["sabotage"]["visible"], false, "a jam is a running sabotage")
	return true


func test_emergency_console_without_emergencies_left() -> bool:
	var near: Dictionary = {"id": "meeting_btn", "kind": "emergency"}
	assert_eq(_states({"near_console": near, "emergencies_left": 0})["use"], false, "none left")
	assert_eq(_states({"near_console": near, "emergencies_left": 1})["use"], true, "one left")
	return true


func test_meeting_phase_hides_everything() -> bool:
	var s: Dictionary = _states({
		"phase": "meeting", "role": "polluter", "near_console": {"id": "market_bins", "kind": "task"},
		"fresh_pile": 7, "ability": "gas"})
	assert_eq(s["use"], false, "use")
	assert_eq(s["report"], false, "report")
	assert_eq(s["repollute"]["visible"], false, "repollute")
	assert_eq(s["ability"]["visible"], false, "ability")
	return true


func test_out_player_hides_everything() -> bool:
	var s: Dictionary = _states({
		"is_out": true, "role": "polluter", "near_console": {"id": "market_bins", "kind": "task"},
		"fresh_pile": 7, "ability": "gas"})
	assert_eq(s["use"], false, "use")
	assert_eq(s["report"], false, "report")
	assert_eq(s["repollute"]["visible"], false, "repollute")
	assert_eq(s["ability"]["visible"], false, "ability")
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


const MACHINE: Dictionary = {"id": "machine", "kind": "machine"}


## v2: USE at the Recycling Machine delivers the bags; a jammed machine offers a FIX hold instead.
func test_machine_deliver_and_fix_table() -> bool:
	assert_eq(_states({"near_console": MACHINE, "bags": 1})["use"], true, "deliver with a bag")
	assert_eq(_states({"near_console": MACHINE, "bags": 0})["use"], false, "nothing to deliver")
	var jammed: Dictionary = _states({"near_console": MACHINE, "bags": 1, "machine_jammed": true})
	assert_eq(jammed["use"], false, "no delivery while jammed")
	assert_eq(jammed["fix"], {"visible": true, "kind": "machine"}, "hold to fix")
	var trasher: Dictionary = _states({"role": "polluter", "near_console": MACHINE, "machine_jammed": true})
	assert_eq(trasher["use"], false, "a Trasher has nothing to deliver")
	assert_eq(trasher["fix"]["visible"], false, "a Trasher does not fix")
	assert_eq(_states({"phase": "meeting", "near_console": MACHINE, "bags": 1})["use"], false, "not in a meeting")
	return true
