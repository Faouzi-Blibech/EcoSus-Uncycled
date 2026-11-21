class_name GridPath
extends RefCounted
## Walk grid of a map JSON and a 4-neighbour BFS path over it. Positions are in tiles.
## Plain RefCounted: usable from `--script` runs where no autoload exists.

var width: int = 0
var height: int = 0
var _walk: Array = []
var _json: Dictionary = {}


## Reads the map JSON. A relative path is looked up from the Godot project folder, then the cwd.
## Returns false and prints why when the file is missing or its shape is wrong.
func load_map(json_path: String) -> bool:
	var full: String = json_path
	if json_path.is_relative_path():
		full = ProjectSettings.globalize_path("res://").path_join(json_path).simplify_path()
		if not FileAccess.file_exists(full):
			full = json_path
	if not FileAccess.file_exists(full):
		printerr("GridPath: map file not found: %s" % full)
		return false
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(full))
	if typeof(parsed) != TYPE_DICTIONARY:
		printerr("GridPath: %s is not a JSON object" % full)
		return false
	var d: Dictionary = parsed
	var w: Variant = d.get("width")
	var h: Variant = d.get("height")
	if (typeof(w) != TYPE_FLOAT and typeof(w) != TYPE_INT) or (typeof(h) != TYPE_FLOAT and typeof(h) != TYPE_INT) or typeof(d.get("walk")) != TYPE_ARRAY:
		printerr("GridPath: %s needs numeric width and height and a walk array" % full)
		return false
	var rows: Array = d["walk"]
	if rows.size() != int(h):
		printerr("GridPath: %s has %d walk rows but height %d" % [full, rows.size(), int(h)])
		return false
	for y in rows.size():
		if typeof(rows[y]) != TYPE_STRING or String(rows[y]).length() != int(w):
			printerr("GridPath: %s walk row %d is not a string of width %d chars" % [full, y, int(w)])
			return false
	_json = d
	_walk = rows
	width = int(w)
	height = int(h)
	return true


func map_id() -> String:
	return String(_json.get("id", ""))


func map_version() -> String:
	return String(_json.get("version", ""))


## Position (tiles) of a console or special marker by id; null when the map has none.
func marker_pos(id: String) -> Variant:
	for list_name in ["consoles", "special"]:
		for c in _json.get(list_name, []):
			if c is Dictionary and c.get("id") == id:
				return Vector2(float(c["pos"][0]), float(c["pos"][1]))
	return null


func is_floor(cell: Vector2i) -> bool:
	if cell.y < 0 or cell.y >= _walk.size():
		return false
	var row: String = _walk[cell.y]
	return cell.x >= 0 and cell.x < row.length() and row[cell.x] != "#"


static func cell_of(p: Vector2) -> Vector2i:
	return Vector2i(floori(p.x), floori(p.y))


static func center_of(c: Vector2i) -> Vector2:
	return Vector2(c.x + 0.5, c.y + 0.5)


## Waypoints from `from_tiles` to `to_tiles`: the centre of every cell on a shortest 4-neighbour
## route, ending exactly on `to_tiles`. Empty when either end is a wall or there is no route.
## Consecutive waypoints are one cell apart on one axis, so a straight walk between them never
## cuts a wall corner.
func path(from_tiles: Vector2, to_tiles: Vector2) -> Array[Vector2]:
	var out: Array[Vector2] = []
	var start: Vector2i = cell_of(from_tiles)
	var goal: Vector2i = cell_of(to_tiles)
	if not is_floor(start) or not is_floor(goal):
		return out
	var came: Dictionary = {start: start}
	var queue: Array[Vector2i] = [start]
	var head: int = 0
	while head < queue.size() and not came.has(goal):
		var cur: Vector2i = queue[head]
		head += 1
		for step in [Vector2i.RIGHT, Vector2i.LEFT, Vector2i.DOWN, Vector2i.UP]:
			var n: Vector2i = cur + step
			if came.has(n) or not is_floor(n):
				continue
			came[n] = cur
			queue.append(n)
	if not came.has(goal):
		return out
	var cells: Array[Vector2i] = []
	var c: Vector2i = goal
	while c != start:
		cells.append(c)
		c = came[c]
	cells.append(start)
	cells.reverse()
	for cell in cells:
		out.append(center_of(cell))
	out[out.size() - 1] = to_tiles
	return out
