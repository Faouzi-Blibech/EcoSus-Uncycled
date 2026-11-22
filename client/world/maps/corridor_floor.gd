@tool
extends Node2D
## Draws a layout map's ground outside the room art, as a campus: red brick between the buildings,
## warm stone tiles in the corridors, and white plaster walls (with a brown skirting board) wherever a
## corridor meets anything but more corridor or a room. The wall facing us (a corridor's north side)
## shows its painted face; the others a plaster cap.

const TILE_PX: int = 32
## Brick: one tile is two courses of two bricks, the second course offset by half a brick.
const BRICK: Color = Color("b0614a")
const BRICK_DARK: Color = Color("9a523d")
const BRICK_LIGHT: Color = Color("c0735a")
const MORTAR: Color = Color("e2d3c0")
const MORTAR_PX: float = 2.0
## Corridor floor: cream stone tiles with soft seams.
const FLOOR: Color = Color("efe6d6")
const FLOOR_ALT: Color = Color("e9dfcc")
const FLOOR_SEAM: Color = Color("d8cab3")
## Plaster walls.
const PLASTER: Color = Color("f6f1e7")
const PLASTER_SHADE: Color = Color("e4dccd")
const SKIRTING: Color = Color("8a6a52")
const INK: Color = Color("3e2b25")
const WALL_FACE_PX: float = 12.0
const WALL_CAP_PX: float = 5.0

var map_size: Vector2i = Vector2i.ZERO
## Corridor cells (Vector2i -> true) and room footprints in tiles.
var corridor_cells: Dictionary = {}
var room_rects: Array[Rect2i] = []


func _init() -> void:
	texture_repeat = CanvasItem.TEXTURE_REPEAT_ENABLED


func _draw() -> void:
	_draw_bricks()
	for cell in corridor_cells:
		var c: Vector2i = cell
		var r: Rect2 = Rect2(Vector2(c * TILE_PX), Vector2(TILE_PX, TILE_PX))
		draw_rect(r, FLOOR if (c.x + c.y) % 2 == 0 else FLOOR_ALT)
		draw_line(r.position, Vector2(r.end.x, r.position.y), FLOOR_SEAM, 1.0)
		draw_line(r.position, Vector2(r.position.x, r.end.y), FLOOR_SEAM, 1.0)
	for cell in corridor_cells:
		var c: Vector2i = cell
		var p: Vector2 = Vector2(c * TILE_PX)
		var t: float = TILE_PX
		if not _open(c + Vector2i.UP):
			# The wall's painted face, seen from the corridor: plaster, then the skirting board.
			draw_rect(Rect2(p, Vector2(t, WALL_FACE_PX)), PLASTER)
			draw_rect(Rect2(p + Vector2(0.0, WALL_FACE_PX * 0.55), Vector2(t, WALL_FACE_PX * 0.2)), PLASTER_SHADE)
			draw_rect(Rect2(p + Vector2(0.0, WALL_FACE_PX - 3.0), Vector2(t, 3.0)), SKIRTING)
			draw_line(p, p + Vector2(t, 0.0), INK, 1.5)
		if not _open(c + Vector2i.DOWN):
			draw_rect(Rect2(p + Vector2(0.0, t - WALL_CAP_PX), Vector2(t, WALL_CAP_PX)), PLASTER)
			draw_line(p + Vector2(0.0, t - WALL_CAP_PX), p + Vector2(t, t - WALL_CAP_PX), INK, 1.0)
			draw_line(p + Vector2(0.0, t), p + Vector2(t, t), INK, 1.5)
		if not _open(c + Vector2i.LEFT):
			draw_rect(Rect2(p, Vector2(WALL_CAP_PX, t)), PLASTER)
			draw_line(p + Vector2(WALL_CAP_PX, 0.0), p + Vector2(WALL_CAP_PX, t), INK, 1.0)
			draw_line(p, p + Vector2(0.0, t), INK, 1.5)
		if not _open(c + Vector2i.RIGHT):
			draw_rect(Rect2(p + Vector2(t - WALL_CAP_PX, 0.0), Vector2(WALL_CAP_PX, t)), PLASTER)
			draw_line(p + Vector2(t - WALL_CAP_PX, 0.0), p + Vector2(t - WALL_CAP_PX, t), INK, 1.0)
			draw_line(p + Vector2(t, 0.0), p + Vector2(t, t), INK, 1.5)


## Red brick over the whole map (the room pictures and corridors cover their parts): one tile of
## brick, made once and repeated, so the whole wall is a single textured quad.
func _draw_bricks() -> void:
	var total: Vector2 = Vector2(map_size * TILE_PX)
	draw_texture_rect(_brick_tile(), Rect2(Vector2.ZERO, total), true)


static var _bricks: ImageTexture


## 4 x 4 tiles of brick (four bricks per tile row, half-brick bond). Shades vary by a fixed hash so the
## wall looks laid by hand and the repeat is hard to spot.
static func _brick_tile() -> ImageTexture:
	if _bricks != null:
		return _bricks
	var tiles: int = 4
	var side: int = TILE_PX * tiles
	var img: Image = Image.create(side, side, false, Image.FORMAT_RGBA8)
	img.fill(MORTAR)
	var bw: int = TILE_PX >> 1
	var bh: int = TILE_PX >> 2
	var half_m: int = int(MORTAR_PX) >> 1
	for row in side >> 3:
		var shift: int = bw >> 1 if row % 2 == 1 else 0
		for col in (side >> 4) + 1:
			var h: int = absi((col * 73856093) ^ (row * 19349663))
			var shade: int = h % 5
			var color: Color = BRICK if shade < 3 else (BRICK_DARK if shade == 3 else BRICK_LIGHT)
			var x0: int = col * bw - shift
			for y in range(row * bh + half_m, row * bh + bh - half_m):
				for x in range(x0 + half_m, x0 + bw - half_m):
					img.set_pixel(posmod(x, side), y, color)
	_bricks = ImageTexture.create_from_image(img)
	return _bricks


## A neighbour that needs no wall line: more corridor, or a room (its art draws its own walls).
func _open(c: Vector2i) -> bool:
	if corridor_cells.has(c):
		return true
	for r in room_rects:
		if r.has_point(c):
			return true
	return false
