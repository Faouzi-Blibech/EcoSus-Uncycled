extends "res://tests/test_case.gd"
## The out player's overlay: cause, meter, phase in plain words, time left.

const Overlay := preload("res://ui/hud/spectator_overlay.gd")


func _texts(node: Node) -> String:
	var parts: Array[String] = []
	if node is Label:
		parts.append((node as Label).text)
	for c in node.get_children():
		parts.append(_texts(c))
	return " | ".join(parts)


func _overlay() -> Control:
	var o: Control = track(load("res://ui/hud/spectator_overlay.tscn").instantiate()) as Control
	o._ready()
	return o


func test_status_shows_meter_phase_and_time_left() -> bool:
	var o: Control = _overlay()
	o.set_cause("poison")
	o.set_status(42.0, "playing", 125)
	var text: String = _texts(o)
	assert_true(text.contains(o.tr("SPECTATOR_CAUSE_POISON")), "cause: " + text)
	assert_true(text.contains(o.tr("HUD_METER") % 42.0), "meter: " + text)
	assert_true(text.contains(o.tr("SPECTATOR_PHASE_PLAYING")), "phase in plain words: " + text)
	assert_true(not text.contains(o.tr("PHASE_PLAYING")), "not the players' goal line: " + text)
	assert_true(text.contains("2:05"), "time left: " + text)
	return true


func test_time_left_matches_the_hud_clock() -> bool:
	assert_eq(Overlay.left_seconds(300.0, 100.5), 200, "ceil like the HUD timer")
	assert_eq(Overlay.left_seconds(300.0, 400.0), 0, "past the deadline")
	assert_eq(Overlay.left_seconds(0.0, 10.0), -1, "no deadline: no line")
	return true
