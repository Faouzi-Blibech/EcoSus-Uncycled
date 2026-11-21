extends RefCounted
## Eight clients start a match. A polluter starts a fire while two citizens already stand at
## fire_a and fire_b and hold them: PASS needs sabotage_ended {result: fixed}. After the shared
## cooldown the polluter starts a second fire and nobody holds: PASS needs sabotage_ended
## {result: expired} and the pollution meter rising by welcome.tuning.meter.fire_expired_add
## (within METER_TOLERANCE, the passive rise over the few ticks between the snapshots included).
## Roles are random, so the room has to contain a polluter (eight seats always do) and two citizens.
## Contract shared with the other scenario files: `static func run(url, grid, deadline_ms)`.

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
## Two walks from the spawn (about 40 s each), the shared cooldown and one full countdown.
const DEFAULT_TIMEOUT_S: float = 420.0
const MSG_S: float = 10.0
const EXPIRE_MARGIN_S: float = 15.0
const COOLDOWN_MARGIN_S: float = 1.5
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
	var citizens: Array[int] = []
	for i in room.everyone():
		if not polluters.has(i):
			citizens.append(i)
	if citizens.size() < 2:
		return _fail("fewer than two citizens (roles %s)" % str(room.role_of))
	var fire_a: Variant = grid.marker_pos("fire_a")
	var fire_b: Variant = grid.marker_pos("fire_b")
	if fire_a == null or fire_b == null:
		return _fail("map has no fire_a / fire_b")
	var tuning: Dictionary = room.tuning
	var add: float = float(Dictionary(tuning.get("meter", {})).get("fire_expired_add", -1.0))
	if add <= 0.0:
		return _fail("welcome.tuning has no meter.fire_expired_add")
	var cooldown_s: float = float(Dictionary(tuning.get("sabotage", {})).get("shared_cooldown_s", -1.0))
	var countdown_s: float = float(Dictionary(tuning.get("sabotage", {})).get("fire_countdown_s", -1.0))
	if cooldown_s < 0.0 or countdown_s <= 0.0:
		return _fail("welcome.tuning has no sabotage.shared_cooldown_s / fire_countdown_s")

	var p: ProbeClient = room.clients[polluters[0]]
	var a: ProbeClient = room.clients[citizens[0]]
	var b: ProbeClient = room.clients[citizens[1]]
	if not room.walk_polled(citizens[0], fire_a, grid):
		return _fail("citizen %d could not walk to fire_a (no route, correction, closed socket or deadline)" % citizens[0])
	if not room.walk_polled(citizens[1], fire_b, grid):
		return _fail("citizen %d could not walk to fire_b" % citizens[1])
	var started_at: int = Time.get_ticks_msec()

	# Phase 1: both consoles held, the fire is fixed.
	p.send("sabotage", {"kind": "fire"})
	var started: Array[Dictionary] = room.wait_each(room.everyone(), "sabotage_started", MSG_S)
	if started.is_empty():
		return _fail("no sabotage_started for every client (polluter got %s)" % str(p.received("action_rejected")))
	if str(started[0].get("kind")) != "fire" or not started[0].has("countdown"):
		return _fail("sabotage_started was %s, expected a fire with a countdown" % str(started[0]))
	a.send("fix", {"console": "fire_a", "holding": true})
	b.send("fix", {"console": "fire_b", "holding": true})
	var ended: Array[Dictionary] = room.wait_each(room.everyone(), "sabotage_ended", MSG_S)
	if ended.is_empty():
		return _fail("no sabotage_ended for every client with both consoles held (citizens got %s / %s)" % [str(a.received("action_rejected")), str(b.received("action_rejected"))])
	if str(ended[0].get("kind")) != "fire" or str(ended[0].get("result")) != "fixed":
		return _fail("first fire ended as %s, expected fixed" % str(ended[0]))
	var phase1_s: float = float(Time.get_ticks_msec() - started_at) / 1000.0

	# Phase 2: nobody holds, the fire burns out and the meter rises.
	room.hub().idle(cooldown_s + COOLDOWN_MARGIN_S)
	p.skip_received("action_rejected")
	p.send("sabotage", {"kind": "fire"})
	started = room.wait_each(room.everyone(), "sabotage_started", MSG_S)
	if started.is_empty():
		return _fail("no second sabotage_started after the cooldown (polluter got %s)" % str(p.received("action_rejected")))
	var limit: int = Time.get_ticks_msec() + int((countdown_s + EXPIRE_MARGIN_S) * 1000.0)
	while room.hub().received("sabotage_ended").size() < 2:
		room.hub().poll()
		if Time.get_ticks_msec() >= limit or room.hub().expired():
			return _fail("the second fire did not end within %.0f s" % (countdown_s + EXPIRE_MARGIN_S))
		OS.delay_msec(10)
	room.hub().idle(SETTLE_S)
	var second: Dictionary = room.hub().received("sabotage_ended")[1]
	if str(second.get("kind")) != "fire" or str(second.get("result")) != "expired":
		return _fail("second fire ended as %s, expected expired" % str(second))
	var meters: Dictionary = _meters_around_end(room.hub().received("snapshot"))
	if meters.is_empty():
		return _fail("no snapshot pair around the end of the fire to read the meter from")
	var delta: float = float(meters["after"]) - float(meters["before"])
	if absf(delta - add) > METER_TOLERANCE:
		return _fail("meter went %.2f -> %.2f (+%.2f), expected +%.2f within %.1f" % [meters["before"], meters["after"], delta, add, METER_TOLERANCE])
	for c in [p, a, b]:
		var rejected: Array = (c as ProbeClient).received("action_rejected")
		if not rejected.is_empty():
			return _fail("an action was rejected: %s" % str(rejected[0]))
	return {"pass": true, "evidence": "fire 1 fixed by seats %d and %d holding fire_a and fire_b (%.1f s after the fire started); fire 2 expired after %.0f s with nobody holding, meter %.2f -> %.2f (+%.2f, expected +%.1f from meter.fire_expired_add); polluter seat %d; no action_rejected" % [
		room.seat_of[citizens[0]], room.seat_of[citizens[1]], phase1_s, countdown_s, meters["before"], meters["after"], delta, add, room.seat_of[polluters[0]]]}


## The meter of the last snapshot that still carried a sabotage and of the first one after it
## without; {} when the list has no such pair.
static func _meters_around_end(snapshots: Array) -> Dictionary:
	var last_with: int = -1
	for i in snapshots.size():
		if typeof((snapshots[i] as Dictionary).get("sabotage")) == TYPE_DICTIONARY:
			last_with = i
	if last_with < 0 or last_with + 1 >= snapshots.size():
		return {}
	return {
		"before": float((snapshots[last_with] as Dictionary).get("meter", 0.0)),
		"after": float((snapshots[last_with + 1] as Dictionary).get("meter", 0.0)),
	}
