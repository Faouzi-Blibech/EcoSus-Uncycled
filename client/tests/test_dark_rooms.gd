extends "res://tests/test_case.gd"


func _lights(rooms: DarkRooms) -> Array[PointLight2D]:
	var out: Array[PointLight2D] = []
	for c in rooms.get_children():
		if c is PointLight2D:
			out.append(c)
	return out


func test_one_subtracting_light_covers_each_rect() -> bool:
	var ambient: Color = Color(0.4, 0.45, 0.5)
	var rects: Array[Rect2] = [Rect2(1, 17, 8, 6), Rect2(33, 17, 8, 6)]
	var rooms: DarkRooms = track(DarkRooms.create(rects, ambient)) as DarkRooms
	var lights: Array[PointLight2D] = _lights(rooms)
	assert_eq(lights.size(), 2, "one light per dark rect")
	for i in lights.size():
		var l: PointLight2D = lights[i]
		var px: Rect2 = Rect2(rects[i].position * MapRoot.TILE_PX, rects[i].size * MapRoot.TILE_PX)
		assert_eq(l.blend_mode, Light2D.BLEND_MODE_SUB, "light %d removes the ambient light" % i)
		assert_eq(l.color, ambient, "light %d removes exactly the ambient level" % i)
		assert_true(not l.shadow_enabled, "light %d casts no shadow" % i)
		assert_true(l.position.is_equal_approx(px.get_center()), "light %d is centred on its room" % i)
		var covered: Vector2 = Vector2(l.texture.get_size()) * l.texture_scale * l.scale
		assert_true(covered.is_equal_approx(px.size), "light %d covers exactly the room %s, got %s" % [i, str(px.size), str(covered)])
	return true


func test_no_rects_no_lights() -> bool:
	var rooms: DarkRooms = track(DarkRooms.create([] as Array[Rect2], Color.GRAY)) as DarkRooms
	assert_eq(_lights(rooms).size(), 0, "no lights")
	return true


const Round: GDScript = preload("res://scenes/round/round.gd")
const VisionScene: PackedScene = preload("res://world/vision/vision.tscn")


func _prepared_session() -> Dictionary:
	var saved: Dictionary = {}
	for f in ["map_id", "map_version", "seat_id", "role", "phase", "has_me_pos", "me_pos", "spawn_pending", "is_out"]:
		saved[f] = Session.get(f)
	var meta: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://world/maps/greybox_01.meta.json"))
	Session.map_id = "greybox_01"
	Session.map_version = str(meta.get("version", ""))
	Session.seat_id = 0
	Session.role = "citizen"
	Session.phase = "playing"
	Session.has_me_pos = true
	Session.me_pos = Vector2(96, 96)
	Session.spawn_pending = false
	Session.is_out = false
	return saved


func _restore_session(saved: Dictionary) -> void:
	for f in saved:
		Session.set(f, saved[f])


## Round._ready adds the map's dark rooms under its Vision, with exactly the Vision's ambient.
func test_round_adds_dark_rooms_under_the_vision() -> bool:
	var saved: Dictionary = _prepared_session()
	var r: RoundScene = track(load("res://scenes/round/round.tscn").instantiate()) as RoundScene
	r._ready()
	_restore_session(saved)
	assert_true(r._vision != null, "round built its vision")
	var rooms: DarkRooms = null
	for c in r._vision.get_children():
		if c is DarkRooms:
			rooms = c
	assert_true(rooms != null, "a DarkRooms node sits under the vision, so it hides with it before the first position")
	if rooms == null:
		return true
	var lights: Array[PointLight2D] = _lights(rooms)
	assert_eq(lights.size(), r._map.dark_rects().size(), "one light per dark rect of the map")
	assert_true(lights.size() > 0, "greybox_01 has dark rooms")
	var modulate: CanvasModulate = r._vision.get_node("Dark") as CanvasModulate
	assert_eq(r._vision.ambient(), modulate.color, "Vision.ambient is the CanvasModulate colour")
	for l in lights:
		assert_eq(l.color, modulate.color, "each light removes exactly the ambient level")
	return true


## The rooms keep the map's coordinates although their parent (the Vision) follows the player.
func test_dark_rooms_do_not_move_with_the_player() -> bool:
	var rooms: DarkRooms = track(DarkRooms.create([Rect2(1, 1, 2, 2)] as Array[Rect2], Color.GRAY)) as DarkRooms
	assert_true(rooms.top_level, "top level: its lights stay at the map position")
	return true


## The round's light radius: the server's value, corrected for the dark room the local player stands in
## when the server's last position (Session.me_pos) disagrees.
func test_round_light_shrinks_while_the_local_player_stands_in_a_dark_room() -> bool:
	var saved: Dictionary = _prepared_session()
	var tuning: Dictionary = Session.tuning
	var vision: float = Session.vision_tiles
	Session.tuning = {"vision": {"citizen": 4.0, "smog_min": 1.0, "dark_mult": 0.5}}
	var r: RoundScene = track(load("res://scenes/round/round.tscn").instantiate()) as RoundScene
	r._ready()
	Session.vision_tiles = 4.0
	Session.sabotage = {}
	Session.me_pos = Vector2(96, 96)  # the server still has us outside
	r._player.global_position = Vector2(34.5, 18.5) * MapRoot.TILE_PX  # inside dark_se
	var inside: float = r._light_tiles()
	r._player.global_position = Vector2(20.5, 10.5) * MapRoot.TILE_PX
	var outside: float = r._light_tiles()
	Session.role = "polluter"
	r._player.global_position = Vector2(34.5, 18.5) * MapRoot.TILE_PX
	var polluter: float = r._light_tiles()
	Session.tuning = tuning
	Session.vision_tiles = vision
	_restore_session(saved)
	assert_eq(inside, 2.0, "half the server radius inside the dark room")
	assert_eq(outside, 4.0, "the server radius outside")
	assert_eq(polluter, 4.0, "a polluter keeps the server radius")
	return true


## The same half-open rect as the server (MapData.IsDark): the near edge is dark, the far edge is not.
func test_map_dark_rect_is_half_open_like_the_server() -> bool:
	var root: MapRoot = track(load("res://world/maps/greybox_01.tscn").instantiate()) as MapRoot
	var r: Rect2 = root.dark_rects()[0]
	assert_true(root.is_dark(r.position), "the top-left corner is inside")
	assert_true(root.is_dark(r.end - Vector2(0.01, 0.01)), "just inside the far corner")
	assert_true(not root.is_dark(Vector2(r.end.x, r.position.y + 1.0)), "the right edge is outside")
	assert_true(not root.is_dark(Vector2(r.position.x + 1.0, r.end.y)), "the bottom edge is outside")
	assert_true(not root.is_dark(r.position - Vector2(0.01, 0.0)), "just left of the rect")
	return true


## Smog eases away over a moment after the sabotage ends (the server sends sabotage null then): the
## light keeps following the eased level, not the full radius.
func test_round_light_uses_the_smog_level_after_the_sabotage_ended() -> bool:
	var saved: Dictionary = _prepared_session()
	var tuning: Dictionary = Session.tuning
	var vision: float = Session.vision_tiles
	var level: float = Session.smog_level
	Session.tuning = {"vision": {"citizen": 4.0, "smog_min": 1.0, "dark_mult": 0.5}}
	var r: RoundScene = track(load("res://scenes/round/round.tscn").instantiate()) as RoundScene
	r._ready()
	Session.sabotage = {}
	Session.smog_level = 0.5
	Session.vision_tiles = 2.0  # the server: still in the dark room
	Session.me_pos = Vector2(34.5, 18.5) * MapRoot.TILE_PX
	r._player.global_position = Vector2(20.5, 10.5) * MapRoot.TILE_PX  # the local player just stepped out
	var out: float = r._light_tiles()
	Session.tuning = tuning
	Session.vision_tiles = vision
	Session.smog_level = level
	_restore_session(saved)
	assert_near(out, 2.5, 0.001, "4 tiles eased half way to 1, not the full 4")
	return true
