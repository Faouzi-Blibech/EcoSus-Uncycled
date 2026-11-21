extends Node2D
## One afterimage of a dash: a disc of the player's colour that appears after `delay` and fades out.

const RADIUS_PX: float = 9.6
const START_ALPHA: float = 0.55
const FADE_S: float = 0.3

var color: Color = Color.WHITE
var delay: float = 0.0


func _ready() -> void:
	modulate.a = 0.0
	var t: Tween = create_tween()
	t.tween_interval(delay)
	t.tween_property(self, "modulate:a", START_ALPHA, 0.0)
	t.tween_property(self, "modulate:a", 0.0, FADE_S).set_ease(Tween.EASE_IN)
	t.tween_callback(queue_free)


func _draw() -> void:
	draw_circle(Vector2.ZERO, RADIUS_PX, color)
	draw_circle(Vector2.ZERO, RADIUS_PX * 0.55, color.lightened(0.5))
