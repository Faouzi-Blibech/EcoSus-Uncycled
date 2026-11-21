extends RefCounted
## Eight clients play a whole match: two emergency meetings, everybody votes for one polluter each
## time. PASS on game_over with winner "citizens" and reason "polluters_voted_out", for every client.

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
const GAME_OVER_S: float = 30.0
## Takes about 175 s; probe.gd uses this when there is no --timeout (its default is 90 s).
const DEFAULT_TIMEOUT_S: float = 360.0


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var room := Room.new()
	var result: Dictionary = _play(room, url, grid, deadline_ms)
	room.close_all()
	return result


static func _play(room: Room, url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var err: String = room.open(url, grid, deadline_ms)
	if not err.is_empty():
		return {"pass": false, "reason": err}
	var pol: Array[int] = room.polluters()
	if pol.size() < 2:
		return {"pass": false, "reason": "expected at least 2 polluters, roles are %s" % str(room.role_of)}
	# The host (client 0) walks to the console both times, so it must stay active: vote out the
	# polluter that is not the host first.
	var first: int = pol[0] if pol[0] != 0 else pol[1]
	var second: int = pol[1] if first == pol[0] else pol[0]
	var voters: Array[int] = room.everyone()
	var gaps: Array[float] = []
	var ejected: Array[int] = []
	var started_at: int = Time.get_ticks_msec()

	for round_no in 2:
		var target: int = first if round_no == 0 else second
		var call: Dictionary = room.call_emergency(grid, voters)
		if call.has("error"):
			return {"pass": false, "reason": "meeting %d: %s" % [round_no + 1, call["error"]]}
		gaps.append(float(call["gap_s"]))
		var mstart: Dictionary = (call["started"] as Array)[0]
		if str(mstart.get("cause")) != "emergency" or int(mstart.get("by", -1)) != room.seat_of[0]:
			return {"pass": false, "reason": "meeting %d started with cause %s by %s, expected emergency by seat %d" % [round_no + 1, str(mstart.get("cause")), str(mstart.get("by")), room.seat_of[0]]}
		var voted: Dictionary = room.vote_out(voters, target)
		if voted.has("error"):
			return {"pass": false, "reason": "meeting %d: %s" % [round_no + 1, voted["error"]]}
		ejected.append(room.seat_of[target])
		voters.erase(target)
		if round_no == 0:
			var resumed: Array[Dictionary] = room.wait_each(voters, "round_resumed", Room.MSG_S + float(room.tuning.get("meeting", {}).get("results_s", 5.0)))
			if resumed.is_empty():
				return {"pass": false, "reason": "no round_resumed after the first ejection"}
			room.playing_since_ms = Time.get_ticks_msec()
			var spawn: Array = resumed[0]["spawn"]
			room.hub().pos = Vector2(float(spawn[0]), float(spawn[1]))

	var overs: Array[Dictionary] = room.wait_each(voters, "game_over", GAME_OVER_S)
	if overs.is_empty():
		return {"pass": false, "reason": "no game_over for every remaining client after the second ejection"}
	var over: Dictionary = overs[0]
	if str(over.get("winner")) != "citizens" or str(over.get("reason")) != "polluters_voted_out":
		return {"pass": false, "reason": "game_over was winner %s reason %s, expected citizens / polluters_voted_out" % [str(over.get("winner")), str(over.get("reason"))]}
	var roles: Dictionary = over.get("roles", {})
	for seat in ejected:
		if str(roles.get(str(seat))) != "polluter":
			return {"pass": false, "reason": "game_over roles say seat %d is %s, it was voted out as a polluter" % [seat, str(roles.get(str(seat)))]}
	var min_since_s: float = room.emergency_min_since_s()
	if gaps[1] < min_since_s:
		return {"pass": false, "reason": "second call was only %.1f s after round_resumed (server minimum %.1f s)" % [gaps[1], min_since_s]}
	return {"pass": true, "evidence": "game_over winner=%s reason=%s after 2 emergency meetings called by seat %d (second %.1f s after round_resumed, minimum %.0f s); polluters voted out: seat %d then seat %d; %d of %d remaining clients got game_over; timeline %d entries; %.0f s total" % [str(over["winner"]), str(over["reason"]), room.seat_of[0], gaps[1], min_since_s, ejected[0], ejected[1], overs.size(), voters.size(), (over.get("timeline", []) as Array).size(), float(Time.get_ticks_msec() - started_at) / 1000.0]}
