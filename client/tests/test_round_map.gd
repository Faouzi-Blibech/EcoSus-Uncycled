extends "res://tests/test_case.gd"

const Round: GDScript = preload("res://scenes/round/round.gd")


func _packed_version(id: String) -> String:
	var meta: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://world/maps/%s.meta.json" % id))
	return str(meta["version"])


func test_map_matches_only_the_packed_version() -> bool:
	assert_true(Round.map_matches("greybox_01", _packed_version("greybox_01")), "same version")
	assert_true(not Round.map_matches("greybox_01", "sha1-other"), "other version")
	assert_true(not Round.map_matches("greybox_01", ""), "empty version")
	return true


func test_map_matches_rejects_unknown_map() -> bool:
	assert_true(not Round.map_matches("", ""), "no id")
	assert_true(not Round.map_matches("no_such_map", "x"), "missing map")
	return true


func test_map_matches_rejects_missing_or_broken_meta() -> bool:
	# The map scene exists in every case; only the meta.json differs. The broken fixture makes the JSON parser log this error.
	expect_error("Parse JSON failed")
	var version: String = _packed_version("greybox_01")
	assert_true(not Round.map_matches("greybox_01", version, "res://tests/fixtures/%s.no_such.meta.json"), "missing meta")
	assert_true(not Round.map_matches("greybox_01", version, "res://tests/fixtures/%s.broken.meta.json"), "unparsable meta")
	assert_true(not Round.map_matches("greybox_01", version, "res://tests/fixtures/%s.noversion.meta.json"), "meta without version")
	return true
