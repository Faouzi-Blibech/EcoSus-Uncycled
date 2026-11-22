class_name FollowCamera
extends Camera2D
## Camera that follows its parent (the local player) smoothly and keeps them in the centre of the
## screen, also at the map's edge: the limits are the map grown by half the visible area, so the
## camera never has to stop before the player is centred (past the edge shows the background).

const SMOOTHING_SPEED: float = 10.0

## Map bounds in pixels (the last set_bounds), empty until then.
var _bounds: Rect2i = Rect2i()


func _ready() -> void:
	process_callback = Camera2D.CAMERA2D_PROCESS_PHYSICS
	position_smoothing_enabled = true
	position_smoothing_speed = SMOOTHING_SPEED
	get_viewport().size_changed.connect(_apply_limits)
	_apply_limits()


## Map bounds in pixels.
func set_bounds(bounds_px: Rect2i) -> void:
	_bounds = bounds_px
	_apply_limits()


## The map rect grown by `half_view` on every side: the camera centre can then reach every map
## point, so the player stays centred.
static func centred_limits(bounds_px: Rect2i, half_view: Vector2) -> Rect2i:
	var pad := Vector2i(ceili(half_view.x), ceili(half_view.y))
	return Rect2i(bounds_px.position - pad, bounds_px.size + pad * 2)


func _apply_limits() -> void:
	if _bounds.size == Vector2i.ZERO:
		return
	var half_view: Vector2 = Vector2.ZERO
	if is_inside_tree():
		half_view = get_viewport_rect().size / zoom * 0.5
	var r: Rect2i = centred_limits(_bounds, half_view)
	limit_left = r.position.x
	limit_top = r.position.y
	limit_right = r.end.x
	limit_bottom = r.end.y


## Jumps to the parent without easing (spawn, server correction).
func snap() -> void:
	reset_smoothing()
