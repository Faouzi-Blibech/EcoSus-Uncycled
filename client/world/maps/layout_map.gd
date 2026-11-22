@tool
class_name LayoutMap
extends MapRoot
## A map built from a layout JSON (see tbs_01.layout.json): room art stitched with corridors.
## Builds, as soon as layout_path is set (so also when the export tool instantiates the scene):
## - Floor: invisible tiles, wall (blocks walking and light) or obstacle (blocks walking only);
## - Art: backdrop and corridor floors, then the room pictures;
## - Occluders: pieces of the art with a base line, y-sorted with the players for depth;
## - Markers: spawns, consoles, specials, zones, dark and gas rooms.

const CorridorFloor := preload("res://world/maps/corridor_floor.gd")
const OCCLUDER_SHADER: String = "res://world/maps/occluder.gdshader"

const FLOOR_ATLAS: Vector2i = Vector2i(0, 0)
const WALL_ATLAS: Vector2i = Vector2i(1, 0)
const OBSTACLE_ATLAS: Vector2i = Vector2i(2, 0)
const GENERATED: Array[String] = ["Art", "Floor", "Occluders", "Markers", "Edge"]
## Thickness of the solid frame around the map (px). Outside the map there are no tiles, so without
## it a floor cell on the edge lets the body walk off the map (the server refuses it: a snap back).
const EDGE_PX: float = 64.0

@export_file("*.json") var layout_path: String = "":
	set(value):
		layout_path = value
		_build()

## The sight grid sent to the server: walls only, so furniture blocks walking but not sight.
var _sight: Array[String] = []


func sight_rows() -> Array[String]:
	return _sight


func _build() -> void:
	for n in GENERATED:
		var old: Node = get_node_or_null(n)
		if old != null:
			remove_child(old)
			old.free()
	_sight.clear()
	if layout_path.is_empty() or not FileAccess.file_exists(layout_path):
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(layout_path))
	if not parsed is Dictionary:
		push_error("LayoutMap: %s is not a JSON object" % layout_path)
		return
	var lay: Dictionary = parsed
	var size: Vector2i = _vec2i(lay.get("size", [1, 1]))
	var art_scale: float = float(lay.get("art_scale", 0.5))

	# Cells: "#" wall, "o" obstacle, "." floor. Everything starts as wall.
	var cells: Array = []
	for y in size.y:
		var row: Array = []
		row.resize(size.x)
		row.fill("#")
		cells.append(row)
	var corridor_cells: Dictionary = {}
	for r in lay.get("corridors", []):
		var rect: Rect2i = _rect2i(r)
		for y in range(rect.position.y, rect.end.y):
			for x in range(rect.position.x, rect.end.x):
				cells[y][x] = "."
				corridor_cells[Vector2i(x, y)] = true

	var art := Node2D.new()
	art.name = "Art"
	add_child(art)
	var ground: Node2D = CorridorFloor.new()
	ground.map_size = size
	ground.corridor_cells = corridor_cells
	art.add_child(ground)
	var occluders := Node2D.new()
	occluders.name = "Occluders"
	occluders.y_sort_enabled = true

	var room_rects: Array[Rect2i] = []
	for room in lay.get("rooms", []):
		# A room may set its own art_scale (its picture drawn larger or smaller), always uniform.
		var rs: float = float((room as Dictionary).get("art_scale", art_scale))
		room_rects.append(_add_room(room as Dictionary, cells, art, occluders, rs, TILE_PX / rs))
	ground.room_rects = room_rects

	add_child(_floor_layer(cells))
	add_child(_edge(size))
	add_child(occluders)
	add_child(_markers(lay.get("markers", [])))
	for row in cells:
		_sight.append("".join(PackedStringArray(row)).replace("o", "."))


## Four solid strips just outside the map, on the walls' collision layer. A separate body, so the
## floor layer's used rect (camera bounds, the export) stays the map itself.
static func _edge(size: Vector2i) -> StaticBody2D:
	var body := StaticBody2D.new()
	body.name = "Edge"
	body.collision_layer = 1
	body.collision_mask = 0
	var w: float = size.x * TILE_PX
	var h: float = size.y * TILE_PX
	var e: float = EDGE_PX
	for r in [Rect2(-e, -e, w + 2.0 * e, e), Rect2(-e, h, w + 2.0 * e, e), Rect2(-e, 0.0, e, h), Rect2(w, 0.0, e, h)]:
		var shape := RectangleShape2D.new()
		shape.size = (r as Rect2).size
		var c := CollisionShape2D.new()
		c.shape = shape
		c.position = (r as Rect2).get_center()
		body.add_child(c)
	return body


## Paints the room's cells and adds its picture and depth pieces. Returns its footprint in tiles.
func _add_room(room: Dictionary, cells: Array, art: Node2D, occluders: Node2D, art_scale: float, art_tile: float) -> Rect2i:
	var origin: Vector2i = _vec2i(room.get("origin", [0, 0]))
	var rsize: Vector2i = _vec2i(room.get("size", [1, 1]))
	var doors: Dictionary = room.get("doors", {})
	for y in rsize.y:
		for x in rsize.x:
			var edge: bool = x == 0 or y == 0 or x == rsize.x - 1 or y == rsize.y - 1
			var c: String = "."
			if edge:
				var door: bool = (y == 0 and _has(doors, "top", x)) or (y == rsize.y - 1 and _has(doors, "bottom", x)) \
					or (x == 0 and _has(doors, "left", y)) or (x == rsize.x - 1 and _has(doors, "right", y))
				c = "." if door else "#"
			cells[origin.y + y][origin.x + x] = c
	for r in room.get("walls", []):
		_paint(cells, origin, _rect2i(r), "#")
	for r in room.get("blocked", []):
		_paint(cells, origin, _rect2i(r), "o")

	# Loaded directly: the export tool runs without autoloads. A missing picture leaves the room blank.
	var image_path: String = str(room.get("image", ""))
	var tex: Texture2D = load(image_path) as Texture2D if ResourceLoader.exists(image_path) else null
	if tex == null:
		push_warning("LayoutMap: missing room art %s" % image_path)
	var sprite := Sprite2D.new()
	sprite.texture = tex
	sprite.centered = false
	sprite.scale = Vector2(art_scale, art_scale)
	sprite.position = Vector2(origin * TILE_PX)
	art.add_child(sprite)

	var mat: ShaderMaterial = null
	for o in room.get("occluders", []):
		var od: Dictionary = o
		var r: Array = od.get("rect", [0, 0, 0, 0])
		var tl := Vector2(float(r[0]), float(r[1]))
		var br := Vector2(float(r[2]), float(r[3]))
		var base: float = float(od.get("base", br.y))
		if tex == null:
			break
		var piece := Sprite2D.new()
		piece.texture = tex
		piece.centered = false
		piece.region_enabled = true
		piece.region_rect = Rect2(tl * art_tile, (br - tl) * art_tile)
		piece.scale = Vector2(art_scale, art_scale)
		# The node sits on the base line (what y-sort compares); the picture is lifted back into place.
		piece.position = Vector2((origin.x + tl.x) * TILE_PX, (origin.y + base) * TILE_PX)
		piece.offset = Vector2(0.0, (tl.y - base) * art_tile)
		piece.set_meta("rect", Rect2(Vector2(origin) * TILE_PX + tl * TILE_PX, (br - tl) * TILE_PX))
		if str(od.get("shape", "rect")) == "ellipse" and ResourceLoader.exists(OCCLUDER_SHADER):
			mat = ShaderMaterial.new()
			mat.shader = load(OCCLUDER_SHADER) as Shader
			var tsize: Vector2 = tex.get_size()
			mat.set_shader_parameter("region", Vector4(piece.region_rect.position.x / tsize.x, piece.region_rect.position.y / tsize.y, piece.region_rect.size.x / tsize.x, piece.region_rect.size.y / tsize.y))
			piece.material = mat
		occluders.add_child(piece)
	return Rect2i(origin, rsize)


func _floor_layer(cells: Array) -> TileMapLayer:
	var layer := TileMapLayer.new()
	layer.name = "Floor"
	layer.tile_set = _tileset()
	for y in cells.size():
		for x in cells[y].size():
			var atlas: Vector2i = FLOOR_ATLAS
			match cells[y][x]:
				"#":
					atlas = WALL_ATLAS
				"o":
					atlas = OBSTACLE_ATLAS
			layer.set_cell(Vector2i(x, y), 0, atlas)
	return layer


## Three invisible 32 px tiles: floor; wall with collision and a light occluder; obstacle with
## collision only (furniture stops a player but not the light of their vision).
static func _tileset() -> TileSet:
	var ts := TileSet.new()
	ts.tile_size = Vector2i(TILE_PX, TILE_PX)
	ts.add_physics_layer()
	ts.set_physics_layer_collision_layer(0, 1)
	ts.add_occlusion_layer()
	ts.set_occlusion_layer_light_mask(0, 1)
	var img: Image = Image.create(TILE_PX * 3, TILE_PX, false, Image.FORMAT_RGBA8)
	img.fill(Color(0.0, 0.0, 0.0, 0.0))
	var src := TileSetAtlasSource.new()
	src.texture = ImageTexture.create_from_image(img)
	src.texture_region_size = Vector2i(TILE_PX, TILE_PX)
	ts.add_source(src, 0)
	var h: float = TILE_PX / 2.0
	var square := PackedVector2Array([Vector2(-h, -h), Vector2(h, -h), Vector2(h, h), Vector2(-h, h)])
	for atlas in [FLOOR_ATLAS, WALL_ATLAS, OBSTACLE_ATLAS]:
		src.create_tile(atlas)
		if atlas == FLOOR_ATLAS:
			continue
		var data: TileData = src.get_tile_data(atlas, 0)
		data.add_collision_polygon(0)
		data.set_collision_polygon_points(0, 0, square)
		if atlas == WALL_ATLAS:
			var occ := OccluderPolygon2D.new()
			occ.polygon = square
			data.add_occluder_polygon(0)
			data.set_occluder_polygon(0, 0, occ)
	return ts


func _markers(list: Array) -> Node2D:
	var holder := Node2D.new()
	holder.name = "Markers"
	for e in list:
		var d: Dictionary = e
		var m := MapMarker.new()
		m.name = str(d.get("id", "marker"))
		m.marker_id = str(d.get("id", ""))
		m.kind = str(d.get("kind", "spawn"))
		m.range_tiles = float(d.get("range", 1.2))
		m.name_key = str(d.get("name_key", ""))
		if d.has("rect"):
			var r: Rect2i = _rect2i(d["rect"])
			m.position = Vector2(r.position * TILE_PX)
			m.zone_size_tiles = r.size
		else:
			var p: Array = d.get("pos", [0, 0])
			m.position = Vector2(float(p[0]), float(p[1])) * TILE_PX
		holder.add_child(m)
	return holder


static func _paint(cells: Array, origin: Vector2i, r: Rect2i, c: String) -> void:
	for y in range(r.position.y, r.end.y):
		for x in range(r.position.x, r.end.x):
			cells[origin.y + y][origin.x + x] = c


static func _has(doors: Dictionary, side: String, i: int) -> bool:
	for v in doors.get(side, []):
		if int(v) == i:
			return true
	return false


static func _vec2i(a: Variant) -> Vector2i:
	var arr: Array = a
	return Vector2i(int(arr[0]), int(arr[1]))


static func _rect2i(a: Variant) -> Rect2i:
	var arr: Array = a
	return Rect2i(int(arr[0]), int(arr[1]), int(arr[2]), int(arr[3]))
