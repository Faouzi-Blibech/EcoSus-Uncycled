class_name ActionButton
extends Button
## A round-corner action button (USE, REPORT, HIT, DUMP, ...) with a cooldown ring drawn from the
## seconds the server reports; it never counts time itself. In `hold_mode` it reports
## `hold_started` / `hold_ended` (finger or mouse down and up) instead of `pressed`.

signal hold_started()
signal hold_ended()

const BUTTON_SIZE: Vector2 = Vector2(64.0, 64.0)
const RING_WIDTH: float = 4.0
const RING_COLOR: Color = Color(1.0, 0.85, 0.3, 0.95)
const RING_TRACK_COLOR: Color = Color(0.0, 0.0, 0.0, 0.45)
const RING_POINTS: int = 48

## Set before use: the button is held, not tapped.
var hold_mode: bool = false

var _fraction: float = 0.0
var _holding: bool = false
var _hold_touch: int = -1


func _init() -> void:
	custom_minimum_size = BUTTON_SIZE
	focus_mode = Control.FOCUS_NONE
	add_theme_font_size_override("font_size", 12)
	button_down.connect(_begin_hold)
	button_up.connect(_end_hold)


## remaining and total in seconds; no ring when nothing is left to wait.
func set_ring(remaining: float, total: float) -> void:
	var f: float = 0.0
	if remaining > 0.0 and total > 0.0:
		f = clampf(remaining / total, 0.0, 1.0)
	elif remaining > 0.0:
		f = 1.0
	if not is_equal_approx(f, _fraction):
		_fraction = f
		queue_redraw()


func ring_fraction() -> float:
	return _fraction


## Godot makes mouse events only from the first touch (index 0), so a second finger (the right
## thumb while the left holds the joystick) would press nothing. The first finger is not handled
## here: its emulated mouse click already presses the button.
func _gui_input(event: InputEvent) -> void:
	if event is InputEventScreenTouch:
		var touch: InputEventScreenTouch = event as InputEventScreenTouch
		if touch.index == 0:
			return
		if touch.pressed and not disabled:
			if hold_mode:
				_hold_touch = touch.index
				_begin_hold()
			else:
				pressed.emit()
			accept_event()
		elif not touch.pressed and hold_mode and touch.index == _hold_touch:
			_hold_touch = -1
			_end_hold()
			accept_event()


## Godot sends no button_up when a pressed button is hidden; the hold ends here instead.
func _notification(what: int) -> void:
	if what == NOTIFICATION_VISIBILITY_CHANGED and not is_visible_in_tree():
		_hold_touch = -1
		_end_hold()


func _begin_hold() -> void:
	if hold_mode and not _holding:
		_holding = true
		hold_started.emit()


func _end_hold() -> void:
	if hold_mode and _holding:
		_holding = false
		hold_ended.emit()


func _draw() -> void:
	if _fraction <= 0.0:
		return
	var center: Vector2 = size * 0.5
	var radius: float = minf(size.x, size.y) * 0.5 - RING_WIDTH * 0.5
	draw_arc(center, radius, 0.0, TAU, RING_POINTS, RING_TRACK_COLOR, RING_WIDTH, true)
	draw_arc(center, radius, -PI * 0.5, -PI * 0.5 + TAU * _fraction, RING_POINTS, RING_COLOR, RING_WIDTH, true)
