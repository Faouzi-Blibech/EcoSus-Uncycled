extends "res://tests/test_case.gd"
## Phase string -> scene path routing.

const Router := preload("res://scenes/router.gd")


func test_scene_for_phase() -> bool:
	assert_eq(Router.scene_for_phase("lobby"), Router.LOBBY, "lobby")
	assert_eq(Router.scene_for_phase("intro"), Router.ROUND, "intro")
	assert_eq(Router.scene_for_phase("playing"), Router.ROUND, "playing")
	assert_eq(Router.scene_for_phase("meeting"), Router.MEETING, "meeting")
	assert_eq(Router.scene_for_phase("ejection"), Router.MEETING, "ejection")
	assert_eq(Router.scene_for_phase("ended"), Router.RESULTS, "ended")
	assert_eq(Router.scene_for_phase("unknown"), Router.PLACEHOLDER, "unknown")
	return true
