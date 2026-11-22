extends "res://tests/test_case.gd"
## The TBS layout map (tbs_01): the scene builds from its layout JSON, exports exactly the server
## JSON in content/maps, keeps every task console, separates furniture from walls in the sight grid,
## and every floor cell a player can need is reachable from the spawn.

const MAP_ID: String = "tbs_01"


func _built() -> Dictionary:
	var root: MapRoot = load("res://world/maps/%s.tscn" % MAP_ID).instantiate() as MapRoot
	var d: Dictionary = MapJson.build(root)
	root.free()
	return d


func _repo_file(rel: String) -> String:
	return ProjectSettings.globalize_path("res://").path_join("../" + rel).simplify_path()


func test_server_json_is_a_fresh_export_of_the_layout() -> bool:
	var d: Dictionary = _built()
	assert_eq(MapJson.validate(d), PackedStringArray(), "the layout map validates")
	var path: String = _repo_file("content/maps/%s.json" % MAP_ID)
	assert_true(MapJson.same_text(FileAccess.get_file_as_string(path), MapJson.export_text(d)), "content/maps/%s.json differs: run tools/export_map.gd --map %s" % [MAP_ID, MAP_ID])
	var meta: Variant = JSON.parse_string(FileAccess.get_file_as_string("res://world/maps/%s.meta.json" % MAP_ID))
	assert_eq(str((meta as Dictionary).get("version", "")), MapJson.version_of(d), "meta.json version")
	return true


func test_every_task_console_of_tasks_json_is_on_the_map() -> bool:
	var d: Dictionary = _built()
	var ids: Dictionary = {}
	for c in d["consoles"]:
		ids[str(c["id"])] = true
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(_repo_file("content/tasks.json")))
	for t in (parsed as Dictionary).get("tasks", []):
		for console in t.get("consoles", []):
			assert_true(ids.has(str(console)), "console %s is on the map" % console)
	return true


## A floor cell on the map's edge (the north corridor) must not let the body walk off the map: a
## solid frame surrounds it, outside the floor layer (its used rect still starts at 0,0).
func test_a_solid_frame_surrounds_the_map() -> bool:
	var root: MapRoot = load("res://world/maps/%s.tscn" % MAP_ID).instantiate() as MapRoot
	var edge: StaticBody2D = root.get_node_or_null("Edge") as StaticBody2D
	assert_true(edge != null, "edge body")
	if edge != null:
		assert_eq(edge.collision_layer, 1, "walls' layer")
		assert_eq(edge.get_child_count(), 4, "four sides")
		var used: Rect2i = root.floor_layer().get_used_rect()
		assert_eq(used.position, Vector2i.ZERO, "floor layer unchanged")
		var map_px := Rect2(Vector2.ZERO, Vector2(used.size * MapRoot.TILE_PX))
		for c in edge.get_children():
			var cs: CollisionShape2D = c as CollisionShape2D
			var r := Rect2(cs.position - (cs.shape as RectangleShape2D).size * 0.5, (cs.shape as RectangleShape2D).size)
			assert_true(not r.intersects(map_px), "outside the map: " + str(r))
	root.free()
	return true


func test_furniture_blocks_walking_but_not_sight() -> bool:
	var d: Dictionary = _built()
	var walk: Array = d["walk"]
	var sight: Array = d["sight"]
	assert_eq(sight.size(), walk.size(), "one sight row per walk row")
	var furniture: int = 0
	for y in walk.size():
		for x in String(walk[y]).length():
			var w: String = String(walk[y])[x]
			var s: String = String(sight[y])[x]
			assert_true(not (s == "#" and w == "."), "a sight wall is never walkable (%d,%d)" % [x, y])
			if w == "#" and s == ".":
				furniture += 1
	assert_true(furniture > 0, "some cells are furniture: they block walking, not sight")
	return true


func test_spawns_reach_every_console_and_special() -> bool:
	var d: Dictionary = _built()
	var walk: Array = d["walk"]
	var start: Array = d["spawns"][0]
	var seen: Dictionary = {}
	var queue: Array[Vector2i] = [Vector2i(int(start[0]), int(start[1]))]
	seen[queue[0]] = true
	while not queue.is_empty():
		var c: Vector2i = queue.pop_front()
		for step in [Vector2i.LEFT, Vector2i.RIGHT, Vector2i.UP, Vector2i.DOWN]:
			var n: Vector2i = c + step
			if n.y < 0 or n.y >= walk.size() or n.x < 0 or n.x >= String(walk[n.y]).length():
				continue
			if seen.has(n) or String(walk[n.y])[n.x] != ".":
				continue
			seen[n] = true
			queue.append(n)
	for list in ["consoles", "special"]:
		for c in d[list]:
			var p: Array = c["pos"]
			assert_true(seen.has(Vector2i(int(p[0]), int(p[1]))), "%s is reachable from the spawn" % c["id"])
	return true


func test_occluders_are_built_from_the_layout() -> bool:
	var root: MapRoot = load("res://world/maps/%s.tscn" % MAP_ID).instantiate() as MapRoot
	var occ: Node2D = root.occluders()
	assert_true(occ != null and occ.y_sort_enabled, "a y-sorted occluder layer exists")
	assert_true(occ.get_child_count() > 0, "the layout has depth pieces")
	var piece: Sprite2D = occ.get_child(0) as Sprite2D
	var rect: Rect2 = piece.get_meta("rect")
	assert_true(piece.position.y > rect.position.y and piece.position.y <= rect.end.y, "a piece sorts on its base line, inside its picture")
	root.free()
	return true
