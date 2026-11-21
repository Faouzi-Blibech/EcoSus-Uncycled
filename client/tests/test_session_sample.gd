extends "res://tests/test_case.gd"


func _session_with(snaps: Array) -> Object:
	var s: Object = track(load("res://autoload/session.gd").new())
	for snap in snaps:
		s.push_snapshot(snap)
	return s


func _snap(t: float, pos: Vector2) -> Dictionary:
	return {"s": t, "players": [{"seat": 1, "pos": pos, "color": 0, "out": false}]}


func _two() -> Object:
	return _session_with([_snap(10.0, Vector2(0, 0)), _snap(10.1, Vector2(10, 0))])


func test_interpolates_between_two_snapshots() -> bool:
	var p: Vector2 = _two().sample(10.05)[1].pos
	assert_near(p.x, 5.0, 0.01, "x")
	assert_near(p.y, 0.0, 0.01, "y")
	return true


func test_holds_last_position_when_seat_missing_next() -> bool:
	var s: Object = _session_with([_snap(10.0, Vector2(0, 0)), {"s": 10.1, "players": []}])
	assert_eq(s.sample(10.05)[1].pos, Vector2(0, 0), "held pos")
	return true


func test_extrapolation_capped_at_0_2_s() -> bool:
	var p: Vector2 = _two().sample(11.0)[1].pos
	assert_near(p.x, 30.0, 0.01, "x")
	return true


func test_stale_snapshots_hide_players() -> bool:
	assert_true(_two().sample(11.2).is_empty(), "empty when stale")
	return true


func test_own_seat_is_skipped() -> bool:
	var s: Object = track(load("res://autoload/session.gd").new())
	s.seat_id = 3
	s.push_snapshot({"s": 10.0, "players": [{"seat": 3, "pos": Vector2.ZERO, "color": 0, "out": false}]})
	assert_true(not s.sample(10.0).has(3), "own seat absent")
	return true

func test_playback_slows_on_thin_buffer() -> bool:
	var s: Object = _session_with([_snap(10.0, Vector2.ZERO), _snap(10.1, Vector2.ZERO)])
	s.sample(10.0)
	s.sample(10.1)
	assert_near(s.playback_t, 10.09, 0.0001, "0.1 s of render time plays as 0.09 s with two snapshots ahead")
	return true


func test_playback_speeds_up_on_deep_buffer() -> bool:
	var snaps: Array = []
	for i in range(8):
		snaps.append(_snap(10.0 + 0.1 * i, Vector2.ZERO))
	var s: Object = _session_with(snaps)
	s.sample(10.0)
	s.sample(10.1)
	assert_near(s.playback_t, 10.11, 0.0001, "0.1 s of render time plays as 0.11 s with eight snapshots ahead")
	return true


func test_playback_keeps_pace_at_target_depth_and_resyncs_after_a_gap() -> bool:
	var s: Object = _session_with([_snap(10.0, Vector2.ZERO), _snap(10.1, Vector2.ZERO), _snap(10.2, Vector2.ZERO), _snap(10.3, Vector2.ZERO)])
	s.sample(10.0)
	s.sample(10.1)
	assert_near(s.playback_t, 10.1, 0.0001, "three ahead: real time")
	s.sample(20.0)
	assert_near(s.playback_t, 20.0, 0.0001, "a long gap restarts from the render time")
	return true


func test_playback_holds_on_a_small_backward_step() -> bool:
	var s: Object = _session_with([_snap(10.0, Vector2.ZERO), _snap(10.1, Vector2.ZERO)])
	s.sample(10.0)
	s.sample(10.1)
	var before: float = s.playback_t
	s.sample(10.098)
	assert_near(s.playback_t, before, 0.0001, "a late snapshot lowers the render time a little: the clock holds, it does not jump to it")
	s.sample(10.198)
	assert_near(s.playback_t, before + 0.1 * 0.9, 0.0001, "and moves on from there")
	return true


func test_playback_restarts_after_a_big_backward_step() -> bool:
	var s: Object = _session_with([_snap(10.0, Vector2.ZERO), _snap(10.1, Vector2.ZERO)])
	s.sample(20.0)
	s.sample(5.0)
	assert_near(s.playback_t, 5.0, 0.0001, "a render time far in the past restarts the clock")
	return true
