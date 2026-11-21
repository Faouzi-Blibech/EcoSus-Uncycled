@tool
extends Node2D
## Draws a layout map's ground outside the room art: the dark backdrop and the corridor floors,
## with a wall line wherever a corridor meets anything but more corridor or a room.

const TILE_PX: int = 32
const BACKDROP: Color = Color(0.17, 0.14, 0.2)
const FLOOR: Color = Color(0.82, 0.76, 0.86)
const FLOOR_SEAM: Color = Color(0.76, 0.7, 0.81)
const EDGE: Color = Color(0.2, 0.15, 0.22)
const EDGE_PX: float = 4.0

var map_size: Vector2i = Vector2i.ZERO
## Corridor cells (Vector2i -> true) and room footprints in tiles.
var corridor_cells: Dictionary = {}
var room_rects: Array[Rect2i] = []


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, Vector2(map_size * TILE_PX)), BACKDROP)
	for cell in corridor_cells:
		var c: Vector2i = cell
		var r: Rect2 = Rect2(Vector2(c * TILE_PX), Vector2(TILE_PX, TILE_PX))
		draw_rect(r, FLOOR)
		draw_line(r.position, Vector2(r.end.x, r.position.y), FLOOR_SEAM, 1.0)
	for cell in corridor_cells:
		var c: Vector2i = cell
		var p: Vector2 = Vector2(c * TILE_PX)
		var t: float = TILE_PX
		if not _open(c + Vector2i.UP):
			draw_rect(Rect2(p, Vector2(t, EDGE_PX)), EDGE)
		if not _open(c + Vector2i.DOWN):
			draw_rect(Rect2(p + Vector2(0.0, t - EDGE_PX), Vector2(t, EDGE_PX)), EDGE)
		if not _open(c + Vector2i.LEFT):
			draw_rect(Rect2(p, Vector2(EDGE_PX, t)), EDGE)
		if not _open(c + Vector2i.RIGHT):
			draw_rect(Rect2(p + Vector2(t - EDGE_PX, 0.0), Vector2(EDGE_PX, t)), EDGE)


## A neighbour that needs no wall line: more corridor, or a room (its art draws its own walls).
func _open(c: Vector2i) -> bool:
	if corridor_cells.has(c):
		return true
	for r in room_rects:
		if r.has_point(c):
			return true
	return false
