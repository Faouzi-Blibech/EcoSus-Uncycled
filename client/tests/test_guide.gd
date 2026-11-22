extends "res://tests/test_case.gd"
## Playtest fixes: objectives card, gold arrow to the machine, gas countdown.

const Guide := preload("res://ui/hud/guide_overlay.gd")
const GasBannerS := preload("res://ui/hud/gas_banner.gd")


func test_next_step_per_role_and_bags() -> bool:
	var cases: Array = [
		# role,       bags, cap, out,   expected
		["citizen",   0,    3,   false, "GUIDE_CLEAN"],
		["citizen",   1,    3,   false, "GUIDE_DELIVER"],
		["citizen",   3,    3,   false, "GUIDE_BAG_FULL"],
		["polluter",  0,    3,   false, "GUIDE_TRASHER_STEP"],
		["citizen",   2,    3,   true,  ""],
	]
	for c in cases:
		assert_eq(Guide.step_key(c[0], c[1], c[2], c[3]), c[4], str(c))
	return true


func test_gold_arrow_only_for_a_cleaner_with_bags_away_from_the_machine() -> bool:
	var far: Vector2 = Vector2(400.0, 0.0)
	assert_true(Guide.wants_arrow("citizen", 1, false, far), "cleaner with a bag")
	assert_true(not Guide.wants_arrow("citizen", 0, false, far), "no bag")
	assert_true(not Guide.wants_arrow("polluter", 2, false, far), "trasher")
	assert_true(not Guide.wants_arrow("citizen", 1, true, far), "out")
	assert_true(not Guide.wants_arrow("citizen", 1, false, Vector2(10.0, 0.0)), "already at the machine")
	return true


func test_gas_countdown_reaches_zero_when_the_gas_lands() -> bool:
	assert_eq(GasBannerS.seconds_shown(9.2), 10, "rounds up")
	assert_eq(GasBannerS.seconds_shown(0.4), 1, "last second")
	assert_eq(GasBannerS.seconds_shown(0.0), 0, "landed")
	assert_eq(GasBannerS.seconds_shown(-1.0), 0, "never negative")
	return true


func test_all_new_strings_exist_in_every_language() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	var keys: Array[String] = ["GUIDE_MAIN_CLEANER", "GUIDE_MAIN_TRASHER", "GUIDE_CLEAN", "GUIDE_DELIVER",
		"GUIDE_BAG_FULL", "GUIDE_TRASHER_STEP", "GAS_LEAVE_NOW", "GAS_INCOMING", "SPECTATOR_LEAVE",
		"SHOP_BUY_FOR", "SHOP_OWNED", "SHOP_NOT_ENOUGH", "SHOP_OFFLINE", "SHOP_BOUGHT",
		"MENU_CURRENCY", "MENU_SCAN_GAIN", "CHAR_OWN_COLOURS"]
	var before: String = TranslationServer.get_locale()
	for lang in ["en", "fr", "ar", "sv"]:
		TranslationServer.set_locale(lang)
		for k in keys:
			assert_true(TranslationServer.translate(k) != k, "%s in %s" % [k, lang])
	TranslationServer.set_locale(before)
	return true


func test_spectator_overlay_has_a_leave_button() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	var o: Control = track(load("res://ui/hud/spectator_overlay.tscn").instantiate()) as Control
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(o)
	var fired: Array[bool] = [false]
	o.leave_pressed.connect(func() -> void: fired[0] = true)
	var btn: Button = null
	for n in o.find_children("*", "Button", true, false):
		btn = n as Button
	assert_true(btn != null and btn.mouse_filter == Control.MOUSE_FILTER_STOP, "a tappable button")
	btn.pressed.emit()
	assert_true(fired[0], "leave_pressed fires")
	return true
