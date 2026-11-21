extends "res://tests/test_case.gd"
## Every action_rejected reason of docs/protocol.md section 7 has player text.

const PROTOCOL_PATH: String = "../docs/protocol.md"


## The reasons listed for `action_rejected.reason` in section 7 of docs/protocol.md.
func _protocol_reasons() -> Array[String]:
	var path: String = ProjectSettings.globalize_path("res://").path_join(PROTOCOL_PATH)
	var in_section: bool = false
	var in_bullet: bool = false
	var reasons: Array[String] = []
	var tick: RegEx = RegEx.create_from_string("`([a-z_]+)`")
	for line in FileAccess.get_file_as_string(path).split("\n"):
		if line.begins_with("## "):
			in_section = line.begins_with("## 7.")
			in_bullet = false
		elif in_section and line.begins_with("- "):
			in_bullet = line.begins_with("- `action_rejected.reason`")
			if in_bullet:
				line = line.substr(line.find(":"))
		if in_section and in_bullet:
			for m in tick.search_all(line):
				reasons.append(m.get_string(1))
	return reasons


func test_protocol_reasons_are_found() -> bool:
	var reasons: Array[String] = _protocol_reasons()
	assert_true(not reasons.is_empty(), "section 7 lists the reasons")
	assert_true(reasons.has("on_cooldown") and reasons.has("rate_limited"), "first and last reason")
	assert_true(not reasons.has("polluters_voted_out"), "game_over reasons are not mixed in")
	return true


func test_every_reject_reason_has_text() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	for reason in _protocol_reasons():
		var key: String = "REJECT_" + reason.to_upper()
		assert_true(TranslationServer.translate(key) != key, key)
	return true


func test_every_task_type_has_a_name() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	var path: String = ProjectSettings.globalize_path("res://").path_join("../content/tasks.json")
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	assert_true(typeof(parsed) == TYPE_DICTIONARY, "tasks.json parses")
	for t in (parsed as Dictionary).get("tasks", []):
		var key: String = "TASK_" + str((t as Dictionary).get("type", "")).to_upper()
		assert_true(TranslationServer.translate(key) != key, key)
	return true
