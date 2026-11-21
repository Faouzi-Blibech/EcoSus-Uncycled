class_name ActionRules
extends RefCounted
## Which action buttons the HUD shows and whether each one can be used right now.
## Pure: everything comes in through `ctx`, so the rules are testable without a scene.
##
## ctx keys: phase, role, is_out, near_console ({id, kind} or {}; kind task, emergency, fire_fix,
## smog_fix), held_console (the fire console I hold or ""), near_spot_state ("dirty", "clean" or "" for the
## task console in near_console: a v2 cleaning spot), bags_full, bags (carried), machine_jammed,
## ability ("", "virus" or "gas"), fresh_pile (pile id or -1), cd ({repollute, virus, gas, sabotage} seconds), emergencies_left, sabotage (Session.sabotage: {} or {kind, ...}), clean_pile (cleanable pile id
## in range or -1).
## Result: {"use": bool, "report": bool, "repollute": {visible, ready, cooldown},
## "ability": {visible, ready, cooldown, kind ("virus", "gas" or "")},
## "sabotage": {...}, "fix": {visible, kind ("fire", "smog", "machine" or "")}, "clean": bool}.
## The server still validates every action; these rules only decide what the player is offered.


static func button_states(ctx: Dictionary) -> Dictionary:
	var states: Dictionary = {
		"use": false, "report": false, "repollute": _hidden(), "ability": _hidden_ability(), "sabotage": _hidden(),
		"fix": {"visible": false, "kind": ""}, "clean": false,
	}
	if str(ctx.get("phase", "")) != "playing" or bool(ctx.get("is_out", false)):
		return states

	var polluter: bool = str(ctx.get("role", "")) == "polluter"
	var near: Dictionary = ctx.get("near_console", {}) as Dictionary
	match str(near.get("kind", "")):
		"task":
			# A task console is a cleaning spot. A Cleaner cleans it while dirty with a free bag slot; a
			# Trasher fakes the clean on the same dirty spots (faking on a clean one would give them away).
			var dirty: bool = str(ctx.get("near_spot_state", "")) == "dirty"
			states["use"] = dirty and (polluter or not bool(ctx.get("bags_full", false)))
		"emergency":
			states["use"] = int(ctx.get("emergencies_left", 0)) > 0
		"machine":
			# The Recycling Machine: a Cleaner delivers the bags carried, or holds FIX while it is jammed.
			if not polluter:
				if bool(ctx.get("machine_jammed", false)):
					states["fix"] = {"visible": true, "kind": "machine"}
				else:
					states["use"] = int(ctx.get("bags", 0)) > 0
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
		var spot_state: String = str(ctx.get("near_spot_state", ""))
		# RE-POLLUTE turns the clean spot in range dirty again.
		var repollute_cd: float = float(cd.get("repollute", 0.0))
		states["repollute"] = {"visible": true, "ready": spot_state == "clean" and repollute_cd <= 0.0, "cooldown": repollute_cd}
		# The unlocked ability: the virus goes into a dirty spot in range, the gas works from anywhere.
		var ability: String = str(ctx.get("ability", ""))
		if ability == "virus" or ability == "gas":
			var ability_cd: float = float(cd.get(ability, 0.0))
			var in_place: bool = ability == "gas" or spot_state == "dirty"
			states["ability"] = {"visible": true, "ready": in_place and ability_cd <= 0.0, "cooldown": ability_cd, "kind": ability}
		# A jammed machine is a running sabotage too (snapshots report it as machine.jammed).
		if (ctx.get("sabotage", {}) as Dictionary).is_empty() and not bool(ctx.get("machine_jammed", false)):
			var sabotage_cd: float = float(cd.get("sabotage", 0.0))
			states["sabotage"] = {"visible": true, "ready": sabotage_cd <= 0.0, "cooldown": sabotage_cd}
	return states


static func _hidden_ability() -> Dictionary:
	return {"visible": false, "ready": false, "cooldown": 0.0, "kind": ""}


static func _hidden() -> Dictionary:
	return {"visible": false, "ready": false, "cooldown": 0.0}
