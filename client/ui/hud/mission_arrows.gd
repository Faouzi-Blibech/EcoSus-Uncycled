class_name MissionArrows
extends Control
## Guides the player to their missions: the nearest targets get an arrow at the screen edge when
## they are off screen, or a bobbing arrow over them when in sight.
## Cleaner: dirty spots while a bag slot is free, the Recycling Machine once the bags are full (or
## nothing is left to clean). Trasher: clean spots to re-pollute. Nothing while out or not playing.

const MAX_ARROWS: int = 2
const EDGE_INSET_PX: float = 26.0
const ARROW_LEN: float = 14.0
const ARROW_HALF_W: float = 9.0
const OUTLINE_PX: float = 2.0
const BOB_PX: float = 4.0
const BOB_HZ: float = 2.0
## Above the target's centre when it is in sight.
const OVER_TARGET_PX: float = 34.0
const INK: Color = Color(0.24, 0.17, 0.15)
const SPOT_COLOR: Color = Color(0.54, 0.67, 0.42)
const MACHINE_COLOR: Color = Color(0.96, 0.76, 0.38)
const TRASHER_COLOR: Color = Color(0.48, 0.39, 0.68)
const MACHINE_ID: String = "machine"

## World positions (px) of the targets, nearest first, and their colour.
var _targets: Array[Vector2] = []
var _color: Color = SPOT_COLOR
var _canvas: Transform2D = Transform2D.IDENTITY


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE


## What the player should head to: {"kind": "spots"/"machine"/"", "state": spot state to look for}.
## Pure, for tests.
static func goal(role: String, phase: String, is_out: bool, bags: int, cap: int, dirty_left: int) -> Dictionary:
	if phase != "playing" or is_out:
		return {"kind": ""}
	if role == "polluter":
		return {"kind": "spots", "state": "clean"}
	var full: bool = cap > 0 and bags >= cap
	if full or (bags > 0 and dirty_left == 0):
		return {"kind": "machine"}
	if dirty_left > 0:
		return {"kind": "spots", "state": "dirty"}
	return {"kind": ""}


## Reads the round each frame: the player, the map's spots and machine, the camera.
func update_from(round_scene: RoundScene) -> void:
	_targets.clear()
	var map: MapRoot = round_scene.map() if round_scene != null else null
	if map == null:
		queue_redraw()
		return
	var dirty_left: int = 0
	for id in Session.spots:
		if str(Session.spots[id]) == "dirty":
			dirty_left += 1
	var g: Dictionary = goal(Session.role, Session.phase, Session.is_out, Session.bags, Session.bag_cap, dirty_left)
	var me: Vector2 = round_scene.local_position_px()
	var found: Array[Vector2] = []
	match str(g["kind"]):
		"machine":
			var m: MapMarker = map.marker(MACHINE_ID)
			if m != null:
				found.append(m.global_position)
			_color = MACHINE_COLOR
		"spots":
			for m in map.markers():
				if m.kind == "task" and str(Session.spots.get(m.marker_id, "")) == str(g["state"]):
					found.append(m.global_position)
			_color = TRASHER_COLOR if Session.role == "polluter" else SPOT_COLOR
	found.sort_custom(func(a: Vector2, b: Vector2) -> bool: return a.distance_squared_to(me) < b.distance_squared_to(me))
	_targets = found.slice(0, MAX_ARROWS)
	_canvas = round_scene.get_viewport().get_canvas_transform()
	queue_redraw()


func _process(_delta: float) -> void:
	if not _targets.is_empty():
		queue_redraw()


func _draw() -> void:
	var view: Rect2 = Rect2(Vector2.ZERO, size).grow(-EDGE_INSET_PX)
	var bob: float = sin(Time.get_ticks_msec() / 1000.0 * TAU * BOB_HZ) * BOB_PX
	for world in _targets:
		var p: Vector2 = get_global_transform().affine_inverse() * (_canvas * world)
		if view.has_point(p):
			_arrow(p + Vector2(0.0, -OVER_TARGET_PX + bob), Vector2.DOWN)
		else:
			var centre: Vector2 = size * 0.5
			var dir: Vector2 = (p - centre).normalized()
			_arrow(_edge_point(view, centre, dir) - dir * bob, dir)


## Where the ray from centre along dir leaves rect.
static func _edge_point(rect: Rect2, centre: Vector2, dir: Vector2) -> Vector2:
	var t: float = INF
	if dir.x != 0.0:
		t = minf(t, ((rect.end.x if dir.x > 0.0 else rect.position.x) - centre.x) / dir.x)
	if dir.y != 0.0:
		t = minf(t, ((rect.end.y if dir.y > 0.0 else rect.position.y) - centre.y) / dir.y)
	return centre + dir * t


## A chunky triangle pointing along dir with its tip at tip, ink outlined.
func _arrow(tip: Vector2, dir: Vector2) -> void:
	var side: Vector2 = dir.orthogonal() * ARROW_HALF_W
	var base: Vector2 = tip - dir * ARROW_LEN
	var pts: PackedVector2Array = PackedVector2Array([tip, base + side, base - side])
	draw_colored_polygon(pts, _color)
	pts.append(tip)
	draw_polyline(pts, INK, OUTLINE_PX, true)
