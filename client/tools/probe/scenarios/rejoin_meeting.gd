extends RefCounted
## One client closes its socket during the discussion and rejoins with its token. PASS when the
## welcome says phase "meeting", carries state.meeting.stage and lists all eight seats.

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
const RECONNECTING: int = 3
const CLOSE_SETTLE_S: float = 0.5
## A different display name: only the token may decide which seat comes back.
const REJOIN_NAME: String = "ProbeRejoined"


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var room := Room.new()
	var result: Dictionary = _play(room, url, grid, deadline_ms)
	room.close_all()
	return result


static func _play(room: Room, url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var err: String = room.open(url, grid, deadline_ms)
	if not err.is_empty():
		return {"pass": false, "reason": err}
	var call: Dictionary = room.call_emergency(grid, room.everyone())
	if call.has("error"):
		return {"pass": false, "reason": call["error"]}

	var who: int = RECONNECTING
	var token: String = str(room.welcomes[who].get("token", ""))
	if token.is_empty():
		return {"pass": false, "reason": "client %d got no token in its welcome" % who}
	room.clients[who].close()
	room.hub().idle(CLOSE_SETTLE_S)

	var back := ProbeClient.new()
	back.deadline_ms = deadline_ms
	var got: Dictionary = _rejoin(back, url, room.code, token)
	back.close()
	if got.has("error"):
		return {"pass": false, "reason": got["error"]}
	var w: Dictionary = got["welcome"]
	if str(w.get("phase")) != "meeting":
		return {"pass": false, "reason": "rejoin welcome.phase is %s, expected meeting" % str(w.get("phase"))}
	var state: Variant = w.get("state")
	if typeof(state) != TYPE_DICTIONARY or typeof((state as Dictionary).get("meeting")) != TYPE_DICTIONARY or not ((state as Dictionary)["meeting"] as Dictionary).has("stage"):
		return {"pass": false, "reason": "rejoin welcome has no state.meeting.stage (state: %s)" % str(state)}
	var seats: Variant = w.get("seats")
	if typeof(seats) != TYPE_ARRAY or (seats as Array).size() != Room.PLAYERS:
		return {"pass": false, "reason": "rejoin welcome.seats has %s entries, expected %d" % [str((seats as Array).size() if typeof(seats) == TYPE_ARRAY else "no"), Room.PLAYERS]}
	if int(w.get("seat", -1)) != room.seat_of[who]:
		return {"pass": false, "reason": "rejoin gave seat %s, expected the old seat %d" % [str(w.get("seat")), room.seat_of[who]]}
	var meeting: Dictionary = (state as Dictionary)["meeting"]
	if str(meeting["stage"]) != "discussion":
		return {"pass": false, "reason": "rejoin welcome.state.meeting.stage is %s, the socket was closed during the discussion" % str(meeting["stage"])}
	return {"pass": true, "evidence": "client %d closed its socket in the discussion and rejoined seat %d with its token under another name (%s): welcome.phase=%s, state.meeting.stage=%s (ends_in %s), %d seats listed" % [who, room.seat_of[who], REJOIN_NAME, str(w["phase"]), str(meeting["stage"]), str(meeting.get("ends_in")), (seats as Array).size()]}


static func _rejoin(c: ProbeClient, url: String, code: String, token: String) -> Dictionary:
	if not c.connect_to(url) or c.wait_for("hello_ok", 5.0).is_empty():
		return {"error": "rejoining client got no hello_ok"}
	c.send("join", {"code": code, "name": REJOIN_NAME, "token": token})
	var w: Dictionary = c.wait_for("welcome", 5.0)
	if w.is_empty():
		return {"error": "rejoining client got no welcome (join_failed: %s)" % str(c.received("join_failed"))}
	return {"welcome": w}
