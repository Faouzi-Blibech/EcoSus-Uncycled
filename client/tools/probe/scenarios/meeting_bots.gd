extends RefCounted
## One client plus seven server bots. Prints every meeting chat line the client receives, with names
## filled in and the seconds since meeting_started, then the vote result.
## Modes (the scenario name in probe.gd): "meeting_bots" = the client walks to the emergency console, calls a meeting
## and accuses the first bot seat in the discussion, so the bots have something to answer;
## "meeting_bots_listen" = the client stands still and only reads the first meeting the bots start by themselves.
## PASS when the meeting ends with a result and at least MIN_BOT_LINES chat lines from bots were received.
## Contract shared with the other scenario files: `static func run(url, grid, deadline_ms)`.

const Phrases := preload("res://content/phrases.gd")
const I18nLoader := preload("res://i18n/i18n_loader.gd")
const DEFAULT_TIMEOUT_S: float = 420.0
const CONNECT_S: float = 5.0
const START_S: float = 10.0
const PLAYING_S: float = 15.0
const BUTTON_ID: String = "meeting_button"
const CALL_MARGIN_S: float = 1.0
const ACCUSE_AFTER_S: float = 6.0
const MIN_BOT_LINES: int = 3


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	return play(url, grid, deadline_ms, true)


static func play(url: String, grid: GridPath, deadline_ms: int, call_it: bool) -> Dictionary:
	var c := ProbeClient.new()
	c.deadline_ms = deadline_ms
	var result: Dictionary = _play(c, url, grid, call_it)
	c.close()
	return result


static func _fail(reason: String) -> Dictionary:
	return {"pass": false, "reason": reason}


static func _play(c: ProbeClient, url: String, grid: GridPath, call_it: bool) -> Dictionary:
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
	var names: Dictionary = {}
	var first_bot: int = -1
	for s in started.get("seats", []):
		names[int(s["seat"])] = str(s["name"])
		if not bool(s.get("human", false)) and first_bot < 0:
			first_bot = int(s["seat"])
	var spawn: Array = started["spawn"]
	c.pos = Vector2(float(spawn[0]), float(spawn[1]))
	while true:
		var snap: Dictionary = c.wait_for("snapshot", PLAYING_S)
		if snap.is_empty():
			return _fail("no playing snapshot")
		if snap.get("phase") == "playing":
			break
	var playing_since_ms: int = Time.get_ticks_msec()

	if call_it:
		var button: Variant = grid.marker_pos(BUTTON_ID)
		if button == null or not c.walk_to(button, grid):
			return _fail("could not walk to %s" % BUTTON_ID)
		var min_since: float = float((welcome.get("tuning", {}).get("meeting", {}) as Dictionary).get("emergency_min_since_meeting_s", 15.0))
		var call_at: int = playing_since_ms + int((min_since + CALL_MARGIN_S) * 1000.0)
		while Time.get_ticks_msec() < call_at and not c.expired():
			c.idle(0.1)
		c.skip_received("action_rejected")
		c.send("call_meeting")

	var ms: Dictionary = c.wait_for("meeting_started", 400.0)
	if ms.is_empty():
		return _fail("no meeting_started (rejected: %s)" % str(c.received("action_rejected")))
	var t0: int = Time.get_ticks_msec()
	I18nLoader.load_csv()
	TranslationServer.set_locale("en")
	var namer: Callable = func(seat: int) -> String: return str(names.get(seat, "seat %d" % seat))
	print("MEETING by %s (%s), living %s" % [namer.call(int(ms.get("by", -1))), str(ms.get("cause")), str(ms.get("living"))])
	var bot_lines: int = 0
	var accused: bool = not call_it
	var result: Dictionary = {}
	while result.is_empty() and not c.expired() and c.is_open():
		c.poll()
		if not accused and Time.get_ticks_msec() - t0 >= int(ACCUSE_AFTER_S * 1000.0):
			c.send("chat_phrase", {"id": "sus", "args": [first_bot]})
			accused = true
		var chat: Dictionary = c.wait_for("chat", 0.0)
		while not chat.is_empty():
			var seat: int = int(chat.get("seat", -1))
			var line: String = Phrases.render(str(chat.get("id")), chat.get("args", []), namer)
			print("t+%5.1fs  %s: %s" % [float(Time.get_ticks_msec() - t0) / 1000.0, namer.call(seat), line])
			if seat != me:
				bot_lines += 1
			chat = c.wait_for("chat", 0.0)
		if not c.wait_for("voting_started", 0.0).is_empty():
			print("t+%5.1fs  -- voting starts" % (float(Time.get_ticks_msec() - t0) / 1000.0))
		result = c.wait_for("meeting_result", 0.0)
		OS.delay_msec(20)
	if result.is_empty():
		return _fail("no meeting_result (bot lines %d)" % bot_lines)
	var votes: Dictionary = result.get("votes", {})
	var shown: Array[String] = []
	for k in votes.keys():
		var v: Variant = votes[k]
		shown.append("%s->%s" % [namer.call(int(k)), "skip" if typeof(v) == TYPE_STRING else namer.call(int(v))])
	print("RESULT ejected %s; votes: %s" % [str(result.get("ejected", "nobody")), ", ".join(shown)])
	if bot_lines < MIN_BOT_LINES:
		return _fail("only %d bot chat lines" % bot_lines)
	return {"pass": true, "evidence": "%d bot chat lines, %d votes" % [bot_lines, votes.size()]}
