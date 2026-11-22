class_name BodyArt
extends RefCounted
## A fallen player drawn with shapes (no art file needed): a toxic puddle, the body lying on its
## side in the seat colour and a head with X eyes. The world body and the report splash share it.
## Sizes are in logical px at scale 1 (about one tile wide).

const PUDDLE_COLOR: Color = Color(0.36, 0.55, 0.12, 0.75)
const OUTLINE_COLOR: Color = Color(0.0, 0.0, 0.0, 0.8)
const EYE_COLOR: Color = Color(0.08, 0.08, 0.08)
const ALERT_RED: Color = Color(0.92, 0.22, 0.18)
const ALERT_WHITE: Color = Color(1.0, 1.0, 1.0)


## Draws the body centred on `center`. `s` scales every size (1 in the world, bigger on the splash).
static func draw_body(ci: CanvasItem, center: Vector2, color: Color, s: float) -> void:
	var outline: float = maxf(1.0, 1.5 * s)
	# Toxic puddle under the body.
	ci.draw_set_transform(center + Vector2(1.0, 3.0) * s, 0.0, Vector2(1.7, 0.75))
	ci.draw_circle(Vector2.ZERO, 9.0 * s, PUDDLE_COLOR)
	# Torso: an ellipse lying on its side, the seat colour a little darker.
	var torso: Color = color.darkened(0.15)
	ci.draw_set_transform(center + Vector2(3.0, 0.0) * s, 0.0, Vector2(1.45, 0.95))
	ci.draw_circle(Vector2.ZERO, 6.0 * s, torso)
	ci.draw_arc(Vector2.ZERO, 6.0 * s, 0.0, TAU, 24, OUTLINE_COLOR, outline / 1.2)
	ci.draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)
	# Two legs sticking out at the end.
	var hip: Vector2 = center + Vector2(10.5, 0.0) * s
	ci.draw_line(hip + Vector2(0.0, -2.5) * s, hip + Vector2(4.0, -4.5) * s, OUTLINE_COLOR, 3.2 * s)
	ci.draw_line(hip + Vector2(0.0, 2.5) * s, hip + Vector2(4.5, 3.5) * s, OUTLINE_COLOR, 3.2 * s)
	ci.draw_line(hip + Vector2(0.0, -2.5) * s, hip + Vector2(4.0, -4.5) * s, torso, 1.8 * s)
	ci.draw_line(hip + Vector2(0.0, 2.5) * s, hip + Vector2(4.5, 3.5) * s, torso, 1.8 * s)
	# Head with X eyes.
	var head: Vector2 = center + Vector2(-7.0, 0.0) * s
	ci.draw_circle(head, 5.0 * s, color)
	ci.draw_arc(head, 5.0 * s, 0.0, TAU, 20, OUTLINE_COLOR, outline)
	for eye in [Vector2(-1.8, -1.6), Vector2(-1.8, 1.6)]:
		_draw_x(ci, head + (eye as Vector2) * s, 1.1 * s, maxf(1.0, 0.8 * s))


## The "!" bubble shown over a body the player can report. `t` is seconds, for the bob.
static func draw_alert(ci: CanvasItem, center: Vector2, s: float, t: float) -> void:
	var c: Vector2 = center + Vector2(0.0, sin(t * 6.0) * 1.5 * s)
	ci.draw_circle(c, 6.0 * s, ALERT_RED)
	ci.draw_arc(c, 6.0 * s, 0.0, TAU, 20, ALERT_WHITE, maxf(1.0, 1.2 * s))
	ci.draw_rect(Rect2(c + Vector2(-1.0, -4.0) * s, Vector2(2.0, 5.0) * s), ALERT_WHITE)
	ci.draw_circle(c + Vector2(0.0, 3.0) * s, 1.1 * s, ALERT_WHITE)


static func _draw_x(ci: CanvasItem, at: Vector2, r: float, width: float) -> void:
	ci.draw_line(at + Vector2(-r, -r), at + Vector2(r, r), EYE_COLOR, width)
	ci.draw_line(at + Vector2(-r, r), at + Vector2(r, -r), EYE_COLOR, width)
