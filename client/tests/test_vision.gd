extends "res://tests/test_case.gd"

## Design 5.1: inside the vision radius your own light is full, also inside a dark room, where it is
## the only light. The ambient level is scene data; the light must be bright enough on its own.
const MIN_OWN_LIGHT: float = 0.8


func _vision() -> Vision:
	return track(load("res://world/vision/vision.tscn").instantiate()) as Vision


func test_light_is_full_out_to_the_vision_radius() -> bool:
	var v: Vision = _vision()
	var light: PointLight2D = v.get_node("Light") as PointLight2D
	var grad: Gradient = (light.texture as GradientTexture2D).gradient
	var full_fraction: float = grad.offsets[1]
	for r in [96.0, 128.0, 160.0]:
		v.set_radius_px(r)
		var full_px: float = light.texture_scale * float(light.texture.get_width()) * 0.5 * full_fraction
		assert_true(full_px >= r - 0.01, "full light reaches the radius %s (reaches %s)" % [str(r), str(full_px)])
		v._target_px = -1.0
	return true


func test_own_light_is_bright_enough_to_read_a_dark_room() -> bool:
	var v: Vision = _vision()
	var light: PointLight2D = v.get_node("Light") as PointLight2D
	assert_true(light.energy >= MIN_OWN_LIGHT, "light energy %s is the whole brightness inside a dark room" % str(light.energy))
	return true


## The server's me.vision already counts the dark room it thinks you stand in. The light follows
## your own position at once when that differs (just walked in or out), by the server's rule.
const TUNING: Dictionary = {"citizen": 4.0, "polluter": 5.0, "smog_min": 1.0, "dark_mult": 0.5}


func test_light_follows_the_server_when_both_agree_on_the_dark() -> bool:
	assert_eq(Vision.light_tiles(4.0, false, false, 0.0, TUNING, true), 4.0, "in the light on both sides")
	assert_eq(Vision.light_tiles(2.0, true, true, 0.0, TUNING, true), 2.0, "in the dark on both sides: not halved twice")
	return true


func test_light_shrinks_at_once_when_a_citizen_walks_into_a_dark_room() -> bool:
	assert_eq(Vision.light_tiles(4.0, false, true, 0.0, TUNING, true), 2.0, "4 tiles halved")
	assert_eq(Vision.light_tiles(1.0, false, true, 1.0, TUNING, true), 1.0, "full smog is already lower than the dark value")
	assert_near(Vision.light_tiles(2.5, false, true, 0.5, TUNING, true), 2.0, 0.001, "half smog 2.5 against dark 2: the lower one")
	return true


func test_light_grows_back_at_once_when_a_citizen_walks_out_of_a_dark_room() -> bool:
	assert_eq(Vision.light_tiles(2.0, true, false, 0.0, TUNING, true), 4.0, "back to the full 4 tiles")
	assert_near(Vision.light_tiles(2.0, true, false, 0.5, TUNING, true), 2.5, 0.001, "smog still eases the radius: 4 -> 1 at half level")
	return true


func test_a_polluter_light_ignores_dark_rooms() -> bool:
	assert_eq(Vision.light_tiles(5.0, false, true, 0.0, TUNING, false), 5.0, "polluter in a dark room")
	assert_eq(Vision.light_tiles(5.0, true, false, 0.0, TUNING, false), 5.0, "polluter out of a dark room")
	return true


func test_light_takes_the_server_value_when_both_sides_agree_whatever_it_is() -> bool:
	assert_eq(Vision.light_tiles(3.0, false, false, 0.0, TUNING, true), 3.0, "a server value the rule would not compute stays as sent")
	assert_eq(Vision.light_tiles(1.7, true, true, 0.0, TUNING, true), 1.7, "also inside a dark room")
	return true
