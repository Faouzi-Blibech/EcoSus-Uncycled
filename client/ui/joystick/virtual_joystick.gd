extends Control
## Virtual joystick: it rests at the bottom-left corner and jumps under a thumb that touches the
## left half of the screen. Reads raw touch events in _input,
## so it works regardless of what Controls sit on top. `value` is a vector of length 0..1.

const RADIUS: float = 40.0
const DEADZONE: float = 0.15
const BASE_PATH: String = "res://assets/ui/hud/joystick_base.png"
const KNOB_PATH: String = "res://assets/ui/hud/joystick_knob.png"
## Drawn size of the base (px); the art keeps its ratio.
const BASE_PX: float = 84.0
const HOME_MARGIN: float = 12.0
const IDLE_ALPHA: float = 0.85
## The base art is see-through; a paper disc under it keeps it readable on any floor.
const PAPER: Color = Color(0.98, 0.95, 0.87, 0.8)
const INK: Color = Color(0.24, 0.17, 0.15, 0.9)

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
var _base: Texture2D
var _knob: Texture2D


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	if ResourceLoader.exists(BASE_PATH) and ResourceLoader.exists(KNOB_PATH):
		_base = load(BASE_PATH) as Texture2D
		_knob = load(KNOB_PATH) as Texture2D
	resized.connect(queue_redraw)


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
	# At rest it waits at its home corner; a touch brings it under the thumb.
	var centre: Vector2 = _origin if _index != -1 else home()
	if _base == null or _knob == null:
		draw_circle(centre, RADIUS, Color(1.0, 1.0, 1.0, 0.12))
		draw_arc(centre, RADIUS, 0.0, TAU, 32, Color(1.0, 1.0, 1.0, 0.35), 2.0)
		draw_circle(centre + value * RADIUS, 16.0, Color(1.0, 1.0, 1.0, 0.45))
		return
	var alpha: float = 1.0 if _index != -1 else IDLE_ALPHA
	var k: float = BASE_PX / float(maxi(_base.get_width(), _base.get_height()))
	var base_size: Vector2 = Vector2(_base.get_width(), _base.get_height()) * k
	var disc_r: float = base_size.x * 0.47
	draw_circle(centre, disc_r, Color(PAPER, PAPER.a * alpha), true, -1.0, true)
	draw_arc(centre, disc_r, 0.0, TAU, 48, Color(INK, INK.a * alpha), 2.0, true)
	draw_texture_rect(_base, Rect2(centre - base_size * 0.5, base_size), false, Color(1.0, 1.0, 1.0, alpha))
	var knob_size: Vector2 = Vector2(_knob.get_width(), _knob.get_height()) * k
	# The knob stays inside the base; `value` still uses the full RADIUS of thumb travel.
	var travel: float = (base_size.x - knob_size.x) * 0.5
	var knob_centre: Vector2 = centre + value * travel
	draw_texture_rect(_knob, Rect2(knob_centre - knob_size * 0.5, knob_size), false, Color(1.0, 1.0, 1.0, alpha))


## Where the stick rests, bottom-left, clear of the screen edge.
func home() -> Vector2:
	return Vector2(HOME_MARGIN + BASE_PX * 0.5, size.y - HOME_MARGIN - BASE_PX * 0.5)
