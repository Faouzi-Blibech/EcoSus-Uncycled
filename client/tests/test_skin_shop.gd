extends "res://tests/test_case.gd"
## The skin shop: each skin's state from the unlocked list and the worn skin.

const Skins := preload("res://content/skins.gd")


func test_status_table() -> bool:
	var unlocked: Array[String] = ["robot"]
	assert_eq(Skins.status("sprout", unlocked, ""), "wearing", "the starter is worn by default")
	assert_eq(Skins.status("robot", unlocked, ""), "owned", "unlocked: can wear")
	assert_eq(Skins.status("eagle", unlocked, ""), "locked", "not unlocked")
	assert_eq(Skins.status("robot", unlocked, "robot"), "wearing", "worn")
	assert_eq(Skins.status("sprout", unlocked, "robot"), "owned", "the starter is always owned")
	return true


func test_catalogue_matches_the_characters() -> bool:
	for s in Skins.all():
		assert_true(load("res://ui/widgets/characters.gd").has(str(s["id"])), "art for " + str(s["id"]))
	var json: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://../content/skins.json")) if FileAccess.file_exists("res://../content/skins.json") else {}
	for entry in Net.as_array(json.get("skins")):
		var found: bool = false
		for s in Skins.CATALOGUE:
			if s["id"] == entry["id"] and int(s.get("streak", 0)) == int(entry.get("streak", 0)) 					and int(s.get("price", 0)) == int(entry.get("price", 0)):
				found = true
		assert_true(found, "in step with content/skins.json: " + str(entry["id"]))
	return true
