class_name TasksScenario
extends RefCounted
## Scenario `tasks`: two clients start a match; every citizen client walks to the console of each
## of its tasks (the current step), sends `use`, gets `use_ok`, waits the task's `min_s`, sends
## `task_step` (first one EARLY_BY_S before min_s, which must be rejected on_cooldown) and gets
## `task_progress`, until all its tasks are done. PASS only when at least one
## client was a citizen, every task step got its `task_progress` and no `action_rejected` arrived except the early ones.
## Takes 240 s by default (DEFAULT_TIMEOUT_S, probe.gd uses it when there is no --timeout).
## Contract shared with the other scenario files: `static func run(url, grid, deadline_ms)`.

## Walking dominates: about 80-110 s per citizen client at 2.4 tiles/s.
const DEFAULT_TIMEOUT_S: float = 240.0
const STEP_WAIT_S: float = 5.0
const MIN_S_MARGIN_S: float = 0.3
## How long before min_s the probe sends one step that the server must reject.
const EARLY_BY_S: float = 1.0


static var _deadline_ms: int = 0
## Per client name: early steps the server rejected on purpose (not counted as failures).
static var _early_rejections: Dictionary = {}


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	_deadline_ms = deadline_ms
	_early_rejections = {}
	var a := ProbeClient.new()
	var b := ProbeClient.new()
	a.deadline_ms = deadline_ms
	b.deadline_ms = deadline_ms
	var result: Dictionary = _run(url, grid, a, b)
	a.close()
	b.close()
	if bool(result.get("pass", false)) and Time.get_ticks_msec() >= deadline_ms:
		return _fail("finished after the deadline")
	return result


static func _fail(reason: String) -> Dictionary:
	if _deadline_ms > 0 and Time.get_ticks_msec() >= _deadline_ms:
		reason = "timed out: %s" % reason
	return {"pass": false, "reason": reason}


static func _run(url: String, grid: GridPath, a: ProbeClient, b: ProbeClient) -> Dictionary:
	var started_ms: int = Time.get_ticks_msec()
	a.companions.append(b)
	if not a.connect_to(url) or a.wait_for("hello_ok", 5.0).is_empty():
		return _fail("client A got no hello_ok from %s" % url)
	a.send("create", {"name": "ProbeA"})
	var wa: Dictionary = a.wait_for("welcome", 5.0)
	if wa.is_empty():
		return _fail("client A got no welcome after create")
	if str(wa.get("map_id")) != grid.map_id() or str(wa.get("map_version")) != grid.map_version():
		return _fail("server runs map %s %s but --map is %s %s" % [str(wa.get("map_id")), str(wa.get("map_version")), grid.map_id(), grid.map_version()])
	var types: Dictionary = {}
	for t in wa.get("tasks", []):
		types[str(t.get("type"))] = t
	if not b.connect_to(url) or b.wait_for("hello_ok", 5.0).is_empty():
		return _fail("client B got no hello_ok")
	b.send("join", {"code": wa["room"], "name": "ProbeB"})
	if b.wait_for("welcome", 5.0).is_empty():
		return _fail("client B got no welcome (join_failed: %s)" % str(b.received("join_failed")))
	a.send("start")
	var starts: Array[Dictionary] = [a.wait_for("match_started", 10.0), b.wait_for("match_started", 10.0)]
	if starts[0].is_empty() or starts[1].is_empty():
		return _fail("a client got no match_started")
	var clients: Array[ProbeClient] = [a, b]
	var names: Array[String] = ["A", "B"]
	for i in 2:
		var sp: Array = starts[i]["spawn"]
		clients[i].pos = Vector2(float(sp[0]), float(sp[1]))
		var playing: bool = false
		while not playing:
			var s: Dictionary = clients[i].wait_for("snapshot", 15.0)
			if s.is_empty():
				return _fail("client %s got no playing snapshot" % names[i])
			playing = s.get("phase") == "playing"

	var done: PackedStringArray = []
	var roles: PackedStringArray = []
	var citizens: int = 0
	for i in 2:
		var role: String = str(starts[i].get("role"))
		roles.append("%s %s" % [names[i], role])
		if role != "citizen":
			continue
		citizens += 1
		# Only one client polls the other, so the two never poll each other in a loop.
		clients[i].companions.clear()
		clients[1 - i].companions.clear()
		clients[i].companions.append(clients[1 - i])
		var err: String = _do_tasks(clients[i], names[i], starts[i].get("tasks", []), types, grid, done)
		if not err.is_empty():
			return _fail(err)
	if citizens == 0:
		return _fail("no citizen client in this match (%s)" % ", ".join(roles))
	for i in 2:
		var rejected: Array = clients[i].received("action_rejected")
		if rejected.size() > int(_early_rejections.get(names[i], 0)):
			return _fail("client %s got action_rejected: %s" % [names[i], str(rejected[0])])
	return {"pass": true, "evidence": "%s; %d steps done, each task_progress received, %d early steps rejected on_cooldown and no other action_rejected; %.1f s; map %s %s" % [
		", ".join(roles), done.size(), _early_total(), (Time.get_ticks_msec() - started_ms) / 1000.0, grid.map_id(), grid.map_version()]}


static func _early_total() -> int:
	var n: int = 0
	for who in _early_rejections:
		n += int(_early_rejections[who])
	return n


## Does every task of `c`, one step after the other. Returns "" or the reason it stopped.
static func _do_tasks(c: ProbeClient, who: String, tasks: Array, types: Dictionary, grid: GridPath, done: PackedStringArray) -> String:
	if tasks.is_empty():
		return "client %s is a citizen with no tasks" % who
	for task in tasks:
		var task_id: int = int(task["id"])
		var type_id: String = str(task["type"])
		var def: Dictionary = types.get(type_id, {})
		if def.is_empty():
			return "client %s has task %s of type %s, which welcome.tasks does not list" % [who, str(task_id), type_id]
		var consoles: Array = def.get("consoles", [])
		var min_s: float = float(def.get("min_s", 0.0))
		for step in int(def.get("steps", 1)):
			var console_id: String = str(consoles[step % consoles.size()])
			var err: String = _do_step(c, who, task_id, type_id, step, console_id, min_s, grid)
			if not err.is_empty():
				return err
			done.append("%s:%s#%d.%d" % [who, type_id, task_id, step])
	return ""


static func _do_step(c: ProbeClient, who: String, task_id: int, type_id: String, step: int, console_id: String, min_s: float, grid: GridPath) -> String:
	var where: String = "client %s task %s#%d step %d at %s" % [who, type_id, task_id, step, console_id]
	var target: Variant = grid.marker_pos(console_id)
	if target == null:
		return "%s: map has no such console" % where
	if not c.walk_to(target, grid):
		return "%s: could not walk there (no route, server correct %s, closed socket or deadline)" % [where, str(c.received("correct"))]
	c.idle(0.3)
	c.send("use", {"console": console_id})
	var ok: Dictionary = c.wait_for("use_ok", STEP_WAIT_S)
	if ok.is_empty():
		return "%s: no use_ok (action_rejected: %s)" % [where, str(c.received("action_rejected"))]
	if str(ok.get("console")) != console_id or int(ok.get("task", -1)) != task_id or int(ok.get("step", -1)) != step:
		return "%s: use_ok says %s" % [where, str(ok)]
	var opened_ms: int = Time.get_ticks_msec()
	if min_s > EARLY_BY_S:
		# The server must enforce min_s: a step EARLY_BY_S before it is rejected on_cooldown and the
		# session stays open (the real step below then succeeds).
		c.idle(min_s - EARLY_BY_S)
		c.send("task_step", {"task": task_id})
		var early: Dictionary = c.wait_for("action_rejected", STEP_WAIT_S)
		if early.is_empty() or str(early.get("reason", "")) != "on_cooldown":
			return "%s: a task_step %.0f s before min_s %.0f got %s, expected action_rejected on_cooldown" % [where, EARLY_BY_S, min_s, str(early)]
		_early_rejections[who] = int(_early_rejections.get(who, 0)) + 1
	c.idle(maxf(min_s + MIN_S_MARGIN_S - (Time.get_ticks_msec() - opened_ms) / 1000.0, 0.0))
	c.send("task_step", {"task": task_id})
	var deadline: int = Time.get_ticks_msec() + int(STEP_WAIT_S * 1000.0)
	while Time.get_ticks_msec() < deadline:
		var p: Dictionary = c.wait_for("task_progress", 0.5)
		if not p.is_empty() and int(p.get("task", -1)) == task_id:
			if int(p.get("steps_done", -1)) != step + 1:
				return "%s: task_progress says %s" % [where, str(p)]
			return ""
		if c.expired() or c.received("action_rejected").size() > int(_early_rejections.get(who, 0)):
			break
	return "%s: no task_progress (action_rejected: %s)" % [where, str(c.received("action_rejected"))]
