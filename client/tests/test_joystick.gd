extends "res://tests/test_case.gd"
## The virtual joystick: it steers from the left half only, and a game on top switches it off.

const Joystick := preload("res://ui/joystick/virtual_joystick.gd")
const WIDTH: float = 640.0


func _stick() -> Control:
	return track(Joystick.new()) as Control


func test_touch_on_the_left_half_steers() -> bool:
	var j: Control = _stick()
	j.handle_touch(0, true, Vector2(100.0, 200.0), WIDTH)
	j.handle_drag(0, Vector2(140.0, 200.0))
	assert_near(j.value.x, 1.0, 0.001, "full push to the right")
	j.handle_touch(0, false, Vector2(140.0, 200.0), WIDTH)
	assert_eq(j.value, Vector2.ZERO, "release stops it")
	j.handle_touch(1, true, Vector2(400.0, 200.0), WIDTH)
	j.handle_drag(1, Vector2(440.0, 200.0))
	assert_eq(j.value, Vector2.ZERO, "the right half is not the stick")
	return true


func test_disabling_resets_a_held_stick_and_ignores_new_touches() -> bool:
	var j: Control = _stick()
	j.handle_touch(0, true, Vector2(100.0, 200.0), WIDTH)
	j.handle_drag(0, Vector2(140.0, 200.0))
	j.enabled = false
	assert_eq(j.value, Vector2.ZERO, "a game opening drops the held stick")
	j.handle_touch(2, true, Vector2(100.0, 200.0), WIDTH)
	j.handle_drag(2, Vector2(100.0, 240.0))
	assert_eq(j.value, Vector2.ZERO, "a drag inside the game is not a push")
	j.enabled = true
	j.handle_touch(3, true, Vector2(100.0, 200.0), WIDTH)
	j.handle_drag(3, Vector2(100.0, 240.0))
	assert_near(j.value.y, 1.0, 0.001, "steers again once the game is closed")
	return true


func test_losing_focus_releases_the_stick() -> bool:
	var j: Control = _stick()
	j.handle_touch(0, true, Vector2(100.0, 200.0), WIDTH)
	j.handle_drag(0, Vector2(140.0, 200.0))
	j._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_OUT)
	assert_eq(j.value, Vector2.ZERO, "the lost release does not keep the player walking")
	return true
