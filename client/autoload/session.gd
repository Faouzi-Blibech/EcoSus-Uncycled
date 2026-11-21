extends Node
## Local mirror of the server state: my seat, room, phase, lobby seats, and the snapshot ring buffer.
## Connects to Net signals first (autoloads are ready before scenes), so scenes always see
## updated data when their own handler of the same signal runs.

const Router := preload("res://scenes/router.gd")
const ChatLimits := preload("res://content/chat_limits.gd")
const SESSION_PATH: String = "user://session.cfg"
const BUFFER_SIZE: int = 8
const MAX_EXTRAPOLATION_S: float = 0.2
const STALE_HIDE_S: float = 1.0
const EJECTIONS_PATH: String = "user://ejections.cfg"
## Fallback for Session.tuning["polluters"].
const DEFAULT_POLLUTERS: int = 2
## Fallbacks for Session.tuning["net"]; the server may not send the block yet.
const DEFAULT_TARGET_DEPTH: int = 3
const DEFAULT_SLOW_RATE: float = 0.9
const DEFAULT_FAST_RATE: float = 1.1
const DEFAULT_MAX_LAG_S: float = 0.5
## A render time that jumps by more than this, forward or back (app paused, new match), restarts the
## playback clock. Smaller backward steps (a late snapshot moves the server offset) just hold it.
const PLAYBACK_RESET_GAP_S: float = 1.0
## Ghost lines kept for the out player's panel (the server keeps 100 for a welcome).
const GHOST_KEEP: int = 100


class Snap:
	var t: float = 0.0
	## int seat -> {"pos": Vector2 (px), "color": int, "out": bool}
	var players: Dictionary = {}


## Where the reconnect data is saved; tests point it elsewhere.
var save_path: String = SESSION_PATH
## Where the per-seat ejection count is saved; tests point it elsewhere.
var ejections_path: String = EJECTIONS_PATH

var seat_id: int = -1
var room_code: String = ""
var reconnect_token: String = ""
var host_seat: int = -1
var map_id: String = ""
var phase: String = "lobby"
var tuning: Dictionary = {}
var speed_px_per_s: float = 0.0
var my_tasks: Array = []
var seats: Array = []

var map_version: String = ""
## task type id -> definition ({category, steps, min_s, consoles}) from welcome.tasks.tasks.
var task_types: Dictionary = {}
var poison: int = 0
## "hit", "dump", "sabotage" -> seconds left.
var cooldowns: Dictionary = {}
## The running sabotage: {} or {kind ("fire"/"smog"), countdown (fire), held (fire console ids)}.
var sabotage: Dictionary = {}
var emergencies_left: int = 0
var vision_tiles: float = 0.0
## Smog strength 0..1 (snapshot me.smog_level): citizen vision eases to the smog minimum with it and back
## after the sabotage ended, so it is kept outside Session.sabotage.
var smog_level: float = 0.0
var deadline_t: float = 0.0
## Deadline clock (snapshot clock_t): active play seconds, frozen during meetings and ejections.
var clock_t: float = 0.0
## Latest snapshot piles, [{id, pos (px), kind, state}].
var piles: Array = []
var is_out: bool = false
## Why the local player is out ("poison" or "vote"), "" when in or unknown (rejoined as out).
var out_cause: String = ""
## seat -> true
var out_seats: Dictionary = {}
## cause, by, clues, task_bar, stage (discussion/voting/results), ends_at (local time, Net.local_time()),
## voted (Array[int]), votes. Empty when no meeting.
var meeting: Dictionary = {}
## Chat phrases I sent in this meeting. A rejoin welcome reports it (meeting.my_chat); a
## "phrase_limit" rejection of chat_phrase corrects it.
var phrases_used: int = 0
## Typed chat lines I sent in this meeting (welcome meeting.my_text; "text_limit" corrects it).
var text_used: int = 0
## Net.local_time() of my last chat line, typed, phrase or ghost: the server keeps a gap between two.
var last_chat_t: float = -1.0e9
## Ghost chat lines seen so far, as the server sent them ({seat, text} or {seat, id, args}, ghost
## true). Only an out seat receives any.
var ghost_chat: Array = []
var last_game_over: Dictionary = {}
## Polluters voted out so far in this match, as far as the client knows; -1 while unknown (rejoined
## on a device that has no saved count). Read it through polluters_left().
var ejected_polluters: int = 0
## Adaptive remote playback: the server time sample() renders at (see _advance_playback).
var playback_t: float = 0.0

var role: String = ""
var partner_seat: int = -1
var signature: Variant = null

var meter: float = 0.0
## v2 (protocol 2). Spot id -> "dirty" or "clean", from every snapshot.
var spots: Dictionary = {}
var machine_jammed: bool = false
## The room a toxic gas warning runs in ("" when none) and the seconds before it lands.
var gas_room: String = ""
var gas_in_s: float = 0.0
## Cleaner-only private fields. bag_cap 0 means this seat has no bags (a Trasher): no bag counter.
var bags: int = 0
var bag_cap: int = 0
var has_tool: bool = false
var heals: int = 0
var masks: int = 0
## Seconds before the virus takes me out; -1 when not infected.
var infected_s: float = -1.0
## Trasher-only: re-pollutes so far and the unlocked ability ("", "virus" or "gas").
var repollutes: int = 0
var ability: String = ""
var match_t: float = 0.0
var me_pos: Vector2 = Vector2.ZERO
var has_me_pos: bool = false
## Set by match_started / round_resumed; the round scene consumes it.
var spawn: Vector2 = Vector2.ZERO
var spawn_pending: bool = false
## Translation key shown once by the menu (e.g. session expired).
var pending_notice: String = ""
## Next move number. It lives here, not in the round scene, because the server only resets its
## last number when this client rejoins (welcome) or a new match starts, not after a meeting.
var move_seq: int = 0

var player_colors: Array[Color] = [
	Color(0.90, 0.25, 0.25),
	Color(0.25, 0.50, 0.95),
	Color(0.30, 0.80, 0.35),
	Color(0.95, 0.85, 0.25),
	Color(0.95, 0.55, 0.20),
	Color(0.65, 0.35, 0.90),
	Color(0.25, 0.85, 0.85),
	Color(0.95, 0.55, 0.75),
	Color(0.55, 0.40, 0.25),
	Color(0.60, 0.85, 0.20),
	Color(0.90, 0.90, 0.90),
	Color(0.20, 0.35, 0.70),
]

var _snaps: Array[Snap] = []
var _playback_input_t: float = 0.0
var _playback_started: bool = false


func _ready() -> void:
	speed_px_per_s = Net.tiles_to_px(Net.DEFAULT_SPEED_TILES_PER_S)
	Net.welcome.connect(_on_welcome)
	Net.lobby.connect(_on_lobby)
	Net.match_started.connect(_on_match_started)
	Net.snapshot.connect(_on_snapshot)
	Net.event.connect(_on_event)


# ---------------------------------------------------------------- queries

func has_room() -> bool:
	return room_code != ""


## The number for the next `move`: counts up for the whole match, across round scenes.
func next_move_seq() -> int:
	var n: int = move_seq
	move_seq += 1
	return n


func is_host() -> bool:
	return seat_id >= 0 and seat_id == host_seat


func seat_info(seat: int) -> Dictionary:
	for s in seats:
		if typeof(s) == TYPE_DICTIONARY:
			var d: Dictionary = s
			if int(d.get("seat", -1)) == seat:
				return d
	return {}


func seat_name(seat: int) -> String:
	return str(seat_info(seat).get("name", ""))


func seat_out(seat: int) -> bool:
	return bool(out_seats.get(seat, false))


func task_by_id(id: int) -> Dictionary:
	for t in my_tasks:
		if typeof(t) == TYPE_DICTIONARY:
			var d: Dictionary = t
			if int(d.get("id", -1)) == id:
				return d
	return {}


## The unfinished task whose current console is console_id.
func my_task_at_console(console_id: String) -> Dictionary:
	for t in my_tasks:
		if typeof(t) == TYPE_DICTIONARY:
			var d: Dictionary = t
			if int(d.get("steps_done", 0)) < int(d.get("steps", 1)) and str(d.get("console", "")) == console_id:
				return d
	return {}


func min_s_for(task_type: String) -> float:
	return float(Net.as_dict(task_types.get(task_type)).get("min_s", 0.0))


## consoles[step % size], "" when the type is unknown.
func console_for_step(task_type: String, step: int) -> String:
	var consoles: Array = Net.as_array(Net.as_dict(task_types.get(task_type)).get("consoles"))
	if consoles.is_empty():
		return ""
	return str(consoles[step % consoles.size()])


func color_of(index: int) -> Color:
	if index >= 0 and index < player_colors.size():
		return player_colors[index]
	return Color.WHITE


func seat_color(seat: int) -> Color:
	return color_of(int(seat_info(seat).get("color", -1)))


func _meeting_tuning() -> Dictionary:
	return Net.as_dict(tuning.get("meeting"))


func phrase_limit() -> int:
	return int(_meeting_tuning().get("phrases_per_meeting", ChatLimits.PHRASES_PER_MEETING))


func text_limit() -> int:
	return int(_meeting_tuning().get("text_per_meeting", ChatLimits.TEXT_PER_MEETING))


## Longest typed line, in characters (the server counts code points, like a Godot LineEdit).
func text_max_chars() -> int:
	return int(_meeting_tuning().get("text_max_chars", ChatLimits.TEXT_MAX_CHARS))


## Seconds the server keeps between two chat lines of one seat.
func chat_gap_s() -> float:
	return float(_meeting_tuning().get("chat_min_gap_s", ChatLimits.MIN_GAP_S))


## Chat phrases I may still send in this meeting.
func chat_left() -> int:
	return maxi(phrase_limit() - phrases_used, 0)


## Typed chat lines I may still send in this meeting.
func text_left() -> int:
	return maxi(text_limit() - text_used, 0)


## Seconds before my next chat line may go; 0 or less when it may go now.
func chat_gap_left() -> float:
	return last_chat_t + chat_gap_s() - Net.local_time()


## Polluters still hidden: tuning.polluters minus the ejected ones; -1 when the count is unknown
## (a rejoin on a device that did not see the match), so the UI hides it.
func polluters_left() -> int:
	if ejected_polluters < 0:
		return -1
	return maxi(_total_polluters() - ejected_polluters, 0)


func _total_polluters() -> int:
	return int(tuning.get("polluters", DEFAULT_POLLUTERS))


# ---------------------------------------------------------------- persistence

func load_saved() -> bool:
	var cfg: ConfigFile = ConfigFile.new()
	if cfg.load(save_path) != OK:
		return false
	var code: String = str(cfg.get_value("session", "room", ""))
	var token: String = str(cfg.get_value("session", "token", ""))
	if code.is_empty() or token.is_empty():
		return false
	room_code = code
	reconnect_token = token
	seats = Net.as_array(cfg.get_value("session", "seats", []))
	phase = str(cfg.get_value("session", "phase", "lobby"))
	_mark_out_from_seats()
	return true


func _save_saved() -> void:
	var cfg: ConfigFile = ConfigFile.new()
	cfg.set_value("session", "room", room_code)
	cfg.set_value("session", "token", reconnect_token)
	cfg.set_value("session", "seats", seats)
	cfg.set_value("session", "phase", phase)
	if cfg.save(save_path) != OK:
		push_warning("Session: could not save %s" % save_path)


## The menu notice for a seat that cannot be taken back: only when a match was running (a saved
## lobby or a finished match that simply no longer exists is not worth a "session expired" line).
func lost_seat_notice() -> String:
	return "MENU_SESSION_EXPIRED" if Router.is_match_phase(phase) else ""


## Forget the room and the saved token (leave, token expired).
func clear_room() -> void:
	seat_id = -1
	room_code = ""
	reconnect_token = ""
	host_seat = -1
	phase = "lobby"
	seats = []
	my_tasks = []
	role = ""
	partner_seat = -1
	signature = null
	has_me_pos = false
	spawn_pending = false
	_reset_match_state()
	_clear_snaps()
	_save_saved()


## The saved count for this room and token, -1 when this device has none.
## Also -1 when a seat is out now that was not out when the count was saved: it went out while this
## client was away (a bot held the seat), so the count may be stale.
func _load_ejections() -> int:
	var cfg: ConfigFile = ConfigFile.new()
	if room_code.is_empty() or reconnect_token.is_empty() or cfg.load(ejections_path) != OK:
		return -1
	var key: String = room_code + ":" + reconnect_token
	var saved_out: Array = Net.as_array(cfg.get_value("out", key, []))
	for seat in out_seats:
		if not saved_out.has(int(seat)):
			return -1
	return int(cfg.get_value("ejections", key, -1))


## Saves the count and the out seats under <room>:<token>; other rooms' keys are dropped.
func _save_ejections() -> void:
	if room_code.is_empty() or reconnect_token.is_empty():
		return
	var cfg: ConfigFile = ConfigFile.new()
	cfg.load(ejections_path)
	var key: String = room_code + ":" + reconnect_token
	for section in ["ejections", "out"]:
		if cfg.has_section(section):
			for k in cfg.get_section_keys(section):
				if not k.begins_with(room_code + ":"):
					cfg.erase_section_key(section, k)
	cfg.set_value("ejections", key, ejected_polluters)
	var out: Array = out_seats.keys()
	out.sort()
	cfg.set_value("out", key, out)
	if cfg.save(ejections_path) != OK:
		push_warning("Session: could not save %s" % ejections_path)


## A seat seen going out live: remembered, so a later rejoin can tell it from a missed one.
func _mark_out(seat: int) -> void:
	if seat < 0 or out_seats.has(seat):
		return
	out_seats[seat] = true
	_save_ejections()


## A clue with a victim is public at meeting start: that seat is out.
func _mark_victims(clues: Array) -> void:
	for clue in clues:
		if typeof(clue) == TYPE_DICTIONARY and (clue as Dictionary).get("victim") != null:
			_mark_out(int((clue as Dictionary)["victim"]))


## meeting.living (seat ids alive when the meeting started), when the server sends it: every
## listed seat is alive, every other known seat is out. Kept as Array[int].
func _apply_living() -> void:
	if not meeting.has("living"):
		return
	var living: Array[int] = []
	for v in Net.as_array(meeting["living"]):
		living.append(int(v))
	meeting["living"] = living
	for s in seats:
		if typeof(s) == TYPE_DICTIONARY:
			var seat: int = int((s as Dictionary).get("seat", -1))
			if seat >= 0 and not living.has(seat):
				_mark_out(seat)


func _reset_match_state() -> void:
	ejected_polluters = 0
	poison = 0
	cooldowns = {}
	sabotage = {}
	emergencies_left = 0
	vision_tiles = 0.0
	smog_level = 0.0
	deadline_t = 0.0
	clock_t = 0.0
	piles = []
	is_out = false
	out_cause = ""
	out_seats = {}
	meeting = {}
	phrases_used = 0
	text_used = 0
	last_chat_t = -1.0e9
	ghost_chat = []
	last_game_over = {}
	spots = {}
	machine_jammed = false
	gas_room = ""
	gas_in_s = 0.0
	_read_private({})


func _mark_out_from_seats() -> void:
	out_seats = {}
	for s in seats:
		if typeof(s) == TYPE_DICTIONARY:
			var d: Dictionary = s
			if bool(d.get("out", false)):
				out_seats[int(d.get("seat", -1))] = true


# ---------------------------------------------------------------- Net handlers

func _on_welcome(data: Dictionary) -> void:
	move_seq = 0
	seat_id = int(data.get("seat", -1))
	room_code = str(data.get("room", ""))
	reconnect_token = str(data.get("token", ""))
	host_seat = int(data.get("host_seat", -1))
	map_id = str(data.get("map_id", ""))
	tuning = Net.as_dict(data.get("tuning"))
	speed_px_per_s = float(tuning.get("speed_px_per_s", Net.tiles_to_px(Net.DEFAULT_SPEED_TILES_PER_S)))
	phase = str(data.get("phase", "lobby"))
	map_version = str(data.get("map_version", ""))
	# The server sends the "tasks" array of content/tasks.json ([{type, steps, min_s, consoles}]);
	# the {"tasks": [...]} object shape is accepted too.
	task_types = {}
	var defs: Variant = data.get("tasks")
	if typeof(defs) == TYPE_DICTIONARY:
		defs = (defs as Dictionary).get("tasks")
	for def in Net.as_array(defs):
		if typeof(def) == TYPE_DICTIONARY:
			var dd: Dictionary = def
			task_types[str(dd.get("type", ""))] = dd
	my_tasks = []
	_reset_match_state()
	_clear_snaps()
	# Without welcome.seats keep the seats from the lobby or the saved file.
	if data.has("seats"):
		seats = Net.as_array(data.get("seats"))
	_mark_out_from_seats()
	# Only an out seat's welcome carries the ghost chat so far.
	ghost_chat = Net.as_array(data.get("ghost_chat")).duplicate()

	var st: Dictionary = Net.as_dict(data.get("state"))
	if not st.is_empty():
		phase = str(st.get("phase", "playing"))
		role = str(st.get("role", ""))
		var p: Variant = st.get("partner")
		partner_seat = int(p) if p != null else -1
		signature = st.get("signature")
		my_tasks = Net.as_array(st.get("tasks"))
		match_t = float(st.get("match_t", 0.0))
		clock_t = float(st.get("clock_t", 0.0))
		if deadline_t <= 0.0:
			deadline_t = float(Net.as_dict(tuning.get("match")).get("deadline_s", 0.0))
		has_me_pos = false
		is_out = str(st.get("status", "active")) == "out"
		if is_out and seat_id >= 0:
			out_seats[seat_id] = true
		poison = int(st.get("poison", 0)) if st.get("poison") != null else 0
		cooldowns = Net.as_dict(st.get("cooldowns")).duplicate()
		sabotage = Net.as_dict(st.get("sabotage")).duplicate(true)
		emergencies_left = int(st.get("emergencies_left", 0))
		_read_private(st)
		var mt: Dictionary = Net.as_dict(st.get("meeting")).duplicate()
		if not mt.is_empty():
			mt["ends_at"] = Net.local_time() + float(mt.get("ends_in", 0.0))
			mt.erase("ends_in")
			var voted: Array[int] = []
			for v in Net.as_array(mt.get("voted")):
				voted.append(int(v))
			mt["voted"] = voted
		meeting = mt
		last_game_over = Net.as_dict(st.get("game_over")).duplicate()
		ejected_polluters = _load_ejections()
		if not mt.is_empty():
			# The server may report how many phrases (my_chat) and typed lines (my_text) I sent
			# already and who is alive (living); without them a rejoin counts from 0 and trusts
			# what the client saw.
			phrases_used = int(mt.get("my_chat", 0))
			text_used = int(mt.get("my_text", 0))
			_mark_victims(Net.as_array(mt.get("clues")))
			_apply_living()
	_save_saved()


func _on_lobby(data: Dictionary) -> void:
	seats = Net.as_array(data.get("seats"))
	host_seat = int(data.get("host_seat", host_seat))
	_mark_out_from_seats()
	phase = "lobby"
	_save_saved()


func _on_match_started(data: Dictionary) -> void:
	move_seq = 0
	role = str(data.get("role", "citizen"))
	var p: Variant = data.get("partner")
	partner_seat = int(p) if p != null else -1
	signature = data.get("signature")
	my_tasks = Net.as_array(data.get("tasks"))
	spawn = data.get("spawn", Vector2.ZERO)
	spawn_pending = true
	has_me_pos = false
	meter = 0.0
	phase = "playing"
	_reset_match_state()
	_clear_snaps()
	_save_ejections()
	# Bots are named when the match starts, so this list is newer than the lobby's.
	if data.has("seats"):
		seats = Net.as_array(data.get("seats"))
		_mark_out_from_seats()
		_save_saved()


func _on_snapshot(data: Dictionary) -> void:
	phase = str(data.get("phase", phase))
	meter = float(data.get("meter", meter))
	match_t = float(data.get("match_t", match_t))
	deadline_t = float(data.get("deadline_t", deadline_t))
	clock_t = float(data.get("clock_t", clock_t))
	var me: Dictionary = Net.as_dict(data.get("me"))
	if me.has("pos"):
		me_pos = me["pos"]
		has_me_pos = true
	vision_tiles = float(me.get("vision", vision_tiles))
	smog_level = float(me.get("smog_level", 0.0))
	emergencies_left = int(me.get("emergencies_left", emergencies_left))
	if me.has("poison"):
		poison = int(me["poison"])
	if me.has("cd"):
		cooldowns = Net.as_dict(me["cd"]).duplicate()
	if data.has("me"):
		_read_private(me)
	if data.has("spots"):
		spots = {}
		for e in Net.as_array(data["spots"]):
			var spot: Dictionary = Net.as_dict(e)
			if spot.has("id"):
				spots[str(spot["id"])] = str(spot.get("state", "dirty"))
	machine_jammed = Net.as_dict(data.get("machine")).get("jammed") == true
	var gas: Dictionary = Net.as_dict(data.get("gas"))
	gas_room = str(gas.get("room", ""))
	gas_in_s = _num(gas.get("in_s"), 0.0)
	if me.has("status"):
		is_out = str(me["status"]) == "out"
		if is_out:
			_mark_out(seat_id)
	if data.has("piles"):
		piles = Net.as_array(data["piles"])
	elif is_out:
		piles = []
	sabotage = Net.as_dict(data.get("sabotage")).duplicate(true)
	push_snapshot(data)


## The v2 private fields of a snapshot's `me` or a rejoin's `welcome.state`. The server sends each only
## to the seats it belongs to, so a missing (or null) key means "not mine", never an error.
func _read_private(me: Dictionary) -> void:
	bags = int(_num(me.get("bags"), 0.0))
	bag_cap = int(_num(me.get("cap"), 0.0))
	has_tool = me.get("tool") == true
	heals = int(_num(me.get("heals"), 0.0))
	masks = int(_num(me.get("masks"), 0.0))
	infected_s = _num(me.get("infected_s"), -1.0)
	repollutes = int(_num(me.get("repollutes"), 0.0))
	ability = str(me["ability"]) if me.get("ability") is String else ""


static func _num(v: Variant, fallback: float) -> float:
	if typeof(v) == TYPE_INT or typeof(v) == TYPE_FLOAT:
		return float(v)
	return fallback


func _on_event(data: Dictionary) -> void:
	match str(data.get("t", "")):
		"task_progress":
			var task: Dictionary = task_by_id(int(data.get("task", -1)))
			if not task.is_empty():
				task["steps_done"] = int(data.get("steps_done", task.get("steps_done", 0)))
				var next: String = console_for_step(str(task.get("type", "")), int(task["steps_done"]))
				if next != "":
					task["console"] = next
		"task_added":
			var added: Dictionary = Net.as_dict(data.get("task"))
			if not added.is_empty():
				my_tasks.append(added)
		"poisoned":
			poison = int(data.get("level", poison))
		"hit_ok":
			cooldowns["hit"] = float(data.get("cooldown", 0.0))
		"dump_ok":
			cooldowns["dump"] = float(data.get("cooldown", 0.0))
		"you_are_out":
			is_out = true
			out_cause = str(data.get("cause", ""))
			_mark_out(seat_id)
		"sabotage_started":
			sabotage = {"kind": str(data.get("kind", "")), "held": []}
			if data.has("countdown"):
				sabotage["countdown"] = float(data["countdown"])
		"sabotage_ended":
			sabotage = {}
		"meeting_started":
			phase = "meeting"
			phrases_used = 0
			text_used = 0
			meeting = {
				"cause": str(data.get("cause", "")),
				"by": int(data.get("by", -1)),
				"clues": Net.as_array(data.get("clues")),
				"task_bar": float(data.get("task_bar", 0.0)),
				"stage": "discussion",
				"ends_at": Net.local_time() + float(data.get("discussion_s", 0.0)),
				"voted": [] as Array[int],
				"votes": {},
			}
			if data.has("living"):
				meeting["living"] = Net.as_array(data.get("living"))
			_mark_victims(meeting["clues"])
			_apply_living()
		"chat":
			_on_chat(data)
		"action_rejected":
			# The server counts the limits itself: a refusal for a limit closes that control only.
			var reason: String = str(data.get("reason", ""))
			if str(data.get("action", "")) == "chat_phrase" and reason == "phrase_limit":
				phrases_used = phrase_limit()
			elif str(data.get("action", "")) == "chat_text" and reason == "text_limit":
				text_used = text_limit()
		"voting_started":
			phase = "meeting"
			meeting["stage"] = "voting"
			meeting["ends_at"] = Net.local_time() + float(data.get("voting_s", 0.0))
		"vote_cast":
			var voted: Array[int] = meeting.get("voted", [] as Array[int])
			var who: int = int(data.get("seat", -1))
			if not voted.has(who):
				voted.append(who)
			meeting["voted"] = voted
		"meeting_result":
			phase = "meeting"
			meeting["stage"] = "results"
			meeting["votes"] = Net.as_dict(data.get("votes"))
		"ejection":
			phase = "ejection"
			var ejected: int = int(data.get("seat", -1))
			if ejected >= 0:
				out_seats[ejected] = true
				if ejected == seat_id:
					is_out = true
					out_cause = "vote"
			_count_ejection(data)
		"round_resumed":
			phase = "playing"
			meeting = {}
			spawn = data.get("spawn", Vector2.ZERO)
			spawn_pending = true
		"game_over":
			phase = "ended"
			last_game_over = data


## A chat line: a ghost line goes to the ghost list and counts for no meeting limit; one of mine
## starts the gap and takes a phrase or a typed line.
func _on_chat(data: Dictionary) -> void:
	var mine: bool = int(data.get("seat", -1)) == seat_id
	if bool(data.get("ghost", false)):
		# Only an out seat receives ghost lines; one that reaches a living seat is dropped, not kept.
		if is_out or seat_out(seat_id):
			ghost_chat.append(data.duplicate())
			if ghost_chat.size() > GHOST_KEEP:
				ghost_chat.pop_front()
	elif mine:
		if data.has("text"):
			text_used += 1
		else:
			phrases_used += 1
	if mine:
		last_chat_t = Net.local_time()


## A polluter voted out lowers the count; the server's own count, when it sends one, wins. An
## ejection that does not reveal the role makes the count unknown (-1), never a stale number.
func _count_ejection(data: Dictionary) -> void:
	if data.get("polluters_left") != null:
		ejected_polluters = maxi(_total_polluters() - int(data["polluters_left"]), 0)
	elif data.get("was_polluter") == null:
		ejected_polluters = -1
	elif data.get("was_polluter") == true and ejected_polluters >= 0:
		ejected_polluters += 1
	_save_ejections()


# ---------------------------------------------------------------- snapshot ring buffer

func _clear_snaps() -> void:
	_snaps.clear()
	_playback_started = false


## Stores the remote players of a snapshot (positions already in pixels). Own seat is skipped.
func push_snapshot(s: Dictionary) -> void:
	var snap: Snap = Snap.new()
	snap.t = float(s.get("s", 0.0))
	if not _snaps.is_empty() and snap.t <= _snaps[_snaps.size() - 1].t:
		return
	for p in Net.as_array(s.get("players")):
		if typeof(p) != TYPE_DICTIONARY:
			continue
		var d: Dictionary = p
		var seat: int = int(d.get("seat", -1))
		if bool(d.get("out", false)):
			_mark_out(seat)
		if seat == seat_id:
			continue
		snap.players[seat] = {
			"pos": d.get("pos", Vector2.ZERO),
			"color": int(d.get("color", 0)),
			"out": bool(d.get("out", false)),
		}
	_snaps.append(snap)
	if _snaps.size() > BUFFER_SIZE:
		_snaps.pop_front()


## Moves the playback clock by the render time that passed since the last call, scaled by how many
## snapshots are still ahead of it: a thin buffer plays slower (tuning.net.slow_rate), a deep one
## faster (net.fast_rate), so jitter is absorbed instead of showing as stutter. It stays within
## net.max_lag_s of the render time it is given.
func _advance_playback(render_t: float) -> void:
	var gap: float = render_t - _playback_input_t
	if not _playback_started or absf(gap) > PLAYBACK_RESET_GAP_S:
		playback_t = render_t
	elif gap > 0.0:
		var net: Dictionary = Net.as_dict(tuning.get("net"))
		var target: int = int(net.get("target_depth", DEFAULT_TARGET_DEPTH))
		var depth: int = 0
		for snap in _snaps:
			if snap.t > playback_t:
				depth += 1
		var rate: float = 1.0
		if depth < target:
			rate = float(net.get("slow_rate", DEFAULT_SLOW_RATE))
		elif depth > target + 2:
			rate = float(net.get("fast_rate", DEFAULT_FAST_RATE))
		var max_lag: float = float(net.get("max_lag_s", DEFAULT_MAX_LAG_S))
		playback_t = clampf(playback_t + gap * rate, render_t - max_lag, render_t + max_lag)
	_playback_input_t = render_t
	_playback_started = true


## Remote players for render_t (server time, already minus the 100 ms delay), played back through
## the adaptive clock above (playback_t).
## Returns seat -> {"pos": Vector2, "color": int, "out": bool}. Seats missing from the snapshots
## around playback_t are not returned (hidden). Extrapolates at most MAX_EXTRAPOLATION_S.
func sample(render_t: float) -> Dictionary:
	var out: Dictionary = {}
	var n: int = _snaps.size()
	if n == 0:
		_playback_started = false
		return out
	_advance_playback(render_t)
	render_t = playback_t

	var newest: Snap = _snaps[n - 1]
	if render_t >= newest.t:
		var over: float = render_t - newest.t
		if over > STALE_HIDE_S:
			return out
		var prev: Snap = _snaps[n - 2] if n >= 2 else null
		for seat in newest.players:
			var cur: Dictionary = newest.players[seat]
			var pos: Vector2 = cur["pos"]
			if prev != null and prev.players.has(seat) and newest.t > prev.t:
				var before: Dictionary = prev.players[seat]
				var vel: Vector2 = (pos - (before["pos"] as Vector2)) / (newest.t - prev.t)
				pos += vel * minf(over, MAX_EXTRAPOLATION_S)
			out[seat] = {"pos": pos, "color": cur["color"], "out": cur["out"]}
		return out

	var a: Snap = _snaps[0]
	var b: Snap = _snaps[0]
	for i in range(n - 1):
		if render_t < _snaps[i + 1].t:
			a = _snaps[i]
			b = _snaps[i + 1]
			break

	if render_t <= a.t or b.t <= a.t:
		for seat in a.players:
			var held: Dictionary = a.players[seat]
			out[seat] = {"pos": held["pos"], "color": held["color"], "out": held["out"]}
		return out

	var alpha: float = clampf((render_t - a.t) / (b.t - a.t), 0.0, 1.0)
	for seat in a.players:
		var pa: Dictionary = a.players[seat]
		if b.players.has(seat):
			var pb: Dictionary = b.players[seat]
			var p0: Vector2 = pa["pos"]
			var p1: Vector2 = pb["pos"]
			out[seat] = {"pos": p0.lerp(p1, alpha), "color": pb["color"], "out": pb["out"]}
		else:
			# Left sight in b: hold the last visible position until b is reached.
			out[seat] = {"pos": pa["pos"], "color": pa["color"], "out": pa["out"]}
	# Seats only in b (newly visible) appear once render time reaches b.
	return out
