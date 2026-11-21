extends RefCounted
## Shared steps for the full-round probe scenarios: eight probe clients fill every seat, the host
## starts the match, and meetings are called from the emergency console. No autoloads here
## (--script mode), so this only uses ProbeClient and GridPath. Client 0 is the hub: it polls the
## other seven, so every wait goes through it and no inbound buffer fills up.

const PLAYERS: int = 8
const CONNECT_S: float = 5.0
const START_S: float = 10.0
const PLAYING_S: float = 15.0
const MSG_S: float = 10.0
## Added to the server emergency spacing before the call, so clock skew never makes a call early.
const CALL_MARGIN_S: float = 1.0
const BUTTON_ID: String = "meeting_button"

var clients: Array[ProbeClient] = []
var seat_of: Array[int] = []
var role_of: Array[String] = []
var code: String = ""
var tuning: Dictionary = {}
var welcomes: Array[Dictionary] = []
## Time.get_ticks_msec() of the last moment the round was playing again (first playing snapshot,
## then each round_resumed). The server counts emergency spacing from there.
var playing_since_ms: int = 0
var deadline_ms: int = 0


func hub() -> ProbeClient:
	return clients[0]


## Opens the room with PLAYERS clients (the server must run the map of `grid`), starts the match
## and waits for the first playing snapshot.
## Returns "" or the reason it failed.
func open(url: String, grid: GridPath, deadline: int) -> String:
	deadline_ms = deadline
	for i in PLAYERS:
		var c := ProbeClient.new()
		c.deadline_ms = deadline_ms
		clients.append(c)
	for i in range(1, PLAYERS):
		hub().companions.append(clients[i])
	if not hub().connect_to(url) or hub().wait_for("hello_ok", CONNECT_S).is_empty():
		return "host got no hello_ok from %s" % url
	hub().send("create", {"name": "Probe0"})
	var w0: Dictionary = hub().wait_for("welcome", CONNECT_S)
	if w0.is_empty():
		return "host got no welcome after create"
	code = str(w0["room"])
	tuning = w0.get("tuning", {})
	if str(w0.get("map_id")) != grid.map_id() or str(w0.get("map_version")) != grid.map_version():
		return "server runs map %s %s but --map is %s %s" % [str(w0.get("map_id")), str(w0.get("map_version")), grid.map_id(), grid.map_version()]
	welcomes.append(w0)
	for i in range(1, PLAYERS):
		if not clients[i].connect_to(url) or clients[i].wait_for("hello_ok", CONNECT_S).is_empty():
			return "client %d got no hello_ok" % i
		clients[i].send("join", {"code": code, "name": "Probe%d" % i})
		var w: Dictionary = clients[i].wait_for("welcome", CONNECT_S)
		if w.is_empty():
			return "client %d got no welcome (join_failed: %s)" % [i, str(clients[i].received("join_failed"))]
		welcomes.append(w)
	for w in welcomes:
		seat_of.append(int(w["seat"]))
	hub().send("start")
	var ms: Array[Dictionary] = wait_each(everyone(), "match_started", START_S)
	if ms.is_empty():
		return "not every client got match_started"
	for i in PLAYERS:
		role_of.append(str(ms[i].get("role", "")))
		var spawn: Array = ms[i]["spawn"]
		clients[i].pos = Vector2(float(spawn[0]), float(spawn[1]))
	# The server ignores moves during the intro; wait for the first snapshot of the playing phase.
	while true:
		var s: Dictionary = hub().wait_for("snapshot", PLAYING_S)
		if s.is_empty():
			return "host got no playing snapshot"
		if s.get("phase") == "playing":
			break
	playing_since_ms = Time.get_ticks_msec()
	return ""


## Client `who` walks to `target` (tiles) while its sockets-mates are still read: the walker polls
## all the others and the others poll nobody (no poll cycle), then the hub polling is restored.
## False when it cannot get there (see ProbeClient.walk_to).
func walk_polled(who: int, target: Vector2, grid: GridPath) -> bool:
	_set_poller(who)
	var ok: bool = clients[who].walk_to(target, grid)
	_set_poller(0)
	return ok


func _set_poller(poller: int) -> void:
	for c in clients:
		c.companions.clear()
	for i in clients.size():
		if i != poller:
			clients[poller].companions.append(clients[i])


## Client indices 0..PLAYERS-1.
func everyone() -> Array[int]:
	var all: Array[int] = []
	for i in PLAYERS:
		all.append(i)
	return all


## Client indices whose role is polluter.
func polluters() -> Array[int]:
	var out: Array[int] = []
	for i in role_of.size():
		if role_of[i] == "polluter":
			out.append(i)
	return out


## One message of type `t` from every client in `who` (oldest not yet taken). Empty array when
## one is missing after `timeout_s` or the deadline passes. Results are in the order of `who`.
func wait_each(who: Array[int], t: String, timeout_s: float) -> Array[Dictionary]:
	var got: Dictionary = {}
	var limit: int = mini(Time.get_ticks_msec() + int(timeout_s * 1000.0), deadline_ms)
	while true:
		hub().poll()
		for i in who:
			if not got.has(i):
				var m: Dictionary = clients[i].wait_for(t, 0.0)
				if not m.is_empty():
					got[i] = m
		if got.size() == who.size():
			break
		if Time.get_ticks_msec() >= limit:
			return []
		OS.delay_msec(5)
	var out: Array[Dictionary] = []
	for i in who:
		out.append(got[i])
	return out


## Server spacing between meetings from the welcome tuning, or -1.0 when the key is missing.
func emergency_min_since_s() -> float:
	var m: Variant = tuning.get("meeting")
	if typeof(m) != TYPE_DICTIONARY or not (m as Dictionary).has("emergency_min_since_meeting_s"):
		return -1.0
	return float((m as Dictionary)["emergency_min_since_meeting_s"])


## The host walks to the emergency console and calls a meeting, at least the server spacing
## after the round last started playing. Returns {"error"} or {"gap_s", "started": [...]}, where
## `started` is meeting_started for each client in `who`. `gap_s` is how long the round had been
## playing when call_meeting was sent.
func call_emergency(grid: GridPath, who: Array[int]) -> Dictionary:
	var button: Variant = grid.marker_pos(BUTTON_ID)
	if button == null:
		return {"error": "map has no console %s" % BUTTON_ID}
	if not hub().walk_to(button, grid):
		return {"error": "host could not walk to %s %s from %s (no route, correction, closed socket or deadline)" % [BUTTON_ID, str(button), str(hub().pos)]}
	var min_since_s: float = emergency_min_since_s()
	if min_since_s < 0.0:
		return {"error": "welcome.tuning has no meeting.emergency_min_since_meeting_s"}
	var call_at: int = playing_since_ms + int((min_since_s + CALL_MARGIN_S) * 1000.0)
	while Time.get_ticks_msec() < call_at and not hub().expired():
		hub().idle(0.1)
	var gap_s: float = float(Time.get_ticks_msec() - playing_since_ms) / 1000.0
	hub().skip_received("action_rejected")
	hub().send("call_meeting")
	var started: Array[Dictionary] = wait_each(who, "meeting_started", MSG_S)
	if started.is_empty():
		return {"error": "no meeting_started for every client after call_meeting (action_rejected: %s)" % str(hub().received("action_rejected"))}
	return {"gap_s": gap_s, "started": started}


## Waits out the discussion, every client in `voters` votes for `target_idx`, then follows the
## result and the ejection. Returns {"error"} or {"result": meeting_result, "ejection": ejection}.
func vote_out(voters: Array[int], target_idx: int) -> Dictionary:
	var m: Dictionary = tuning.get("meeting", {})
	var voting: Array[Dictionary] = wait_each(voters, "voting_started", float(m.get("discussion_s", 60.0)) + MSG_S)
	if voting.is_empty():
		return {"error": "voting never started for every voter"}
	var rejected_before: Dictionary = {}
	for i in voters:
		rejected_before[i] = clients[i].received("action_rejected").size()
		clients[i].send("vote", {"target": seat_of[target_idx]})
	var results: Array[Dictionary] = wait_each(voters, "meeting_result", float(m.get("voting_s", 30.0)) + MSG_S)
	if results.is_empty():
		return {"error": "no meeting_result for every voter"}
	var result: Dictionary = results[0]
	var votes: Dictionary = result.get("votes", {})
	for i in voters:
		var rejected: Array = clients[i].received("action_rejected").slice(int(rejected_before[i]))
		for r in rejected:
			if str(r.get("action")) == "vote":
				return {"error": "server rejected the vote of seat %d: %s" % [seat_of[i], str(r)]}
		var counted: Variant = votes.get(str(seat_of[i]))
		if (typeof(counted) != TYPE_FLOAT and typeof(counted) != TYPE_INT) or int(counted) != seat_of[target_idx]:
			return {"error": "vote of seat %d was not counted for seat %d (meeting_result votes: %s)" % [seat_of[i], seat_of[target_idx], str(votes)]}
	if int(result.get("ejected", -1)) != seat_of[target_idx]:
		return {"error": "meeting_result ejected %s, expected seat %d (votes %s)" % [str(result.get("ejected")), seat_of[target_idx], str(votes)]}
	var ejections: Array[Dictionary] = wait_each(voters, "ejection", float(m.get("results_s", 5.0)) + MSG_S)
	if ejections.is_empty():
		return {"error": "no ejection for every voter"}
	return {"result": result, "ejection": ejections[0]}


func close_all() -> void:
	for c in clients:
		c.close()
