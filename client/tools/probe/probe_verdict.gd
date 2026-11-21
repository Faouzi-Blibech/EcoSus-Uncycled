class_name ProbeVerdict
extends RefCounted
## Turns what a scenario returned into the last output line and the exit code.
## PASS only for a Dictionary with `pass` exactly true and a non-empty `evidence` String.
## Everything else is FAIL: null, "", a Dictionary without `pass`, `pass` not a bool true, no
## evidence. A scenario that aborts on a script error returns such a value, so a crash cannot pass.


## Returns {"line": String, "code": int}. `result` is the scenario's return value.
static func line(scenario: String, result: Variant) -> Dictionary:
	if typeof(result) == TYPE_DICTIONARY:
		var d: Dictionary = result
		var passed: Variant = d.get("pass")
		if typeof(passed) == TYPE_BOOL and passed == true:
			var evidence: String = _one_line(d.get("evidence"))
			if not evidence.is_empty():
				return {"line": "PROBE PASS %s" % scenario, "code": 0, "evidence": evidence}
			return {"line": "PROBE FAIL %s: pass without evidence" % scenario, "code": 1}
		var reason: String = _one_line(d.get("reason"))
		if typeof(passed) == TYPE_BOOL and not reason.is_empty():
			return {"line": "PROBE FAIL %s: %s" % [scenario, reason], "code": 1}
	return {"line": "PROBE FAIL %s: scenario gave no verdict (script error or crash)" % scenario, "code": 1}


static func _one_line(v: Variant) -> String:
	if typeof(v) != TYPE_STRING:
		return ""
	return String(v).replace("\r", " ").replace("\n", " ").strip_edges()
