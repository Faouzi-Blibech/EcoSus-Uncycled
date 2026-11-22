extends "res://tests/test_case.gd"
## Button sizes: the basic actions every player has are smaller than the role's special abilities.


func _hud() -> Hud:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var hud: Hud = track(Hud.new()) as Hud
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(hud)
	return hud


func test_basic_actions_are_smaller_than_special_ones() -> bool:
	var table: Dictionary = {
		"use": ActionButton.BASIC_SIZE, "report": ActionButton.BASIC_SIZE,
		"ability": ActionButton.BUTTON_SIZE, "repollute": ActionButton.BUTTON_SIZE, "sabotage": ActionButton.BUTTON_SIZE,
	}
	for action in table:
		assert_eq(Hud.button_size_for(action), table[action], action)
	assert_true(ActionButton.BASIC_SIZE.x < ActionButton.BUTTON_SIZE.x, "basic disc is smaller")
	assert_true(ActionButton.BASIC_SIZE.x >= 48.0, "basic stays a 48 px touch target")
	return true


func test_buttons_get_their_size_class() -> bool:
	var hud: Hud = _hud()
	assert_eq((hud._buttons["use"] as Control).custom_minimum_size, ActionButton.BASIC_SIZE, "use is basic")
	assert_eq(hud._fix_button.custom_minimum_size, ActionButton.BASIC_SIZE, "fix shares the use slot: basic")
	assert_eq(hud._dash_button.custom_minimum_size, ActionButton.BASIC_SIZE, "dash is basic")
	assert_eq((hud._buttons["repollute"] as Control).custom_minimum_size, ActionButton.BUTTON_SIZE, "re-pollute is special")
	assert_eq(hud._heal_button.custom_minimum_size, ActionButton.BUTTON_SIZE, "heal is special")
	assert_eq(hud._clean_button.custom_minimum_size, ActionButton.BUTTON_SIZE, "clean is special")
	assert_eq(hud._alert_button.custom_minimum_size, ActionButton.BUTTON_SIZE, "alert is special")
	return true


func test_use_button_face_per_look() -> bool:
	_hud()
	assert_eq(Hud.use_look_face({"kind": "use", "count": 0}), ["use", "Use"], "use")
	assert_eq(Hud.use_look_face({"kind": "clean", "count": 0}), ["clean", "Clean"], "clean")
	assert_eq(Hud.use_look_face({"kind": "recycle", "count": 3}), ["recycle", "Recycle (3)"], "recycle shows the bags")
	assert_eq(Hud.use_look_face({"kind": "meeting", "count": 0}), ["meeting", "Meeting"], "meeting has its own button")
	assert_eq(Hud.use_look_face({}), ["use", "Use"], "no look yet")
	return true


func test_use_button_takes_the_look() -> bool:
	var hud: Hud = _hud()
	var use_b: ActionButton = hud._buttons["use"] as ActionButton
	hud._apply_use_look(use_b, {"kind": "recycle", "count": 2})
	assert_true(use_b.text.begins_with("Recycle (2)"), "label: " + use_b.text)
	assert_eq(use_b._art_name, "recycle", "recycle disc")
	hud._apply_use_look(use_b, {"kind": "use", "count": 0})
	assert_eq(use_b._art_name, "use", "back to the hand")
	return true
