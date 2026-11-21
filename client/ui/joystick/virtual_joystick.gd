extends Control
## Floating virtual joystick on the left half of the screen. Reads raw touch events in _input,
## so it works regardless of what Controls sit on top. `value` is a vector of length 0..1.

const RADIUS: float = 40.0
const DEADZONE: float = 0.15

var value: Vector2 = Vector2.ZERO
## Off while a mini-game is open: its drags must not steer, and a held stick is dropped.
var enabled: bool = true:
	set(on):
		enabled = on
		if not on:
			_reset()

var _index: int = -1
var _origin: Vector2 = Vector2.ZERO
var _current: Vector2 = Vector2.ZERO


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)


func _input(event: InputEvent) -> void:
	if event is InputEventScreenTouch:
		var touch: InputEventScreenTouch = event as InputEventScreenTouch
		handle_touch(touch.index, touch.pressed, touch.position, get_viewport_rect().size.x)
	elif event is InputEventScreenDrag:
		var drag: InputEventScreenDrag = event as InputEventScreenDrag
		handle_drag(drag.index, drag.position)


func _notification(what: int) -> void:
	# A release lost to the notification shade or a tab switch must not keep the player walking.
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT or what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		_reset()


func handle_touch(index: int, pressed: bool, pos: Vector2, screen_width: float) -> void:
	if pressed:
		if enabled and _index == -1 and pos.x < screen_width * 0.5:
			_index = index
			_origin = pos
			_current = pos
			_update()
	elif index == _index:
		_reset()


func handle_drag(index: int, pos: Vector2) -> void:
	if index == _index:
		_current = pos
		_update()


func _update() -> void:
	var offset: Vector2 = _current - _origin
	var strength: float = clampf(offset.length() / RADIUS, 0.0, 1.0)
	if strength < DEADZONE:
		value = Vector2.ZERO
	else:
		value = offset.normalized() * strength
	queue_redraw()


func _reset() -> void:
	_index = -1
	value = Vector2.ZERO
	queue_redraw()


func _draw() -> void:
	if _index != -1:
		draw_circle(_origin, RADIUS, Color(1.0, 1.0, 1.0, 0.12))
		draw_arc(_origin, RADIUS, 0.0, TAU, 32, Color(1.0, 1.0, 1.0, 0.35), 2.0)
		draw_circle(_origin + value * RADIUS, 16.0, Color(1.0, 1.0, 1.0, 0.45))
	elif DisplayServer.is_touchscreen_available():
		var hint: Vector2 = Vector2(90.0, size.y - 90.0)
		draw_arc(hint, RADIUS, 0.0, TAU, 32, Color(1.0, 1.0, 1.0, 0.2), 2.0)
