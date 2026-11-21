extends SceneTree
## Scripted client for checking the real server and a map. No autoloads in --script mode.
## godot --headless --path client --script res://tools/probe/probe.gd -- --scenario <name> [--url ws://localhost:5000/ws] [--map ../content/maps/greybox_01.json] [--timeout 90]
## Last line: "PROBE PASS <scenario>" or "PROBE FAIL <scenario>: <reason>". Exit 0 pass, 1 fail.
## A scenario returns {"pass": true, "evidence": "..."} or {"pass": false, "reason": "..."};
## ProbeVerdict decides the line, and anything else (a crash included) is a FAIL.
## Scenarios: join_move (A creates, B joins, A starts and walks to market_bins), and the ones in
## EXTERNAL, each a file under tools/probe/scenarios/ with `static func run(url, grid, deadline_ms)`.
## A scenario file may set `const DEFAULT_TIMEOUT_S`; it replaces the 90 s default when there is no
## --timeout (tasks: 240 s).

const DEFAULT_URL: String = "ws://localhost:5000/ws"
const DEFAULT_MAP: String = "../content/maps/greybox_01.json"
const DEFAULT_TIMEOUT_S: float = 90.0
const ARRIVE_TOLERANCE_TILES: float = 0.5
## One line per scenario file: name -> script.
const EXTERNAL: Dictionary = {
	"bots_round": preload("res://tools/probe/scenarios/bots_round.gd"),
	"clean": preload("res://tools/probe/scenarios/clean.gd"),
	"full_round_citizens": preload("res://tools/probe/scenarios/full_round_citizens.gd"),
	"full_round_deadline": preload("res://tools/probe/scenarios/full_round_deadline.gd"),
	"meeting_bots": preload("res://tools/probe/scenarios/meeting_bots.gd"),
	"meeting_bots_listen": preload("res://tools/probe/scenarios/meeting_bots_listen.gd"),
	"meeting_chat": preload("res://tools/probe/scenarios/meeting_chat.gd"),
	"rejoin_meeting": preload("res://tools/probe/scenarios/rejoin_meeting.gd"),
	"sabotage_fire": preload("res://tools/probe/scenarios/sabotage_fire.gd"),
	"tasks": preload("res://tools/probe/scenarios/tasks_scenario.gd"),
}

var _deadline_ms: int = 0
var _timeout_s: float = DEFAULT_TIMEOUT_S


func _initialize() -> void:
	quit(_run(OS.get_cmdline_user_args()))


func _run(args: PackedStringArray) -> int:
	var scenario: String = ""
	var url: String = DEFAULT_URL
	var map_path: String = DEFAULT_MAP
	var timeout_given: bool = false
	var i: int = 0
	while i < args.size():
		if (args[i] == "--scenario" or args[i] == "--url" or args[i] == "--map" or args[i] == "--timeout") and i + 1 < args.size():
			match args[i]:
				"--scenario":
					scenario = args[i + 1]
				"--url":
					url = args[i + 1]
				"--map":
					map_path = args[i + 1]
				_:
					_timeout_s = float(args[i + 1])
					timeout_given = true
			i += 2
		else:
			return _print_verdict(scenario, _fail("bad argument %s" % args[i]))
	var known: PackedStringArray = PackedStringArray(["join_move"] + EXTERNAL.keys())
	if not known.has(scenario):
		return _print_verdict(scenario, _fail("unknown scenario (known: %s)" % ", ".join(known)))
	if not timeout_given and EXTERNAL.has(scenario):
		var consts: Dictionary = (EXTERNAL[scenario] as GDScript).get_script_constant_map()
		if consts.has("DEFAULT_TIMEOUT_S"):
			_timeout_s = float(consts["DEFAULT_TIMEOUT_S"])
	_deadline_ms = Time.get_ticks_msec() + int(_timeout_s * 1000.0)
	if EXTERNAL.has(scenario):
		return _print_verdict(scenario, _run_external(EXTERNAL[scenario], url, map_path))
	return _print_verdict(scenario, _join_move(url, map_path))


func _print_verdict(scenario: String, result: Variant) -> int:
	var v: Dictionary = ProbeVerdict.line(scenario, result)
	if v.has("evidence"):
		print("evidence: %s" % v["evidence"])
	print(v["line"])
	return int(v["code"])


func _fail(reason: String) -> Dictionary:
	if Time.get_ticks_msec() >= _deadline_ms and _deadline_ms > 0:
		reason = "timed out after %s s: %s" % [str(_timeout_s), reason]
	return {"pass": false, "reason": reason}


func _join_move(url: String, map_path: String) -> Dictionary:
	var grid := GridPath.new()
	if not grid.load_map(map_path):
		return _fail("cannot load map %s" % map_path)
	var console: Variant = grid.marker_pos("market_bins")
	if console == null:
		return _fail("map has no console market_bins")

	var a := ProbeClient.new()
	var b := ProbeClient.new()
	a.deadline_ms = _deadline_ms
	b.deadline_ms = _deadline_ms
	a.companions.append(b)
	var result: Dictionary = _join_move_run(url, grid, console, a, b)
	a.close()
	b.close()
	return result


func _join_move_run(url: String, grid: GridPath, console: Vector2, a: ProbeClient, b: ProbeClient) -> Dictionary:
	if not a.connect_to(url) or a.wait_for("hello_ok", 5.0).is_empty():
		return _fail("client A got no hello_ok from %s" % url)
	a.send("create", {"name": "ProbeA"})
	var wa: Dictionary = a.wait_for("welcome", 5.0)
	if wa.is_empty():
		return _fail("client A got no welcome after create")
	if str(wa.get("map_id")) != grid.map_id() or str(wa.get("map_version")) != grid.map_version():
		return _fail("server runs map %s %s but --map is %s %s" % [str(wa.get("map_id")), str(wa.get("map_version")), grid.map_id(), grid.map_version()])
	var seat_a: int = int(wa["seat"])

	if not b.connect_to(url) or b.wait_for("hello_ok", 5.0).is_empty():
		return _fail("client B got no hello_ok")
	b.send("join", {"code": wa["room"], "name": "ProbeB"})
	if b.wait_for("welcome", 5.0).is_empty():
		return _fail("client B got no welcome (join_failed: %s)" % str(b.received("join_failed")))

	a.send("start")
	var ms: Dictionary = a.wait_for("match_started", 10.0)
	if ms.is_empty():
		return _fail("client A got no match_started")
	if b.wait_for("match_started", 10.0).is_empty():
		return _fail("client B got no match_started")
	var spawn: Array = ms["spawn"]
	a.pos = Vector2(float(spawn[0]), float(spawn[1]))

	# The server ignores moves during the intro; wait for the first snapshot of the playing phase.
	var playing: bool = false
	while not playing:
		var s: Dictionary = a.wait_for("snapshot", 15.0)
		if s.is_empty():
			return _fail("client A got no playing snapshot")
		playing = s.get("phase") == "playing"

	if not a.walk_to(console, grid):
		return _fail("A could not walk from spawn %s to market_bins %s (no route, server correct %s, closed socket or deadline)" % [str(a.pos), str(console), str(a.received("correct"))])

	# Let the server apply the last move, then take snapshots that arrived after it.
	a.idle(0.4)
	a.skip_received("snapshot")
	b.skip_received("snapshot")
	var snap_a: Dictionary = a.wait_for("snapshot", 3.0)
	var snap_b: Dictionary = b.wait_for("snapshot", 3.0)
	if snap_a.is_empty():
		return _fail("client A got no snapshot after arriving")
	if not a.received("correct").is_empty():
		return _fail("server sent correct to A")

	var moves: String = "%d moves, no correct" % a.seq
	if not snap_b.is_empty():
		for p in snap_b.get("players", []):
			if int(p["seat"]) == seat_a:
				var seen := Vector2(float(p["pos"][0]), float(p["pos"][1]))
				if seen.distance_to(console) > ARRIVE_TOLERANCE_TILES:
					return _fail("B sees A at %s, %.2f tiles from market_bins %s" % [str(seen), seen.distance_to(console), str(console)])
				return _pass("B saw A at %s, %.2f tiles from market_bins %s; %s; map %s %s" % [str(seen), seen.distance_to(console), str(console), moves, grid.map_id(), grid.map_version()])
	var me: Array = snap_a["me"]["pos"]
	var mine := Vector2(float(me[0]), float(me[1]))
	if mine.distance_to(console) > ARRIVE_TOLERANCE_TILES:
		return _fail("A's own pos %s is %.2f tiles from market_bins %s" % [str(mine), mine.distance_to(console), str(console)])
	return _pass("A's own snapshot pos %s is %.2f tiles from market_bins %s (B did not see A); %s; map %s %s" % [str(mine), mine.distance_to(console), str(console), moves, grid.map_id(), grid.map_version()])


func _pass(evidence: String) -> Dictionary:
	if Time.get_ticks_msec() >= _deadline_ms:
		return _fail("finished after the deadline")
	return {"pass": true, "evidence": evidence}


## The scenario's own result goes to ProbeVerdict unchanged; only a pass that came in after the
## hard deadline is turned into a fail here.
func _run_external(script: GDScript, url: String, map_path: String) -> Variant:
	var grid := GridPath.new()
	if not grid.load_map(map_path):
		return _fail("cannot load map %s" % map_path)
	var result: Variant = script.run(url, grid, _deadline_ms)
	if typeof(result) == TYPE_DICTIONARY and typeof((result as Dictionary).get("pass")) == TYPE_BOOL and (result as Dictionary)["pass"] and Time.get_ticks_msec() >= _deadline_ms:
		return _fail("finished after the deadline")
	return result
