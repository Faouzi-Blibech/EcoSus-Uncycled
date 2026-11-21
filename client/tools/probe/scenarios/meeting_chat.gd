extends RefCounted
## Two clients send a phrase each in the discussion. PASS when every client gets both `chat`
## messages in the same order, with the seat and the args as sent, and the client's own phrase
## renderer (content/phrases.gd, the i18n file) turns each into a line with seat names and the zone name.
## The server may allow fewer lines or stages than the client rules; this sends two in discussion.

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
const Phrases := preload("res://content/phrases.gd")
const I18nLoader := preload("res://i18n/i18n_loader.gd")
## The wait for the emergency spacing and the walk to the console.
const DEFAULT_TIMEOUT_S: float = 120.0
const SPEAKER_A: int = 1
const SPEAKER_B: int = 2
const SEEN_A: int = 3
const SEEN_B: int = 4


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
	var seat_a: int = room.seat_of[SPEAKER_A]
	var seat_b: int = room.seat_of[SPEAKER_B]
	room.clients[SPEAKER_A].send("chat_phrase", {"id": "saw_dump", "args": [room.seat_of[SEEN_A], "depot"]})
	var first: Array[Dictionary] = room.wait_each(room.everyone(), "chat", Room.MSG_S)
	if first.is_empty():
		return {"pass": false, "reason": "not every client got the first chat (action_rejected: %s)" % str(room.clients[SPEAKER_A].received("action_rejected"))}
	room.clients[SPEAKER_B].send("chat_phrase", {"id": "with", "args": [room.seat_of[SEEN_B], "market"]})
	var second: Array[Dictionary] = room.wait_each(room.everyone(), "chat", Room.MSG_S)
	if second.is_empty():
		return {"pass": false, "reason": "not every client got the second chat (action_rejected: %s)" % str(room.clients[SPEAKER_B].received("action_rejected"))}

	I18nLoader.load_csv()
	var names: Dictionary = {}
	for i in Room.PLAYERS:
		names[room.seat_of[i]] = "Probe%d" % i
	var namer: Callable = func(seat: int) -> String: return str(names.get(seat, seat))
	var lines: Array[String] = []
	for locale in ["en", "fr"]:
		TranslationServer.set_locale(locale)
		for i in Room.PLAYERS:
			var a: Dictionary = first[i]
			var b: Dictionary = second[i]
			if int(a.get("seat", -1)) != seat_a or str(a.get("id")) != "saw_dump" or int(b.get("seat", -1)) != seat_b or str(b.get("id")) != "with":
				return {"pass": false, "reason": "client %d got chat %s then %s, expected saw_dump by seat %d then with by seat %d" % [i, str(a), str(b), seat_a, seat_b]}
			var line_a: String = Phrases.render("saw_dump", a.get("args", []), namer)
			var line_b: String = Phrases.render("with", b.get("args", []), namer)
			var want_zone_a: String = "Depot" if locale == "en" else "Dépôt"
			var want_zone_b: String = "Market" if locale == "en" else "Marché"
			if not (line_a.contains("Probe%d" % SEEN_A) and line_a.contains(want_zone_a) and line_b.contains("Probe%d" % SEEN_B) and line_b.contains(want_zone_b)) or line_a.contains("%") or line_b.contains("%"):
				return {"pass": false, "reason": "client %d (%s) lines read: [%s] [%s]" % [i, locale, line_a, line_b]}
			if i == SEEN_A:
				lines.append("%s: %s / %s" % [locale, "Probe%d: %s" % [SPEAKER_A, line_a], "Probe%d: %s" % [SPEAKER_B, line_b]])
	TranslationServer.set_locale("en")
	return {"pass": true, "evidence": "all %d clients got both chat lines in the same order; client %d reads (%s)" % [Room.PLAYERS, SEEN_A, " ;; ".join(lines)]}
