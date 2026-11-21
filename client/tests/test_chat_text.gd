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


func test_out_player_chats_as_a_ghost_in_the_meeting() -> bool:
	_live_session()
	Session.is_out = true
	Session.out_seats[0] = true
	var m: Control = _meeting_scene()
	assert_true(m._text_edit.editable and not m._send_btn.disabled, "an out player may type (ghost chat)")
	assert_true(not m._say_btn.disabled, "and pick phrases")
	var p: Object = track(PhrasePicker.new())
	p.session = Session
	p.ghost = true
	assert_true(p.is_enabled(), "the picker is open to a ghost")
	assert_true(p.seat_options().has(2), "a ghost may name a seat that is out")
	var living: Object = track(PhrasePicker.new())
	living.session = Session
	assert_true(not living.is_enabled(), "but not as a living picker")
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
