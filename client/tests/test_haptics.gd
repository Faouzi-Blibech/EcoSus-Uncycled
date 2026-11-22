extends "res://tests/test_case.gd"
## Haptics: one buzz of the right length per event, none for snapshots or other messages.

const HapticsScript := preload("res://autoload/haptics.gd")


func _haptics(log: Array[int]) -> Node:
	var h: Node = track(HapticsScript.new())
	h.vibrate_fn = func(ms: int) -> void: log.append(ms)
	return h


func test_meeting_infection_and_gas_each_buzz_once_with_their_length() -> bool:
	var log: Array[int] = []
	var h: Node = _haptics(log)
	h.handle_message({"t": "meeting_started", "cause": "report"})
	assert_eq(log, [HapticsScript.MEETING_MS] as Array[int], "meeting")
	h.handle_message({"t": "infected", "countdown": 24.0})
	assert_eq(log, [HapticsScript.MEETING_MS, HapticsScript.HIT_MS] as Array[int], "infected")
	h.handle_message({"t": "you_are_out", "cause": "gas"})
	assert_eq(log.size(), 3, "gas")
	assert_eq(log[2], HapticsScript.HIT_MS, "gas length")
	h.handle_message({"t": "mask_used"})
	assert_eq(log.size(), 4, "gas stopped by a mask")
	assert_eq(log[3], HapticsScript.HIT_MS, "mask length")
	assert_true(HapticsScript.HIT_MS > HapticsScript.MEETING_MS, "hit is longer than meeting")
	return true


func test_snapshots_and_other_messages_do_not_buzz() -> bool:
	var log: Array[int] = []
	var h: Node = _haptics(log)
	for i in 3:
		h.handle_message({"t": "snapshot", "me": {"infected_s": 20.0}})
	h.handle_message({"t": "you_are_out", "cause": "vote"})
	h.handle_message({"t": "you_are_out", "cause": "virus"})
	h.handle_message({"t": "chat"})
	assert_eq(log.size(), 0, "no buzz")
	return true


func test_turned_off_in_settings_means_no_buzz() -> bool:
	var log: Array[int] = []
	var h: Node = _haptics(log)
	h.enabled_fn = func() -> bool: return false
	h.handle_message({"t": "meeting_started"})
	assert_eq(log.size(), 0, "off")
	return true
