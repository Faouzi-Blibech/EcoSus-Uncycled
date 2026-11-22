extends "res://tests/test_case.gd"
## Sfx: the right sound per server message, one per message, quiet for snapshots and unknown types,
## footsteps on a rhythm, and nothing at all when sound is off in Settings.

const SfxScript := preload("res://autoload/sfx.gd")


func _sfx(log: Array[String]) -> Node:
	var s: Node = track(SfxScript.new())
	s.play_fn = func(n: String, _db: float, _pitch: float) -> void: log.append(n)
	s.enabled_fn = func() -> bool: return true
	return s


func test_each_game_event_maps_to_its_sound() -> bool:
	var cases: Dictionary = {
		"spot_cleaned": "spot_cleaned", "delivered": "deliver", "upgrade_ok": "upgrade_ok",
		"healed": "healed", "mask_used": "mask_used", "infected": "infected",
		"repollute_ok": "repollute", "virus_planted": "virus_planted", "gas_warning": "gas_warning",
		"meeting_started": "meeting_started", "voting_started": "voting_started",
		"vote_cast": "vote_cast", "dash_ok": "dash", "action_rejected": "action_rejected",
	}
	for t: String in cases:
		assert_eq(SfxScript.sound_for({"t": t}, "citizen"), cases[t], t)
	return true


func test_sounds_that_depend_on_the_message_details() -> bool:
	assert_eq(SfxScript.sound_for({"t": "sabotage_started", "kind": "smog"}, ""), "smog", "smog")
	assert_eq(SfxScript.sound_for({"t": "sabotage_started", "kind": "machine"}, ""), "sabotage_machine", "jam")
	assert_eq(SfxScript.sound_for({"t": "sabotage_ended", "result": "fixed"}, ""), "sabotage_fixed", "fixed")
	assert_eq(SfxScript.sound_for({"t": "sabotage_ended", "result": "expired"}, ""), "", "expired is quiet")
	assert_eq(SfxScript.sound_for({"t": "ejection", "was_polluter": true}, ""), "ejection_trasher", "caught")
	assert_eq(SfxScript.sound_for({"t": "ejection", "was_polluter": false}, ""), "ejection_innocent", "wrong vote")
	assert_eq(SfxScript.sound_for({"t": "you_are_out", "cause": "vote"}, ""), "", "vote-out plays the ejection only")
	assert_eq(SfxScript.sound_for({"t": "you_are_out", "cause": "gas"}, ""), "you_are_out", "gas")
	assert_eq(SfxScript.sound_for({"t": "cue", "kind": "repollute"}, ""), "repollute", "witnessed")
	return true


func test_game_over_is_a_victory_or_a_defeat_for_the_local_role() -> bool:
	var cleaners_win: Dictionary = {"t": "game_over", "winner": "citizens"}
	var trashers_win: Dictionary = {"t": "game_over", "winner": "polluters"}
	assert_eq(SfxScript.sound_for(cleaners_win, "citizen"), "victory", "cleaner wins")
	assert_eq(SfxScript.sound_for(cleaners_win, "polluter"), "defeat", "trasher loses")
	assert_eq(SfxScript.sound_for(trashers_win, "polluter"), "victory", "trasher wins")
	assert_eq(SfxScript.sound_for(trashers_win, "citizen"), "defeat", "cleaner loses")
	return true


func test_unknown_messages_play_nothing() -> bool:
	var log: Array[String] = []
	var s: Node = _sfx(log)
	s.handle_message({"t": "snapshot"})
	s.handle_message({"t": "round_resumed"})
	assert_eq(log.size(), 0, "silent")
	return true


func test_footsteps_follow_a_rhythm_and_stop_when_standing() -> bool:
	var log: Array[String] = []
	var s: Node = _sfx(log)
	s.step_tick(0.016, false)
	s.step_tick(0.016, true)
	assert_eq(log.size(), 1, "first step at once")
	s.step_tick(SfxScript.STEP_INTERVAL_S * 0.5, true)
	assert_eq(log.size(), 1, "not yet")
	s.step_tick(SfxScript.STEP_INTERVAL_S * 0.6, true)
	assert_eq(log.size(), 2, "second step")
	assert_true(log[0] != log[1], "steps alternate")
	return true


func test_sound_off_in_settings_means_silence() -> bool:
	var log: Array[String] = []
	var s: Node = _sfx(log)
	s.enabled_fn = func() -> bool: return false
	s.handle_message({"t": "meeting_started"})
	s.step_tick(1.0, true)
	assert_eq(log.size(), 0, "off")
	return true
