extends "res://tests/test_case.gd"
## The HUD task line: translated name, step count and the zone name of the console.


func test_task_line_has_name_steps_and_zone() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var map: MapRoot = track(load("res://world/maps/greybox_01.tscn").instantiate()) as MapRoot
	var line: String = Hud.task_line({"type": "turn_compost", "steps": 2, "steps_done": 1, "console": "garden_compost"}, map)
	assert_eq(line, "Turn the compost 1/2 (Garden)", "two-step task in the garden")
	var single: String = Hud.task_line({"type": "sort_waste", "steps": 1, "steps_done": 0, "console": "market_bins"}, map)
	assert_eq(single, "Sort waste (Market)", "one-step task has no counter")
	return true
