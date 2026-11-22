extends SceneTree
## Exports a map scene to the server's JSON.
## godot --headless --path client --script res://tools/export_map.gd -- --map <id> [--out <path>] [--check] [--preview <png>] [--stats]
## Exit 0 ok, 1 --check mismatch, 2 validation failure or bad arguments.

const STATS_SPEED_TILES_PER_S: float = 3.0
const PREVIEW_PX: int = 8
const FLOOR_COLOR := Color(0.8, 0.8, 0.8)
const WALL_COLOR := Color(0.25, 0.25, 0.25)
const ZONE_COLOR := Color(1.0, 0.85, 0.2)
const TASK_COLOR := Color(0.2, 0.8, 0.3)
const SPECIAL_COLOR := Color(0.9, 0.2, 0.2)
const SPAWN_COLOR := Color(0.2, 0.4, 0.95)
const DARK_COLOR := Color(0.15, 0.1, 0.4)


func _initialize() -> void:
	quit(_run(OS.get_cmdline_user_args()))


func _fail(msg: String) -> int:
	printerr(msg)
	return 2


func _run(args: PackedStringArray) -> int:
	var map_id: String = ""
	var out_path: String = ""
	var preview: String = ""
	var check: bool = false
	var stats: bool = false
	var i: int = 0
	while i < args.size():
		var arg: String = args[i]
		if arg == "--check":
			check = true
		elif arg == "--stats":
			stats = true
		elif arg == "--map" or arg == "--out" or arg == "--preview":
			if i + 1 >= args.size():
				return _fail("%s needs a value" % arg)
			i += 1
			if arg == "--map":
				map_id = args[i]
			elif arg == "--out":
				out_path = args[i]
			else:
				preview = args[i]
		else:
			return _fail("unknown argument %s" % arg)
		i += 1
	if map_id.is_empty():
		return _fail("usage: --map <id> [--out <path>] [--check] [--preview <png>] [--stats]")
	if out_path.is_empty():
		out_path = ProjectSettings.globalize_path("res://").path_join("../content/maps/%s.json" % map_id).simplify_path()

	var scene_path: String = "res://world/maps/%s.tscn" % map_id
	var packed: PackedScene = load(scene_path) as PackedScene
	if packed == null:
		return _fail("cannot load %s" % scene_path)
	var root: MapRoot = packed.instantiate() as MapRoot
	if root == null:
		return _fail("%s root is not a MapRoot" % scene_path)
	if root.map_id != map_id:
		return _fail("map_id %s does not match --map %s" % [root.map_id, map_id])

	var errors: PackedStringArray = MapJson.root_errors(root)
	var d: Dictionary = MapJson.build(root)
	var dark: Array = MapJson.dark_of(root)
	var gas: Array = MapJson.gas_rooms_of(root)
	root.free()
	errors.append_array(MapJson.validate(d))
	errors.append_array(MapJson.dark_errors(d, dark))
	errors.append_array(MapJson.gas_errors(d, gas))
	if not errors.is_empty():
		for e in errors:
			printerr(e)
		return 2
	var version: String = MapJson.version_of(d)
	var text: String = MapJson.export_text(d)
	var meta_path: String = "res://world/maps/%s.meta.json" % map_id
	var meta_text: String = MapJson.meta_text(d, dark)

	if check:
		# Line endings are ignored: git may check the file out with CRLF.
		if not FileAccess.file_exists(out_path) or not MapJson.same_text(FileAccess.get_file_as_string(out_path), text):
			printerr("%s differs from a fresh export" % out_path)
			return 1
		if not FileAccess.file_exists(meta_path) or not MapJson.same_text(FileAccess.get_file_as_string(meta_path), meta_text):
			printerr("%s differs from a fresh export" % meta_path)
			return 1
		print("%s is up to date (%s)" % [out_path, version])
	else:
		DirAccess.make_dir_recursive_absolute(out_path.get_base_dir())
		var f: FileAccess = FileAccess.open(out_path, FileAccess.WRITE)
		if f == null:
			return _fail("cannot write %s" % out_path)
		f.store_string(text)
		f.close()
		var meta: FileAccess = FileAccess.open(meta_path, FileAccess.WRITE)
		if meta == null:
			return _fail("cannot write the meta file")
		meta.store_string(meta_text)
		meta.close()
		print("wrote %s (%s)" % [out_path, version])

	if not preview.is_empty():
		_preview(d, dark).save_png(preview)
	if stats:
		print(JSON.stringify(MapJson.path_stats(d, STATS_SPEED_TILES_PER_S), "  "))
	return 0


func _dot(img: Image, p: Array, color: Color) -> void:
	var c := Vector2i(int(float(p[0]) * PREVIEW_PX), int(float(p[1]) * PREVIEW_PX))
	img.fill_rect(Rect2i(c - Vector2i(2, 2), Vector2i(4, 4)), color)


func _preview(d: Dictionary, dark: Array) -> Image:
	var img: Image = Image.create(int(d["width"]) * PREVIEW_PX, int(d["height"]) * PREVIEW_PX, false, Image.FORMAT_RGBA8)
	var walk: Array = d["walk"]
	for y in walk.size():
		for x in String(walk[y]).length():
			var color: Color = WALL_COLOR if String(walk[y])[x] == "#" else FLOOR_COLOR
			img.fill_rect(Rect2i(x * PREVIEW_PX, y * PREVIEW_PX, PREVIEW_PX, PREVIEW_PX), color)
	for e in dark:
		var r: Array = e["rect"]
		img.fill_rect(Rect2i(int(float(r[0]) * PREVIEW_PX), int(float(r[1]) * PREVIEW_PX), int(float(r[2]) * PREVIEW_PX), int(float(r[3]) * PREVIEW_PX)), DARK_COLOR)
	for z in d["zones"]:
		var r: Array = z["rect"]
		var px := Rect2i(int(float(r[0]) * PREVIEW_PX), int(float(r[1]) * PREVIEW_PX), int(float(r[2]) * PREVIEW_PX), int(float(r[3]) * PREVIEW_PX))
		img.fill_rect(Rect2i(px.position, Vector2i(px.size.x, 1)), ZONE_COLOR)
		img.fill_rect(Rect2i(Vector2i(px.position.x, px.end.y - 1), Vector2i(px.size.x, 1)), ZONE_COLOR)
		img.fill_rect(Rect2i(px.position, Vector2i(1, px.size.y)), ZONE_COLOR)
		img.fill_rect(Rect2i(Vector2i(px.end.x - 1, px.position.y), Vector2i(1, px.size.y)), ZONE_COLOR)
	for c in d["consoles"]:
		_dot(img, c["pos"], TASK_COLOR)
	for c in d["special"]:
		_dot(img, c["pos"], SPECIAL_COLOR)
	for s in d["spawns"]:
		_dot(img, s, SPAWN_COLOR)
	return img
