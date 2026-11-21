extends "res://tests/test_case.gd"
## A really dead link is noticed and goes through the normal rejoin path (disconnected ->
## reconnecting scene -> token -> welcome with state). A healthy link is never dropped: not after a
## focus change, not after a frame gap, and in the lobby (where the server frees the seat at once)
## only a socket that reports itself closed counts. The drop logic is driven through Net._process
## with a fake socket and clock.

const NetScript := preload("res://autoload/net.gd")
const T0: float = 1000.0
const FRAME_S: float = 0.016

var _disconnects: int = 0
var _phase_before: String = ""


## Net without a real socket: the clock, the socket state and the inbound packets are ours.
class FakeNet extends "res://autoload/net.gd":
	var now: float = 1000.0
	var closed: bool = false
	var inbound: Array[String] = []
	var sent: Array[String] = []

	func local_time() -> float:
		return now

	func _poll_socket() -> void:
		pass

	func _socket_state() -> int:
		return WebSocketPeer.STATE_CLOSED if closed else WebSocketPeer.STATE_OPEN

	func _pending_texts() -> Array[String]:
		var out: Array[String] = inbound.duplicate()
		inbound.clear()
		return out

	func send(type: String, _fields: Dictionary = {}) -> void:
		sent.append(type)


func _on_disconnected() -> void:
	_disconnects += 1


func _fake(phase: String) -> FakeNet:
	var n: FakeNet = track(FakeNet.new()) as FakeNet
	n.state = n.State.OPEN
	n._was_open = true
	n._last_rx = T0
	n._last_frame = T0
	n.disconnected.connect(_on_disconnected)
	_disconnects = 0
	if _phase_before.is_empty():
		_phase_before = Session.phase
	Session.phase = phase
	return n


func _done() -> void:
	Session.phase = _phase_before
	_phase_before = ""


## Runs `seconds` of frames, `FRAME_S` apart.
func _run(n: FakeNet, seconds: float) -> void:
	var steps: int = int(seconds / FRAME_S)
	for i in steps:
		n.now += FRAME_S
		n._process(FRAME_S)


func test_silence_over_the_limit_in_a_match_drops_the_link_once() -> bool:
	var n: FakeNet = _fake("playing")
	_run(n, n.silence_limit_s() - 1.0)
	assert_eq(_disconnects, 0, "still within the limit")
	_run(n, 2.0 + 1.0)
	assert_eq(_disconnects, 1, "nothing at all for longer than the limit: the scenes go to reconnecting")
	assert_eq(n.state, n.State.CLOSED, "closed")
	_done()
	return true


func test_a_pong_in_time_keeps_the_link() -> bool:
	var n: FakeNet = _fake("playing")
	var limit: float = n.silence_limit_s()
	for i in 4:
		_run(n, limit - 1.0)
		n.inbound.append('{"t":"pong","c":0.0,"s":1.0}')
		_run(n, FRAME_S * 2.0)
	assert_eq(_disconnects, 0, "a pong every limit-1 seconds is enough")
	_done()
	return true


func test_long_absence_mid_match_resyncs_once() -> bool:
	for phase in ["intro", "playing", "meeting", "ejection", "ended"]:
		var by_focus: FakeNet = _fake(phase)
		by_focus._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_OUT)
		by_focus.now += by_focus.silence_limit_s() + 5.0
		by_focus.inbound.append('{"t":"pong","c":0.0,"s":1.0}')
		by_focus._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_IN)
		by_focus._process(FRAME_S)
		assert_eq(_disconnects, 1, "%s: back after a long absence: rejoin for the full state" % phase)
		var by_gap: FakeNet = _fake(phase)
		by_gap.now += by_gap.silence_limit_s() + 5.0
		by_gap._process(FRAME_S)
		by_gap._process(FRAME_S)
		assert_eq(_disconnects, 1, "%s: a frozen tab (frame gap) resyncs once, not twice" % phase)
	_done()
	return true


func test_short_absence_mid_match_keeps_the_link() -> bool:
	var n: FakeNet = _fake("playing")
	n._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_OUT)
	n.now += n.silence_limit_s() - 2.0
	n.inbound.append('{"t":"pong","c":0.0,"s":1.0}')
	n._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_IN)
	n._process(FRAME_S)
	assert_eq(_disconnects, 0, "a quick look away is not a resync")
	n.now += n.silence_limit_s() - 2.0
	n.inbound.append('{"t":"pong","c":0.0,"s":1.0}')
	n._process(FRAME_S)
	assert_eq(_disconnects, 0, "nor is a short frame gap")
	assert_eq(n.state, n.State.OPEN, "still open")
	_done()
	return true


func test_long_absence_in_the_lobby_keeps_the_seat() -> bool:
	var n: FakeNet = _fake("lobby")
	n._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_OUT)
	n.now += 60.0
	n._notification(MainLoop.NOTIFICATION_APPLICATION_FOCUS_IN)
	n._process(FRAME_S)
	assert_eq(_disconnects, 0, "a host who left for a minute keeps the seat")
	assert_true(n.sent.has("ping"), "it asks for a pong instead")
	n.now += 60.0
	n._process(FRAME_S)
	assert_eq(_disconnects, 0, "a long frame gap in the lobby too")
	_done()
	return true


func test_the_lobby_keeps_the_seat_unless_the_socket_reports_closed() -> bool:
	var n: FakeNet = _fake("lobby")
	n.now += 60.0
	_run(n, n.silence_limit_s() + 5.0)
	assert_eq(_disconnects, 0, "silence in the lobby is not a drop: the server would free the seat")
	n.closed = true
	n._process(FRAME_S)
	assert_eq(_disconnects, 1, "a socket that really closed still goes to reconnecting")
	_done()
	return true


func test_silence_limit_comes_from_tuning_with_a_fallback() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	var saved: Dictionary = Session.tuning
	Session.tuning = {}
	assert_near(n.silence_limit_s(), n.SILENCE_FALLBACK_S, 0.001, "fallback without the key")
	assert_true(n.silence_limit_s() > n.PING_INTERVAL_S, "longer than the ping interval, or a quiet phase would flap")
	Session.tuning = {"net": {"silence_s": 9.0}}
	assert_near(n.silence_limit_s(), 9.0, 0.001, "tuning wins")
	Session.tuning = saved
	return true


func test_dropping_the_link_reports_a_disconnect_once() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.state = n.State.OPEN
	n._was_open = true
	n.disconnected.connect(_on_disconnected)
	_disconnects = 0
	n.drop_link()
	assert_eq(n.state, n.State.CLOSED, "closed")
	assert_eq(_disconnects, 1, "the scenes hear it and route to reconnecting")
	n.drop_link()
	assert_eq(_disconnects, 1, "already closed: nothing more")
	return true


func test_dropping_the_link_during_a_reconnect_retries_instead() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.state = n.State.OPEN
	n._was_open = true
	n.disconnected.connect(_on_disconnected)
	_disconnects = 0
	n._reconnect_active = true
	n.drop_link()
	assert_eq(_disconnects, 0, "the reconnecting scene is already showing")
	assert_true(n._reconnect_waiting, "the next attempt is scheduled")
	return true


func test_cancelling_a_reconnect_stops_the_retry_loop() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.disconnected.connect(_on_disconnected)
	_disconnects = 0
	n.start_reconnect()
	assert_true(n._reconnect_waiting, "retrying")
	n.cancel_reconnect()
	assert_true(not n._reconnect_active and not n._reconnect_waiting, "no more attempts")
	assert_eq(n.state, n.State.CLOSED, "socket closed")
	assert_eq(_disconnects, 0, "giving up is not a drop")
	return true
