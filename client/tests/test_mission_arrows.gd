extends "res://tests/test_case.gd"
## MissionArrows.goal: where the arrows point for each role and bag state.


func test_goal_table() -> bool:
	var rows: Array = [
		# role, phase, out, bags, cap, dirty_left -> kind, why
		["citizen", "playing", false, 0, 1, 3, "spots", "free slot: dirty spots"],
		["citizen", "playing", false, 1, 1, 3, "machine", "bags full: the machine"],
		["citizen", "playing", false, 1, 3, 0, "machine", "nothing left to clean: deliver"],
		["citizen", "playing", false, 0, 1, 0, "", "nothing to do"],
		["polluter", "playing", false, 0, 0, 0, "spots", "Trashers look for clean spots"],
		["citizen", "meeting", false, 0, 1, 3, "", "not while in a meeting"],
		["citizen", "playing", true, 0, 1, 3, "", "not while out"],
	]
	for r in rows:
		var g: Dictionary = MissionArrows.goal(r[0], r[1], r[2], r[3], r[4], r[5])
		assert_eq(str(g["kind"]), str(r[6]), str(r[7]))
	assert_eq(str(MissionArrows.goal("polluter", "playing", false, 0, 0, 0)["state"]), "clean", "Trasher state")
	assert_eq(str(MissionArrows.goal("citizen", "playing", false, 0, 1, 2)["state"]), "dirty", "Cleaner state")
	return true


func test_edge_point_on_the_rect() -> bool:
	var r: Rect2 = Rect2(0, 0, 100, 50)
	assert_true(MissionArrows._edge_point(r, Vector2(50, 25), Vector2.RIGHT).is_equal_approx(Vector2(100, 25)), "right edge")
	assert_true(MissionArrows._edge_point(r, Vector2(50, 25), Vector2.UP).is_equal_approx(Vector2(50, 0)), "top edge")
	return true
