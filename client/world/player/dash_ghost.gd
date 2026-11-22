extends Node2D
## One afterimage of a dash: the player's pose (set by Avatar.dress_ghost), washed in the player's
## colour, that appears after `delay` and fades out. Without a pose it draws a colour disc.

const RADIUS_PX: float = 9.6
const START_ALPHA: float = 0.55
const FADE_S: float = 0.3
## How far the pose is washed towards the player's colour.
const COLOR_WASH: float = 0.5

var color: Color = Color.WHITE
var delay: float = 0.0
var texture: Texture2D = null
var flip: bool = false
var tex_scale: Vector2 = Vector2.ONE
var tex_offset: Vector2 = Vector2.ZERO


func _ready() -> void:
	modulate.a = 0.0
	var t: Tween = create_tween()
	t.tween_interval(delay)
	t.tween_property(self, "modulate:a", START_ALPHA, 0.0)
	t.tween_property(self, "modulate:a", 0.0, FADE_S).set_ease(Tween.EASE_IN)
	t.tween_callback(queue_free)


func _draw() -> void:
	if texture == null:
		draw_circle(Vector2.ZERO, RADIUS_PX, color)
		draw_circle(Vector2.ZERO, RADIUS_PX * 0.55, color.lightened(0.5))
		return
	var size: Vector2 = texture.get_size() * tex_scale
	draw_set_transform(tex_offset, 0.0, Vector2(-1.0 if flip else 1.0, 1.0))
	draw_texture_rect(texture, Rect2(-size / 2.0, size), false, Color.WHITE.lerp(color, COLOR_WASH))
	draw_set_transform(Vector2.ZERO)
