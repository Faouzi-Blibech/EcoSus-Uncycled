class_name FollowCamera
extends Camera2D
## Camera that follows its parent (the local player) smoothly and never shows past the map edge.

const SMOOTHING_SPEED: float = 10.0


func _ready() -> void:
	process_callback = Camera2D.CAMERA2D_PROCESS_PHYSICS
	position_smoothing_enabled = true
	position_smoothing_speed = SMOOTHING_SPEED


## Map bounds in pixels.
func set_bounds(bounds_px: Rect2i) -> void:
	limit_left = bounds_px.position.x
	limit_top = bounds_px.position.y
	limit_right = bounds_px.end.x
	limit_bottom = bounds_px.end.y


## Jumps to the parent without easing (spawn, server correction).
func snap() -> void:
	reset_smoothing()
