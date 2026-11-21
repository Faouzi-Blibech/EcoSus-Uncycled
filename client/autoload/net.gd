extends Node
## The only script that touches the WebSocket. Speaks the JSON protocol (docs/protocol.md),
## converts tiles <-> pixels, keeps ping and server clock offset, and runs the reconnect loop.
## Scenes listen to the signals below and read Session; they never open sockets.

signal connected()
signal connect_failed()
signal disconnected()
signal reconnecting(attempt: int)
signal reconnect_failed()
signal hello_ok(data: Dictionary)
signal error(data: Dictionary)
signal welcome(data: Dictionary)
signal join_failed(data: Dictionary)
signal lobby(data: Dictionary)
signal match_started(data: Dictionary)
signal correct(data: Dictionary)
signal snapshot(data: Dictionary)
## In-match messages, one signal per message type, named exactly like `t` (docs/protocol.md).
signal use_ok(data: Dictionary)
signal task_progress(data: Dictionary)
signal task_added(data: Dictionary)
signal cue(data: Dictionary)
signal hit_ok(data: Dictionary)
signal dump_ok(data: Dictionary)
signal poisoned(data: Dictionary)
signal you_are_out(data: Dictionary)
signal action_rejected(data: Dictionary)
signal phase_changed(data: Dictionary)
signal meeting_started(data: Dictionary)
signal chat(data: Dictionary)
signal voting_started(data: Dictionary)
signal vote_cast(data: Dictionary)
signal meeting_result(data: Dictionary)
signal ejection(data: Dictionary)
signal round_resumed(data: Dictionary)
signal game_over(data: Dictionary)
signal clean_ok(data: Dictionary)
signal clean_done(data: Dictionary)
signal clean_cancelled(data: Dictionary)
signal sabotage_started(data: Dictionary)
signal sabotage_ended(data: Dictionary)
## Every message that is not handled above, and also every typed one (existing listeners).
## The envelope key is data["t"].
signal event(data: Dictionary)

## In-match message types that also get their own typed signal (same name).
const TYPED_MESSAGES: Array[String] = [
	"use_ok",
	"task_progress",
	"task_added",
	"cue",
	"hit_ok",
	"dump_ok",
	"poisoned",
	"you_are_out",
	"action_rejected",
	"phase_changed",
	"meeting_started",
	"chat",
	"voting_started",
	"vote_cast",
	"meeting_result",
	"ejection",
	"round_resumed",
	"game_over",
	"clean_ok",
	"clean_done",
	"clean_cancelled",
	"sabotage_started",
	"sabotage_ended",
]

const PROTOCOL_VERSION: int = 2
const TILE_PX: float = 32.0
const DEFAULT_SPEED_TILES_PER_S: float = 2.5
const CONNECT_TIMEOUT_S: float = 6.0
const PING_INTERVAL_S: float = 2.0
const RECONNECT_BACKOFF_S: Array[float] = [0.5, 1.0, 2.0, 4.0]
const RECONNECT_TOTAL_S: float = 30.0
## Fallback for Session.tuning["net"]["silence_s"]: with no packet (snapshot or pong) for this long, or no
## frame for this long (background tab), the link is dropped and rejoined. Above PING_INTERVAL_S.
const SILENCE_FALLBACK_S: float = 6.0

enum State { CLOSED, CONNECTING, OPEN }

var state: State = State.CLOSED
var ping_ms: int = 0
## Seconds to add to local_time() to get server time (snapshot based, pong based until then).
var server_offset: float:
	get:
		return _offset_snap if _have_snap else _offset_pong

## A welcome (tuning, tasks, seats, ghost chat history) can be larger than the 64 KB default; the
## messages this client sends are small (a chat line is a few hundred bytes), so the outbound default is fine.
const INBOUND_BUFFER_BYTES: int = 1 << 20

var _ws: WebSocketPeer = _new_socket()
var _was_open: bool = false
var _connect_started: float = 0.0
var _ping_timer: float = 0.0
var _focus_out_at: float = 0.0
var _away: bool = false
var _last_rx: float = 0.0
var _last_frame: float = 0.0

var _offset_pong: float = 0.0
var _have_pong: bool = false
var _offset_snap: float = 0.0
var _have_snap: bool = false

var _reconnect_active: bool = false
var _reconnect_waiting: bool = false
var _reconnect_wait: float = 0.0
var _reconnect_attempt: int = 0
var _reconnect_elapsed: float = 0.0


# ---------------------------------------------------------------- public API

func local_time() -> float:
	return float(Time.get_ticks_msec()) / 1000.0


func server_time() -> float:
	return local_time() + server_offset


func tiles_to_px(tiles: float) -> float:
	return tiles * TILE_PX


func px_to_tiles(px: float) -> float:
	return px / TILE_PX


func as_dict(v: Variant) -> Dictionary:
	if typeof(v) == TYPE_DICTIONARY:
		return v
	return {}


func as_array(v: Variant) -> Array:
	if typeof(v) == TYPE_ARRAY:
		return v
	return []


## Converts a protocol [x, y] (tiles) to a Vector2 in pixels.
func to_px(v: Variant) -> Vector2:
	if typeof(v) == TYPE_ARRAY:
		var a: Array = v
		if a.size() >= 2:
			return Vector2(float(a[0]), float(a[1])) * TILE_PX
	return Vector2.ZERO


static func _new_socket() -> WebSocketPeer:
	var ws: WebSocketPeer = WebSocketPeer.new()
	ws.inbound_buffer_size = INBOUND_BUFFER_BYTES
	return ws


func connect_to(url: String) -> void:
	close_socket()
	_have_pong = false
	_have_snap = false
	_ws = _new_socket()
	var err: int = _ws.connect_to_url(url)
	if err != OK:
		push_warning("Net: connect_to_url(%s) failed with error %d" % [url, err])
		_handle_closed.call_deferred()
		return
	state = State.CONNECTING
	_was_open = false
	_connect_started = local_time()


## Drops the socket without emitting disconnected.
func close_socket() -> void:
	if state != State.CLOSED:
		_ws.close()
	state = State.CLOSED
	_was_open = false


## Seconds without a packet or a frame after which the link is treated as dead.
func silence_limit_s() -> float:
	return float(as_dict(Session.tuning.get("net")).get("silence_s", SILENCE_FALLBACK_S))


## True when nothing was received for longer than the limit. Never in the lobby: the server frees
## the seat the moment the socket closes there, so a drop would cost the player the seat and the
## host role; only a socket that reports itself closed counts in the lobby.
static func link_is_dead(silent_s: float, limit_s: float, phase: String) -> bool:
	return phase != "lobby" and silent_s > limit_s


## True when the app was away longer than the limit outside the lobby: events may have been lost
## (a frozen tab keeps the socket but its buffer can overflow), so the state is taken again through
## the rejoin path. In the lobby the server frees the seat on close, so there the answer is no.
static func absence_needs_resync(absence_s: float, limit_s: float, phase: String) -> bool:
	return phase != "lobby" and absence_s > limit_s


## Frames stopped (background tab) or the window came back: what the link did meanwhile is unknown,
## so the silence clock starts again and a ping asks for a pong right now. The link is dropped only
## if nothing arrives within the limit after that.
func _rearm_silence_clock() -> void:
	_last_rx = local_time()
	_ping_timer = PING_INTERVAL_S


# The three socket reads below are seams: tests replace them with a fake socket.
func _poll_socket() -> void:
	_ws.poll()


func _socket_state() -> int:
	return _ws.get_ready_state()


## Every text packet that arrived since the last call.
func _pending_texts() -> Array[String]:
	var out: Array[String] = []
	while _ws.get_available_packet_count() > 0:
		out.append(_ws.get_packet().get_string_from_utf8())
	return out


## Closes a dead or stale socket. The scenes hear `disconnected` and go to the reconnecting screen,
## which rejoins with the token (welcome carries the full state); during a reconnect it just retries.
func drop_link() -> void:
	if state == State.CLOSED:
		return
	_ws.close()
	_handle_closed()


func send(type: String, fields: Dictionary = {}) -> void:
	if state != State.OPEN:
		return
	var msg: Dictionary = fields.duplicate()
	msg["t"] = type
	_ws.send_text(JSON.stringify(msg))


## pos_px is in pixels; the wire format is tiles.
func send_move(pos_px: Vector2, seq: int) -> void:
	var tx: float = snappedf(px_to_tiles(pos_px.x), 0.01)
	var ty: float = snappedf(px_to_tiles(pos_px.y), 0.01)
	send("move", {"pos": [tx, ty], "seq": seq})


func send_use(console_id: String) -> void:
	send("use", {"console": console_id})


func send_pick_upgrade(item: String) -> void:
	send("pick_upgrade", {"item": item})


func send_spot_done(spot_id: String) -> void:
	send("spot_done", {"spot": spot_id})


func send_task_step(task_id: int) -> void:
	send("task_step", {"task": task_id})


func send_repollute(spot_id: String) -> void:
	send("repollute", {"spot": spot_id})


func send_plant_virus(spot_id: String) -> void:
	send("plant_virus", {"spot": spot_id})


func send_gas(room_id: String) -> void:
	send("gas", {"room": room_id})


func send_heal(target: int) -> void:
	send("heal", {"target": target})


func send_pick_ability(ability: String) -> void:
	send("pick_ability", {"ability": ability})


func send_report(pile_id: int) -> void:
	send("report", {"pile": pile_id})


func send_call_meeting() -> void:
	send("call_meeting")


func send_clean_start(pile_id: int) -> void:
	send("clean_start", {"pile": pile_id})


func send_clean_cancel(pile_id: int) -> void:
	send("clean_cancel", {"pile": pile_id})


## kind is "fire" or "smog".
func send_sabotage(kind: String) -> void:
	send("sabotage", {"kind": kind})


## Fire consoles: holding true/false. The smog panel ignores `holding`.
func send_fix(console_id: String, holding: bool) -> void:
	send("fix", {"console": console_id, "holding": holding})


## target -1 skips.
func send_vote(target: int) -> void:
	if target < 0:
		send("vote", {"skip": true})
	else:
		send("vote", {"target": target})


## One catalogue phrase. args: seat ids (ints, the server reads whole numbers) and zone ids (strings).
func send_chat_phrase(id: String, args: Array) -> void:
	var wire: Array = []
	for a in args:
		wire.append(a if typeof(a) == TYPE_STRING else int(a))
	send("chat_phrase", {"id": id, "args": wire})


## One typed chat line; the server checks the length, the limits and the gap and filters words.
func send_chat_text(text: String) -> void:
	send("chat_text", {"text": text})


## Starts the 0.5, 1, 2, 4 s backoff loop (30 s total). Emits reconnecting(attempt) per try,
## then welcome on success or reconnect_failed.
func start_reconnect() -> void:
	_reconnect_active = true
	_reconnect_attempt = 0
	_reconnect_elapsed = 0.0
	if state == State.CLOSED:
		_schedule_reconnect_attempt()
	else:
		# Socket still up (rare): just try to rejoin on it.
		_reconnect_waiting = false
		if state == State.OPEN:
			_send_rejoin()


## Stops the retry loop and closes the socket (the player gave up); nothing is emitted.
func cancel_reconnect() -> void:
	_reconnect_active = false
	_reconnect_waiting = false
	close_socket()


# ---------------------------------------------------------------- frame loop

func _process(delta: float) -> void:
	if _reconnect_active:
		_reconnect_elapsed += delta
		if _reconnect_elapsed >= RECONNECT_TOTAL_S:
			_fail_reconnect(true)
			return
		if _reconnect_waiting:
			_reconnect_wait -= delta
			if _reconnect_wait <= 0.0:
				_reconnect_waiting = false
				_reconnect_attempt += 1
				reconnecting.emit(_reconnect_attempt)
				connect_to(Settings.server_url)

	if state == State.CLOSED:
		return

	_poll_socket()
	var rs: int = _socket_state()

	if state == State.CONNECTING:
		if rs == WebSocketPeer.STATE_OPEN:
			_on_open()
		elif rs == WebSocketPeer.STATE_CLOSED:
			_handle_closed()
		elif local_time() - _connect_started > CONNECT_TIMEOUT_S:
			_ws.close()
			_handle_closed()
		return

	# state == OPEN
	var now: float = local_time()
	var frame_gap: float = now - _last_frame
	_last_frame = now
	if absence_needs_resync(frame_gap, silence_limit_s(), Session.phase):
		drop_link()
		return
	if frame_gap > silence_limit_s():
		_rearm_silence_clock()
	for text in _pending_texts():
		if state != State.OPEN:
			break
		_last_rx = now
		_handle_text(text)
	if state != State.OPEN:
		return
	if _socket_state() == WebSocketPeer.STATE_CLOSED:
		_handle_closed()
		return
	if link_is_dead(now - _last_rx, silence_limit_s(), Session.phase):
		drop_link()
		return
	_ping_timer += delta
	if _ping_timer >= PING_INTERVAL_S:
		_ping_timer = 0.0
		send("ping", {"c": local_time()})


func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		_focus_out_at = local_time()
		_away = true
	elif what == NOTIFICATION_APPLICATION_FOCUS_IN and state == State.OPEN:
		var absence: float = local_time() - _focus_out_at if _away else 0.0
		_away = false
		if absence_needs_resync(absence, silence_limit_s(), Session.phase):
			drop_link()
		else:
			_rearm_silence_clock()


# ---------------------------------------------------------------- socket events

func _on_open() -> void:
	state = State.OPEN
	_was_open = true
	_ping_timer = 0.0
	_last_rx = local_time()
	_last_frame = _last_rx
	send("hello", {"proto": PROTOCOL_VERSION, "client": _client_name(), "build": _build()})
	connected.emit()


func _handle_closed() -> void:
	var was_open: bool = _was_open
	state = State.CLOSED
	_was_open = false
	if _reconnect_active:
		_schedule_reconnect_attempt()
	elif was_open:
		disconnected.emit()
	else:
		connect_failed.emit()


func _schedule_reconnect_attempt() -> void:
	_reconnect_waiting = true
	var idx: int = mini(_reconnect_attempt, RECONNECT_BACKOFF_S.size() - 1)
	_reconnect_wait = RECONNECT_BACKOFF_S[idx]


func _fail_reconnect(close_it: bool) -> void:
	_reconnect_active = false
	_reconnect_waiting = false
	if close_it:
		close_socket()
	reconnect_failed.emit()


func _send_rejoin() -> void:
	send("join", {
		"code": Session.room_code,
		"name": Settings.player_name,
		"token": Session.reconnect_token,
	})


func _client_name() -> String:
	if OS.has_feature("web"):
		return "web"
	if OS.has_feature("android"):
		return "android"
	return "windows"


func _build() -> String:
	return str(ProjectSettings.get_setting("application/config/version", "dev"))


# ---------------------------------------------------------------- message handling

func _handle_text(text: String) -> void:
	var parsed: Variant = JSON.parse_string(text)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("Net: ignoring non-object message")
		return
	var msg: Dictionary = parsed
	var t: String = str(msg.get("t", ""))
	match t:
		"hello_ok":
			if _reconnect_active:
				_send_rejoin()
			hello_ok.emit(msg)
		"error":
			if _reconnect_active:
				_fail_reconnect(false)
			error.emit(msg)
		"pong":
			_on_pong(msg)
		"welcome":
			_on_welcome(msg)
		"join_failed":
			if _reconnect_active:
				_fail_reconnect(false)
			else:
				join_failed.emit(msg)
		"lobby":
			lobby.emit(msg)
		"match_started":
			msg["spawn"] = to_px(msg.get("spawn"))
			match_started.emit(msg)
		"correct":
			msg["pos"] = to_px(msg.get("pos"))
			correct.emit(msg)
		"snapshot":
			_on_snapshot(msg)
		_:
			if msg.has("spawn"):
				msg["spawn"] = to_px(msg["spawn"])
			if msg.has("pos"):
				msg["pos"] = to_px(msg["pos"])
			if TYPED_MESSAGES.has(t):
				emit_signal(t, msg)
			event.emit(msg)


func _on_welcome(msg: Dictionary) -> void:
	_reconnect_active = false
	_reconnect_waiting = false
	var tuning: Dictionary = as_dict(msg.get("tuning"))
	var speed_tiles: float = float(tuning.get("speed_tiles_per_s", DEFAULT_SPEED_TILES_PER_S))
	tuning["speed_px_per_s"] = tiles_to_px(speed_tiles)
	msg["tuning"] = tuning
	welcome.emit(msg)


func _on_pong(msg: Dictionary) -> void:
	var c: float = float(msg.get("c", 0.0))
	var s: float = float(msg.get("s", 0.0))
	var rtt: float = maxf(local_time() - c, 0.0)
	ping_ms = roundi(rtt * 1000.0)
	var sample: float = s + rtt * 0.5 - local_time()
	if not _have_pong:
		_offset_pong = sample
		_have_pong = true
	else:
		_offset_pong = lerpf(_offset_pong, sample, 0.2)


func _on_snapshot(msg: Dictionary) -> void:
	var me: Dictionary = as_dict(msg.get("me"))
	if me.has("pos"):
		me["pos"] = to_px(me["pos"])
	for p in as_array(msg.get("players")):
		if typeof(p) == TYPE_DICTIONARY:
			var pd: Dictionary = p
			pd["pos"] = to_px(pd.get("pos"))
	for p in as_array(msg.get("piles")):
		if typeof(p) == TYPE_DICTIONARY:
			var pd: Dictionary = p
			pd["pos"] = to_px(pd.get("pos"))
	if msg.has("s"):
		var sample: float = float(msg["s"]) - local_time()
		if not _have_snap or absf(sample - _offset_snap) > 1.0:
			_offset_snap = sample
			_have_snap = true
		else:
			_offset_snap = lerpf(_offset_snap, sample, 0.1)
	snapshot.emit(msg)
