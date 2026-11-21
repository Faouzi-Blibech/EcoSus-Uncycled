class_name GasOverlay
extends Node2D
## A green toxic tint over the gas room on the map while a gas warning runs.

const TILE_PX: float = 32.0
const ALPHA_START: float = 0.15
const ALPHA_END: float = 0.6

var _rect: Rect2 = Rect2()
var _has_rect: bool = false
var _alpha: float = ALPHA_END


func _init() -> void:
	z_index = 5
	visible = false


## `rect_tiles` is the room in tiles, `in_s` the seconds until the gas lands, `warning_s` the warning length.
func show_room(rect_tiles: Rect2, in_s: float, warning_s: float) -> void:
	_rect = rect_tiles
	_has_rect = true
	_alpha = alpha_for(in_s, warning_s)
	visible = true
	queue_redraw()


func clear() -> void:
	_has_rect = false
	visible = false
	queue_redraw()


## 0.15 when the warning starts, growing to 0.6 when the gas lands.
static func alpha_for(in_s: float, warning_s: float) -> float:
	if warning_s <= 0.0:
		return ALPHA_END
	var left: float = clampf(in_s, 0.0, warning_s)
	return lerpf(ALPHA_END, ALPHA_START, left / warning_s)


func _draw() -> void:
	if not visible or not _has_rect:
		return
	var px: Rect2 = Rect2(_rect.position * TILE_PX, _rect.size * TILE_PX)
	draw_rect(px, Color(0.45, 0.85, 0.2, _alpha), true)
	draw_rect(px, Color(0.45, 0.85, 0.2, minf(_alpha + 0.3, 1.0)), false, 2.0)
