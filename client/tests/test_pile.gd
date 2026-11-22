extends "res://tests/test_case.gd"

const PileScene: PackedScene = preload("res://world/pile/pile.tscn")


func test_same_kind_and_state_loads_the_texture_once() -> bool:
	var p: Pile = track(PileScene.instantiate()) as Pile
	p.setup(7, "dump", "fresh")
	p.setup(7, "dump", "fresh")
	p.setup(7, "dump", "fresh")
	assert_eq(p.texture_loads, 1, "loads after 3 identical setups")
	p.setup(7, "dump", "reported")
	assert_eq(p.texture_loads, 2, "loads after a state change")
	return true


func test_body_is_drawn_in_the_victim_colour_without_a_texture() -> bool:
	var p: Pile = track(PileScene.instantiate()) as Pile
	p.setup(3, "body", "fresh", Color.RED)
	assert_true(p.is_body(), "body")
	assert_eq(p.texture_loads, 0, "a body needs no art file")
	assert_true(not (p.get_node("Sprite") as Sprite2D).visible, "sprite hidden")
	p.set_reportable(true)
	assert_true(p.reportable, "marked")
	p.set_reportable(false)
	assert_true(not p.reportable, "unmarked")
	return true
