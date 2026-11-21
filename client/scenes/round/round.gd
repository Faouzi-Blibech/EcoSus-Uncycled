class_name RoundScene
extends Node2D
## Round: the real map scene, the local player with wall collision, remote players from
## interpolated snapshots, waste piles, a follow camera and the vision fog. Moves are sent at
## 10 Hz. Hosts the HUD and the mini-game layer; the action rules live in ui/hud/action_rules.gd.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const Joystick := preload("res://ui/joystick/virtual_joystick.gd")
const AvatarScene: PackedScene = preload("res://world/player/avatar.tscn")
const LocalPlayerScene: PackedScene = preload("res://world/player/local_player.tscn")
const PileScene: PackedScene = preload("res://world/pile/pile.tscn")
const SpotViewScript: GDScript = preload("res://world/spot/spot_view.gd")
const CameraScript: GDScript = preload("res://world/camera/follow_camera.gd")
const VisionScene: PackedScene = preload("res://world/vision/vision.tscn")
const HudScene: PackedScene = preload("res://ui/hud/hud.tscn")
const MinigameLayerScene: PackedScene = preload("res://minigames/minigame_layer.tscn")

const MAP_SCENE_PATH: String = "res://world/maps/%s.tscn"
const MAP_META_PATH: String = "res://world/maps/%s.meta.json"
const MOVE_SEND_INTERVAL_S: float = 0.1
const RENDER_DELAY_S: float = 0.1
## Used while Session.tuning has no "report_range"; equals the server's current default.
const REPORT_RANGE_FALLBACK_TILES: float = 3.0
## Used while Session.tuning has no dump.clean_range; equals the current server default (CleanRange).
const CLEAN_RANGE_FALLBACK_TILES: float = 1.0
## A held fire console missing from the snapshot list `sabotage.held` for this long is re-sent
## once, then dropped (the server refused it or let it go without telling us).
const HOLD_RECONCILE_S: float = 0.5
## Used while Session.tuning has no sabotage.smog_fix_s; equals the server code default SmogFixS.
const SMOG_FIX_FALLBACK_S: float = 3.0
## Extra seconds after the repair time before the "Repairing..." line stops claiming a repair.
const SMOG_REPAIR_GRACE_S: float = 1.0
## The joystick draws above the smog haze (layer 1) and below the HUD (layer 10).
const JOYSTICK_LAYER: int = 5
## Map marker kinds the player can stand at to act.
const CONSOLE_KINDS: Array[String] = ["task", "emergency", "fire_fix", "smog_fix", "machine"]
## The Recycling Machine's marker id (map special of kind "machine").
const MACHINE_ID: String = "machine"
## Used while Session.tuning has no machine.fix_s; equals the server default (4 s).
const MACHINE_FIX_FALLBACK_S: float = 4.0
## Used while Session.tuning has no virus.heal_range; equals the server default.
const HEAL_RANGE_FALLBACK_TILES: float = 1.5
const GasOverlayScript: GDScript = preload("res://world/vision/gas_overlay.gd")

var _map: MapRoot = null
var _player: LocalPlayer = null
var _camera: FollowCamera = null
var _vision: Vision = null
var _piles_holder: Node2D = null
## Cleaning spots by console id (one SpotView per task console of the map).
var _spot_views: Dictionary = {}
## The toxic tint over the room a gas warning runs in.
var _gas_overlay: GasOverlay = null
var _avatars_holder: Node2D = null
var _fx_holder: Node2D = null
## Y-sorted layer for depth: players and the map's occluders (art drawn in front of a player who
## stands above its base line, such as a tree top).
var _depth: Node2D = null
const OCCLUDER_BEHIND_ALPHA: float = 0.55
var _occluders: Node2D = null
var _world_shown: bool = true
var _avatars: Dictionary = {}
var _piles: Dictionary = {}
var _awaiting_pos: bool = false
var _send_timer: float = 0.0
var _leaving: bool = false
## The fire console the local player holds ("" when none) and the smog panel being repaired.
var _held_console: String = ""
var _repairing: bool = false
## True while FIX is held at a jammed Recycling Machine (the server counts machine.fix_s).
var _holding_machine: bool = false
var _repair_console: String = ""
var _repair_age_s: float = 0.0
## True once a snapshot carried sabotage.repair_t (the server is counting this player's repair).
var _repair_confirmed: bool = false
## Seconds the held console has been absent from the server list, and whether it was re-sent.
var _hold_missing_s: float = 0.0
var _hold_resent: bool = false
## Piles whose clean_done came; hidden from CLEAN until a snapshot stops listing them.
var _cleaned_piles: Dictionary = {}
## The pile being cleaned (-1 when none), the duration the server gave for it (0 until clean_ok)
## and the seconds the ring has run.
var _cleaning_pile: int = -1
var _clean_duration: float = 0.0
var _clean_elapsed: float = 0.0

## What the actions send; tests replace the callables with recorders.
var senders: Dictionary = {
	"use": Net.send_use,
	"report": Net.send_report,
	"repollute": Net.send_repollute,
	"plant_virus": Net.send_plant_virus,
	"gas": Net.send_gas,
	"pick_ability": Net.send_pick_ability,
	"sabotage": Net.send_sabotage,
	"fix": Net.send_fix,
	"clean_start": Net.send_clean_start,
	"clean_cancel": Net.send_clean_cancel,
	"move": Net.send_move,
	"call_meeting": Net.send_call_meeting,
	"pick_upgrade": Net.send_pick_upgrade,
	"heal": Net.send_heal,
}
var _hud: Hud = null
var _minigames: MinigameLayer = null
var _joystick: Joystick


## True when the packed map `id` exists and its meta.json version equals `version`.
static func map_matches(id: String, version: String, meta_path_format: String = MAP_META_PATH) -> bool:
	if id.is_empty() or not ResourceLoader.exists(MAP_SCENE_PATH % id):
		return false
	var meta_path: String = meta_path_format % id
	if not FileAccess.file_exists(meta_path):
		return false
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(meta_path))
	if typeof(parsed) != TYPE_DICTIONARY:
		return false
	return str((parsed as Dictionary).get("version", "")) == version


func _ready() -> void:
	if not map_matches(Session.map_id, Session.map_version):
		_leave_for_update()
		return

	_map = _load_map()
	if _map == null:
		_leave_for_update()
		return
	add_child(_map)
	_build_spots()
	_gas_overlay = GasOverlayScript.new() as GasOverlay
	add_child(_gas_overlay)
	_piles_holder = Node2D.new()
	add_child(_piles_holder)
	_depth = Node2D.new()
	_depth.y_sort_enabled = true
	add_child(_depth)
	_avatars_holder = Node2D.new()
	_avatars_holder.y_sort_enabled = true
	_depth.add_child(_avatars_holder)
	_occluders = _map.occluders()
	if _occluders != null:
		_occluders.reparent(_depth)
	_fx_holder = Node2D.new()
	add_child(_fx_holder)

	_player = LocalPlayerScene.instantiate() as LocalPlayer
	_player.avatar.setup(Session.seat_id, _own_name(), Session.seat_color(Session.seat_id), maxi(Session.seat_id, 0))
	_depth.add_child(_player)
	_camera = CameraScript.new() as FollowCamera
	_player.add_child(_camera)
	var used: Rect2i = _map.floor_layer().get_used_rect()
	_camera.set_bounds(Rect2i(used.position * MapRoot.TILE_PX, used.size * MapRoot.TILE_PX))
	_vision = VisionScene.instantiate() as Vision
	_player.add_child(_vision)
	_vision.add_child(DarkRooms.create(_map.dark_rects(), _vision.ambient()))

	_build_joystick()
	_build_overlays()

	if Session.spawn_pending:
		_place_player(Session.spawn)
		Session.spawn_pending = false
	elif Session.has_me_pos:
		_place_player(Session.me_pos)
	else:
		_awaiting_pos = true
		_player.visible = false
	_update_piles()

	Net.correct.connect(_on_correct)
	Net.snapshot.connect(_on_snapshot)
	Net.clean_ok.connect(_on_clean_ok)
	Net.clean_done.connect(_on_clean_done)
	Net.clean_cancelled.connect(_on_clean_cancelled)
	Net.action_rejected.connect(_on_action_rejected)
	Net.cue.connect(_on_cue)
	Net.event.connect(_on_event)
	Net.lobby.connect(_on_lobby)
	Net.disconnected.connect(_on_disconnected)


func _load_map() -> MapRoot:
	var packed: PackedScene = load(MAP_SCENE_PATH % Session.map_id) as PackedScene
	if packed == null:
		return null
	var node: Node = packed.instantiate()
	if node is MapRoot:
		return node as MapRoot
	if node != null:
		node.free()
	return null


func map() -> MapRoot:
	return _map


func local_position_px() -> Vector2:
	return _player.global_position


## The radius the light is drawn with, in tiles: the server's vision, corrected for a dark room the
## local player has just entered or left (see Vision.light_tiles). Session.me_pos is where the server
## thinks we are.
func _light_tiles() -> float:
	var server_dark: bool = Session.has_me_pos and _map.is_dark(Session.me_pos / MapRoot.TILE_PX)
	var local_dark: bool = _map.is_dark(local_position_px() / MapRoot.TILE_PX)
	return Vision.light_tiles(Session.vision_tiles, server_dark, local_dark, Session.smog_level, Net.as_dict(Session.tuning.get("vision")), Session.role != "polluter")


## The avatar of that seat (the local one included), null if it is not shown.
func avatar_for(seat: int) -> Node2D:
	if _player != null and seat == Session.seat_id:
		return _player.avatar if _player.visible else null
	var a: Avatar = _avatars.get(seat) as Avatar
	if a != null and a.visible:
		return a
	return null


func _own_name() -> String:
	var n: String = Session.seat_name(Session.seat_id)
	return n if not n.is_empty() else Settings.player_name


## The virtual joystick is input, not HUD: it stays here because the round reads its value.
func _build_joystick() -> void:
	var layer: CanvasLayer = CanvasLayer.new()
	layer.layer = JOYSTICK_LAYER
	add_child(layer)
	_joystick = Joystick.new()
	layer.add_child(_joystick)


## The HUD and the mini-game layer; the layer asks for the player position and the console
## geometry (from the map markers) to close a game when the player walks away.
func _build_overlays() -> void:
	_hud = HudScene.instantiate() as Hud
	add_child(_hud)
	_hud.bind(self)
	_minigames = MinigameLayerScene.instantiate() as MinigameLayer
	add_child(_minigames)
	_minigames.set_player_pos_provider(local_position_px)
	_minigames.set_console_lookup(_console_info)


## {pos_px, range_px} of a console marker, {} when the map has no such marker.
func _console_info(console_id: String) -> Dictionary:
	var m: MapMarker = _map.marker(console_id) if _map != null else null
	if m == null:
		return {}
	return {"pos_px": m.global_position, "range_px": Net.tiles_to_px(m.range_tiles)}


func _place_player(pos_px: Vector2) -> void:
	_player.teleport_to(pos_px)
	_player.visible = not Session.is_out
	_camera.snap()


func _physics_process(_delta: float) -> void:
	if _leaving or _player == null:
		return
	var dir: Vector2 = Vector2.ZERO
	if not _awaiting_pos and Session.phase == "playing" and not Session.is_out:
		dir = _read_direction()
	_player.move_with(dir, Session.speed_px_per_s)


func _process(delta: float) -> void:
	if _leaving or _player == null:
		return
	if _awaiting_pos and Session.has_me_pos:
		_place_player(Session.me_pos)
		_awaiting_pos = false

	if Session.vision_tiles > 0.0:
		_vision.set_radius_px(Net.tiles_to_px(_light_tiles()))
	_show_world(not Session.is_out)
	_vision.set_haze(str(Session.sabotage.get("kind", "")) == "smog")
	_sync_holds(delta)
	if _clean_duration > 0.0:
		_clean_elapsed += delta
	_player.avatar.set_out(Session.is_out)
	_player.avatar.set_poison_tint(Session.poison)
	_fade_occluders()

	_player.avatar.set_display_name(_own_name())
	_update_remote(Session.sample(Net.server_time() - RENDER_DELAY_S))

	_joystick.enabled = not _minigames.is_open()
	_send_timer += delta
	if _send_timer >= MOVE_SEND_INTERVAL_S:
		_send_timer = fmod(_send_timer, MOVE_SEND_INTERVAL_S)
		if not _awaiting_pos and Session.phase == "playing" and not Session.is_out:
			_send_move()


## The move number comes from Session: it must not restart when a meeting builds a new round scene.
func _send_move() -> void:
	senders["move"].call(_player.global_position, Session.next_move_seq())


## No walking while a game is open: its drags and keys are not steering.
func _read_direction() -> Vector2:
	if _minigames != null and _minigames.is_open():
		return Vector2.ZERO
	var dir: Vector2 = Input.get_vector("move_left", "move_right", "move_up", "move_down")
	dir += _joystick.value
	if dir.length() > 1.0:
		dir = dir.normalized()
	return dir


## Shows the seats in `remote` (seat -> {pos, color, out}) and hides the others.
func _update_remote(remote: Dictionary) -> void:
	for seat in _avatars:
		if not remote.has(seat):
			(_avatars[seat] as Avatar).visible = false
	for seat in remote:
		var info: Dictionary = remote[seat]
		var a: Avatar = _avatars.get(seat) as Avatar
		if a == null:
			a = AvatarScene.instantiate() as Avatar
			a.setup(int(seat), Session.seat_name(int(seat)), Session.color_of(int(info["color"])), int(seat))
			_avatars_holder.add_child(a)
			_avatars[seat] = a
		a.set_display_name(Session.seat_name(int(seat)))
		a.visible = true
		a.position = info["pos"]
		a.set_out(bool(info["out"]))


## Mirrors Session.piles ([{id, pos (px), kind, state}]): adds, updates and removes. Runs once per
## snapshot, not per frame; Pile.setup ignores unchanged data.
## One SpotView per task console of the map, drawn under the piles and players.
func _build_spots() -> void:
	var holder: Node2D = Node2D.new()
	add_child(holder)
	for m in _map.markers():
		if m.kind != "task":
			continue
		var v: SpotView = SpotViewScript.new() as SpotView
		v.setup(m.marker_id, MinigameRegistry.base_type(_task_type_at_console(m.marker_id)), m.global_position)
		holder.add_child(v)
		_spot_views[m.marker_id] = v
	_update_spots()


## Tints the room of the running gas warning (Session.gas_room), clears it when none.
func _update_gas() -> void:
	if _gas_overlay == null:
		return
	var m: MapMarker = _map.marker(Session.gas_room) if _map != null and not Session.gas_room.is_empty() else null
	if m == null:
		_gas_overlay.clear()
		return
	var rect: Rect2 = Rect2(m.position / MapRoot.TILE_PX, Vector2(m.zone_size_tiles))
	var warning_s: float = float(Net.as_dict(Session.tuning.get("trasher")).get("gas_warning_s", 3.0))
	_gas_overlay.show_room(rect, Session.gas_in_s, warning_s)


func _update_spots() -> void:
	for id in _spot_views:
		(_spot_views[id] as SpotView).show_state(str(Session.spots.get(id, "dirty")))


func _update_piles() -> void:
	var present: Dictionary = {}
	for entry in Session.piles:
		var d: Dictionary = Net.as_dict(entry)
		var id: int = int(d.get("id", -1))
		present[id] = true
		var p: Pile = _piles.get(id) as Pile
		if p == null:
			p = PileScene.instantiate() as Pile
			_piles_holder.add_child(p)
			_piles[id] = p
		p.position = d.get("pos", Vector2.ZERO)
		p.setup(id, str(d.get("kind", "")), str(d.get("state", "")))
	for id in _cleaned_piles.keys():
		if not present.has(id):
			_cleaned_piles.erase(id)
	for id in _piles.keys():
		if not present.has(id):
			(_piles[id] as Pile).queue_free()
			_piles.erase(id)


# ---------------------------------------------------------------- actions

## What the player can reach right now: `near_console` ({id, kind} of the nearest task, emergency
## or fire/smog fix marker inside its range, else {}), `near_spot_state` (Session.spots state of that
## console when it is a task console: a cleaning spot), `bags_full`, `fresh_pile` (nearest
## visible fresh pile id or -1), `sabotage` (the running one, {} when none) and
## `holding_fire`, `clean_pile` (nearest dump pile, fresh or reported, inside the clean range, or -1). Visibility is the server's: only seats and piles of the latest snapshot are known.
func interaction_context() -> Dictionary:
	var pos: Vector2 = local_position_px()
	var near: Dictionary = {}
	var best: float = INF
	if _map != null:
		for m in _map.markers():
			if not CONSOLE_KINDS.has(m.kind):
				continue
			var d: float = pos.distance_to(m.global_position)
			if d <= Net.tiles_to_px(m.range_tiles) and d < best:
				best = d
				near = {"id": m.marker_id, "kind": m.kind}
	var spot_state: String = str(Session.spots.get(str(near["id"]), "")) if str(near.get("kind", "")) == "task" else ""
	return {
		"near_console": near,
		"near_spot_state": spot_state,
		"bags_full": Session.bag_cap > 0 and Session.bags >= Session.bag_cap,
		"fresh_pile": _nearest_fresh_pile(pos),
		"clean_pile": _nearest_clean_pile(pos),
		"heal_target": _nearest_seat_in_reach(pos, heal_range_px()),
		"sabotage": Session.sabotage,
		"holding_fire": not _held_console.is_empty(),
		"held_console": _held_console,
	}


## How close a pile must be to report it (the server rejects it from farther away).
func report_range_px() -> float:
	return Net.tiles_to_px(float(Session.tuning.get("report_range", REPORT_RANGE_FALLBACK_TILES)))


## The nearest fresh pile inside the report range, -1 when there is none.
func _nearest_fresh_pile(pos: Vector2) -> int:
	var found: int = -1
	var best: float = report_range_px()
	for entry in Session.piles:
		var d: Dictionary = Net.as_dict(entry)
		if str(d.get("state", "")) != "fresh":
			continue
		var dist: float = pos.distance_to(d.get("pos", Vector2.ZERO))
		if dist <= best:
			best = dist
			found = int(d.get("id", -1))
	return found


## How close a pile must be to clean it.
func clean_range_px() -> float:
	var range_tiles: float = float(Net.as_dict(Session.tuning.get("dump")).get("clean_range", CLEAN_RANGE_FALLBACK_TILES))
	return Net.tiles_to_px(range_tiles)


## The nearest cleanable pile inside the clean range (a dump pile, fresh or reported), -1 when none.
func _nearest_clean_pile(pos: Vector2) -> int:
	var found: int = -1
	var best: float = clean_range_px()
	for entry in Session.piles:
		var d: Dictionary = Net.as_dict(entry)
		if str(d.get("kind", "")) != "dump" or not ["fresh", "reported"].has(str(d.get("state", ""))):
			continue
		if _cleaned_piles.has(int(d.get("id", -1))):
			continue
		var dist: float = pos.distance_to(d.get("pos", Vector2.ZERO))
		if dist <= best:
			best = dist
			found = int(d.get("id", -1))
	return found


## The button states (ActionRules.button_states) for the current moment.
func action_states() -> Dictionary:
	var ctx: Dictionary = interaction_context()
	ctx["phase"] = Session.phase
	ctx["role"] = Session.role
	ctx["is_out"] = Session.is_out
	ctx["cd"] = Session.cooldowns
	ctx["emergencies_left"] = Session.emergencies_left
	ctx["bags"] = Session.bags
	ctx["machine_jammed"] = Session.machine_jammed
	ctx["ability"] = Session.ability
	ctx["heals"] = Session.heals
	ctx["infected"] = Session.infected_s >= 0.0
	ctx["alert_used"] = Session.alert_used
	return ActionRules.button_states(ctx)


## Runs "use", "report", "repollute", "ability" (the virus; the gas goes through send_gas after the
## room picker), "sabotage_machine" or "sabotage_smog" if the rules allow it
## right now (only "use" while a game is open). The server checks again.
func perform_action(action: String) -> void:
	if _leaving or _player == null:
		return
	# The touch buttons sit under the layer's dim; the keys must not reach past an open game.
	if action != "use" and _minigames.is_open():
		return
	var states: Dictionary = action_states()
	match action:
		"use":
			if bool(states["use"]) and not _minigames.is_open():
				_use_nearest_console()
		"report":
			if bool(states["report"]):
				senders["report"].call(_nearest_fresh_pile(local_position_px()))
		"repollute":
			if bool(Net.as_dict(states["repollute"]).get("ready", false)):
				senders["repollute"].call(_near_spot_id())
		"ability":
			var ability: Dictionary = Net.as_dict(states["ability"])
			if bool(ability.get("ready", false)) and str(ability.get("kind", "")) == "virus":
				senders["plant_virus"].call(_near_spot_id())
		"heal":
			if bool(states["heal"]):
				var target: int = Session.seat_id if Session.infected_s >= 0.0 else _nearest_seat_in_reach(local_position_px(), heal_range_px())
				senders["heal"].call(target)
		"alert":
			if bool(states["alert"]):
				Session.alert_used = true
				senders["call_meeting"].call()
		"sabotage_machine", "sabotage_smog":
			if bool(Net.as_dict(states["sabotage"]).get("ready", false)):
				senders["sabotage"].call(action.trim_prefix("sabotage_"))


## Gases `room` (an id of the map's gas rooms) when the gas is ready.
func send_gas(room: String) -> void:
	if _leaving or _player == null:
		return
	var ability: Dictionary = Net.as_dict(action_states()["ability"])
	if bool(ability.get("ready", false)) and str(ability.get("kind", "")) == "gas":
		senders["gas"].call(room)


## The gas room ids of the map (marker kind "gas_room"), sorted.
func gas_room_ids() -> Array[String]:
	var ids: Array[String] = []
	if _map != null:
		for m in _map.markers():
			if m.kind == "gas_room":
				ids.append(m.marker_id)
	ids.sort()
	return ids


## The Trasher chose the virus or the gas (AbilityPicker.picked). The choice closes at once and is
## not offered again before ability_ok arrives.
func pick_ability(ability: String) -> void:
	senders["pick_ability"].call(ability)
	Session.ability_choices = []
	Session.ability_pick_sent = true


## How close another player must be to heal them (tuning virus.heal_range, 1.5 tiles by default).
func heal_range_px() -> float:
	return Net.tiles_to_px(float(Net.as_dict(Session.tuning.get("virus")).get("heal_range", HEAL_RANGE_FALLBACK_TILES)))


## The nearest visible, active other seat within `range_px`, or -1.
func _nearest_seat_in_reach(pos: Vector2, range_px: float) -> int:
	var found: int = -1
	var best: float = INF
	for seat in _avatars:
		var a: Avatar = _avatars[seat] as Avatar
		if not a.visible or int(seat) == Session.seat_id or Session.seat_out(int(seat)):
			continue
		var dist: float = pos.distance_to(a.position)
		if dist <= range_px and dist < best:
			best = dist
			found = int(seat)
	return found


## The id of the spot (task console) in range, "" when none.
func _near_spot_id() -> String:
	var near: Dictionary = Net.as_dict(interaction_context().get("near_console"))
	return str(near.get("id", "")) if str(near.get("kind", "")) == "task" else ""


## Presses a hold button. "fix": a fire console is held until end_hold (or until it is no longer
## offered); the smog panel is a single tap and `is_repairing()` stays true until the repair ends.
## "clean": starts cleaning the nearest pile; the ring runs from clean_ok until clean_done.
func begin_hold(action: String) -> void:
	if _leaving or _player == null or _minigames.is_open():
		return
	if action == "clean":
		_begin_clean()
		return
	if action != "fix":
		return
	var fix: Dictionary = Net.as_dict(action_states()["fix"])
	if not bool(fix["visible"]):
		return
	var id: String = str(Net.as_dict(interaction_context().get("near_console")).get("id", ""))
	if str(fix["kind"]) == "machine":
		if not _holding_machine:
			_holding_machine = true
			senders["fix"].call(id, true)
		return
	if str(fix["kind"]) == "fire":
		if _held_console != id:
			_held_console = id
			_hold_missing_s = 0.0
			_hold_resent = false
			senders["fix"].call(id, true)
	else:
		if _repairing and _repair_console == id:
			return  # a repeat tap: the server keeps its own timer, so the local state must not restart
		_repairing = true
		_repair_console = id
		_repair_age_s = 0.0
		_repair_confirmed = false
		senders["fix"].call(id, false)


## Lets go of what begin_hold holds. A smog tap has nothing to release.
func end_hold(action: String) -> void:
	if action == "fix":
		_release_fire()
		_release_machine()
	elif action == "clean":
		_cancel_clean()


## {active, elapsed, duration} of the cleaning ring; active from clean_ok until the end.
func clean_progress() -> Dictionary:
	return {"active": _clean_duration > 0.0, "elapsed": _clean_elapsed, "duration": _clean_duration}


func _begin_clean() -> void:
	var pile: int = int(interaction_context()["clean_pile"])
	if not bool(action_states()["clean"]) or _cleaning_pile == pile:
		return
	_cleaning_pile = pile
	_clean_duration = 0.0
	_clean_elapsed = 0.0
	senders["clean_start"].call(pile)


## Tells the server to stop, unless the clean already ended (done, cancelled or refused).
func _cancel_clean() -> void:
	if _cleaning_pile < 0:
		return
	var pile: int = _cleaning_pile
	_forget_clean()
	senders["clean_cancel"].call(pile)


func _forget_clean() -> void:
	_cleaning_pile = -1
	_clean_duration = 0.0
	_clean_elapsed = 0.0


## The seconds the server needs for a smog repair (tuning sabotage.smog_fix_s, a fallback until it is sent).
func smog_fix_s() -> float:
	return float(Net.as_dict(Session.tuning.get("sabotage")).get("smog_fix_s", SMOG_FIX_FALLBACK_S))


## {active, elapsed, duration} of the smog repair, from the server's own count (snapshot
## sabotage.repair_t, sent to the repairer only): active only while the server is counting. Elapsed
## stops at the duration while the server's answer (sabotage_ended) is still on its way.
func repair_progress() -> Dictionary:
	var duration: float = smog_fix_s()
	var counted: Variant = Session.sabotage.get("repair_t")
	var active: bool = _repairing and counted != null
	return {"active": active, "elapsed": minf(float(counted), duration) if active else 0.0, "duration": duration}


func is_repairing() -> bool:
	return _repairing


## {active, elapsed, duration} of the machine fix, from the server's own count (snapshot
## machine.repair_t, sent to the repairer only): active only while the server counts this hold.
func machine_repair_progress() -> Dictionary:
	var duration: float = float(Net.as_dict(Session.tuning.get("machine")).get("fix_s", MACHINE_FIX_FALLBACK_S))
	var active: bool = _holding_machine and Session.machine_repair_t >= 0.0
	return {"active": active, "elapsed": minf(Session.machine_repair_t, duration) if active else 0.0, "duration": duration}


func is_holding_machine() -> bool:
	return _holding_machine


## The player chose a card of the upgrade offer (UpgradePicker.picked).
func pick_upgrade(item: String) -> void:
	senders["pick_upgrade"].call(item)
	Session.upgrade_offer = []


func _release_machine() -> void:
	if not _holding_machine:
		return
	_holding_machine = false
	senders["fix"].call(MACHINE_ID, false)


func _release_fire() -> void:
	if _held_console.is_empty():
		return
	var id: String = _held_console
	_held_console = ""
	senders["fix"].call(id, false)


## Every hold this scene owes the server a release for; called before the scene leaves.
func _release_holds() -> void:
	_release_fire()
	_release_machine()
	_cancel_clean()
	_repairing = false


## Keeps the local holds honest. Cleaning and a held fire console end when their own target is out
## of reach (the server checks only that, not who is nearest). A held console the server does not
## list is re-sent once, then dropped. The smog repair line ends when the panel is out of reach or
## the repair time has passed without sabotage_ended (the server cancels a repair without telling us).
func _sync_holds(delta: float = 0.0) -> void:
	if _cleaning_pile >= 0 and not _pile_in_clean_range(_cleaning_pile):
		_cancel_clean()
	_sync_fire_hold(delta)
	_sync_repair(delta)
	_sync_machine_hold()


func _pile_in_clean_range(id: int) -> bool:
	var pos: Vector2 = local_position_px()
	for entry in Session.piles:
		var d: Dictionary = Net.as_dict(entry)
		if int(d.get("id", -1)) == id:
			return pos.distance_to(d.get("pos", Vector2.ZERO)) <= clean_range_px()
	return false


func _console_in_reach(id: String) -> bool:
	var info: Dictionary = _console_info(id)
	return not info.is_empty() and local_position_px().distance_to(info["pos_px"]) <= float(info["range_px"])


func _sync_fire_hold(delta: float) -> void:
	if _held_console.is_empty():
		return
	if str(Session.sabotage.get("kind", "")) != "fire" or not _console_in_reach(_held_console):
		_release_fire()
		return
	if Net.as_array(Session.sabotage.get("held")).has(_held_console):
		_hold_missing_s = 0.0
		_hold_resent = false
		return
	_hold_missing_s += delta
	if _hold_missing_s < HOLD_RECONCILE_S:
		return
	if _hold_resent:
		_held_console = ""
		return
	_hold_resent = true
	_hold_missing_s = 0.0
	senders["fix"].call(_held_console, true)


## A machine hold ends when the jam is over (the server fixed it: nothing to release) or when the
## player walks out of the machine's range (release, so the server stops counting).
func _sync_machine_hold() -> void:
	if not _holding_machine:
		return
	if not Session.machine_jammed:
		_holding_machine = false
	elif not _console_in_reach(MACHINE_ID):
		_release_machine()


func _sync_repair(delta: float) -> void:
	if not _repairing:
		return
	if Session.sabotage.has("repair_t"):
		_repair_confirmed = true
	elif _repair_confirmed:
		_repairing = false  # the server was counting and stopped: it dropped the repair without a message
		_repair_confirmed = false
		return
	var fix_s: float = smog_fix_s()
	_repair_age_s += delta
	# Until the server counts, a timeout ends the line; once it counts, its count and sabotage_ended rule.
	var timed_out: bool = not _repair_confirmed and _repair_age_s > fix_s + SMOG_REPAIR_GRACE_S
	if str(Session.sabotage.get("kind", "")) != "smog" or not _console_in_reach(_repair_console) or timed_out:
		_repairing = false
		_repair_confirmed = false


## A task console opens the game (a polluter gets a fake that sends nothing); the emergency
## console calls the meeting.
func _use_nearest_console() -> void:
	var near: Dictionary = Net.as_dict(interaction_context().get("near_console"))
	var id: String = str(near.get("id", ""))
	if str(near.get("kind", "")) == "emergency":
		senders["call_meeting"].call()
	elif Session.role == "polluter":
		var type: String = _task_type_at_console(id)
		if not type.is_empty():
			_minigames.open_fake(type, id)
	else:
		senders["use"].call(id)


## The first task type (content order) that has this console.
func _task_type_at_console(console_id: String) -> String:
	for type in Session.task_types:
		if Net.as_array(Net.as_dict(Session.task_types[type]).get("consoles")).has(console_id):
			return str(type)
	return ""


## The local player and the world are hidden for a player who is out; the HUD shows the overlay.
func _show_world(on: bool) -> void:
	if on == _world_shown:
		return
	_world_shown = on
	_map.visible = on
	if _occluders != null:
		_occluders.visible = on
	_piles_holder.visible = on
	_avatars_holder.visible = on
	_fx_holder.visible = on
	_player.visible = on and not _awaiting_pos


## An occluder the local player stands behind turns see-through, so the player never loses sight
## of themselves behind a tree.
func _fade_occluders() -> void:
	if _occluders == null:
		return
	var p: Vector2 = _player.global_position
	for child in _occluders.get_children():
		var piece: CanvasItem = child as CanvasItem
		if piece == null or not piece.has_meta("rect"):
			continue
		var rect: Rect2 = piece.get_meta("rect")
		var behind: bool = rect.has_point(p) and p.y < (piece as Node2D).position.y
		piece.modulate.a = OCCLUDER_BEHIND_ALPHA if behind else 1.0


## The map is missing or older/newer than the server's: this build cannot play the round.
func _leave_for_update() -> void:
	_leaving = true
	_release_holds()
	Net.send("leave")
	Session.clear_room()
	Session.pending_notice = "BOOT_UPDATE_NEEDED"
	Router.go(get_tree(), Router.MENU)


# ---------------------------------------------------------------- Net handlers

func _on_clean_ok(data: Dictionary) -> void:
	if int(data.get("pile", -2)) == _cleaning_pile:
		_clean_duration = float(data.get("duration", 0.0))
		_clean_elapsed = 0.0


func _on_clean_done(data: Dictionary) -> void:
	if int(data.get("pile", -2)) == _cleaning_pile:
		_cleaned_piles[_cleaning_pile] = true
		_forget_clean()


func _on_clean_cancelled(data: Dictionary) -> void:
	if int(data.get("pile", -2)) == _cleaning_pile:
		_forget_clean()


## A refused clean_start leaves no job on the server, so there is nothing to cancel; a refused
## fix means the server holds nothing for us (the hold or the repair is not real).
func _on_action_rejected(data: Dictionary) -> void:
	match str(data.get("action", "")):
		"clean_start":
			_forget_clean()
		"fix":
			_held_console = ""
			_repairing = false


func _on_correct(data: Dictionary) -> void:
	if data.has("pos"):
		_place_player(data["pos"])


func _on_snapshot(_data: Dictionary) -> void:
	_update_piles()
	_update_spots()
	_update_gas()
	_leave_if_phase_changed()


func _on_event(data: Dictionary) -> void:
	if str(data.get("t", "")) == "spot_cleaned":
		_update_spots()
	_leave_if_phase_changed()


## The intro (role reveal) stays in this scene with movement locked; other phases leave once.
func _leave_if_phase_changed() -> void:
	if _leaving or Router.is_round_phase(Session.phase):
		return
	_leaving = true
	_release_holds()
	if is_inside_tree():
		Router.go(get_tree(), Router.scene_for_phase(Session.phase))


func _on_lobby(_data: Dictionary) -> void:
	_leaving = true
	_release_holds()
	if is_inside_tree():
		Router.go(get_tree(), Router.LOBBY)


func _on_disconnected() -> void:
	_leaving = true
	_release_holds()
	if is_inside_tree():
		Router.on_disconnected(get_tree())


## Backstop for any other way out of the scene (releasing twice is harmless: the holds are cleared).
func _exit_tree() -> void:
	_release_holds()


func _on_cue(data: Dictionary) -> void:
	if data.has("pos"):
		CueEffect.spawn(_fx_holder, str(data.get("kind", "")), data["pos"])
