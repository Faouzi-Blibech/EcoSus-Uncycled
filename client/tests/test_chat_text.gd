extends "res://tests/test_case.gd"
## Typed meeting chat on the client: separate counters for phrases and typed lines, one short note per
## rejection, plain-text lines, "has voted" notes, and the ghost chat of out players.

const Phrases := preload("res://content/phrases.gd")
const ChatRules := preload("res://scenes/meeting/chat_rules.gd")
const PhrasePicker := preload("res://scenes/meeting/phrase_picker.gd")
const GhostPanel := preload("res://ui/hud/ghost_chat_panel.gd")
const Hud := preload("res://ui/hud/hud.gd")
const ProbeClient := preload("res://tools/probe/probe_client.gd")
const TEST_SAVE: String = "user://test_session.cfg"
const TEST_EJECTIONS: String = "user://test_ejections.cfg"
const SESSION_FIELDS: Array[String] = ["seat_id", "seats", "out_seats", "is_out", "phase", "meeting",
		"map_id", "phrases_used", "text_used", "ghost_chat", "last_chat_t", "tuning"]

var _saved: Dictionary = {}


func _session() -> Object:
	var s: Object = track(load("res://autoload/session.gd").new())
	s.save_path = TEST_SAVE
	s.ejections_path = TEST_EJECTIONS
	return s


func _seats() -> Array:
	return [
		{"seat": 0, "name": "Ana", "color": 0, "human": true},
		{"seat": 1, "name": "Bo", "color": 1, "human": true},
		{"seat": 2, "name": "Cyrus", "color": 2, "human": true, "out": true},
		{"seat": 3, "name": "Dee", "color": 3, "human": true},
	]


## A rejoin welcome into a discussion; `meeting` adds keys to the meeting state, `extra` to the top level.
func _welcome(meeting: Dictionary = {}, extra: Dictionary = {}) -> Dictionary:
	var mt: Dictionary = {"stage": "discussion", "ends_in": 40.0, "clues": [], "voted": [], "task_bar": 0.1}
	mt.merge(meeting, true)
	var data: Dictionary = {"seat": 0, "room": "ABCD", "token": "tok", "map_id": "greybox_01", "seats": _seats(),
		"state": {"phase": "meeting", "role": "citizen", "status": "active", "tasks": [], "meeting": mt}}
	data.merge(extra, true)
	return data


## A welcome of an out seat in the round.
func _out_welcome(ghost: Array) -> Dictionary:
	return {"seat": 2, "room": "ABCD", "token": "tok", "map_id": "greybox_01", "seats": _seats(),
		"ghost_chat": ghost,
		"state": {"phase": "playing", "role": "citizen", "status": "out", "tasks": []}}


## The autoload session set to a discussion (me = seat 0, living), saved fields restored by _restore().
func _live_session(meeting: Dictionary = {}) -> void:
	for f in SESSION_FIELDS:
		var v: Variant = Session.get(f)
		_saved[f] = v.duplicate(true) if v is Array or v is Dictionary else v
	Session.save_path = TEST_SAVE
	Session.ejections_path = TEST_EJECTIONS
	Session._on_welcome(_welcome(meeting))


func _restore() -> void:
	for f in SESSION_FIELDS:
		Session.set(f, _saved[f])


func _meeting_scene() -> Control:
	var m: Control = track(load("res://scenes/meeting/meeting.tscn").instantiate()) as Control
	m._ready()
	return m


## What the network does: the Session autoload sees an event first, then the scene.
func _feed(m: Control, ev: Dictionary) -> void:
	Session._on_event(ev)
	m._on_event(ev)


func _labels(node: Node) -> Array[Label]:
	var found: Array[Label] = []
	if node is Label:
		found.append(node as Label)
	for c in node.get_children():
		found.append_array(_labels(c))
	return found


func _label_texts(node: Node) -> Array[String]:
	var texts: Array[String] = []
	for l in _labels(node):
		texts.append(l.text)
	return texts


func _has_rich_text(node: Node) -> bool:
	if node is RichTextLabel:
		return true
	for c in node.get_children():
		if _has_rich_text(c):
			return true
	return false


# ---------------------------------------------------------------- counters

func test_counters_separate() -> bool:
	var s: Object = _session()
	s._on_welcome(_welcome())
	assert_eq(s.chat_left(), 5, "five phrases at the start")
	assert_eq(s.text_left(), 5, "five typed lines at the start")
	s._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_eq(s.chat_left(), 4, "a phrase takes one phrase")
	assert_eq(s.text_left(), 5, "and no typed line")
	s._on_event({"t": "chat", "seat": 0, "text": "hello there"})
	assert_eq(s.chat_left(), 4, "a typed line takes no phrase")
	assert_eq(s.text_left(), 4, "it takes one typed line")
	s._on_event({"t": "chat", "seat": 1, "text": "not mine"})
	s._on_event({"t": "chat", "seat": 1, "id": "hmm", "args": []})
	assert_eq(s.chat_left() + s.text_left(), 8, "another seat's lines count for nobody here")
	s._on_event({"t": "meeting_started", "cause": "report", "by": 1})
	assert_eq(s.chat_left(), 5, "a new meeting refills the phrases")
	assert_eq(s.text_left(), 5, "and the typed lines")
	return true


func test_rejoin_counters() -> bool:
	var s: Object = _session()
	s._on_welcome(_welcome({"my_chat": 2, "my_text": 3}))
	assert_eq(s.chat_left(), 3, "my_chat 2: three phrases left")
	assert_eq(s.text_left(), 2, "my_text 3: two typed lines left")
	s._on_welcome(_welcome({"my_chat": 5, "my_text": 5}))
	assert_eq(s.chat_left(), 0, "all phrases used")
	assert_eq(s.text_left(), 0, "all typed lines used")
	s._on_welcome(_welcome())
	assert_eq(s.text_left(), 5, "a welcome without my_text counts from 0 (the server refuses the excess)")
	return true


func test_limits_come_from_tuning_with_fallbacks() -> bool:
	var s: Object = _session()
	assert_eq(s.text_max_chars(), 120, "fallback: 120 characters")
	assert_eq(s.text_limit(), 5, "fallback: 5 typed lines")
	assert_eq(s.phrase_limit(), 5, "fallback: 5 phrases")
	assert_near(s.chat_gap_s(), 2.0, 0.001, "fallback: 2 s")
	s._on_welcome(_welcome({}, {"tuning": {"meeting": {"text_max_chars": 80, "text_per_meeting": 3,
			"phrases_per_meeting": 4, "chat_min_gap_s": 1.5}}}))
	assert_eq(s.text_max_chars(), 80, "tuning wins: characters")
	assert_eq(s.text_left(), 3, "tuning wins: typed lines")
	assert_eq(s.chat_left(), 4, "tuning wins: phrases")
	assert_near(s.chat_gap_s(), 1.5, 0.001, "tuning wins: gap")
	return true


func test_own_lines_start_the_gap_but_ghost_lines_take_no_count() -> bool:
	var s: Object = _session()
	s._on_welcome(_welcome())
	assert_true(s.chat_gap_left() <= 0.0, "no line yet: no wait")
	s._on_event({"t": "chat", "seat": 0, "text": "hi"})
	assert_true(s.chat_gap_left() > 1.0, "my line starts the gap")
	var o: Object = _session()
	o._on_welcome(_welcome())
	o._on_event({"t": "chat", "seat": 0, "text": "boo", "ghost": true})
	assert_eq(o.text_left(), 5, "a ghost line of mine takes no meeting line")
	assert_true(o.chat_gap_left() > 1.0, "but the gap applies to it too")
	return true


# ---------------------------------------------------------------- rejections

func test_reason_notes() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	var keys: Array[String] = []
	for reason in ["too_long", "text_limit", "phrase_limit", "rate_limited"]:
		var key: String = ChatRules.note_key(reason)
		assert_true(TranslationServer.translate(key) != key, "%s has text" % reason)
		assert_true(not keys.has(key), "%s has a note of its own" % reason)
		keys.append(key)
	assert_eq(ChatRules.note_key("wrong_phase"), "CHAT_REJECTED", "other reasons get the general note")

	_live_session()
	var m: Control = _meeting_scene()
	assert_true(m._text_edit.editable and not m._send_btn.disabled and not m._say_btn.disabled, "all open at first")

	_feed(m, {"t": "action_rejected", "action": "chat_text", "reason": "text_limit"})
	assert_true(not m._text_edit.editable and m._send_btn.disabled, "typed lines used up: the box and Send close")
	assert_true(not m._say_btn.disabled, "the phrases stay open")
	assert_eq(m._say_note.text, m.tr("REJECT_TEXT_LIMIT"), "its note")

	_restore()
	_live_session()
	m = _meeting_scene()
	_feed(m, {"t": "action_rejected", "action": "chat_phrase", "reason": "phrase_limit"})
	assert_true(m._say_btn.disabled, "phrases used up: Say closes")
	assert_true(m._text_edit.editable and not m._send_btn.disabled, "the typed box stays open")
	assert_eq(m._say_note.text, m.tr("REJECT_PHRASE_LIMIT"), "its note")

	for reason in ["too_long", "rate_limited"]:
		_restore()
		_live_session()
		m = _meeting_scene()
		_feed(m, {"t": "action_rejected", "action": "chat_text", "reason": reason})
		assert_true(m._text_edit.editable and not m._send_btn.disabled and not m._say_btn.disabled, "%s closes nothing" % reason)
		assert_eq(m._say_note.text, m.tr(ChatRules.note_key(reason)), "%s: its note" % reason)
	_restore()
	return true


func test_rate_note_once() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	var lines: int = m._chat_box.get_child_count()
	_feed(m, {"t": "action_rejected", "action": "chat_text", "reason": "rate_limited"})
	_feed(m, {"t": "action_rejected", "action": "chat_text", "reason": "rate_limited"})
	var notes: int = 0
	for t in _label_texts(m):
		if t == m.tr("REJECT_RATE_LIMITED"):
			notes += 1
	assert_eq(notes, 1, "two refusals in a row show one note, no stack")
	assert_eq(m._chat_box.get_child_count(), lines, "the note is not a chat line")
	m._process(m.FLASH_S + 0.1)
	assert_true(m._say_note.text != m.tr("REJECT_RATE_LIMITED"), "and it goes away by itself")
	_restore()
	return true


func test_send_keeps_the_text_on_a_refusal_and_clears_it_on_the_echo() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	m._text_edit.text = "   "
	m._on_send_text()
	assert_true(not m._awaiting_echo, "blank text is not sent")
	m._text_edit.text = "  I was in the depot  "
	m._on_send_text()
	assert_true(m._awaiting_echo and m._send_btn.disabled, "sent: no second send before the answer")
	_feed(m, {"t": "action_rejected", "action": "chat_text", "reason": "too_long"})
	assert_eq(m._text_edit.text, "  I was in the depot  ", "a refusal keeps what was typed")
	assert_true(not m._send_btn.disabled, "and opens Send again")
	m._on_send_text()
	_feed(m, {"t": "chat", "seat": 0, "text": "I was in the depot"})
	assert_eq(m._text_edit.text, "", "my own line coming back clears the box")
	_restore()
	return true


func test_text_box_length_follows_tuning() -> bool:
	_live_session()
	Session.tuning = {"meeting": {"text_max_chars": 60}}
	var m: Control = _meeting_scene()
	assert_eq(m._text_edit.max_length, 60, "max_length from tuning")
	_restore()
	_live_session()
	m = _meeting_scene()
	assert_eq(m._text_edit.max_length, 120, "the fallback when tuning has none")
	_restore()
	return true


# ---------------------------------------------------------------- lines and notes

func test_chat_line_plain_text() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	_feed(m, {"t": "chat", "seat": 1, "text": "[b]x[/b] [url=http://a.example]link[/url] [color=red]c[/color]"})
	var shown: String = ""
	for l in _labels(m._chat_box):
		shown = l.text
	assert_eq(shown, "Bo: [b]x[/b] [url=http://a.example]link[/url] [color=red]c[/color]", "BBCode shows as typed")
	assert_true(not _has_rich_text(m), "no rich text anywhere in the scene")
	_feed(m, {"t": "chat", "seat": 1, "text": "100% sure %s %d"})
	var last: String = ""
	for l in _labels(m._chat_box):
		last = l.text
	assert_eq(last, "Bo: 100% sure %s %d", "format characters in the text are not read as a format")
	_restore()
	return true


func test_has_voted_note() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	_feed(m, {"t": "voting_started", "voting_s": 30.0})
	_feed(m, {"t": "vote_cast", "seat": 3, "target": 1, "skip": false})
	var notes: Array[String] = []
	for l in _labels(m._chat_box):
		notes.append(l.text)
	assert_eq(notes, [m.tr("CHAT_HAS_VOTED") % "Dee"], "one note: who voted")
	assert_true(not " ".join(notes).contains("Bo"), "never the target")
	_feed(m, {"t": "vote_cast", "seat": 1})
	assert_eq(m._chat_box.get_child_count(), 2, "each vote adds its note")
	assert_eq(ChatRules.voted_note("Dee"), m.tr("CHAT_HAS_VOTED") % "Dee", "one place builds it")
	_restore()
	return true


# ---------------------------------------------------------------- ghost chat

func test_ghost_lines_from_the_welcome_and_events() -> bool:
	var s: Object = _session()
	s._on_welcome(_out_welcome([{"seat": 3, "text": "hello ghosts", "ghost": true},
			{"seat": 2, "id": "hmm", "args": [], "ghost": true}]))
	assert_true(s.is_out, "the welcome says I am out")
	assert_eq(s.ghost_chat.size(), 2, "the history comes with the welcome")
	s._on_event({"t": "chat", "seat": 3, "text": "again", "ghost": true})
	assert_eq(s.ghost_chat.size(), 3, "a ghost line is kept")
	s._on_event({"t": "chat", "seat": 3, "text": "a meeting line"})
	assert_eq(s.ghost_chat.size(), 3, "a meeting line is not a ghost line")
	s._on_welcome(_welcome())
	assert_eq(s.ghost_chat.size(), 0, "a living seat's welcome has none")
	return true


func test_ghost_panel_only_when_out() -> bool:
	for f in SESSION_FIELDS:
		var v: Variant = Session.get(f)
		_saved[f] = v.duplicate(true) if v is Array or v is Dictionary else v
	var panel: Control = track(GhostPanel.new()) as Control
	panel._ready()
	Session.seats = _seats()
	Session.is_out = false
	Session.phase = "playing"
	Session.ghost_chat = [{"seat": 3, "text": "stray", "ghost": true}]
	panel.sync()
	assert_true(not panel.visible, "a living player gets no ghost panel")
	assert_true(not _label_texts(panel).any(func(t: String) -> bool: return t.contains("stray")), "and no ghost line in it")
	Session.is_out = true
	Session.out_seats = {0: true}
	Session.seat_id = 0
	panel.sync()
	assert_true(panel.visible, "an out player does")
	assert_true(_label_texts(panel).any(func(t: String) -> bool: return t.contains("stray")), "with the lines so far")
	Session.ghost_chat.append({"seat": 1, "id": "not_me", "args": [], "ghost": true})
	Session.ghost_chat.append({"seat": 3, "text": "[b]boo[/b]", "ghost": true})
	panel.sync()
	var texts: Array[String] = _label_texts(panel)
	assert_true(texts.any(func(t: String) -> bool: return t.contains(Phrases.render("not_me", [], Callable()))), "a phrase line reads as its sentence")
	assert_true(texts.any(func(t: String) -> bool: return t.contains("Dee: [b]boo[/b]")), "a typed line shows as typed")
	assert_true(not _has_rich_text(panel), "plain text only")
	Session.is_out = false
	Session.out_seats = {}
	panel.sync()
	assert_true(not panel.visible, "back to living (next match): hidden again")
	_restore()
	return true


func test_ghost_panel_collapses_and_sends_typed_lines() -> bool:
	for f in SESSION_FIELDS:
		var v: Variant = Session.get(f)
		_saved[f] = v.duplicate(true) if v is Array or v is Dictionary else v
	Session.is_out = true
	Session.seat_id = 0
	Session.phase = "playing"
	Session.ghost_chat = []
	var panel: Control = track(GhostPanel.new()) as Control
	panel._ready()
	panel.sync()
	assert_true(panel.body.visible, "open at first")
	panel.toggle()
	assert_true(not panel.body.visible, "collapsed")
	panel.toggle()
	assert_true(panel.body.visible, "open again")
	assert_true(panel.can_send("hello"), "a typed line may go at any time of the match")
	assert_true(not panel.can_send("   "), "not blank text")
	Session.phase = "lobby"
	assert_true(not panel.can_send("hello"), "not in the lobby")
	Session.phase = "playing"
	panel._on_rejected({"action": "chat_text", "reason": "rate_limited"})
	panel._on_rejected({"action": "chat_text", "reason": "rate_limited"})
	assert_eq(panel.note.text, panel.tr("REJECT_RATE_LIMITED"), "one short note")
	panel._on_rejected({"action": "vote", "reason": "not_allowed"})
	assert_eq(panel.note.text, panel.tr("REJECT_RATE_LIMITED"), "another action's refusal is not its business")
	_restore()
	return true


func test_ghost_lines_stay_out_of_a_living_players_meeting() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	_feed(m, {"t": "chat", "seat": 3, "text": "secret", "ghost": true})
	assert_eq(m._chat_box.get_child_count(), 0, "a living player never shows a ghost line")
	Session.is_out = true
	Session.out_seats[0] = true
	_feed(m, {"t": "chat", "seat": 3, "text": "psst", "ghost": true})
	assert_eq(m._chat_box.get_child_count(), 1, "an out player does")
	var line: String = (m._chat_box.get_child(0) as Label).text
	assert_eq(line, m.tr("MEETING_GHOST_LINE") % ["Dee", "psst"], "tagged as a ghost line")
	_restore()
	return true


func _find_picker(node: Node) -> Node:
	if node.get_script() == PhrasePicker:
		return node
	for c in node.get_children():
		var found: Node = _find_picker(c)
		if found != null:
			return found
	return null


## Taps Say, picks the phrase "hmm" and sends it; true when a picker was there to do it.
func _send_hmm_through_the_picker(m: Control) -> bool:
	m._on_say()
	var picker: Node = _find_picker(m._left)
	if not m._picking or picker == null:
		return false
	picker.choose_kind("filler")
	picker.choose_phrase("hmm")
	picker.send()
	return true


func test_out_player_sends_ghost_phrases_and_lines_at_any_stage_of_the_meeting() -> bool:
	_live_session()
	Session.is_out = true
	Session.out_seats[0] = true
	var m: Control = _meeting_scene()
	var spy: Object = track(SpyNet.new())
	m.net = spy
	for stage in ["discussion", "voting", "results"]:
		Session.meeting["stage"] = stage
		m._rebuild()
		assert_true(m._chat_panel.visible, "%s: an out player keeps the chat panel" % stage)
		assert_true(not m._say_btn.disabled, "%s: Say is open" % stage)
		spy.last_type = ""
		assert_true(_send_hmm_through_the_picker(m), "%s: Say opens the picker" % stage)
		assert_eq(spy.last_type, "chat_phrase", "%s: a phrase was sent" % stage)
		assert_eq(spy.last_fields, {"id": "hmm", "args": []}, "%s: the phrase as picked" % stage)
		assert_true(not m._picking, "%s: the picker closes after the send" % stage)
		spy.last_type = ""
		m._awaiting_echo = false
		Session.last_chat_t = -1.0e9
		m._text_edit.text = "boo from stage " + stage
		m._on_send_text()
		assert_eq(spy.last_type, "chat_text", "%s: a typed line was sent" % stage)
		assert_eq(spy.last_fields, {"text": "boo from stage " + stage}, "%s: the typed text" % stage)
	_restore()
	return true


func test_a_living_player_gets_no_picker_outside_the_discussion() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	m.net = track(SpyNet.new())
	Session.meeting["stage"] = "voting"
	m._rebuild()
	assert_true(not _send_hmm_through_the_picker(m), "voting: Say is closed for a living player")
	assert_true(not m._picking, "and nothing opens")
	var p: Object = track(PhrasePicker.new())
	p.session = Session
	p.ghost = true
	assert_true(not p.is_enabled(), "a ghost picker is closed to a living player")
	_restore()
	return true


# ---------------------------------------------------------------- wire and keyboard

class SpyNet extends "res://autoload/net.gd":
	var last_type: String = ""
	var last_fields: Dictionary = {}

	func send(type: String, fields: Dictionary = {}) -> void:
		last_type = type
		last_fields = fields


func test_wire_chat_text_and_protocol_version() -> bool:
	var n: Object = track(SpyNet.new())
	n.send_chat_text("hello")
	assert_eq(n.last_type, "chat_text", "message type")
	assert_eq(n.last_fields, {"text": "hello"}, "the text and nothing else")
	assert_eq(n.PROTOCOL_VERSION, 1, "protocol version 1")
	assert_eq(ProbeClient.PROTOCOL_VERSION, n.PROTOCOL_VERSION, "the probe client says the same")
	return true


func test_keyboard_lift_keeps_the_box_above_the_soft_keyboard() -> bool:
	assert_eq(ChatRules.lift_px(0, 720, 360.0), 0.0, "no keyboard: no lift")
	assert_eq(ChatRules.lift_px(360, 720, 360.0), 180.0, "window pixels scaled to the 360 px viewport")
	assert_eq(ChatRules.lift_px(100, 0, 360.0), 0.0, "no window height: no lift, no division by zero")
	return true


func test_a_refused_chat_line_has_no_toast() -> bool:
	assert_true(not Hud.shows_rejection({"action": "chat_text", "reason": "rate_limited"}), "the ghost panel has its own note")
	assert_true(not Hud.shows_rejection({"action": "chat_phrase", "reason": "too_long"}), "so for a phrase")
	assert_true(Hud.shows_rejection({"action": "hit", "reason": "on_cooldown"}), "other refusals keep their toast")
	return true


# ---------------------------------------------------------------- the ghost history is capped

func _ghost_lines(prefix: String, count: int) -> Array:
	var lines: Array = []
	for i in count:
		lines.append({"seat": 3, "text": "%s %d" % [prefix, i], "ghost": true})
	return lines


func _line_texts(panel: Control) -> Array[String]:
	var texts: Array[String] = []
	for c in panel._lines.get_children():
		texts.append((c as Label).text)
	return texts


func _out_session_panel() -> Control:
	_live_session()
	Session.seats = _seats()
	Session.is_out = true
	Session.out_seats = {0: true}
	Session.seat_id = 0
	Session.phase = "playing"
	var panel: Control = track(GhostPanel.new()) as Control
	panel._ready()
	return panel


func test_ghost_panel_keeps_up_when_the_history_is_full() -> bool:
	var panel: Control = _out_session_panel()
	Session.ghost_chat = _ghost_lines("old", 100)
	panel.sync()
	assert_eq(panel._lines.get_child_count(), 100, "a full history is drawn")
	Session._on_event({"t": "chat", "seat": 3, "text": "newest", "ghost": true})
	assert_eq(Session.ghost_chat.size(), 100, "the history stays at 100")
	panel.sync()
	var texts: Array[String] = _line_texts(panel)
	assert_eq(texts.size(), 100, "still 100 labels")
	assert_eq(texts[99], "Dee: newest", "the new line is the last")
	assert_eq(texts[0], "Dee: old 1", "the oldest line is gone")
	for i in 3:
		Session._on_event({"t": "chat", "seat": 3, "text": "more %d" % i, "ghost": true})
	panel.sync()
	texts = _line_texts(panel)
	assert_eq(texts.size(), 100, "several at once: still 100")
	assert_eq(texts[99], "Dee: more 2", "in order, the last is last")
	assert_eq(texts[96], "Dee: newest", "and the one before them follows")
	_restore()
	return true


func test_ghost_panel_after_a_rejoin_with_a_full_history() -> bool:
	var panel: Control = _out_session_panel()
	Session.ghost_chat = _ghost_lines("before", 3)
	panel.sync()
	assert_eq(panel._lines.get_child_count(), 3, "the lines of the first session")
	var welcome: Dictionary = _out_welcome(_ghost_lines("rejoin", 100))
	Session._on_welcome(welcome)
	panel.sync()
	var texts: Array[String] = _line_texts(panel)
	assert_eq(texts.size(), 100, "the welcome history replaces the old lines")
	assert_eq(texts[0], "Dee: rejoin 0", "from its first line")
	Session._on_event({"t": "chat", "seat": 3, "text": "after rejoin", "ghost": true})
	panel.sync()
	texts = _line_texts(panel)
	assert_eq(texts.size(), 100, "a new line after the rejoin: still 100")
	assert_eq(texts[99], "Dee: after rejoin", "it shows")
	assert_eq(texts[0], "Dee: rejoin 1", "the oldest went")
	Session._on_welcome(_welcome())
	panel.sync()
	assert_true(not panel.visible, "a living welcome hides the panel")
	_restore()
	return true


func test_one_definition_of_out_and_no_ghost_line_kept_for_a_living_seat() -> bool:
	_live_session()
	Session.seats = _seats()
	Session.is_out = false
	Session.out_seats = {0: true}
	Session.seat_id = 0
	Session.phase = "playing"
	var panel: Control = track(GhostPanel.new()) as Control
	panel._ready()
	panel.sync()
	assert_true(panel.visible, "out by the seat list is out for the panel too")
	_restore()
	_live_session()
	Session._on_event({"t": "chat", "seat": 3, "text": "leaked", "ghost": true})
	assert_eq(Session.ghost_chat.size(), 0, "a ghost line that reaches a living seat is dropped")
	Session._on_event({"t": "action_rejected", "action": "chat_phrase", "reason": "not_allowed"})
	assert_eq(Session.chat_left(), 5, "not_allowed (seat out) no longer uses up the phrases")
	_restore()
	return true


# ---------------------------------------------------------------- compact typing layout on touch devices

func test_meeting_box_docks_at_the_top_while_it_has_focus_on_a_touch_device() -> bool:
	_live_session()
	var m: Control = _meeting_scene()
	m.touch = true
	assert_eq(m._chat_panel.get_child(0), m._chat_title, "normal layout: the title first")
	assert_eq(m._type_row.get_index(), 4, "the box is under the list and the counters")
	m._text_edit.focus_entered.emit()
	assert_eq(m._chat_panel.get_child(0), m._type_row, "focus in: the text row is first")
	assert_eq(m._chat_scroll.get_index(), 2, "the history sits right under the row and its note")
	assert_true(not m._top_row.visible and not m._left.visible, "the title row and the left column wait")
	assert_true(not m._say_btn.visible and not m._chat_title.visible and not m._counters.visible, "so do Say, the title and the counters")
	assert_true(m._done_btn.visible and m._send_btn.visible and m._text_edit.visible, "the row shows box, Send and Done")
	assert_eq(m._chat_panel.custom_minimum_size.x, 0.0, "the panel takes the whole width")
	m._text_edit.focus_exited.emit()
	assert_eq(m._chat_panel.get_child(0), m._chat_title, "focus out: back to the normal order")
	assert_eq(m._type_row.get_index(), 4, "box under the counters again")
	assert_true(m._top_row.visible and m._left.visible and m._say_btn.visible and m._counters.visible, "everything is back")
	assert_true(not m._done_btn.visible, "no Done")
	m._text_edit.focus_entered.emit()
	m._done_btn.pressed.emit()
	assert_true(m._left.visible and m._chat_panel.get_child(0) == m._chat_title, "Done leaves the compact layout")
	m.touch = false
	m._text_edit.focus_entered.emit()
	assert_true(m._left.visible and m._chat_panel.get_child(0) == m._chat_title, "a desktop keeps its layout")
	_restore()
	return true


func test_meeting_compact_layout_really_puts_the_box_on_top() -> bool:
	_live_session()
	var root: Window = (Engine.get_main_loop() as SceneTree).root
	var m: Control = load("res://scenes/meeting/meeting.tscn").instantiate() as Control
	root.add_child(m)
	m.touch = true
	await (Engine.get_main_loop() as SceneTree).process_frame
	var before: float = m._type_row.global_position.y
	m._text_edit.focus_entered.emit()
	await (Engine.get_main_loop() as SceneTree).process_frame
	await (Engine.get_main_loop() as SceneTree).process_frame
	var row_y: float = m._type_row.global_position.y
	assert_true(row_y < 24.0, "the text row is at the top of the screen (y %f)" % row_y)
	assert_true(row_y < before, "it moved up from %f" % before)
	assert_true(m._chat_scroll.global_position.y > row_y, "the history is under it")
	assert_true(m._chat_scroll.size.y > 60.0, "and has room")
	m.queue_free()
	await (Engine.get_main_loop() as SceneTree).process_frame
	_restore()
	return true


func test_ghost_panel_box_docks_at_the_top_while_it_has_focus_on_a_touch_device() -> bool:
	var panel: Control = _out_session_panel()
	panel.touch = true
	panel.sync()
	assert_eq(panel.anchor_top, 1.0, "normal: docked at the bottom")
	assert_eq(panel.body.get_child(0), panel._scroll, "the history first")
	panel.text_edit.focus_entered.emit()
	assert_eq(panel.anchor_top, 0.0, "focus in: docked at the top")
	assert_eq(panel.anchor_left, 0.0, "full width")
	assert_eq(panel.anchor_right, 1.0, "full width")
	assert_eq(panel.body.get_child(0), panel._type_row, "the text row first")
	assert_eq(panel._scroll.get_index(), 2, "the history right under the row and the note")
	assert_true(not panel._toggle_btn.visible and not panel._say_btn.visible, "no header and no Say")
	assert_true(panel._done_btn.visible, "Done shows")
	panel.text_edit.focus_exited.emit()
	assert_eq(panel.anchor_top, 1.0, "focus out: back at the bottom")
	assert_eq(panel.body.get_child(0), panel._scroll, "history first again")
	assert_true(panel._toggle_btn.visible and panel._say_btn.visible and not panel._done_btn.visible, "controls back")
	panel.touch = false
	panel.text_edit.focus_entered.emit()
	assert_eq(panel.anchor_top, 1.0, "not on a desktop")
	_restore()
	return true


func test_ghost_panel_sends_typed_lines_and_phrases_through_net() -> bool:
	var panel: Control = _out_session_panel()
	var spy: Object = track(SpyNet.new())
	panel.net = spy
	panel.sync()
	panel.text_edit.text = "psst"
	panel.send()
	assert_eq(spy.last_type, "chat_text", "a typed ghost line")
	assert_eq(spy.last_fields, {"text": "psst"}, "as typed")
	panel._open_picker()
	panel._picker.choose_kind("filler")
	panel._picker.choose_phrase("idk")
	panel._picker.send()
	assert_eq(spy.last_type, "chat_phrase", "a ghost phrase")
	assert_eq(spy.last_fields, {"id": "idk", "args": []}, "as picked")
	_restore()
	return true


func test_inbound_buffer_holds_a_large_welcome() -> bool:
	var n: Object = track(SpyNet.new())
	assert_eq(n._ws.inbound_buffer_size, 1 << 20, "Net: 1 MB inbound")
	assert_eq(ProbeClient.new()._ws.inbound_buffer_size, 1 << 20, "the probe client too")
	return true
