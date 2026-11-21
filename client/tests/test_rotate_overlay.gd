extends "res://tests/test_case.gd"
## A phone held upright gets a "turn your phone sideways" cover instead of a tall, unplayable view.

const RotateOverlay := preload("res://ui/widgets/rotate_overlay.gd")


func test_portrait_means_taller_than_wide() -> bool:
	assert_true(RotateOverlay.is_portrait(Vector2(360.0, 640.0)), "upright phone")
	assert_true(not RotateOverlay.is_portrait(Vector2(640.0, 360.0)), "landscape")
	assert_true(not RotateOverlay.is_portrait(Vector2(500.0, 500.0)), "square is playable")
	return true


func test_a_tall_laptop_window_is_not_covered() -> bool:
	assert_true(RotateOverlay.should_cover(Vector2(360.0, 640.0), true), "upright phone")
	assert_true(not RotateOverlay.should_cover(Vector2(960.0, 1080.0), false), "half-screen browser window on a laptop")
	assert_true(not RotateOverlay.should_cover(Vector2(640.0, 360.0), true), "phone in landscape")
	return true


func test_cover_shows_only_in_portrait() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var o: CanvasLayer = track(RotateOverlay.new()) as CanvasLayer
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(o)
	o.update_for(Vector2(360.0, 640.0), true)
	assert_true(o.visible, "covered when upright")
	assert_eq(o._label.text, "Turn your phone sideways to play.", "the hint")
	o.update_for(Vector2(640.0, 360.0), true)
	assert_true(not o.visible, "gone in landscape")
	return true
