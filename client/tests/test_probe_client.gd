extends "res://tests/test_case.gd"
## ProbeClient walking maths (no socket): one step is 2.4 / 10 tiles along the route polyline.

const STEP: float = 0.24


func _route(points: Array) -> Array[Vector2]:
	var r: Array[Vector2] = []
	for p in points:
		r.append(p)
	return r


func test_step_carries_leftover_past_waypoints() -> bool:
	var c: ProbeClient = ProbeClient.new()
	var route: Array[Vector2] = _route([Vector2(1, 0), Vector2(2, 0), Vector2(3, 0)])
	var wp: int = 0
	var ticks: int = 0
	while wp < route.size():
		var before: Vector2 = c.pos
		wp = c.advance_along(route, wp)
		ticks += 1
		assert_true(c.pos.distance_to(before) <= STEP + 0.0001, "tick %d never longer than a step" % ticks)
	assert_eq(c.pos, Vector2(3, 0), "ends on the target")
	assert_eq(ticks, 13, "3 tiles at 0.24 per tick, not 1 partial tick per waypoint")
	return true


func test_step_follows_the_corner() -> bool:
	var c: ProbeClient = ProbeClient.new()
	var route: Array[Vector2] = _route([Vector2(1, 0), Vector2(1, 1)])
	var wp: int = 0
	for i in 5:
		wp = c.advance_along(route, wp)
	assert_near(c.pos.x, 1.0, 0.0001, "on the vertical leg")
	assert_near(c.pos.y, 0.2, 0.0001, "1.2 tiles of route walked, corner not cut")
	return true


func test_tasks_scenario_names_a_deadline_failure() -> bool:
	var r: Dictionary = TasksScenario.run("ws://127.0.0.1:1/ws", GridPath.new(), Time.get_ticks_msec() - 1)
	assert_eq(r.get("pass"), false, "no server, no pass")
	assert_true(str(r.get("reason", "")).begins_with("timed out: "), "reason is labelled: %s" % str(r.get("reason", "")))
	return true
