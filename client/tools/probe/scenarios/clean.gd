extends RefCounted
## Eight clients start a match. A polluter dumps; a citizen walks to the new pile, sends clean_start,
## gets clean_ok {duration}, stays, and gets clean_done. PASS when the pollution meter drops by
## welcome.tuning.dump.clean_meter_sub between two consecutive snapshots (within METER_TOLERANCE, the
## passive rise included) and the pile is gone from the citizen's next snapshots.
## Contract shared with the other scenario files: `static func run(url, grid, deadline_ms)`.

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
## The dump lock after the start, one walk from the spawn (about 40 s) and the cleaning itself.
const DEFAULT_TIMEOUT_S: float = 240.0
const MSG_S: float = 10.0
const DUMP_READY_S: float = 60.0
const DONE_MARGIN_S: float = 5.0
const SETTLE_S: float = 0.6
const METER_TOLERANCE: float = 0.5


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var room := Room.new()
	var result: Dictionary = _play(room, url, grid, deadline_ms)
	room.close_all()
	return result


static func _fail(reason: String) -> Dictionary:
	return {"pass": false, "reason": reason}


static func _play(room: Room, url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var err: String = room.open(url, grid, deadline_ms)
	if not err.is_empty():
		return _fail(err)
	var polluters: Array[int] = room.polluters()
	if polluters.is_empty():
		return _fail("no polluter in this room (roles %s)" % str(room.role_of))
	var citizen: int = -1
	for i in room.everyone():
		if not polluters.has(i):
			citizen = i
			break
	if citizen < 0:
		return _fail("no citizen in this room (roles %s)" % str(room.role_of))
	var sub: float = float(Dictionary(room.tuning.get("dump", {})).get("clean_meter_sub", -1.0))
	if sub <= 0.0:
		return _fail("welcome.tuning has no dump.clean_meter_sub")

	var p: ProbeClient = room.clients[polluters[0]]
	var c: ProbeClient = room.clients[citizen]
	if not _wait_dump_ready(room, p):
		return _fail("the polluter's dump cooldown never reached 0 (last snapshot cd %s)" % str(p.received("snapshot").back().get("me", {}).get("cd") if not p.received("snapshot").is_empty() else "none"))
	p.skip_received("action_rejected")
	p.send("dump")
	var ok: Dictionary = {}
	var limit: int = mini(Time.get_ticks_msec() + int(MSG_S * 1000.0), deadline_ms)
	while ok.is_empty() and Time.get_ticks_msec() < limit:
		room.hub().poll()
		ok = p.wait_for("dump_ok", 0.0)
		OS.delay_msec(5)
	if ok.is_empty():
		return _fail("no dump_ok (polluter got %s)" % str(p.received("action_rejected")))
	var pile_id: int = int(ok["pile"])
	var pile_pos: Variant = _pile_pos(p, pile_id, limit)
	if pile_pos == null:
		return _fail("the polluter's snapshots never listed pile %d" % pile_id)

	if not room.walk_polled(citizen, pile_pos, grid):
		return _fail("citizen %d could not walk to pile %d at %s (no route, correction, closed socket or deadline)" % [citizen, pile_id, str(pile_pos)])
	c.skip_received("action_rejected")
	c.send("clean_start", {"pile": pile_id})
	var started: Dictionary = c.wait_for("clean_ok", MSG_S)
	if started.is_empty():
		return _fail("no clean_ok (citizen got %s)" % str(c.received("action_rejected")))
	if int(started.get("pile", -1)) != pile_id or float(started.get("duration", 0.0)) <= 0.0:
		return _fail("clean_ok was %s, expected pile %d with a duration" % [str(started), pile_id])
	var duration: float = float(started["duration"])
	var done: Dictionary = c.wait_for("clean_done", duration + DONE_MARGIN_S)
	if done.is_empty():
		return _fail("no clean_done within %.1f s (cancelled: %s, rejected: %s)" % [duration + DONE_MARGIN_S, str(c.received("clean_cancelled")), str(c.received("action_rejected"))])
	if int(done.get("pile", -1)) != pile_id:
		return _fail("clean_done was for pile %s, expected %d" % [str(done.get("pile")), pile_id])
	c.idle(SETTLE_S)

	var drop: float = _biggest_drop(c.received("snapshot"))
	if absf(drop - sub) > METER_TOLERANCE:
		return _fail("the biggest meter drop between snapshots was %.2f, expected %.2f within %.1f" % [drop, sub, METER_TOLERANCE])
	var last: Dictionary = c.received("snapshot").back()
	for entry in last.get("piles", []):
		if int((entry as Dictionary).get("id", -1)) == pile_id:
			return _fail("pile %d is still in the citizen's latest snapshot after clean_done" % pile_id)
	if not c.received("action_rejected").is_empty():
		return _fail("an action was rejected: %s" % str(c.received("action_rejected")[0]))
	return {"pass": true, "evidence": "polluter seat %d dumped pile %d; citizen seat %d walked there, clean_ok duration %.1f s, clean_done, meter dropped %.2f (expected %.1f from dump.clean_meter_sub) and the pile left the snapshot; no action_rejected" % [
		room.seat_of[polluters[0]], pile_id, room.seat_of[citizen], duration, drop, sub]}


## Waits (hub polling every socket) until a snapshot says the polluter's dump cooldown is 0: the
## server locks dump for a while after the match starts.
static func _wait_dump_ready(room: Room, p: ProbeClient) -> bool:
	var limit: int = mini(Time.get_ticks_msec() + int(DUMP_READY_S * 1000.0), room.deadline_ms)
	while Time.get_ticks_msec() < limit:
		room.hub().poll()
		var snaps: Array = p.received("snapshot")
		if not snaps.is_empty():
			var cd: Variant = Dictionary((snaps.back() as Dictionary).get("me", {})).get("cd")
			if typeof(cd) == TYPE_DICTIONARY and float((cd as Dictionary).get("dump", 1.0)) <= 0.0:
				return true
		OS.delay_msec(20)
	return false


## Position (tiles) of pile `id` in the polluter's snapshots; null if it never shows before `limit`.
static func _pile_pos(p: ProbeClient, id: int, limit: int) -> Variant:
	while Time.get_ticks_msec() < limit:
		var s: Dictionary = p.wait_for("snapshot", 1.0)
		for entry in s.get("piles", []):
			var d: Dictionary = entry
			if int(d.get("id", -1)) == id:
				var pos: Array = d["pos"]
				return Vector2(float(pos[0]), float(pos[1]))
	return null


## The largest fall of `meter` between two consecutive snapshots; 0 when it never falls.
static func _biggest_drop(snapshots: Array) -> float:
	var biggest: float = 0.0
	for i in range(1, snapshots.size()):
		var fall: float = float((snapshots[i - 1] as Dictionary).get("meter", 0.0)) - float((snapshots[i] as Dictionary).get("meter", 0.0))
		biggest = maxf(biggest, fall)
	return biggest
