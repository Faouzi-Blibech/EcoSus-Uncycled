extends "res://tests/test_case.gd"

const MAP_PATH: String = "user://test_grid_path_map.json"


func _write(d: Dictionary) -> void:
	var f: FileAccess = FileAccess.open(MAP_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(d))
	f.close()


func _map(rows: Array, height: int = -1, width: int = 6) -> Dictionary:
	return {"id": "t", "version": "v1", "width": width, "height": rows.size() if height < 0 else height, "walk": rows, "consoles": [{"id": "c", "pos": [4.5, 1.5]}], "special": []}


func _loaded() -> GridPath:
	_write(_map(["######", "#....#", "#.##.#", "#....#", "######"]))
	var g := GridPath.new()
	assert_true(g.load_map(MAP_PATH), "loads")
	return g


func test_load_rejects_row_count_mismatch() -> bool:
	_write(_map(["######", "#....#", "######"], 5))
	assert_true(not GridPath.new().load_map(MAP_PATH), "height 5, 3 rows")
	return true


func test_load_rejects_wrong_row_width() -> bool:
	_write(_map(["######", "#...#", "######"]))
	assert_true(not GridPath.new().load_map(MAP_PATH), "short row")
	return true


func test_load_keeps_id_and_version() -> bool:
	var g: GridPath = _loaded()
	assert_eq(g.map_id(), "t", "id")
	assert_eq(g.map_version(), "v1", "version")
	return true


func test_path_ends_exactly_at_target() -> bool:
	var g: GridPath = _loaded()
	var p: Array[Vector2] = g.path(Vector2(1.5, 1.5), Vector2(4.2, 3.7))
	assert_true(not p.is_empty(), "route exists")
	assert_eq(p[p.size() - 1], Vector2(4.2, 3.7), "last waypoint is the target")
	assert_eq(p[0], Vector2(1.5, 1.5), "first waypoint is the start cell centre")
	for i in range(1, p.size() - 1):
		assert_near(p[i].distance_to(p[i - 1]), 1.0, 0.001, "waypoints one cell apart")
	return true


func test_path_empty_for_wall_target() -> bool:
	var g: GridPath = _loaded()
	assert_true(g.path(Vector2(1.5, 1.5), Vector2(2.5, 2.5)).is_empty(), "target in a wall")
	return true
