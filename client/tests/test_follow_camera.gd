extends "res://tests/test_case.gd"
## The follow camera keeps the player centred, also at the map's edge (regression: the camera
## stopped at the map edge and the player in the north corridor slid under the top HUD).

const MAP_PX: Rect2i = Rect2i(0, 0, 4192, 1568)
const HALF_VIEW: Vector2 = Vector2(320.0, 180.0)


func test_limits_grow_by_half_the_view() -> bool:
	var r: Rect2i = FollowCamera.centred_limits(MAP_PX, HALF_VIEW)
	assert_eq(r.position, Vector2i(-320, -180), "top left")
	assert_eq(r.end, Vector2i(4192 + 320, 1568 + 180), "bottom right")
	return true


## Camera2D clamps its centre to [limit + half view, limit_end - half view]: every map point,
## corners included, must stay reachable so the player is never pushed off centre.
func test_every_map_corner_can_be_the_centre() -> bool:
	var r: Rect2i = FollowCamera.centred_limits(MAP_PX, HALF_VIEW)
	var lo := Vector2(r.position) + HALF_VIEW
	var hi := Vector2(r.end) - HALF_VIEW
	for corner in [Vector2(0, 0), Vector2(4192, 0), Vector2(0, 1568), Vector2(4192, 1568)]:
		var p: Vector2 = corner
		assert_true(p.x >= lo.x and p.x <= hi.x and p.y >= lo.y and p.y <= hi.y, "centre reaches " + str(p))
	return true


func test_an_odd_half_view_rounds_up() -> bool:
	var r: Rect2i = FollowCamera.centred_limits(MAP_PX, Vector2(320.4, 180.5))
	assert_eq(r.position, Vector2i(-321, -181), "never a pixel short")
	return true
