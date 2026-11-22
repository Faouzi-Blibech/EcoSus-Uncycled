class_name ActionButton
extends Button
## A round-corner action button (USE, REPORT, HIT, DUMP, ...) with a cooldown ring drawn from the
## seconds the server reports; it never counts time itself. In `hold_mode` it reports
## `hold_started` / `hold_ended` (finger or mouse down and up) instead of `pressed`.
## With `set_art` it is drawn as a painted round button (normal, pressed and disabled pictures,
## ratio kept) with its label written across the bottom; without art it keeps the theme look.

signal hold_started()
signal hold_ended()

const BUTTON_SIZE: Vector2 = Vector2(64.0, 64.0)
const RING_WIDTH: float = 4.0
const RING_COLOR: Color = Color(1.0, 0.85, 0.3, 0.95)
const RING_TRACK_COLOR: Color = Color(0.0, 0.0, 0.0, 0.45)
const RING_POINTS: int = 48
const ART_PATH: String = "res://assets/ui/hud/btn_%s%s.png"
const LABEL_FONT_SIZE: int = 10
const LABEL_OUTLINE: int = 4
const LABEL_COLOR: Color = Color(1.0, 0.98, 0.92)
const LABEL_OUTLINE_COLOR: Color = Color(0.24, 0.17, 0.15)

## Set before use: the button is held, not tapped.
var hold_mode: bool = false

var _fraction: float = 0.0
var _holding: bool = false
var _hold_touch: int = -1
var _art: Dictionary = {}
var _art_name: String = ""


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


## Uses the painted button `name` (assets/ui/hud/btn_<name>.png, with _pressed and _disabled).
## Missing pictures leave the button as it was.
func set_art(art_name: String) -> void:
	if art_name == _art_name:
		return
	var normal: String = ART_PATH % [art_name, ""]
	if not ResourceLoader.exists(normal):
		return
	_art_name = art_name
	_art.clear()
	for state in ["", "_pressed", "_disabled"]:
		var path: String = ART_PATH % [art_name, state]
		_art[state] = load(path) as Texture2D if ResourceLoader.exists(path) else load(normal) as Texture2D
	var none: StyleBoxEmpty = StyleBoxEmpty.new()
	for box in ["normal", "hover", "pressed", "hover_pressed", "disabled", "focus"]:
		add_theme_stylebox_override(box, none)
	# The label is drawn over the picture in _draw; the theme text would sit under it.
	for c in ["font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_disabled_color", "font_focus_color", "font_outline_color"]:
		add_theme_color_override(c, Color(0.0, 0.0, 0.0, 0.0))
	queue_redraw()


func art_name() -> String:
	return _art_name


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
	if not _art.is_empty():
		_draw_art()
	if _fraction <= 0.0:
		return
	var center: Vector2 = size * 0.5
	var radius: float = minf(size.x, size.y) * 0.5 - RING_WIDTH * 0.5
	draw_arc(center, radius, 0.0, TAU, RING_POINTS, RING_TRACK_COLOR, RING_WIDTH, true)
	draw_arc(center, radius, -PI * 0.5, -PI * 0.5 + TAU * _fraction, RING_POINTS, RING_COLOR, RING_WIDTH, true)


func _draw_art() -> void:
	var state: String = ""
	if disabled:
		state = "_disabled"
	elif get_draw_mode() == DRAW_PRESSED or get_draw_mode() == DRAW_HOVER_PRESSED:
		state = "_pressed"
	var tex: Texture2D = _art[state] as Texture2D
	var side: float = minf(size.x, size.y)
	var k: float = side / maxf(float(tex.get_width()), float(tex.get_height()))
	var art_size: Vector2 = Vector2(tex.get_width(), tex.get_height()) * k
	draw_texture_rect(tex, Rect2((size - art_size) * 0.5, art_size), false)
	if text.is_empty():
		return
	var font: Font = get_theme_font("font")
	var lines: PackedStringArray = text.split("
")
	var line_h: float = font.get_height(LABEL_FONT_SIZE)
	var y: float = size.y - line_h * float(lines.size()) + font.get_ascent(LABEL_FONT_SIZE) - 1.0
	for line in lines:
		var w: float = font.get_string_size(line, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE).x
		var pos: Vector2 = Vector2((size.x - w) * 0.5, y)
		draw_string_outline(font, pos, line, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE, LABEL_OUTLINE, LABEL_OUTLINE_COLOR)
		draw_string(font, pos, line, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE, LABEL_COLOR)
		y += line_h
