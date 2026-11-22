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


## Regression: the meeting button looked ready right after a meeting and the server refused it.
func test_emergency_waits_after_a_meeting() -> bool:
	var near: Dictionary = {"id": "meeting_btn", "kind": "emergency"}
	var s: Dictionary = _states({"near_console": near, "emergencies_left": 1, "emergency_wait": 6.5})
	assert_eq(s["use"], true, "shown")
	assert_eq(s["use_wait"], 6.5, "waits 6.5 s")
	assert_eq(_states({"near_console": near, "emergencies_left": 1, "emergency_wait": 0.0})["use_wait"], 0.0, "ready")
	assert_eq(_states({"near_console": SPOT, "near_spot_state": "dirty", "emergency_wait": 9.0})["use_wait"], 0.0, "only the meeting button waits")
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


## v2 virus counterplay: HEAL (self when infected, else the nearest player in reach, the server checks
## the infection) needs a heal; ALERT calls a meeting from anywhere, once per infection.
func test_heal_and_alert_table() -> bool:
	assert_eq(_states({"heals": 1, "infected": true})["heal"], true, "heal myself")
	assert_eq(_states({"heals": 1, "heal_target": 4})["heal"], true, "heal a player in reach")
	assert_eq(_states({"heals": 1})["heal"], false, "nobody to heal")
	assert_eq(_states({"heals": 0, "infected": true})["heal"], false, "no heal kit")
	assert_eq(_states({"infected": true})["alert"], true, "infected: alert from anywhere")
	assert_eq(_states({"infected": true, "alert_used": true})["alert"], false, "one alert per infection")
	assert_eq(_states({})["alert"], false, "not infected")
	var trasher: Dictionary = _states({"role": "polluter", "heals": 1, "infected": true, "heal_target": 4})
	assert_eq(trasher["heal"] or trasher["alert"], false, "Trashers do neither")
	assert_eq(_states({"phase": "meeting", "heals": 1, "infected": true})["heal"], false, "not in a meeting")
	return true


## Dash: every role while playing; hidden until the first snapshot gives its timer (dash_s -1).
func test_dash_button_states() -> bool:
	var cases: Array = [
		# [over, visible, ready, cooldown]
		[{"dash_s": 0.0}, true, true, 0.0],
		[{"dash_s": 7.5}, true, false, 7.5],
		[{"dash_s": 0.0, "role": "polluter"}, true, true, 0.0],
		[{"dash_s": -1.0}, false, false, 0.0],
		[{"dash_s": 0.0, "is_out": true}, false, false, 0.0],
		[{"dash_s": 0.0, "phase": "meeting"}, false, false, 0.0],
	]
	for c in cases:
		var d: Dictionary = _states(c[0] as Dictionary)["dash"]
		assert_eq(d["visible"], c[1], "visible %s" % str(c[0]))
		assert_eq(d["ready"], c[2], "ready %s" % str(c[0]))
		assert_near(float(d["cooldown"]), float(c[3]), 0.001, "cooldown %s" % str(c[0]))
	return true


## Step 3: USE says what it will do at the console in reach.
func test_use_look_per_console() -> bool:
	var meeting: Dictionary = {"id": "meeting_btn", "kind": "emergency"}
	var table: Array = [
		[{}, {"kind": "use", "count": 0}, "nothing in reach"],
		[{"near_console": SPOT, "near_spot_state": "dirty"}, {"kind": "clean", "count": 0}, "dirty spot"],
		[{"role": "polluter", "near_console": SPOT, "near_spot_state": "dirty"}, {"kind": "clean", "count": 0}, "a Trasher's fake clean looks the same"],
		[{"near_console": MACHINE, "bags": 2}, {"kind": "recycle", "count": 2}, "machine with 2 bags"],
		[{"near_console": MACHINE, "bags": 0}, {"kind": "recycle", "count": 0}, "machine, no bags (USE hidden)"],
		[{"near_console": MACHINE, "bags": 1, "machine_jammed": true}, {"kind": "use", "count": 0}, "jammed: FIX instead"],
		[{"near_console": meeting}, {"kind": "meeting", "count": 0}, "emergency table"],
	]
	for row in table:
		assert_eq(_states(row[0] as Dictionary)["use_look"], row[1], str(row[2]))
	return true
