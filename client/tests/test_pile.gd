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
