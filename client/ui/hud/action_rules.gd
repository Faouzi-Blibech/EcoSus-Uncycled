class_name ActionRules
extends RefCounted
## Which action buttons the HUD shows and whether HIT and DUMP can be used right now.
## Pure: everything comes in through `ctx`, so the rules are testable without a scene.
##
## ctx keys: phase, role, is_out, near_console ({id, kind} or {}; kind task, emergency, fire_fix,
## smog_fix), held_console (the fire console I hold or ""), has_task_here, fresh_pile (pile id or -1), hit_target (seat or -1), cd ({hit, dump,
## sabotage} seconds), emergencies_left, sabotage (Session.sabotage: {} or {kind, ...}), clean_pile (cleanable pile id
## in range or -1).
## Result: {"use": bool, "report": bool, "hit": {visible, ready, cooldown}, "dump": {...},
## "sabotage": {...}, "fix": {visible, kind ("fire", "smog" or "")}, "clean": bool}.
## The server still validates every action; these rules only decide what the player is offered.


static func button_states(ctx: Dictionary) -> Dictionary:
	var states: Dictionary = {
		"use": false, "report": false, "hit": _hidden(), "dump": _hidden(), "sabotage": _hidden(),
		"fix": {"visible": false, "kind": ""}, "clean": false,
	}
	if str(ctx.get("phase", "")) != "playing" or bool(ctx.get("is_out", false)):
		return states

	var polluter: bool = str(ctx.get("role", "")) == "polluter"
	var near: Dictionary = ctx.get("near_console", {}) as Dictionary
	match str(near.get("kind", "")):
		"task":
			states["use"] = polluter or bool(ctx.get("has_task_here", false))
		"emergency":
			states["use"] = int(ctx.get("emergencies_left", 0)) > 0
		"fire_fix", "smog_fix":
			# Only an active citizen fixes (the server rejects a polluter), and only the kind that runs.
			var running: String = str((ctx.get("sabotage", {}) as Dictionary).get("kind", ""))
			var fix_kind: String = "fire" if str(near["kind"]) == "fire_fix" else "smog"
			var held: Array = Net.as_array((ctx.get("sabotage", {}) as Dictionary).get("held"))
			var held_by_other: bool = held.has(str(near.get("id", ""))) and str(ctx.get("held_console", "")) != str(near.get("id", ""))
			if not polluter and running == fix_kind and not held_by_other:
				states["fix"] = {"visible": true, "kind": fix_kind}
	states["report"] = int(ctx.get("fresh_pile", -1)) >= 0
	states["clean"] = not polluter and int(ctx.get("clean_pile", -1)) >= 0

	if polluter:
		var cd: Dictionary = ctx.get("cd", {}) as Dictionary
		var hit_cd: float = float(cd.get("hit", 0.0))
		var dump_cd: float = float(cd.get("dump", 0.0))
		states["hit"] = {
			"visible": true,
			"ready": int(ctx.get("hit_target", -1)) >= 0 and hit_cd <= 0.0,
			"cooldown": hit_cd,
		}
		states["dump"] = {"visible": true, "ready": dump_cd <= 0.0, "cooldown": dump_cd}
		if (ctx.get("sabotage", {}) as Dictionary).is_empty():
			var sabotage_cd: float = float(cd.get("sabotage", 0.0))
			states["sabotage"] = {"visible": true, "ready": sabotage_cd <= 0.0, "cooldown": sabotage_cd}
	return states


static func _hidden() -> Dictionary:
	return {"visible": false, "ready": false, "cooldown": 0.0}
