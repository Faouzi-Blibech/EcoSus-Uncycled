extends RefCounted
## One client creates a room and starts it; the other seven seats are bots. The client stands
## still and only listens until game_over. PASS when game_over arrives and at least MIN_MOVERS
## different bot seats were seen changing position, so the bots really act. Evidence: seats seen
## moving, meetings and the task bar published in each meeting_started, the winner and the timeline.
## Contract shared with the other scenario files: `static func run(url, grid, deadline_ms)`.

## A full round with a human who does no tasks lasts up to the deadline plus the meetings.
const DEFAULT_TIMEOUT_S: float = 1500.0
const CONNECT_S: float = 5.0
const START_S: float = 10.0
const MIN_MOVERS: int = 3
const MOVE_EPS_TILES: float = 0.5


static func run(url: String, _grid: GridPath, deadline_ms: int) -> Dictionary:
	var c := ProbeClient.new()
	c.deadline_ms = deadline_ms
	var result: Dictionary = _play(c, url)
	c.close()
	return result


static func _fail(reason: String) -> Dictionary:
	return {"pass": false, "reason": reason}


static func _play(c: ProbeClient, url: String) -> Dictionary:
	if not c.connect_to(url) or c.wait_for("hello_ok", CONNECT_S).is_empty():
		return _fail("no hello_ok from %s" % url)
	c.send("create", {"name": "Probe0"})
	var welcome: Dictionary = c.wait_for("welcome", CONNECT_S)
	if welcome.is_empty():
		return _fail("no welcome after create")
	var me: int = int(welcome["seat"])
	c.send("start")
	var started: Dictionary = c.wait_for("match_started", START_S)
	if started.is_empty():
		return _fail("no match_started")
	var bot_seats: Array[int] = []
	for s in started.get("seats", []):
		if not bool(s.get("human", false)):
			bot_seats.append(int(s["seat"]))
	if bot_seats.size() != 7:
		return _fail("expected 7 bot seats, match_started lists %d" % bot_seats.size())
	var first_pos: Dictionary = {}
	var movers: Dictionary = {}
	var snapshots: int = 0
	var meetings: int = 0
	var task_bars: Array = []
	var last_match_t: float = 0.0
	var over: Dictionary = {}
	while over.is_empty():
		if c.expired() or not c.is_open():
			break
		c.poll()
		var snap: Dictionary = c.wait_for("snapshot", 0.0)
		while not snap.is_empty():
			snapshots += 1
			last_match_t = float(snap.get("match_t", last_match_t))
			for p in snap.get("players", []):
				var seat: int = int(p["seat"])
				if seat == me:
					continue
				var pos := Vector2(float(p["pos"][0]), float(p["pos"][1]))
				if not first_pos.has(seat):
					first_pos[seat] = pos
				elif (first_pos[seat] as Vector2).distance_to(pos) > MOVE_EPS_TILES:
					movers[seat] = true
			snap = c.wait_for("snapshot", 0.0)
		var meeting: Dictionary = c.wait_for("meeting_started", 0.0)
		while not meeting.is_empty():
			meetings += 1
			task_bars.append(snappedf(float(meeting.get("task_bar", 0.0)), 0.01))
			meeting = c.wait_for("meeting_started", 0.0)
		over = c.wait_for("game_over", 0.0)
		OS.delay_msec(20)
	if over.is_empty():
		return _fail("no game_over (snapshots %d, last match_t %.0f s, meetings %d, bots seen moving %d)" % [snapshots, last_match_t, meetings, movers.size()])
	var kinds: Dictionary = {}
	for e in over.get("timeline", []):
		var k: String = str(e.get("kind"))
		kinds[k] = int(kinds.get(k, 0)) + 1
	var evidence: String = "game_over winner=%s reason=%s match_t=%.1f; %d snapshots; bot seats seen moving %d of %d (%s); meetings %d, task_bar at each meeting %s; timeline %s" % [str(over.get("winner")), str(over.get("reason")), float(over.get("match_t", last_match_t)), snapshots, movers.size(), bot_seats.size(), str(movers.keys()), meetings, str(task_bars), str(kinds)]
	if movers.size() < MIN_MOVERS:
		return _fail("only %d bot seats seen moving: %s" % [movers.size(), evidence])
	return {"pass": true, "evidence": evidence}
