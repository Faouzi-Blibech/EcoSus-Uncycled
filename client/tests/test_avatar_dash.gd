extends "res://tests/test_case.gd"
## The dash look on an avatar (Avatar.play_dash): it always ends with the body and the name tag at
## rest, also when interrupted. Visual only; nothing here touches the network.

const STEP_S: float = 1.0 / 60.0


## A ready avatar in the tree (its body and tag exist), standing at `pos`.
func _avatar(pos: Vector2 = Vector2.ZERO) -> Avatar:
	var a: Avatar = load("res://world/player/avatar.tscn").instantiate() as Avatar
	a.setup(1, "Tester", Color.RED, 1)
	a.position = pos
	(Engine.get_main_loop() as SceneTree).root.add_child(a)
	track(a)
	return a


## Steps the look like frames would, exactly `seconds` long.
func _run(a: Avatar, seconds: float) -> void:
	var left: float = seconds
	while left > 0.0:
		a.step_dash(minf(STEP_S, left))
		left -= STEP_S


func _assert_at_rest(a: Avatar, scale: Vector2, pos: Vector2, why: String) -> void:
	assert_true(not a.is_dashing(), why + ": look ended")
	assert_true(a._body.visible, why + ": body shown")
	assert_true(a._body.scale.is_equal_approx(scale), why + ": body scale back to rest")
	assert_true(a._body.position.is_equal_approx(pos), why + ": body back in place")
	assert_eq(a._body.modulate, Color.WHITE, why + ": no flash, alpha 1")
	assert_near(a._label.modulate.a, 1.0, 0.001, why + ": name shown")
	assert_near(a._icon.modulate.a, 1.0, 0.001, why + ": icon shown")


func test_dash_look_squishes_travels_pops_and_ends_at_rest() -> bool:
	var a: Avatar = _avatar()
	var scale: Vector2 = a._body.scale
	var pos: Vector2 = a._body.position
	a.play_dash(Vector2.ZERO, Vector2.RIGHT, true)
	assert_true(a.is_dashing(), "playing")
	_run(a, Avatar.DASH_OUT_S + Avatar.DASH_TRAVEL_S / 2.0)
	assert_true(not a._body.visible, "the body is a dot while it travels")
	assert_near(a._label.modulate.a, 0.0, 0.001, "name hidden while it travels")
	_run(a, Avatar.DASH_TRAVEL_S / 2.0 + Avatar.DASH_IN_S * Avatar.DASH_POP_GROW)
	assert_true(a._body.scale.x > scale.x * 1.1, "the pop overshoots full size")
	_run(a, Avatar.DASH_IN_S)
	_assert_at_rest(a, scale, pos, "after the look")
	assert_true(Avatar.DASH_TOTAL_S <= 0.3, "the whole look stays short")
	return true


## A remote seat already stands at the end: the squish shows at the start point, then comes home.
func test_remote_dash_squishes_at_the_start_point() -> bool:
	var a: Avatar = _avatar(Vector2(200, 0))
	var pos: Vector2 = a._body.position
	a.play_dash(Vector2(-150, 0), Vector2.RIGHT, false)
	a.step_dash(STEP_S)
	assert_near(a._body.position.x, pos.x - 150.0, 0.01, "the body squishes where the dash began")
	a.play_dash(Vector2(-40, 0), Vector2.RIGHT, false)
	a.step_dash(STEP_S)
	assert_near(a._body.position.x, pos.x - 150.0, 0.01, "a jump spread over frames keeps the first start")
	_run(a, Avatar.DASH_TOTAL_S)
	_assert_at_rest(a, a._body_scale, pos, "after the remote look")
	return true


func test_dash_look_interrupted_restores_the_body() -> bool:
	var a: Avatar = _avatar()
	var scale: Vector2 = a._body.scale
	var pos: Vector2 = a._body.position
	a.play_dash(Vector2.ZERO, Vector2.UP, true)
	_run(a, Avatar.DASH_OUT_S + Avatar.DASH_TRAVEL_S / 2.0)
	a.set_out(true)
	_assert_at_rest(a, scale, pos, "set out mid-dash")
	a.set_out(false)
	a.play_dash(Vector2(-60, 0), Vector2.RIGHT, false)
	_run(a, Avatar.DASH_OUT_S / 2.0)
	a.visible = false
	_assert_at_rest(a, scale, pos, "hidden mid-dash")
	return true
