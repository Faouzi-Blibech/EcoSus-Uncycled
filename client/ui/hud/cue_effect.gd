class_name CueEffect
extends Node2D
## A short world-space flourish for what another player did in sight: a toxic puff for a hit,
## a splat for a dump. Draws plain shapes and frees itself.

const PUFF_COLOR: Color = Color(0.55, 0.85, 0.2)
const SPLAT_COLOR: Color = Color(0.45, 0.32, 0.18)
const DURATION_S: float = 0.9
const PUFF_BLOBS: int = 6
const SPLAT_BLOBS: int = 9
const PUFF_RISE_PX: float = 18.0
const BLOB_RADIUS_PX: float = 7.0
const SPREAD_PX: float = 14.0

var kind: String = ""
var progress: float = 0.0:
	set(v):
		progress = v
		queue_redraw()


## Adds an effect for `kind` ("hit" or "dump") at pos (pixels, parent space).
static func spawn(parent: Node, effect_kind: String, pos: Vector2) -> CueEffect:
	var fx: CueEffect = CueEffect.new()
	fx.kind = effect_kind
	fx.position = pos
	fx.z_index = 5
	parent.add_child(fx)
	var tween: Tween = fx.create_tween()
	tween.tween_property(fx, "progress", 1.0, DURATION_S)
	tween.tween_callback(fx.queue_free)
	return fx


func _draw() -> void:
	var fade: float = 1.0 - progress
	if kind == "hit":
		for i in PUFF_BLOBS:
			var a: float = TAU * float(i) / float(PUFF_BLOBS)
			var c: Vector2 = Vector2.from_angle(a) * SPREAD_PX * progress + Vector2(0.0, -PUFF_RISE_PX * progress)
			draw_circle(c, BLOB_RADIUS_PX * (0.6 + progress), Color(PUFF_COLOR, 0.7 * fade))
	elif kind == "dump":
		for i in SPLAT_BLOBS:
			var a: float = TAU * float(i) / float(SPLAT_BLOBS) + 0.4 * float(i % 3)
			var reach: float = SPREAD_PX * (0.5 + 0.5 * float(i % 3)) * minf(progress * 4.0, 1.0)
			var r: float = BLOB_RADIUS_PX * (0.4 + 0.25 * float(i % 4))
			draw_circle(Vector2.from_angle(a) * reach, r, Color(SPLAT_COLOR, 0.85 * fade))
		draw_circle(Vector2.ZERO, BLOB_RADIUS_PX, Color(SPLAT_COLOR, 0.9 * fade))
