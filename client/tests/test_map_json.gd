extends "res://tests/test_case.gd"

const FIXTURE: String = "res://tests/fixtures/fixture_map.tscn"


func _built() -> Dictionary:
	var root: MapRoot = track(load(FIXTURE).instantiate())
	return MapJson.build(root)


func _ids(list: Array) -> Array:
	var out: Array = []
	for e in list:
		out.append(e["id"])
	out.sort()
	return out


func test_walk_rows_from_tiles() -> bool:
	var d: Dictionary = _built()
	assert_eq(d["width"], 6, "width")
	assert_eq(d["height"], 5, "height")
	assert_eq(d["walk"][0], "######", "row 0")
	assert_eq(d["walk"][2], "#....#", "row 2")
	return true


func test_marker_positions_in_tiles() -> bool:
	var d: Dictionary = _built()
	var c: Dictionary = d["consoles"][0]
	assert_eq(c["id"], "fx_bins", "id")
	assert_eq(c["kind"], "task", "kind")
	assert_near(c["pos"][0], 2.5, 0.001, "x")
	assert_near(c["pos"][1], 2.5, 0.001, "y")
	assert_near(c["range"], 1.2, 0.001, "range")
	return true


func test_specials_and_spawns() -> bool:
	var d: Dictionary = _built()
	assert_eq(_ids(d["special"]), ["fire_a", "fire_b", "meeting_button", "smog_panel"], "specials")
	assert_eq(d["spawns"].size(), 8, "spawn count")
	return true


func test_validate_ok_on_fixture() -> bool:
	var errors: PackedStringArray = MapJson.validate(_built())
	assert_eq(errors.size(), 0, "errors: %s" % str(errors))
	return true


func test_validate_catches_spawn_on_wall() -> bool:
	var d: Dictionary = _built()
	d["spawns"][0] = [0.5, 0.5]
	var errors: PackedStringArray = MapJson.validate(d)
	assert_true(errors.size() > 0, "has errors")
	var mentions: bool = false
	for e in errors:
		if e.contains("spawn"):
			mentions = true
	assert_true(mentions, "mentions spawn")
	return true


func test_validate_catches_missing_special() -> bool:
	var d: Dictionary = _built()
	var kept: Array = []
	for s in d["special"]:
		if s["id"] != "fire_b":
			kept.append(s)
	d["special"] = kept
	assert_true(MapJson.validate(d).size() > 0, "has errors")
	return true


func test_version_changes_with_walk() -> bool:
	var d: Dictionary = _built()
	var v: String = MapJson.version_of(d)
	assert_true(v.begins_with("sha1-") and v.length() == 17, "format " + v)
	assert_eq(MapJson.version_of(d.duplicate(true)), v, "stable")
	var e: Dictionary = d.duplicate(true)
	e["walk"][2] = "#.#..#"
	assert_true(MapJson.version_of(e) != v, "changes")
	return true


func test_path_stats_on_fixture() -> bool:
	var stats: Dictionary = MapJson.path_stats(_built(), 2.5)
	var t: float = stats["spawn_to_console_s"]["fx_bins"]
	assert_true(t >= 0.0 and is_finite(t), "finite %s" % str(t))
	return true


func _first_zone(d: Dictionary) -> Dictionary:
	return d["zones"][0]


func _has_error(errors: PackedStringArray, word: String) -> bool:
	for e in errors:
		if e.contains(word):
			return true
	return false


func test_validate_catches_zero_size_zone() -> bool:
	var d: Dictionary = _built()
	_first_zone(d)["rect"] = [1, 1, 0, 3]
	assert_true(_has_error(MapJson.validate(d), "zone"), "zone error")
	return true


func test_validate_catches_negative_width_zone() -> bool:
	var d: Dictionary = _built()
	_first_zone(d)["rect"] = [5, 1, -4, 3]
	assert_true(_has_error(MapJson.validate(d), "zone"), "zone error")
	return true


func test_validate_catches_row_count_not_height() -> bool:
	var d: Dictionary = _built()
	d["walk"].pop_back()
	assert_true(_has_error(MapJson.validate(d), "rows"), "rows error")
	return true


func test_validate_catches_wrong_kind_for_required_special() -> bool:
	var d: Dictionary = _built()
	for s in d["special"]:
		if s["id"] == "meeting_button":
			s["kind"] = "fire_fix"
	assert_true(_has_error(MapJson.validate(d), "meeting_button"), "kind error")
	return true


func test_root_errors_catch_tiles_outside_origin() -> bool:
	var root: MapRoot = track(load(FIXTURE).instantiate())
	assert_eq(MapJson.root_errors(root).size(), 0, "fixture ok")
	root.floor_layer().set_cell(Vector2i(-1, 0), 0, Vector2i(1, 0))
	assert_true(MapJson.root_errors(root).size() > 0, "origin error")
	return true


func test_same_text_ignores_crlf() -> bool:
	var lf: String = "{\n\t\"id\": \"a\"\n}\n"
	var crlf: String = lf.replace("\n", "\r\n")
	assert_true(crlf.contains("\r"), "crlf text really has CR")
	assert_true(MapJson.same_text(lf, crlf), "crlf equals lf")
	assert_true(not MapJson.same_text(lf, crlf.replace("a", "b")), "changed char differs")
	return true


func _repo_file(rel: String) -> String:
	return ProjectSettings.globalize_path("res://").path_join("../" + rel).simplify_path()


func _json_file(path: String) -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if typeof(parsed) == TYPE_DICTIONARY else {}


func _greybox() -> Dictionary:
	var root: MapRoot = load("res://world/maps/greybox_01.tscn").instantiate() as MapRoot
	var d: Dictionary = MapJson.build(root)
	root.free()
	return d


## The server and the probes read content/maps/greybox_01.json, the client reads the scene: a map
## edited without re-running the export must fail here, not on a phone.
func test_greybox_json_is_a_fresh_export_of_the_scene() -> bool:
	var d: Dictionary = _greybox()
	var path: String = _repo_file("content/maps/greybox_01.json")
	assert_true(FileAccess.file_exists(path), "exported json exists")
	assert_true(MapJson.same_text(FileAccess.get_file_as_string(path), MapJson.export_text(d)), "content/maps/greybox_01.json differs from the scene: run tools/export_map.gd --map greybox_01")
	var meta: Dictionary = _json_file("res://world/maps/greybox_01.meta.json")
	assert_eq(str(meta.get("version", "")), MapJson.version_of(d), "meta.json version")
	return true


func test_greybox_has_every_task_console_of_tasks_json() -> bool:
	var d: Dictionary = _greybox()
	var kinds: Dictionary = {}
	for c in d["consoles"]:
		kinds[str(c["id"])] = "task"
	var tasks: Array = _json_file(_repo_file("content/tasks.json")).get("tasks", [])
	assert_true(not tasks.is_empty(), "tasks.json has tasks")
	for t in tasks:
		for console in t.get("consoles", []):
			assert_eq(kinds.get(str(console), "missing"), "task", "console %s of %s is a task console in the scene" % [console, t.get("type", "")])
	return true


## A TileMapLayer with an empty cell, a floor tile (no collision), and two wall tiles with a
## collision polygon at different atlas coordinates.
func _layer_with_two_wall_tiles() -> TileMapLayer:
	var ts: TileSet = TileSet.new()
	ts.tile_size = Vector2i(32, 32)
	ts.add_physics_layer()
	var src: TileSetAtlasSource = TileSetAtlasSource.new()
	src.texture = ImageTexture.create_from_image(Image.create(96, 32, false, Image.FORMAT_RGBA8))
	src.texture_region_size = Vector2i(32, 32)
	for x in 3:
		src.create_tile(Vector2i(x, 0))
	ts.add_source(src, 0)
	for x in [1, 2]:
		var td: TileData = src.get_tile_data(Vector2i(x, 0), 0)
		td.add_collision_polygon(0)
		td.set_collision_polygon_points(0, 0, PackedVector2Array([Vector2(-16, -16), Vector2(16, -16), Vector2(16, 16), Vector2(-16, 16)]))
	var layer: TileMapLayer = track(TileMapLayer.new()) as TileMapLayer
	layer.tile_set = ts
	layer.set_cell(Vector2i(0, 0), 0, Vector2i(0, 0))
	layer.set_cell(Vector2i(1, 0), 0, Vector2i(1, 0))
	layer.set_cell(Vector2i(2, 0), 0, Vector2i(2, 0))
	return layer


func test_wall_is_a_tile_with_collision_not_one_atlas_coordinate() -> bool:
	var layer: TileMapLayer = _layer_with_two_wall_tiles()
	assert_true(not MapJson.is_wall(layer, Vector2i(0, 0)), "floor tile without collision")
	assert_true(MapJson.is_wall(layer, Vector2i(1, 0)), "wall tile")
	assert_true(MapJson.is_wall(layer, Vector2i(2, 0)), "a second wall tile with collision is a wall too")
	assert_true(MapJson.is_wall(layer, Vector2i(5, 0)), "empty cell blocks the player")
	return true


## A fixture map plus one dark marker at tile (1, 2), 4x1 tiles (no spawn inside) (kind "dark" is a rect like a zone).
func _fixture_with_dark(pos_px: Vector2 = Vector2(32, 64), size_tiles: Vector2i = Vector2i(4, 1)) -> MapRoot:
	var root: MapRoot = track(load(FIXTURE).instantiate())
	var m: MapMarker = MapMarker.new()
	m.marker_id = "dk_test"
	m.kind = "dark"
	m.position = pos_px
	m.zone_size_tiles = size_tiles
	root.get_node("Markers").add_child(m)
	return root


func test_dark_rect_exported_to_server_json_and_meta() -> bool:
	var plain: MapRoot = track(load(FIXTURE).instantiate())
	var with_dark: MapRoot = _fixture_with_dark()
	var d: Dictionary = MapJson.build(with_dark)
	var dark: Array = MapJson.dark_of(with_dark)
	assert_eq(dark.size(), 1, "one dark rect")
	assert_eq(dark[0]["id"], "dk_test", "id")
	assert_eq(dark[0]["rect"], [1.0, 2.0, 4, 1], "rect in tiles")
	assert_eq(d.get("dark"), dark, "the server dictionary carries the dark rects")
	assert_true(MapJson.export_text(d).contains("dk_test"), "the server json names the dark rect")
	assert_true(MapJson.version_of(d) != MapJson.version_of(MapJson.build(plain)), "the map version covers the dark rects")
	assert_eq(MapJson.build(plain).get("dark"), [], "a map without dark markers writes an empty list")
	var meta: Variant = JSON.parse_string(MapJson.meta_text(d, dark))
	assert_true(meta is Dictionary, "meta parses")
	assert_eq(str(meta["version"]), MapJson.version_of(d), "meta version is the server json's")
	assert_eq(str(meta["dark"][0]["id"]), "dk_test", "meta lists the dark rect")
	assert_eq(with_dark.dark_rects(), [Rect2(1, 2, 4, 1)] as Array[Rect2], "MapRoot.dark_rects in tiles")
	return true


func test_dark_rect_off_map_fails_validate() -> bool:
	var root: MapRoot = _fixture_with_dark()
	var d: Dictionary = MapJson.build(root)
	assert_eq(MapJson.dark_errors(d, MapJson.dark_of(root)).size(), 0, "fixture dark rect is valid")
	var off: Array = [{"id": "dk_off", "rect": [4, 3, 5, 5]}]
	assert_true(_has_error(MapJson.dark_errors(d, off), "dk_off"), "rect past the map edge")
	var negative: Array = [{"id": "dk_neg", "rect": [-1, 1, 2, 2]}]
	assert_true(_has_error(MapJson.dark_errors(d, negative), "dk_neg"), "rect before the map origin")
	var empty: Array = [{"id": "dk_zero", "rect": [1, 1, 0, 2]}]
	assert_true(_has_error(MapJson.dark_errors(d, empty), "dk_zero"), "rect without area")
	var on_walls: Array = [{"id": "dk_wall", "rect": [0, 0, 6, 1]}]
	assert_true(_has_error(MapJson.dark_errors(d, on_walls), "dk_wall"), "rect with no walkable cell")
	return true


func test_dark_rect_over_a_spawn_fails_validate() -> bool:
	var root: MapRoot = _fixture_with_dark(Vector2(32, 32), Vector2i(2, 2))
	var d: Dictionary = MapJson.build(root)
	var over_spawn: Array = [{"id": "dk_spawn", "rect": [1, 1, 2, 2]}]
	assert_true(_has_error(MapJson.dark_errors(d, over_spawn), "dk_spawn"), "spawn at (1.5, 1.5) is inside the rect")
	return true


func test_greybox_server_json_carries_the_dark_rooms() -> bool:
	var d: Dictionary = _greybox()
	assert_eq((d.get("dark", []) as Array).size(), 2, "two dark rooms in the server dictionary")
	var text: String = FileAccess.get_file_as_string(_repo_file("content/maps/greybox_01.json"))
	assert_true(text.contains("dark_se") and text.contains("dark_sw"), "content/maps/greybox_01.json lists the dark rooms")
	assert_true(text.contains(MapJson.version_of(d)), "content json carries the version the client checks")
	return true


func test_greybox_has_two_dark_rooms_in_meta_matching_the_scene() -> bool:
	var root: MapRoot = load("res://world/maps/greybox_01.tscn").instantiate() as MapRoot
	var d: Dictionary = MapJson.build(root)
	var dark: Array = MapJson.dark_of(root)
	assert_eq(MapJson.dark_errors(d, dark).size(), 0, "dark rects valid")
	root.free()
	assert_eq(dark.size(), 2, "two dark rooms")
	var meta_text: String = FileAccess.get_file_as_string("res://world/maps/greybox_01.meta.json")
	assert_true(MapJson.same_text(meta_text, MapJson.meta_text(d, dark)), "meta.json is a fresh export: run tools/export_map.gd --map greybox_01")
	return true


func test_overlapping_dark_rects_fail_validate() -> bool:
	var root: MapRoot = _fixture_with_dark()
	var d: Dictionary = MapJson.build(root)
	var overlap: Array = [{"id": "dk_a", "rect": [1, 2, 3, 1]}, {"id": "dk_b", "rect": [3, 2, 2, 1]}]
	var errors: PackedStringArray = MapJson.dark_errors(d, overlap)
	assert_true(_has_error(errors, "dk_a") and _has_error(errors, "overlap"), "partly overlapping rects: %s" % str(errors))
	var same: Array = [{"id": "dk_a", "rect": [1, 2, 2, 1]}, {"id": "dk_b", "rect": [1, 2, 2, 1]}]
	assert_true(_has_error(MapJson.dark_errors(d, same), "overlap"), "identical rects")
	var inside: Array = [{"id": "dk_a", "rect": [1, 2, 4, 1]}, {"id": "dk_b", "rect": [2, 2, 1, 1]}]
	assert_true(_has_error(MapJson.dark_errors(d, inside), "overlap"), "one rect inside the other")
	return true


func test_touching_dark_rects_are_valid() -> bool:
	var root: MapRoot = _fixture_with_dark()
	var d: Dictionary = MapJson.build(root)
	var touching: Array = [{"id": "dk_a", "rect": [1, 2, 2, 1]}, {"id": "dk_b", "rect": [3, 2, 2, 1]}]
	assert_eq(MapJson.dark_errors(d, touching).size(), 0, "rects that share an edge only are two rooms side by side")
	return true


func test_duplicate_dark_id_fails_validate() -> bool:
	var root: MapRoot = _fixture_with_dark()
	var d: Dictionary = MapJson.build(root)
	var twice: Array = [{"id": "dk_a", "rect": [1, 2, 1, 1]}, {"id": "dk_a", "rect": [3, 2, 1, 1]}]
	assert_true(_has_error(MapJson.dark_errors(d, twice), "duplicate"), "same id twice")
	return true
