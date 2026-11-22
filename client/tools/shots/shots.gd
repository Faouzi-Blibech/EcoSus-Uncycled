extends Node
## Screenshot tour: opens every screen with sample state and saves one PNG each, for art reviews.
## godot --path client res://tools/shots/shots.tscn -- --out <dir> [--only name,name]
## Needs a real window (the headless renderer draws nothing). Not part of the game.

const SEATS: Array = [
	{"seat": 0, "name": "Ana", "color": 0, "human": true, "skin": "sprout"},
	{"seat": 1, "name": "Bo", "color": 1, "human": true, "skin": "eagle"},
	{"seat": 2, "name": "Cy", "color": 2, "human": false, "skin": "robot"},
	{"seat": 3, "name": "Dee", "color": 3, "human": true, "skin": "rabbit"},
]
const MAP_STAND_IN: String = "res://assets/maps/tbs/garden.jpg"
const MINIGAMES: Array[String] = ["sort_waste", "crush_cans", "rinse_bottles", "turn_compost", "deliver_glass", "check_meter"]

var _out: String = ""
var _only: PackedStringArray = []


func _ready() -> void:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	for i in args.size():
		if args[i] == "--out" and i + 1 < args.size():
			_out = args[i + 1]
		if args[i] == "--only" and i + 1 < args.size():
			_only = args[i + 1].split(",")
	if _out.is_empty():
		_out = OS.get_user_data_dir() + "/shots"
	DirAccess.make_dir_recursive_absolute(_out)
	await get_tree().process_frame
	# Boot first, while it is still connecting (it moves on to the menu once the server answers).
	if _only.is_empty() or _only.has("boot"):
		var boot: Node = _scene("res://scenes/boot/boot.tscn")
		get_tree().root.add_child(boot)
		get_tree().current_scene = boot
		for i in 4:
			await get_tree().process_frame
		get_viewport().get_texture().get_image().save_png("%s/boot.png" % _out)
		print("SHOT boot")
		get_tree().current_scene.queue_free()
		await get_tree().process_frame
		Net.close_socket()
	# Screens such as the menu go back to boot without a server: connect first.
	Net.connect_to(Settings.server_url)
	await Net.hello_ok
	await _tour()
	get_tree().quit()


func _tour() -> void:
	_base_session()
	var cases: Array = [
		["menu", func() -> Node: return _scene("res://scenes/menu/menu.tscn")],
		["menu_profile", func() -> Node: var m: Node = _scene("res://scenes/menu/menu.tscn"); m.call_deferred("_open_profile_popup"); return m],
		["menu_scan", func() -> Node: var m: Node = _scene("res://scenes/menu/menu.tscn"); m.call_deferred("_open_scan_popup"); return m],
		["menu_settings", func() -> Node: var m: Node = _scene("res://scenes/menu/menu.tscn"); m.call_deferred("_open_settings"); return m],
		["create", func() -> Node: return _scene("res://scenes/create/create.tscn")],
		["join", func() -> Node: return _scene("res://scenes/join/join.tscn")],
		["lobby", func() -> Node: return _scene("res://scenes/lobby/lobby.tscn")],
		["reconnecting", func() -> Node: return _scene("res://scenes/reconnect/reconnecting.tscn")],
		["placeholder", func() -> Node: return _scene("res://scenes/placeholder/placeholder.tscn")],
		["results_win", func() -> Node: _game_over("citizens", "meter_full"); return _scene("res://scenes/results/results.tscn")],
		["results_lose", func() -> Node: _game_over("polluters", "deadline"); return _scene("res://scenes/results/results.tscn")],
		["meeting_discussion", func() -> Node: _meeting("discussion"); return _scene("res://scenes/meeting/meeting.tscn")],
		["meeting_voting", func() -> Node: _meeting("voting"); return _scene("res://scenes/meeting/meeting.tscn")],
		["meeting_results", func() -> Node: _meeting("results"); return _scene("res://scenes/meeting/meeting.tscn")],
		["meeting_ejection", func() -> Node: _meeting("results"); Session.phase = "ejection"; var m: Node = _scene("res://scenes/meeting/meeting.tscn"); m.set("_ejection", {"seat": 1, "was_polluter": true}); return m],
		["report_splash", func() -> Node: return _splash()],
		["role_cleaner", func() -> Node: return _reveal("citizen", "")],
		["role_trasher", func() -> Node: return _reveal("polluter", "Bo")],
		["upgrade_picker", func() -> Node: return _picker(UpgradePicker.new(), ["backpack", "tool", "heal", "mask"])],
		["ability_picker", func() -> Node: return _picker(AbilityPicker.new(), ["virus", "gas"])],
		["spectator", func() -> Node: return _spectator()],
	]
	for g in MINIGAMES:
		cases.append(["mg_" + g, _minigame.bind(g)])
	for c in cases:
		var name: String = str(c[0])
		if not _only.is_empty() and not _only.has(name):
			continue
		_base_session()
		var node: Node = (c[1] as Callable).call()
		get_tree().root.add_child(node)
		# The screen is the current scene, so a screen that routes away replaces itself, not the tour.
		get_tree().current_scene = node
		# The ejection plays a short tumble: shoot it once the character is mid-way and the line is in.
		var frames: int = 130 if name == "meeting_ejection" else 20
		for i in frames:
			await get_tree().process_frame
		var img: Image = get_viewport().get_texture().get_image()
		img.save_png("%s/%s.png" % [_out, name])
		print("SHOT ", name)
		if is_instance_valid(get_tree().current_scene) and get_tree().current_scene != self:
			get_tree().current_scene.queue_free()
		if is_instance_valid(node):
			node.queue_free()
		await get_tree().process_frame


func _scene(path: String) -> Node:
	return (load(path) as PackedScene).instantiate()


func _base_session() -> void:
	Session.seat_id = 0
	Session.host_seat = 0
	Session.room_code = "ECO42"
	Session.room_size = 8
	Session.map_id = "tbs"
	Session.seats = SEATS.duplicate(true)
	Session.phase = "lobby"
	Session.meter = 46.0
	Session.out_seats = {}
	Session.is_out = false


func _game_over(winner: String, reason: String) -> void:
	Session.phase = "ended"
	Session.last_game_over = {
		"winner": winner, "reason": reason, "roles": {"0": "citizen", "1": "polluter", "2": "citizen", "3": "citizen"},
		"timeline": [
			{"t": 32.0, "kind": "clean", "seat": 0}, {"t": 75.0, "kind": "meeting", "seat": 2},
			{"t": 110.0, "kind": "ejected", "seat": 3},
		],
		"fact": "fact_plastic_bottle",
	}


func _meeting(stage: String) -> void:
	Session.phase = "meeting"
	Session.meeting = {"stage": stage, "voted": [1] as Array[int], "votes": {"1": 2, "2": 1}, "clues": [], "caller": 2,
		"living": [0, 1, 2, 3], "ejected": -1}


func _splash() -> Node:
	var s: Node = load("res://scenes/meeting/report_splash.gd").new()
	s.call_deferred("setup", Color(0.9, 0.4, 0.3), tr("MEETING_SPLASH_REPORT"), "Cy")
	return s


func _reveal(role: String, partner: String) -> Node:
	var r: RoleReveal = RoleReveal.new()
	r.call_deferred("setup", role, partner)
	return r


func _picker(p: CardPicker, items: Array[String]) -> Node:
	var holder: Control = Control.new()
	holder.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	holder.add_child(p)
	p.call_deferred("open", items)
	return holder


func _spectator() -> Node:
	Session.phase = "playing"
	Session.is_out = true
	var s: SpectatorOverlay = SpectatorOverlay.new()
	s.call_deferred("set_cause", "virus")
	return s


func _minigame(task_type: String) -> Node:
	# The games float over the round: a map picture stands in for it.
	var root: Control = Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var map: TextureRect = TextureRect.new()
	map.texture = load(MAP_STAND_IN) as Texture2D
	map.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	map.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	root.add_child(map)
	map.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var layer: Node = (load("res://minigames/minigame_layer.tscn") as PackedScene).instantiate()
	layer.call_deferred("_open", {"id": 1, "type": task_type, "steps": 1}, 0, false, 0.5)
	root.add_child(layer)
	return root
