extends "res://tests/test_case.gd"
## A second finger presses an action button: Godot turns only the first touch into mouse events,
## so the button reads the other fingers' touch events itself.

var _presses: int = 0


func _on_pressed() -> void:
	_presses += 1


func _button() -> ActionButton:
	var b: ActionButton = track(ActionButton.new()) as ActionButton
	b.pressed.connect(_on_pressed)
	_presses = 0
	return b


func _touch(index: int, pressed: bool) -> InputEventScreenTouch:
	var ev: InputEventScreenTouch = InputEventScreenTouch.new()
	ev.index = index
	ev.pressed = pressed
	ev.position = Vector2(10.0, 10.0)
	return ev


func test_a_second_finger_presses_the_button_once() -> bool:
	var b: ActionButton = _button()
	b._gui_input(_touch(1, true))
	b._gui_input(_touch(1, false))
	assert_eq(_presses, 1, "one tap by the right thumb while the left holds the stick")
	return true


func test_the_first_finger_is_left_to_the_mouse_path() -> bool:
	var b: ActionButton = _button()
	b._gui_input(_touch(0, true))
	b._gui_input(_touch(0, false))
	assert_eq(_presses, 0, "its emulated mouse click already presses the button: no double press")
	return true


func test_a_disabled_button_ignores_a_second_finger() -> bool:
	var b: ActionButton = _button()
	b.disabled = true
	b._gui_input(_touch(1, true))
	assert_eq(_presses, 0, "cooldown running")
	return true


func test_hold_mode_reports_start_and_end_of_a_second_finger() -> bool:
	var b: ActionButton = _button()
	b.hold_mode = true
	var log: Array[String] = []
	b.hold_started.connect(func() -> void: log.append("start"))
	b.hold_ended.connect(func() -> void: log.append("end"))
	b._gui_input(_touch(1, true))
	b._gui_input(_touch(2, false))
	assert_eq(log, ["start"], "another finger lifting does not end the hold")
	b._gui_input(_touch(1, false))
	assert_eq(log, ["start", "end"], "the finger that pressed lets go")
	assert_eq(_presses, 0, "a hold button never emits pressed from touch")
	return true


func test_hiding_a_held_button_ends_the_hold_and_it_can_be_held_again() -> bool:
	var b: ActionButton = _button()
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(b)
	b.hold_mode = true
	var log: Array[String] = []
	b.hold_started.connect(func() -> void: log.append("start"))
	b.hold_ended.connect(func() -> void: log.append("end"))
	b._gui_input(_touch(1, true))
	b.visible = false
	assert_eq(log, ["start", "end"], "hidden while held: ended")
	b.visible = true
	b._gui_input(_touch(1, true))
	assert_eq(log, ["start", "end", "start"], "the next press is not swallowed")
	return true
