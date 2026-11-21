extends "res://tests/test_case.gd"
## The settings panel must fit the 640x360 logical screen (it was 372 px tall: Save sat on the
## bottom edge, under a phone's home bar).

const SettingsPanel := preload("res://ui/widgets/settings_panel.gd")
const SCREEN: Vector2 = Vector2(640.0, 360.0)
const SPARE_PX: float = 16.0


func test_panel_fits_the_logical_screen_with_room_to_spare() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	var p: Control = track(SettingsPanel.new()) as Control
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(p)
	var need: Vector2 = p._panel.get_combined_minimum_size()
	assert_true(need.y <= SCREEN.y - SPARE_PX, "height %s fits with spare room" % need.y)
	assert_true(need.x <= SCREEN.x - SPARE_PX, "width %s fits" % need.x)
	return true
