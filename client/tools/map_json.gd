class_name MapJson
extends RefCounted
## Map scene -> the JSON the server's ContentLoader reads. Pure functions, no autoloads.

const TILE_PX: int = 32
const SPAWN_COUNT: int = 8
const REQUIRED_SPECIALS: Dictionary = {
	"meeting_button": "emergency",
	"fire_a": "fire_fix",
	"fire_b": "fire_fix",
	"smog_panel": "smog_fix",
}
const SPECIAL_KINDS: PackedStringArray = ["emergency", "fire_fix", "smog_fix"]


static func _tiles(v: float) -> float:
	return snappedf(v / TILE_PX, 0.01)


static func _by_id(a: MapMarker, b: MapMarker) -> bool:
	return a.marker_id < b.marker_id


static func _entry(m: MapMarker) -> Dictionary:
	return {
		"id": m.marker_id,
		"kind": m.kind,
		"pos": [_tiles(m.position.x), _tiles(m.position.y)],
		"range": m.range_tiles,
	}


## A cell blocks movement when it is empty or its tile has a collision polygon on physics layer 0
## (the same rule the client's physics applies), whatever its atlas coordinate.
static func is_wall(layer: TileMapLayer, cell: Vector2i) -> bool:
	if layer.get_cell_source_id(cell) == -1:
		return true
	var data: TileData = layer.get_cell_tile_data(cell)
	return data != null and data.get_collision_polygons_count(0) > 0


## Map dictionary without `version`. The used tile rect must start at (0, 0) (see root_errors).
static func build(root: MapRoot) -> Dictionary:
	var layer: TileMapLayer = root.floor_layer()
	var rect: Rect2i = layer.get_used_rect()
	var walk: Array[String] = []
	for y in rect.end.y:
		var row: String = ""
		for x in rect.end.x:
			var cell := Vector2i(x, y)
			var is_wall: bool = is_wall(layer, cell)
			row += "#" if is_wall else "."
		walk.append(row)

	var ms: Array[MapMarker] = root.markers()
	ms.sort_custom(_by_id)
	var zones: Array = []
	var consoles: Array = []
	var special: Array = []
	var spawns: Array = []
	for m in ms:
		match m.kind:
			"zone":
				zones.append({
					"id": m.marker_id,
					"name_key": m.name_key,
					"rect": [_tiles(m.position.x), _tiles(m.position.y), m.zone_size_tiles.x, m.zone_size_tiles.y],
				})
			"task":
				consoles.append(_entry(m))
			"spawn":
				spawns.append([_tiles(m.position.x), _tiles(m.position.y)])
			"dark":
				pass  # written from dark_of (sorted by id) below
			_:
				special.append(_entry(m))
	return {
		"id": root.map_id,
		"tile_px": TILE_PX,
		"width": rect.end.x,
		"height": rect.end.y,
		"walk": walk,
		"zones": zones,
		"dark": dark_of(root),
		"consoles": consoles,
		"special": special,
		"spawns": spawns,
	}


## The dark rooms of a map scene as [{"id", "rect": [x, y, w, h]}] in tiles, sorted by id. They go to
## the server JSON (build) and the client's .meta.json (meta_text); the server reads them to cut a citizen's vision.
static func dark_of(root: MapRoot) -> Array:
	var ms: Array[MapMarker] = root.markers()
	ms.sort_custom(_by_id)
	var out: Array = []
	for m in ms:
		if m.kind == "dark":
			out.append({"id": m.marker_id, "rect": [_tiles(m.position.x), _tiles(m.position.y), m.zone_size_tiles.x, m.zone_size_tiles.y]})
	return out


## One message per problem with the dark rooms of `dark_of`, checked against the map dictionary `d`:
## a rect needs an id, a size of at least 1x1, to lie inside the map, to cover a walkable cell, and
## to hold no spawn.
static func dark_errors(d: Dictionary, dark: Array) -> PackedStringArray:
	var errors := PackedStringArray()
	var walk: Array = d.get("walk", [])
	var width: int = int(d.get("width", 0))
	var height: int = int(d.get("height", 0))
	var seen: Dictionary = {}
	var placed: Dictionary = {}  # id -> Rect2 of the valid rects so far
	for e in dark:
		var id: String = String(e.get("id", "")) if e is Dictionary else ""
		if id.is_empty():
			errors.append("dark rect without an id")
			continue
		if seen.has(id):
			errors.append("duplicate dark id %s" % id)
		seen[id] = true
		var r: Variant = e.get("rect")
		if not (r is Array and r.size() == 4 and _is_num(r[0]) and _is_num(r[1]) and _is_num(r[2]) and _is_num(r[3])):
			errors.append("dark %s rect must be [x, y, w, h]" % id)
			continue
		var rect := Rect2(float(r[0]), float(r[1]), float(r[2]), float(r[3]))
		if rect.size.x < 1.0 or rect.size.y < 1.0:
			errors.append("dark %s rect %s must be at least 1x1 tiles" % [id, str(r)])
		elif rect.position.x < 0.0 or rect.position.y < 0.0 or rect.end.x > width or rect.end.y > height:
			errors.append("dark %s rect %s is outside the map" % [id, str(r)])
		else:
			var walkable: bool = false
			for y in range(int(rect.position.y), int(rect.end.y)):
				for x in range(int(rect.position.x), int(rect.end.x)):
					if not _is_wall(walk, Vector2i(x, y)):
						walkable = true
			if not walkable:
				errors.append("dark %s rect %s covers no walkable cell" % [id, str(r)])
			for s in d.get("spawns", []):
				if rect.has_point(Vector2(float(s[0]), float(s[1]))):
					errors.append("dark %s rect %s contains the spawn %s" % [id, str(r), str(s)])
			# Two lights would take the ambient away twice there: black even inside the player's light.
			# Rects that only share an edge are fine.
			for other in placed:
				if rect.intersects(placed[other], false):
					errors.append("dark %s overlaps dark %s" % [id, other])
			placed[id] = rect
	return errors


## Text of the client's <id>.meta.json: the map version (what Round checks against the server) and the
## dark rooms. Never read by the server (it reads the same dark rects from the map JSON).
static func meta_text(d: Dictionary, dark: Array) -> String:
	return JSON.stringify({"id": d["id"], "version": version_of(d), "dark": dark}, "\t") + "\n"


static func _cell(p: Array) -> Vector2i:
	return Vector2i(floori(float(p[0])), floori(float(p[1])))


static func _is_wall(walk: Array, c: Vector2i) -> bool:
	if c.y < 0 or c.y >= walk.size() or c.x < 0 or c.x >= String(walk[c.y]).length():
		return true
	return String(walk[c.y])[c.x] == "#"


## Problems with the map scene itself (not visible in the dictionary).
static func root_errors(root: MapRoot) -> PackedStringArray:
	var errors := PackedStringArray()
	var rect: Rect2i = root.floor_layer().get_used_rect()
	if rect.position != Vector2i.ZERO:
		errors.append("used tile rect starts at %s, it must start at (0, 0)" % str(rect.position))
	return errors


static func _is_num(v: Variant) -> bool:
	return typeof(v) == TYPE_INT or typeof(v) == TYPE_FLOAT


static func _is_pair(v: Variant) -> bool:
	return typeof(v) == TYPE_ARRAY and v.size() == 2 and _is_num(v[0]) and _is_num(v[1])


static func _is_whole(v: Variant) -> bool:
	return _is_num(v) and float(v) == floorf(float(v))


## One message per problem; empty means valid. Mirrors what the server's ContentLoader.ReadMap
## consumes and throws on, plus the map rules (8 spawns, required specials, floor positions).
static func validate(d: Dictionary) -> PackedStringArray:
	var errors := PackedStringArray()
	if not (_is_whole(d.get("width")) and _is_whole(d.get("height"))):
		errors.append("width and height must be whole numbers")
		return errors
	var width: int = int(d["width"])
	var height: int = int(d["height"])
	if width < 1 or height < 1:
		errors.append("map size must be at least 1x1, got %dx%d" % [width, height])
	var walk: Array = d["walk"] if d.get("walk") is Array else []
	if walk.size() != height:
		errors.append("walk must have %d rows, got %d" % [height, walk.size()])
	for i in walk.size():
		if typeof(walk[i]) != TYPE_STRING or String(walk[i]).length() != width:
			errors.append("walk row %d must be a string of %d chars" % [i, width])
	var spawns: Array = d["spawns"] if d.get("spawns") is Array else []
	if spawns.size() != SPAWN_COUNT:
		errors.append("expected %d spawns, got %d" % [SPAWN_COUNT, spawns.size()])
	for i in spawns.size():
		if not _is_pair(spawns[i]):
			errors.append("spawn %d must be [x, y]" % i)
		elif _is_wall(walk, _cell(spawns[i])):
			errors.append("spawn %d at %s is on a wall or outside the map" % [i, str(spawns[i])])

	var seen: Dictionary = {}
	var special_kinds: Dictionary = {}
	for list_name in ["consoles", "special"]:
		var allowed: PackedStringArray = ["task"] if list_name == "consoles" else SPECIAL_KINDS
		for c in (d[list_name] if d.get(list_name) is Array else []):
			var id: String = String(c.get("id", "")) if c is Dictionary else ""
			if id.is_empty():
				errors.append("%s entry without an id" % list_name)
				continue
			if seen.has(id):
				errors.append("duplicate id %s" % id)
			seen[id] = true
			if not allowed.has(String(c.get("kind", ""))):
				errors.append("%s has kind %s, allowed in %s: %s" % [id, str(c.get("kind")), list_name, ", ".join(allowed)])
			if list_name == "special":
				special_kinds[id] = c.get("kind")
			if not _is_num(c.get("range")) or float(c["range"]) <= 0.0:
				errors.append("%s has range <= 0 or missing" % id)
			if not _is_pair(c.get("pos")):
				errors.append("%s pos must be [x, y]" % id)
			elif _is_wall(walk, _cell(c["pos"])):
				errors.append("%s at %s is on a wall or outside the map" % [id, str(c["pos"])])
	for id in REQUIRED_SPECIALS:
		if not special_kinds.has(id):
			errors.append("missing special marker %s" % id)
		elif special_kinds[id] != REQUIRED_SPECIALS[id]:
			errors.append("%s must have kind %s, got %s" % [id, REQUIRED_SPECIALS[id], str(special_kinds[id])])
	for z in (d["zones"] if d.get("zones") is Array else []):
		var id: String = String(z.get("id", "")) if z is Dictionary else ""
		if id.is_empty():
			errors.append("zone without an id")
			continue
		if seen.has(id):
			errors.append("duplicate id %s" % id)
		seen[id] = true
		var r: Variant = z.get("rect")
		if not (r is Array and r.size() == 4 and _is_num(r[0]) and _is_num(r[1]) and _is_num(r[2]) and _is_num(r[3])):
			errors.append("zone %s rect must be [x, y, w, h]" % id)
		elif float(r[2]) < 1.0 or float(r[3]) < 1.0:
			errors.append("zone %s rect %s must be at least 1x1 tiles" % [id, str(r)])
		elif float(r[0]) < 0.0 or float(r[1]) < 0.0 or float(r[0]) + float(r[2]) > width or float(r[1]) + float(r[3]) > height:
			errors.append("zone %s rect %s is outside the map" % [id, str(r)])
	return errors


## Text equality that ignores line endings (git may check a file out with CRLF).
static func same_text(a: String, b: String) -> bool:
	return a.replace("\r\n", "\n").replace("\r", "\n") == b.replace("\r\n", "\n").replace("\r", "\n")


## "sha1-" + first 12 hex of the sha1 of the sorted-key JSON with `version` removed.
static func version_of(d: Dictionary) -> String:
	var c: Dictionary = d.duplicate(true)
	c.erase("version")
	return "sha1-" + JSON.stringify(c, "", true).sha1_text().substr(0, 12)


## The exact text of the map JSON the server reads: `id` and `version` first, then the rest.
static func export_text(d: Dictionary) -> String:
	var out: Dictionary = {"id": d["id"], "version": version_of(d)}
	for k in d:
		if k != "id":
			out[k] = d[k]
	return JSON.stringify(out, "\t", false) + "\n"


## 4-neighbour BFS step counts over `walk` from a cell; empty when the start is a wall.
static func _bfs(walk: Array, start: Vector2i) -> Dictionary:
	var dist: Dictionary = {}
	if _is_wall(walk, start):
		return dist
	dist[start] = 0
	var queue: Array[Vector2i] = [start]
	var head: int = 0
	while head < queue.size():
		var cur: Vector2i = queue[head]
		head += 1
		for step in [Vector2i.LEFT, Vector2i.RIGHT, Vector2i.UP, Vector2i.DOWN]:
			var n: Vector2i = cur + step
			if dist.has(n) or _is_wall(walk, n):
				continue
			dist[n] = dist[cur] + 1
			queue.append(n)
	return dist


## {"spawn_to_console_s": {id: seconds}, "max_console_to_console_s": seconds}. Unreachable = -1.
static func path_stats(d: Dictionary, speed_tiles_per_s: float) -> Dictionary:
	var walk: Array = d["walk"]
	var centroid := Vector2.ZERO
	var spawns: Array = d.get("spawns", [])
	for s in spawns:
		centroid += Vector2(float(s[0]), float(s[1]))
	if not spawns.is_empty():
		centroid /= spawns.size()
	var from_spawn: Dictionary = _bfs(walk, _cell([centroid.x, centroid.y]))
	var to_console: Dictionary = {}
	var longest: float = 0.0
	var consoles: Array = d.get("consoles", [])
	for c in consoles:
		var cell: Vector2i = _cell(c["pos"])
		to_console[c["id"]] = from_spawn[cell] / speed_tiles_per_s if from_spawn.has(cell) else -1.0
		var from_c: Dictionary = _bfs(walk, cell)
		for o in consoles:
			var other: Vector2i = _cell(o["pos"])
			if from_c.has(other):
				longest = maxf(longest, from_c[other] / speed_tiles_per_s)
	return {"spawn_to_console_s": to_console, "max_console_to_console_s": longest}
