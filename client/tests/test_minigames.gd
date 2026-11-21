extends "res://tests/test_case.gd"
## Mini-game framework: registry lookup, min-time gate, fake never sends, layer closes on meeting.

var _now: float = 0.0
var _emitted: int = 0
var _sent_ids: Array[int] = []


func _now_s() -> float:
	return _now


func _count(_task_id: int, _step: int) -> void:
	_emitted += 1


func _game(is_fake: bool) -> Minigame:
	var g: Minigame = track(Minigame.new()) as Minigame
	_now = 0.0
	_emitted = 0
	g.clock = _now_s
	g.step_done.connect(_count)
	g.begin({"id": 3, "type": "sort_waste"}, 0, is_fake, 0.5)
	return g


func test_registry_exact_and_prefix() -> bool:
	var reg: GDScript = load("res://minigames/registry.gd")
	var sort_scene: String = "res://minigames/sort_waste/sort_waste.tscn"
	var compost_scene: String = "res://minigames/turn_compost/turn_compost.tscn"
	assert_eq(reg.scene_for("sort_waste"), sort_scene, "exact")
	assert_eq(reg.scene_for("sort_waste_depot"), sort_scene, "prefix")
	assert_eq(reg.scene_for("turn_compost"), compost_scene, "compost")
	assert_eq(reg.scene_for("sort"), "", "partial key")
	assert_eq(reg.scene_for("sort_wastex"), "", "no separator")
	assert_eq(reg.scene_for("unknown"), "", "unknown")
	return true


func test_step_not_emitted_before_min_s() -> bool:
	var g: Minigame = _game(false)
	_now = 0.2
	g.finish_step()
	assert_eq(_emitted, 0, "too early")
	_now = 0.6
	g.poll()
	assert_eq(_emitted, 1, "due")
	g.poll()
	assert_eq(_emitted, 1, "only once")
	return true


func test_fake_never_emits() -> bool:
	var g: Minigame = _game(true)
	g.finish_step()
	_now = 1.0
	g.poll()
	assert_eq(_emitted, 0, "fake")
	return true


func test_layer_closes_on_meeting() -> bool:
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer.open_fake("sort_waste")
	assert_true(layer.is_open(), "open")
	layer._on_meeting_started({})
	assert_true(not layer.is_open(), "closed")
	return true


## The use_ok the server sends for a sort_waste step (console from the type's list).
func _use_ok_for(task_id: int, step_index: int) -> Dictionary:
	return {"kind": "task", "task": task_id, "type": "sort_waste", "step": step_index, "console": Session.console_for_step("sort_waste", step_index)}


func _layer_with_step(min_s: float) -> Node:
	Session.task_types["sort_waste"] = {"min_s": min_s, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer._on_use_ok(_use_ok_for(3, 1))
	return layer


func test_fake_closes_only_when_due() -> bool:
	var g: Minigame = _game(true)
	var closes: Array[int] = [0]
	g.closed.connect(func() -> void: closes[0] += 1)
	_now = 0.2
	g.finish_step()
	assert_eq(closes[0], 0, "not due")
	_now = 0.6
	g.poll()
	assert_eq(closes[0], 1, "due")
	assert_eq(_emitted, 0, "still no step")
	return true


func test_closed_minigame_never_emits() -> bool:
	var layer: Node = _layer_with_step(0.5)
	var m: Minigame = layer._current
	_now = 0.0
	_emitted = 0
	m.clock = _now_s
	m.begin({"id": 3, "type": "sort_waste"}, 1, false, 0.5)
	m.step_done.connect(_count)
	m.finish_step()
	layer.close_all()
	_now = 1.0
	m.poll()
	assert_eq(_emitted, 0, "closed game stays silent")
	Session.task_types.erase("sort_waste")
	return true


func _count_send(task_id: int) -> void:
	_sent_ids.append(task_id)


func test_step_sent_once_only_for_real_open_game() -> bool:
	var layer: Node = _layer_with_step(0.0)
	_sent_ids.clear()
	layer.send_step = _count_send
	var m: Minigame = layer._current
	m.finish_step()
	m.poll()
	assert_eq(_sent_ids, [3] as Array[int], "one send, right task")
	layer.open_fake("sort_waste")
	var fake_game: Minigame = layer._current
	fake_game.finish_step()
	fake_game.poll()
	assert_eq(_sent_ids.size(), 1, "fake sends nothing")
	Session.task_types["sort_waste"]["min_s"] = 0.5
	layer._on_use_ok(_use_ok_for(4, 0))
	var closed_game: Minigame = layer._current
	closed_game.clock = _now_s
	closed_game.begin({"id": 4, "type": "sort_waste"}, 0, false, 0.5)
	_now = 0.0
	closed_game.finish_step()
	layer.close_all()
	_now = 1.0
	closed_game.poll()
	assert_eq(_sent_ids.size(), 1, "closed game sends nothing")
	Session.task_types.erase("sort_waste")
	return true


func test_real_step_done_once_then_closes() -> bool:
	var layer: Node = _layer_with_step(0.0)
	var m: Minigame = layer._current
	m.step_done.connect(_count)
	_emitted = 0
	m.finish_step()
	m.poll()
	assert_eq(_emitted, 1, "once")
	assert_true(not layer.is_open(), "closed after step")
	Session.task_types.erase("sort_waste")
	return true


func test_use_ok_step_console_walk_away() -> bool:
	var layer: Node = _layer_with_step(0.0)
	assert_eq(layer._console_id, "bins_b", "console from step")
	layer.set_player_pos_provider(func() -> Variant: return null)
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 32.0})
	layer._process(0.016)
	assert_true(layer.is_open(), "null position is skipped")
	layer.set_player_pos_provider(func() -> Variant: return Vector2(32.0, 0.0))
	layer._process(0.016)
	assert_true(layer.is_open(), "exactly at the range")
	layer.set_player_pos_provider(func() -> Variant: return Vector2(32.0 + 1.0, 0.0))
	layer._process(0.016)
	assert_true(not layer.is_open(), "walked away")
	Session.task_types.erase("sort_waste")
	return true


func _task_types() -> Array[String]:
	var path: String = ProjectSettings.globalize_path("res://").path_join("../content/tasks.json")
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	var types: Array[String] = []
	if typeof(parsed) == TYPE_DICTIONARY:
		for t in (parsed as Dictionary).get("tasks", []):
			types.append(str(t.get("type", "")))
	return types


func test_registry_has_all_six() -> bool:
	var reg: GDScript = load("res://minigames/registry.gd")
	var types: Array[String] = _task_types()
	assert_eq(types.size(), 9, "tasks.json lists nine task types")
	for t in types:
		var path: String = reg.scene_for(t)
		assert_true(not path.is_empty(), "scene for %s" % t)
		assert_true(ResourceLoader.exists(path), "scene exists for %s" % t)
	for t in ["check_meter", "crush_cans_garden", "rinse_bottles_market", "deliver_glass"]:
		assert_true(types.has(t), "tasks.json has %s" % t)
	return true


## A mini-game scene outside the tree (the runner's tree is busy in _ready), _ready called by hand, begun with no time gate; counts step_done.
func _spawn(type: String, step_index: int) -> Minigame:
	var g: Minigame = load(MinigameRegistry.scene_for(type)).instantiate() as Minigame
	g.begin({"id": 7, "type": type}, step_index, false, 0.0)
	g.step_done.connect(_count)
	_emitted = 0
	track(g)
	g._ready()
	return g


func test_check_meter_wrong_entry_clears_right_one_finishes() -> bool:
	var g: Minigame = _spawn("check_meter", 0)
	assert_eq(g.reading.length(), 3, "three digits from tuning fallback")
	var wrong: String = "000" if g.reading != "000" else "111"
	for d in wrong:
		g.press_digit(d)
	assert_eq(g.entry, "", "wrong entry clears")
	g.poll()
	assert_eq(_emitted, 0, "wrong entry sends nothing")
	for d in g.reading:
		g.press_digit(d)
	g.poll()
	assert_eq(_emitted, 1, "right entry finishes once")
	return true


func test_crush_cans_needs_every_can_once() -> bool:
	var g: Minigame = _spawn("crush_cans_garden", 0)
	for i in 4:
		g.crush(i)
		g.crush(i)
	g.poll()
	assert_eq(_emitted, 0, "four of five cans, tapped twice, is not done")
	g.crush(4)
	g.poll()
	assert_eq(_emitted, 1, "fifth can finishes")
	return true


func test_deliver_glass_lift_then_bottles() -> bool:
	var lift: Minigame = _spawn("deliver_glass", 0)
	lift.drag_crate(-40.0)
	lift.poll()
	assert_eq(_emitted, 0, "half lifted")
	lift.drag_crate(60.0)
	lift.drag_crate(-200.0)
	lift.poll()
	assert_eq(_emitted, 1, "lifted")
	var drop: Minigame = _spawn("deliver_glass", 1)
	var bottles: Array = []
	for c in drop.get_children():
		if c is Panel and c.has_meta("home"):
			bottles.append(c)
	assert_eq(bottles.size(), 4, "four bottles")
	var bank: Control = null
	for c in drop.get_children():
		if c is Panel and not c.has_meta("home"):
			bank = c
	for i in 3:
		bottles[i].position = bank.position
		drop.drop_bottle(bottles[i])
	bottles[3].position = Vector2(0.0, 300.0)
	drop.drop_bottle(bottles[3])
	drop.poll()
	assert_eq(_emitted, 0, "a missed drop does not count")
	bottles[3].position = bank.position
	drop.drop_bottle(bottles[3])
	drop.poll()
	assert_eq(_emitted, 1, "fourth bottle in the bank finishes")
	return true


func test_check_meter_wrong_hint_resets_on_next_key() -> bool:
	var g: Minigame = _spawn("check_meter", 0)
	var hint: String = g._hint.text
	var wrong: String = "000" if g.reading != "000" else "111"
	for d in wrong:
		g.press_digit(d)
	assert_true(g._hint.text != hint, "wrong entry shows the wrong-value hint")
	g.press_digit(g.reading[0])
	assert_eq(g._hint.text, hint, "next key brings the instruction back")
	return true


func _with_minigame_tuning(cfg: Dictionary) -> Dictionary:
	var old: Dictionary = Session.tuning
	Session.tuning = {"minigames": cfg}
	return old


func test_rinse_uses_own_keys_and_finishes_only_in_band() -> bool:
	var old: Dictionary = _with_minigame_tuning({"gauge_band_lo": 0.1, "gauge_band_hi": 0.2, "rinse_fill_per_s": 0.7, "rinse_band_lo": 0.4, "rinse_band_hi": 0.5})
	var g: Minigame = _spawn("rinse_bottles_market", 0)
	var gauge: HoldGauge = g._gauge
	assert_near(gauge.fill_per_s, 0.7, 0.0001, "rinse rate")
	assert_near(gauge.band_lo, 0.4, 0.0001, "rinse band lo, not the shared gauge key")
	assert_near(gauge.band_hi, 0.5, 0.0001, "rinse band hi")
	gauge.press()
	gauge.value = 0.9
	gauge.release()
	g.poll()
	assert_eq(_emitted, 0, "release above the band does not finish")
	gauge.press()
	gauge.value = 0.45
	gauge.release()
	g.poll()
	assert_eq(_emitted, 1, "release in the band finishes")
	Session.tuning = old
	return true


func _all_inside_screen(g: Control) -> bool:
	var screen: Rect2 = Rect2(0.0, 0.0, 640.0, 360.0)
	var n: int = 0
	for c in g.get_children():
		if c is Button or (c is Panel and c.has_meta("home")):
			n += 1
			var r: Rect2 = Rect2(c.position, c.size * c.scale)
			if not screen.encloses(r):
				return false
	return n > 0


func test_big_can_and_bottle_counts_stay_on_screen() -> bool:
	var old: Dictionary = _with_minigame_tuning({"crush_cans": 20, "glass_bottles": 20})
	var cans: Minigame = _spawn("crush_cans", 0)
	assert_true(_all_inside_screen(cans), "20 cans fit the screen")
	var bottles: Minigame = _spawn("deliver_glass", 1)
	assert_true(_all_inside_screen(bottles), "20 bottles fit the screen")
	Session.tuning = old
	return true


func test_fake_opened_at_console_closes_when_player_walks_out() -> bool:
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer.open_fake("sort_waste", "market_bins")
	assert_true(layer.is_open(), "fake open")
	assert_eq(layer._console_id, "market_bins", "console kept for the fake")
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 32.0})
	layer.set_player_pos_provider(func() -> Variant: return Vector2(32.0, 0.0))
	layer._process(0.016)
	assert_true(layer.is_open(), "exactly at the range")
	layer.set_player_pos_provider(func() -> Variant: return Vector2(32.0 + 1.0, 0.0))
	layer._process(0.016)
	assert_true(not layer.is_open(), "fake closed after walking out")
	return true


func test_fake_opens_the_step_of_its_console() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 4, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer.open_fake("sort_waste", "bins_b")
	assert_eq((layer._current as Minigame).step, 1, "second console is the second step")
	assert_eq(layer._console_id, "bins_b", "console kept")
	layer.open_fake("sort_waste", "bins_a")
	assert_eq((layer._current as Minigame).step, 0, "first console is the first step")
	layer.open_fake("sort_waste")
	assert_eq((layer._current as Minigame).step, 0, "no console falls back to step 0")
	layer.open_fake("sort_waste", "no_such_console")
	assert_eq((layer._current as Minigame).step, 0, "unknown console falls back to step 0")
	Session.task_types.erase("sort_waste")
	return true


func _real_use_ok() -> Dictionary:
	return {"kind": "task", "task": 3, "type": "sort_waste", "console": "bins_b", "step": 1}


## The production open path: use_ok from the server -> _on_use_ok, with the type's min_s from welcome.
func test_use_ok_gates_the_step_on_the_servers_min_s() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 4.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	_now = 0.0
	_sent_ids.clear()
	layer.clock = _now_s
	layer.send_step = _count_send
	layer._on_use_ok(_real_use_ok())
	assert_true(layer.is_open(), "use_ok opens the game")
	var m: Minigame = layer._current
	assert_eq(m.step, 1, "step from use_ok")
	m.finish_step()
	_now = 3.9
	m.poll()
	assert_eq(_sent_ids.size(), 0, "nothing sent before min_s")
	assert_true(layer.is_open(), "still open while waiting")
	_now = 4.1
	m.poll()
	assert_eq(_sent_ids, [3] as Array[int], "one send after min_s")
	assert_true(not layer.is_open(), "closed after the send")
	Session.task_types.erase("sort_waste")
	return true


func test_use_ok_game_closes_when_the_player_walks_out_of_range() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 0.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer._on_use_ok(_real_use_ok())
	assert_eq(layer._console_id, "bins_b", "console from use_ok")
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 48.0})
	layer.set_player_pos_provider(func() -> Variant: return Vector2(48.0, 0.0))
	layer._process(0.016)
	assert_true(layer.is_open(), "exactly at the server's range is still in reach")
	layer.set_player_pos_provider(func() -> Variant: return Vector2(48.5, 0.0))
	layer._process(0.016)
	assert_true(not layer.is_open(), "past the server's range closes at once, no extra margin")
	Session.task_types.erase("sort_waste")
	return true


func test_step_is_not_sent_from_beyond_the_servers_range() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 0.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	_sent_ids.clear()
	layer.send_step = _count_send
	layer._on_use_ok(_real_use_ok())
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 48.0})
	layer.set_player_pos_provider(func() -> Variant: return Vector2(60.0, 0.0))
	var m: Minigame = layer._current
	m.finish_step()
	assert_eq(_sent_ids.size(), 0, "the server would answer out_of_range, so nothing is sent")
	assert_true(not layer.is_open(), "the game closes instead")
	Session.task_types.erase("sort_waste")
	return true


func _timed_game(is_fake: bool, min_s: float) -> Minigame:
	var g: Minigame = track(Minigame.new()) as Minigame
	_now = 0.0
	_emitted = 0
	g.clock = _now_s
	g.step_done.connect(_count)
	g.begin({"id": 3, "type": "sort_waste"}, 0, is_fake, min_s)
	return g


func _wait_text(g: Minigame) -> String:
	for c in g.get_children():
		if c is Label and c.name == "WaitLabel" and c.visible:
			return (c as Label).text
	return ""


## Typical play ends before min_s: the player must see that the game is checking, not stuck.
func test_finished_early_shows_a_checking_state_with_the_time_left() -> bool:
	var g: Minigame = _timed_game(false, 4.0)
	_now = 1.0
	g.poll()
	assert_eq(_wait_text(g), "", "nothing to wait for before the step is done")
	g.finish_step()
	var text: String = _wait_text(g)
	assert_true(not text.is_empty(), "a wait label shows once the step is done early")
	assert_true(not text.begins_with("MG_"), "the label is translated")
	assert_true(text.contains("3"), "3 s left at 1.0 of 4.0 (got '%s')" % text)
	_now = 3.5
	g.poll()
	assert_true(_wait_text(g).contains("1"), "1 s left at 3.5 of 4.0")
	assert_eq(_emitted, 0, "still held")
	_now = 4.0
	g.poll()
	assert_eq(_emitted, 1, "sent by itself at min_s, no second tap")
	return true


func test_a_fake_waits_with_the_same_checking_state() -> bool:
	var g: Minigame = _timed_game(true, 4.0)
	_now = 1.0
	g.finish_step()
	assert_true(not _wait_text(g).is_empty(), "a fake looks the same as a real game")
	return true


func test_gauge_locks_after_an_in_band_release() -> bool:
	var g: Minigame = _spawn("rinse_bottles", 0)
	var gauge: HoldGauge = g._gauge
	gauge.press()
	gauge.value = (gauge.band_lo + gauge.band_hi) * 0.5
	gauge.release()
	var kept: float = gauge.value
	var hint: String = g._hint.text
	gauge.press()
	assert_true(not gauge.holding, "a locked gauge ignores a second press")
	assert_near(gauge.value, kept, 0.0001, "value kept")
	gauge.release()
	assert_eq(g._hint.text, hint, "no false 'missed' after success")
	return true


func test_compost_tap_step_has_its_own_hint() -> bool:
	var g: Minigame = _spawn("turn_compost", 1)
	assert_eq(g._hint.text, g.tr("MG_COMPOST_WATER_HINT"), "tap step names the watering")
	assert_true(g.tr("MG_COMPOST_WATER_HINT") != "MG_COMPOST_WATER_HINT", "the key is in strings.csv")
	return true



## On a 19.5:9 phone the logical screen is about 780x360: the game sits in a real 780x360 parent in
## the tree, so its full-rect layout gives it that width when _ready runs.
func _spawn_wide(type: String, step_index: int) -> Minigame:
	var g: Minigame = load(MinigameRegistry.scene_for(type)).instantiate() as Minigame
	g.begin({"id": 7, "type": type}, step_index, false, 0.0)
	var screen: Control = track(Control.new()) as Control
	screen.size = Vector2(780.0, 360.0)
	screen.add_child(g)
	(Engine.get_main_loop() as SceneTree).root.add_child(screen)
	return g


func test_compost_and_rinse_centre_on_a_wide_screen() -> bool:
	# The runner starts its first test inside its own _ready, where the root is still busy.
	await (Engine.get_main_loop() as SceneTree).process_frame
	var compost: Minigame = _spawn_wide("turn_compost", 0)
	assert_near(compost._handle_centre().x, 390.0 + compost.RADIUS, 0.5, "handle circles around the centre of 780 px")
	var tap: Minigame = _spawn_wide("turn_compost", 1)
	var tap_gauge: Control = null
	for c in tap.get_children():
		if c is HoldGauge:
			tap_gauge = c
	assert_near(tap_gauge.position.x + tap_gauge.size.x * 0.5, 390.0, 0.5, "compost tap gauge centred")
	var rinse: Minigame = _spawn_wide("rinse_bottles", 0)
	assert_near(rinse._gauge.position.x + rinse._gauge.size.x * 0.5, 390.0, 0.5, "rinse gauge centred")
	return true


func test_close_button_is_anchored_to_the_top_right() -> bool:
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	var b: Button = layer.get_node("CloseButton") as Button
	assert_near(b.anchor_left, 1.0, 0.001, "anchored right")
	assert_near(b.anchor_right, 1.0, 0.001, "anchored right")
	assert_true(b.offset_right <= 0.0, "offset from the right edge")
	return true


func _item_panels(g: Minigame) -> Array:
	var items: Array = []
	for c in g.get_children():
		if c is Panel and c.has_meta("home"):
			items.append(c)
	return items


func _centre_item_in(item: Control, bin: Control) -> void:
	item.position = bin.position + bin.size * 0.5 - item.size * 0.5


func test_sort_waste_wrong_bin_bounces_and_right_bins_finish() -> bool:
	var g: Minigame = _spawn("sort_waste", 0)
	var items: Array = _item_panels(g)
	assert_eq(items.size(), 5, "five items from the tuning fallback")
	for it in items:
		var wrong: Control = null
		for id in g._bin_nodes:
			if id != str(it.get_meta("bin")):
				wrong = g._bin_nodes[id]
				break
		_centre_item_in(it, wrong)
		g.drop_item(it)
	g.poll()
	assert_eq(_emitted, 0, "five wrong drops finish nothing")
	for it in items:
		assert_true(is_instance_valid(it) and not it.is_queued_for_deletion(), "a wrong drop keeps the item")
	for i in items.size() - 1:
		_centre_item_in(items[i], g._bin_nodes[str(items[i].get_meta("bin"))])
		g.drop_item(items[i])
	g.poll()
	assert_eq(_emitted, 0, "four of five in the right bin is not done")
	_centre_item_in(items[4], g._bin_nodes[str(items[4].get_meta("bin"))])
	g.drop_item(items[4])
	g.poll()
	assert_eq(_emitted, 1, "the fifth right drop finishes")
	return true


## Points on the handle circle, `degrees` apart, from the handle's start (angle 0).
func _turn(g: Minigame, from_deg: float, to_deg: float) -> void:
	var d: float = from_deg
	while d < to_deg:
		d += 30.0
		g.drag_handle_to(g._centre + Vector2.from_angle(deg_to_rad(d)) * g.RADIUS)


func test_compost_needs_all_the_turns() -> bool:
	var g: Minigame = _spawn("turn_compost", 0)
	g.turns_needed = 3
	_turn(g, 0.0, 3.0 * 360.0 - 90.0)
	g.poll()
	assert_eq(_emitted, 0, "2.75 turns is not done")
	_turn(g, 3.0 * 360.0 - 90.0, 3.0 * 360.0)
	g.poll()
	assert_eq(_emitted, 1, "3 turns finish")
	return true


func test_compost_backing_up_is_no_turn() -> bool:
	var g: Minigame = _spawn("turn_compost", 0)
	g.turns_needed = 1
	for i in 5:
		g.drag_handle_to(g._centre + Vector2.from_angle(deg_to_rad(30.0 * (i + 1))) * g.RADIUS)
	for i in 5:
		g.drag_handle_to(g._centre + Vector2.from_angle(deg_to_rad(30.0 * (4 - i))) * g.RADIUS)
	g.poll()
	assert_eq(_emitted, 0, "forward then back is no turn")
	return true


func test_sort_items_above_six_wrap_and_stay_on_screen() -> bool:
	var old: Dictionary = _with_minigame_tuning({"sort_items": 12})
	var g: Minigame = _spawn("sort_waste", 0)
	var items: Array = _item_panels(g)
	assert_eq(items.size(), 12, "twelve items")
	var screen: Rect2 = Rect2(0.0, 0.0, 640.0, 360.0)
	for it in items:
		var r: Rect2 = Rect2(it.position, it.size)
		assert_true(screen.encloses(r), "item inside the screen")
		assert_true(r.end.y <= (g._bin_nodes.values()[0] as Control).position.y, "item row stays above the bins")
	Session.tuning = old
	return true


func test_a_later_finger_drag_moves_but_the_first_touch_is_not_counted_twice() -> bool:
	var mouse: InputEventMouseMotion = InputEventMouseMotion.new()
	mouse.relative = Vector2(3.0, 4.0)
	assert_eq(Minigame.drag_delta(mouse), Vector2(3.0, 4.0), "mouse motion (and the first touch, emulated)")
	var second: InputEventScreenDrag = InputEventScreenDrag.new()
	second.index = 1
	second.relative = Vector2(5.0, 6.0)
	assert_eq(Minigame.drag_delta(second), Vector2(5.0, 6.0), "second finger")
	var first: InputEventScreenDrag = InputEventScreenDrag.new()
	first.index = 0
	first.relative = Vector2(7.0, 8.0)
	assert_eq(Minigame.drag_delta(first), Vector2.ZERO, "first finger already arrives as mouse motion")
	assert_eq(Minigame.drag_delta(InputEventKey.new()), Vector2.ZERO, "other events")
	return true


func test_sort_item_follows_a_second_finger() -> bool:
	var g: Minigame = _spawn("sort_waste", 0)
	var it: Control = _item_panels(g)[0]
	var press: InputEventScreenTouch = InputEventScreenTouch.new()
	press.index = 1
	press.pressed = true
	g._on_item_input(press, it)
	var start: Vector2 = it.position
	var drag: InputEventScreenDrag = InputEventScreenDrag.new()
	drag.index = 1
	drag.relative = Vector2(10.0, 20.0)
	g._on_item_input(drag, it)
	assert_eq(it.position, start + Vector2(10.0, 20.0), "item moved by the second finger's drag")
	return true


func _toast_text(layer: Node) -> String:
	var l: Label = layer.get_node_or_null("TooFarToast") as Label
	return l.text if l != null and l.visible else ""


func test_walking_out_of_range_shows_a_too_far_toast_that_expires() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 0.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer._on_use_ok(_real_use_ok())
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 48.0})
	layer.set_player_pos_provider(func() -> Variant: return Vector2(48.0, 0.0))
	layer._process(0.016)
	assert_eq(_toast_text(layer), "", "no toast while in reach")
	layer.set_player_pos_provider(func() -> Variant: return Vector2(60.0, 0.0))
	layer._process(0.016)
	assert_true(not layer.is_open(), "closed")
	var text: String = _toast_text(layer)
	assert_true(not text.is_empty() and not text.begins_with("MG_"), "a translated toast says why the game closed (got '%s')" % text)
	layer._process(layer.TOAST_S + 0.1)
	assert_eq(_toast_text(layer), "", "the toast goes away")
	Session.task_types.erase("sort_waste")
	return true


func test_a_refused_send_shows_the_toast_too() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 0.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer.send_step = _count_send
	layer._on_use_ok(_real_use_ok())
	layer.set_console_lookup(func(_id: String) -> Dictionary: return {"pos_px": Vector2.ZERO, "range_px": 48.0})
	layer.set_player_pos_provider(func() -> Variant: return Vector2(60.0, 0.0))
	(layer._current as Minigame).finish_step()
	assert_true(not _toast_text(layer).is_empty(), "the lost step is explained")
	Session.task_types.erase("sort_waste")
	return true


func test_closing_by_hand_or_meeting_shows_no_toast() -> bool:
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	layer.open_fake("sort_waste")
	layer.close_all()
	assert_eq(_toast_text(layer), "", "close button")
	return true


var _sent_spots: Array[String] = []


func _count_spot(spot: String) -> void:
	_sent_spots.append(spot)


func _spot_layer() -> Node:
	var layer: Node = track(load("res://minigames/minigame_layer.tscn").instantiate())
	_now = 0.0
	_sent_ids.clear()
	_sent_spots.clear()
	layer.clock = _now_s
	layer.send_step = _count_send
	layer.send_spot = _count_spot
	return layer


## v2: use_ok kind "spot" opens the console's mini-game; finishing it sends spot_done for that spot.
func test_spot_use_ok_opens_the_game_and_sends_spot_done() -> bool:
	Session.task_types["sort_waste"] = {"min_s": 4.0, "consoles": ["bins_a", "bins_b"]}
	var layer: Node = _spot_layer()
	layer._on_use_ok({"kind": "spot", "type": "sort_waste", "console": "bins_b"})
	assert_true(layer.is_open(), "use_ok kind spot opens the game")
	var m: Minigame = layer._current
	assert_eq(m.step, 1, "the step of its console")
	assert_eq(layer._console_id, "bins_b", "walk-away check on the spot")
	m.finish_step()
	_now = 3.9
	m.poll()
	assert_eq(_sent_spots.size(), 0, "nothing before min_s")
	_now = 4.1
	m.poll()
	assert_eq(_sent_spots, ["bins_b"] as Array[String], "spot_done for the spot")
	assert_eq(_sent_ids.size(), 0, "no task_step in v2")
	assert_true(not layer.is_open(), "closed after the send")
	layer._on_use_ok({"kind": "spot", "type": "sort_waste", "console": "bins_a", "step": 1})
	assert_eq((layer._current as Minigame).step, 1, "the server's step wins")
	layer.close_all()
	Session.task_types.erase("sort_waste")
	return true


## With the Cleaning Tool the server wants min_s * tool_time_mult, so the client waits that long only.
func test_spot_game_with_the_tool_waits_the_shorter_time() -> bool:
	var old_tuning: Dictionary = Session.tuning
	Session.task_types["sort_waste"] = {"min_s": 4.0, "consoles": ["bins_a"]}
	Session.tuning = {"spots": {"tool_time_mult": 0.5}}
	Session.has_tool = true
	var layer: Node = _spot_layer()
	layer._on_use_ok({"kind": "spot", "type": "sort_waste", "console": "bins_a"})
	var m: Minigame = layer._current
	m.finish_step()
	_now = 2.1
	m.poll()
	assert_eq(_sent_spots, ["bins_a"] as Array[String], "sent after 4 s x 0.5")
	Session.has_tool = false
	Session.tuning = old_tuning
	Session.task_types.erase("sort_waste")
	return true


func test_registry_base_type_names_the_spot_art() -> bool:
	assert_eq(MinigameRegistry.base_type("sort_waste"), "sort_waste", "exact")
	assert_eq(MinigameRegistry.base_type("sort_waste_depot"), "sort_waste", "variant")
	assert_eq(MinigameRegistry.base_type("rinse_bottles_market"), "rinse_bottles", "variant")
	assert_eq(MinigameRegistry.base_type("nothing"), "", "unknown")
	return true
