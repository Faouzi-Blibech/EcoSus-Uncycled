extends RefCounted
## Two clients type lines in the discussion. PASS when every client gets each typed `chat` (a `text`,
## no phrase id) from the right seat in the same order, the client's own line format shows the text
## as typed (BBCode-looking text included), and the server refuses, with the reason of the contract,
## a line over the length limit (too_long), a second line inside the gap (rate_limited) and the line
## after the last allowed one (text_limit). The limits are read from the welcome tuning when the
## server sends them, else the client defaults (120 characters, 5 typed lines, 2 s gap).

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
const ChatLimits := preload("res://content/chat_limits.gd")
const I18nLoader := preload("res://i18n/i18n_loader.gd")
const DEFAULT_TIMEOUT_S: float = 120.0
const SPEAKER_A: int = 1
const SPEAKER_B: int = 2
## Added to the server's chat gap before the next line so clock skew never makes it early.
const GAP_MARGIN_S: float = 0.3
const REFUSAL_S: float = 5.0


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var room := Room.new()
	var result: Dictionary = _play(room, url, grid, deadline_ms)
	room.close_all()
	return result


## One typed line from `who`, and the `chat` every client gets for it ({} and a reason when one is missing).
static func _say(room: Room, who: int, text: String) -> Array[Dictionary]:
	room.clients[who].send("chat_text", {"text": text})
	return room.wait_each(room.everyone(), "chat", Room.MSG_S)


## The refusal of `who` for the last send, "" when none came.
static func _refusal(room: Room, who: int) -> String:
	var got: Array[Dictionary] = room.wait_each([who], "action_rejected", REFUSAL_S)
	return "" if got.is_empty() else str((got[0] as Dictionary).get("reason", ""))


static func _play(room: Room, url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var err: String = room.open(url, grid, deadline_ms)
	if not err.is_empty():
		return {"pass": false, "reason": err}
	var meeting: Dictionary = room.tuning.get("meeting", {}) as Dictionary
	var max_chars: int = int(meeting.get("text_max_chars", ChatLimits.TEXT_MAX_CHARS))
	var per_meeting: int = int(meeting.get("text_per_meeting", ChatLimits.TEXT_PER_MEETING))
	var gap_s: float = float(meeting.get("chat_min_gap_s", ChatLimits.MIN_GAP_S)) + GAP_MARGIN_S
	var call: Dictionary = room.call_emergency(grid, room.everyone())
	if call.has("error"):
		return {"pass": false, "reason": call["error"]}
	var seat_a: int = room.seat_of[SPEAKER_A]
	var seat_b: int = room.seat_of[SPEAKER_B]
	var line_a: String = "I think Probe%d is sus" % room.seat_of[3]
	var line_b: String = "[b]not me[/b] [url=http://a.example]100%[/url]"

	var first: Array[Dictionary] = _say(room, SPEAKER_A, line_a)
	if first.is_empty():
		return {"pass": false, "reason": "not every client got the first typed line (action_rejected: %s)" % str(room.clients[SPEAKER_A].received("action_rejected"))}
	room.hub().idle(gap_s)
	var second: Array[Dictionary] = _say(room, SPEAKER_B, line_b)
	if second.is_empty():
		return {"pass": false, "reason": "not every client got the second typed line (action_rejected: %s)" % str(room.clients[SPEAKER_B].received("action_rejected"))}

	I18nLoader.load_csv()
	TranslationServer.set_locale("en")
	var format: String = TranslationServer.translate(&"MEETING_CHAT_LINE")
	var shown: String = ""
	for i in Room.PLAYERS:
		var a: Dictionary = first[i]
		var b: Dictionary = second[i]
		if int(a.get("seat", -1)) != seat_a or str(a.get("text", "")) != line_a or a.has("id") or a.has("ghost"):
			return {"pass": false, "reason": "client %d got %s, expected the typed line %s from seat %d" % [i, str(a), line_a, seat_a]}
		if int(b.get("seat", -1)) != seat_b or str(b.get("text", "")) != line_b or b.has("id") or b.has("ghost"):
			return {"pass": false, "reason": "client %d got %s, expected the typed line %s from seat %d" % [i, str(b), line_b, seat_b]}
		if i == 0:
			shown = "%s ;; %s" % [format % ["Probe%d" % SPEAKER_A, a["text"]], format % ["Probe%d" % SPEAKER_B, b["text"]]]

	# Refusals. Over the length: nothing is truncated, the server says too_long.
	room.hub().idle(gap_s)
	room.clients[SPEAKER_A].skip_received("action_rejected")
	room.clients[SPEAKER_A].send("chat_text", {"text": "x".repeat(max_chars + 1)})
	var too_long: String = _refusal(room, SPEAKER_A)
	if too_long != "too_long":
		return {"pass": false, "reason": "a %d character line got reason '%s', expected too_long" % [max_chars + 1, too_long]}

	# A line, then another at once: the second is inside the gap.
	room.hub().idle(gap_s)
	var sent: int = 1
	if _say(room, SPEAKER_A, "quick one").is_empty():
		return {"pass": false, "reason": "the line after the refusal did not come back"}
	sent += 1
	room.clients[SPEAKER_A].skip_received("action_rejected")
	room.clients[SPEAKER_A].send("chat_text", {"text": "too soon"})
	var rate: String = _refusal(room, SPEAKER_A)
	if rate != "rate_limited":
		return {"pass": false, "reason": "a second line inside the gap got reason '%s', expected rate_limited" % rate}

	# Fill the allowance, then one more.
	while sent < per_meeting:
		room.hub().idle(gap_s)
		if _say(room, SPEAKER_A, "line %d" % (sent + 1)).is_empty():
			return {"pass": false, "reason": "typed line %d of %d did not come back" % [sent + 1, per_meeting]}
		sent += 1
	room.hub().idle(gap_s)
	room.clients[SPEAKER_A].skip_received("action_rejected")
	room.clients[SPEAKER_A].send("chat_text", {"text": "one too many"})
	var limit: String = _refusal(room, SPEAKER_A)
	if limit != "text_limit":
		return {"pass": false, "reason": "line %d got reason '%s', expected text_limit" % [per_meeting + 1, limit]}
	return {"pass": true, "evidence": "all %d clients got both typed lines in order; client 0 reads (%s); too_long at %d characters, rate_limited inside %.1f s, text_limit after %d lines" % [Room.PLAYERS, shown, max_chars + 1, gap_s - GAP_MARGIN_S, per_meeting]}
