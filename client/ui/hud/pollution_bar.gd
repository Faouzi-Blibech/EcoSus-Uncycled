class_name PollutionBar
extends Control
## The pollution bar of the HUD: a painted frame whose channel fills from fresh green to smoggy
## brown up to the smog cloud at its end. `value` is the pollution in percent (0..100); the fill
## eases towards it. Drawn at the control's width, its height follows the art's ratio.

const UNDER_PATH: String = "res://assets/ui/hud/bar_pollution_under.png"
const FILL_PATH: String = "res://assets/ui/hud/bar_pollution_fill.png"
const OVER_PATH: String = "res://assets/ui/hud/bar_pollution_over.png"
## Where the channel starts and ends across the art's width (0..1), measured on the art.
const CHANNEL_START: float = 12.7 / 512.0
const CHANNEL_END: float = 488.9 / 512.0
## Fraction of the remaining distance the fill covers per second.
const EASE_PER_S: float = 6.0
const FALLBACK_COLOR: Color = Color(0.55, 0.45, 0.35)

var value: float = 0.0
var _shown: float = 0.0
var _under: Texture2D
var _fill: Texture2D
var _over: Texture2D


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _ready() -> void:
	if ResourceLoader.exists(UNDER_PATH) and ResourceLoader.exists(FILL_PATH) and ResourceLoader.exists(OVER_PATH):
		_under = load(UNDER_PATH) as Texture2D
		_fill = load(FILL_PATH) as Texture2D
		_over = load(OVER_PATH) as Texture2D
	resized.connect(_fit_height)
	_fit_height()
	_shown = value


## Height that keeps the art's ratio at the current width.
func _fit_height() -> void:
	if _under == null or size.x <= 0.0:
		return
	var h: float = size.x * float(_under.get_height()) / float(_under.get_width())
	if not is_equal_approx(custom_minimum_size.y, h):
		custom_minimum_size.y = h


func _process(delta: float) -> void:
	var target: float = clampf(value, 0.0, 100.0)
	if absf(target - _shown) < 0.05:
		if _shown != target:
			_shown = target
			queue_redraw()
		return
	_shown = lerpf(_shown, target, clampf(EASE_PER_S * delta, 0.0, 1.0))
	queue_redraw()


func _draw() -> void:
	var f: float = _shown / 100.0
	if _under == null:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.0, 0.0, 0.0, 0.35))
		draw_rect(Rect2(Vector2.ZERO, Vector2(size.x * f, size.y)), FALLBACK_COLOR)
		return
	var w: float = size.x
	var s: float = w / float(_under.get_width())
	var h: float = float(_under.get_height()) * s
	var top: float = (size.y - h) * 0.5
	draw_texture_rect(_under, Rect2(0.0, top, w, h), false)
	if f > 0.0:
		var src_w: float = float(_fill.get_width()) * (CHANNEL_START + (CHANNEL_END - CHANNEL_START) * f)
		draw_texture_rect_region(_fill, Rect2(0.0, top, src_w * s, h), Rect2(0.0, 0.0, src_w, float(_fill.get_height())))
	draw_texture_rect(_over, Rect2(0.0, top, w, h), false)
