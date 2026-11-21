extends "res://tests/test_case.gd"
## The probe prints PASS only for an explicit verdict with proof.


func _code(v: Variant) -> int:
	return int(ProbeVerdict.line("s", v)["code"])


func _text(v: Variant) -> String:
	return String(ProbeVerdict.line("s", v)["line"])


func test_pass_needs_flag_and_evidence() -> bool:
	assert_eq(_text({"pass": true, "evidence": "A at 7.5,5.5"}), "PROBE PASS s", "pass line")
	assert_eq(_code({"pass": true, "evidence": "A at 7.5,5.5"}), 0, "pass code")
	return true


func test_everything_else_fails() -> bool:
	var bad: Array = [
		"",
		"ok",
		null,
		{},
		{"evidence": "x"},
		{"pass": "true", "evidence": "x"},
		{"pass": 1, "evidence": "x"},
		{"pass": true},
		{"pass": true, "evidence": ""},
		{"pass": true, "evidence": "   "},
		{"pass": true, "evidence": 5},
		{"pass": false, "evidence": "x"},
	]
	for v in bad:
		assert_eq(_code(v), 1, "code for %s" % str(v))
		assert_true(_text(v).begins_with("PROBE FAIL s: "), "line for %s" % str(v))
	return true


func test_fail_carries_its_reason_on_one_line() -> bool:
	assert_eq(_text({"pass": false, "reason": "A got\nno welcome"}), "PROBE FAIL s: A got no welcome", "reason")
	assert_eq(_code({"pass": false, "reason": "x"}), 1, "code")
	return true
