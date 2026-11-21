class_name ProbeClient
extends RefCounted
## Blocking test client for the server's WebSocket protocol. Not Net: `--script` runs have no
## autoloads. Every call polls the socket itself, so no scene tree or frames are needed.

## The wire protocol version of the hello (Net.PROTOCOL_VERSION; a test keeps the two equal).
const PROTOCOL_VERSION: int = 1
const MOVE_HZ: float = 10.0
const MAX_SPEED_TILES_PER_S: float = 2.4
const ARRIVE_EPS: float = 0.01

## Last known position in tiles; the scenario sets it from `match_started.spawn`.
var pos: Vector2 = Vector2.ZERO
var seq: int = 0
## Clients that poll() also polls, so their inbound buffer never fills while this one waits.
var companions: Array[ProbeClient] = []
## Time.get_ticks_msec() after which every call here returns at once (0 = no deadline).
var deadline_ms: int = 0
var _ws: WebSocketPeer = _new_socket()
var _by_type: Dictionary = {}
var _cursor: Dictionary = {}


## A welcome can be larger than the 64 KB default (same size as Net).
static func _new_socket() -> WebSocketPeer:
	var ws: WebSocketPeer = WebSocketPeer.new()
	ws.inbound_buffer_size = 1 << 20
	return ws


## Opens the socket, waits for it to be open and sends `hello`. False on timeout or failure.
func connect_to(url: String) -> bool:
	_ws = _new_socket()
	if _ws.connect_to_url(url) != OK:
		return false
	var deadline: int = _limit(Time.get_ticks_msec() + 6000)
	while Time.get_ticks_msec() < deadline:
		_ws.poll()
		var s: int = _ws.get_ready_state()
		if s == WebSocketPeer.STATE_OPEN:
			send("hello", {"proto": PROTOCOL_VERSION, "client": "windows", "build": "probe"})
			return true
		if s == WebSocketPeer.STATE_CLOSED:
			return false
		OS.delay_msec(10)
	return false


func is_open() -> bool:
	return _ws.get_ready_state() == WebSocketPeer.STATE_OPEN


func expired() -> bool:
	return deadline_ms > 0 and Time.get_ticks_msec() >= deadline_ms


func _limit(ms: int) -> int:
	return mini(ms, deadline_ms) if deadline_ms > 0 else ms


## Reads everything that arrived (also for the companions). Call it often; the other methods do.
func poll() -> void:
	for c in companions:
		c.poll()
	_ws.poll()
	while _ws.get_available_packet_count() > 0:
		var text: String = _ws.get_packet().get_string_from_utf8()
		var parsed: Variant = JSON.parse_string(text)
		if typeof(parsed) != TYPE_DICTIONARY:
			continue
		var msg: Dictionary = parsed
		var t: String = String(msg.get("t", ""))
		if not _by_type.has(t):
			_by_type[t] = []
		_by_type[t].append(msg)


func send(t: String, fields: Dictionary = {}) -> void:
	if not is_open():
		return
	var msg: Dictionary = fields.duplicate()
	msg["t"] = t
	_ws.send_text(JSON.stringify(msg))


## The oldest message of type `t` not returned by an earlier wait_for; empty on timeout.
func wait_for(t: String, timeout_s: float) -> Dictionary:
	var deadline: int = _limit(Time.get_ticks_msec() + int(timeout_s * 1000.0))
	while true:
		poll()
		var list: Array = _by_type.get(t, [])
		var i: int = int(_cursor.get(t, 0))
		if i < list.size():
			_cursor[t] = i + 1
			return list[i]
		if Time.get_ticks_msec() >= deadline or _ws.get_ready_state() == WebSocketPeer.STATE_CLOSED:
			break
		OS.delay_msec(5)
	return {}


## Every message of type `t` received so far (a copy).
func received(t: String) -> Array:
	return (_by_type.get(t, []) as Array).duplicate()


## Skips the wait_for cursor of `t` past everything received so far.
func skip_received(t: String) -> void:
	_cursor[t] = (_by_type.get(t, []) as Array).size()


## Polls for `seconds` without waiting for anything in particular.
func idle(seconds: float) -> void:
	var deadline: int = _limit(Time.get_ticks_msec() + int(seconds * 1000.0))
	while Time.get_ticks_msec() < deadline:
		poll()
		OS.delay_msec(5)


## Walks from `pos` to `target_tiles` along the shortest grid route (GridPath.path) at MOVE_HZ,
## at most MAX_SPEED_TILES_PER_S, sending `move`. Each move advances MAX_SPEED_TILES_PER_S /
## MOVE_HZ tiles along the route polyline, carrying the rest of the step past a waypoint (never
## cutting a corner), and two moves are never closer than 1 / MOVE_HZ, even after a stall.
## It ends when the route ends. False when there is no route, the server sends a `correct`,
## the socket closes or the deadline passes. On true, `pos` is `target_tiles`.
func walk_to(target_tiles: Vector2, grid: GridPath) -> bool:
	var route: Array[Vector2] = grid.path(pos, target_tiles)
	if route.is_empty():
		return false
	poll()
	var corrections: int = received("correct").size()
	var period_ms: int = int(1000.0 / MOVE_HZ)
	var next_ms: int = Time.get_ticks_msec()
	var next_wp: int = 0
	while next_wp < route.size():
		var before: Vector2 = pos
		next_wp = advance_along(route, next_wp)
		if pos.distance_to(before) <= ARRIVE_EPS:
			continue
		while Time.get_ticks_msec() < next_ms:
			poll()
			OS.delay_msec(2)
		if expired() or not is_open():
			return false
		seq += 1
		send("move", {"pos": [snappedf(pos.x, 0.001), snappedf(pos.y, 0.001)], "seq": seq})
		next_ms = maxi(next_ms, Time.get_ticks_msec()) + period_ms
		poll()
		if received("correct").size() > corrections:
			return false
	return true


## Moves `pos` one step (MAX_SPEED_TILES_PER_S / MOVE_HZ tiles) along `route` from waypoint
## `from_wp`, the leftover of a step carried to the next waypoint. Returns the next waypoint index.
func advance_along(route: Array[Vector2], from_wp: int) -> int:
	var left: float = MAX_SPEED_TILES_PER_S / MOVE_HZ
	var i: int = from_wp
	while i < route.size() and left > 0.0:
		var d: float = pos.distance_to(route[i])
		if d <= left:
			pos = route[i]
			left -= d
			i += 1
		else:
			pos += (route[i] - pos) / d * left
			left = 0.0
	return i


func close() -> void:
	if is_open():
		_ws.close()
