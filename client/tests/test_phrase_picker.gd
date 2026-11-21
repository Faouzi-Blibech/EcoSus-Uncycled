extends "res://tests/test_case.gd"
## Meeting chat: the phrase catalogue, how a chat line reads, and when the picker may send.

const Phrases := preload("res://content/phrases.gd")
const PhrasePicker := preload("res://scenes/meeting/phrase_picker.gd")
const TEST_SAVE: String = "user://test_session.cfg"
const TEST_EJECTIONS: String = "user://test_ejections.cfg"

var _saved_locale: String = ""
var _sent: Array = []


func _session() -> Object:
	var s: Object = track(load("res://autoload/session.gd").new())
	s.save_path = TEST_SAVE
	s.ejections_path = TEST_EJECTIONS
	return s


## The welcome of a rejoin in a discussion with four seats (0 is me, 2 is out); `meeting` adds keys
## to the meeting state.
func _welcome_data(meeting: Dictionary = {}) -> Dictionary:
	var mt: Dictionary = {"stage": "discussion", "ends_in": 40.0, "clues": [], "voted": [], "task_bar": 0.1}
	mt.merge(meeting, true)
	return {"seat": 0, "room": "ABCD", "token": "tok", "map_id": "greybox_01",
		"seats": [
			{"seat": 0, "name": "Ana", "color": 0, "human": true},
			{"seat": 1, "name": "Bo", "color": 1, "human": true},
			{"seat": 2, "name": "Cy", "color": 2, "human": true, "out": true},
			{"seat": 3, "name": "Di", "color": 3, "human": true},
		],
		"state": {"phase": "meeting", "role": "citizen", "status": "active", "tasks": [], "meeting": mt}}


## A session in that discussion.
func _discussion_session() -> Object:
	var s: Object = _session()
	s._on_welcome(_welcome_data())
	return s


func _picker(s: Object) -> Object:
	var p: Object = track(PhrasePicker.new())
	p.session = s
	return p


func _on_send(id: String, args: Array) -> void:
	_sent.append([id, args])


func _texts(node: Node) -> Array[String]:
	var found: Array[String] = []
	if node is Label:
		found.append((node as Label).text)
	for c in node.get_children():
		found.append_array(_texts(c))
	return found


func _use_locale(locale: String) -> void:
	if _saved_locale.is_empty():
		_saved_locale = TranslationServer.get_locale()
	TranslationServer.set_locale(locale)


func _restore_locale() -> void:
	if not _saved_locale.is_empty():
		TranslationServer.set_locale(_saved_locale)
		_saved_locale = ""


func _repo_json(rel: String) -> Dictionary:
	var path: String = ProjectSettings.globalize_path("res://").path_join("../" + rel).simplify_path()
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if typeof(parsed) == TYPE_DICTIONARY else {}


func test_catalogue_is_the_shared_content_file() -> bool:
	var file: Array = _repo_json("content/phrases.json").get("phrases", [])
	var mine: Array[Dictionary] = Phrases.all()
	assert_eq(mine.size(), 15, "fifteen phrases")
	assert_eq(file.size(), mine.size(), "same count as content/phrases.json")
	for i in mini(file.size(), mine.size()):
		var f: Dictionary = file[i]
		assert_eq(str(mine[i]["id"]), str(f.get("id")), "id %d" % i)
		assert_eq(str(mine[i]["kind"]), str(f.get("kind")), "kind of " + str(f.get("id")))
		assert_eq(mine[i]["slots"], f.get("slots"), "slots of " + str(f.get("id")))
	return true


func test_every_phrase_has_en_and_fr_with_matching_slots() -> bool:
	for locale in ["en", "fr"]:
		_use_locale(locale)
		for info in Phrases.all():
			var key: String = "CHAT_" + str(info["id"]).to_upper()
			var text: String = TranslationServer.translate(StringName(key))
			assert_true(text != key and text != "", "%s has a %s text" % [key, locale])
			assert_eq(text.count("%s"), (info["slots"] as Array).size(), "%s (%s) has one %%s per slot" % [key, locale])
		for kind in Phrases.KINDS:
			var kkey: String = "CHAT_KIND_" + str(kind).to_upper()
			assert_true(TranslationServer.translate(StringName(kkey)) != kkey, "%s in %s" % [kkey, locale])
	_restore_locale()
	return true


func test_chat_line_fills_seat_names_and_zone_names() -> bool:
	var s: Object = _discussion_session()
	var m: Control = track(load("res://scenes/meeting/meeting.tscn").instantiate()) as Control
	var saved_session: Dictionary = {}
	for f in ["seat_id", "seats", "out_seats", "is_out", "phase", "meeting"]:
		saved_session[f] = Session.get(f)
		Session.set(f, s.get(f))
	m._ready()
	var data: Dictionary = {"t": "chat", "seat": 1, "id": "saw_dump", "args": [3, "depot"]}
	_use_locale("en")
	m._add_chat(data)
	var en: String = " | ".join(_texts(m._chat_box))
	assert_true(en.contains("Bo") and en.contains("Di") and en.contains("Depot"), "EN line: " + en)
	assert_true(not en.contains("%s"), "no slot left unfilled: " + en)
	_use_locale("fr")
	m._add_chat(data)
	var fr: String = _texts(m._chat_box)[-1]
	assert_true(fr.contains("Dépôt") and fr.contains("Di"), "FR line: " + fr)
	_restore_locale()
	for f in saved_session:
		Session.set(f, saved_session[f])
	return true


func test_chat_line_survives_missing_args() -> bool:
	assert_eq(Phrases.render("saw_dump", [3], func(seat: int) -> String: return "S%d" % seat).count("%s"), 0, "short args never leak a format error")
	var namer: Callable = func(seat: int) -> String: return "S%d" % seat
	_use_locale("en")
	assert_eq(Phrases.render("accuse_partner", [1], namer), "Hmm...", "an id from a newer server reads as a neutral line, not the raw id")
	assert_eq(Phrases.render("sus", ["x"], namer), "I suspect %s." % Phrases.UNFILLED, "a seat arg that is not a number is not seat 0")
	assert_true(not Phrases.render("saw_dump", [3, "depot"], namer).contains(".."), "no doubled dots")
	assert_true(not Phrases.render("sus", [], namer).contains(".."), "an unfilled slot before the final dot does not read as four dots")
	_restore_locale()
	return true


func test_picker_disabled_outside_discussion_and_after_five() -> bool:
	var s: Object = _discussion_session()
	var p: Object = _picker(s)
	assert_true(p.is_enabled(), "discussion, nothing sent yet")
	s.meeting["stage"] = "voting"
	assert_true(not p.is_enabled(), "not in the vote stage")
	s.meeting["stage"] = "discussion"
	for i in 4:
		s._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_true(p.is_enabled() and p.lines_left() == 1, "one line left after four")
	s._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_true(not p.is_enabled() and p.lines_left() == 0, "five sent: closed")
	s.is_out = true
	s.phrases_used = 0
	assert_true(not p.is_enabled(), "a seat that is out cannot chat")
	return true


func test_only_my_lines_count_and_a_new_meeting_resets() -> bool:
	var s: Object = _discussion_session()
	s._on_event({"t": "chat", "seat": 1, "id": "hmm", "args": []})
	assert_eq(s.chat_left(), Phrases.LIMIT, "another seat's line is not mine")
	s._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_eq(s.chat_left(), Phrases.LIMIT - 1, "my line counts")
	s._on_event({"t": "action_rejected", "action": "chat_phrase", "reason": "phrase_limit"})
	assert_eq(s.chat_left(), 0, "the server says no more: the picker closes")
	s._on_event({"t": "meeting_started", "cause": "report", "by": 1})
	assert_eq(s.chat_left(), Phrases.LIMIT, "a new meeting starts again")
	s._on_event({"t": "action_rejected", "action": "vote", "reason": "not_allowed"})
	assert_eq(s.chat_left(), Phrases.LIMIT, "a rejected vote says nothing about chat")
	return true


func test_picker_counter_after_rejoin() -> bool:
	var s: Object = _discussion_session()
	var p: Object = _picker(s)
	assert_true(p.is_enabled(), "welcome with a discussion: enabled")
	assert_eq(p.lines_left(), 5, "five left")
	for i in 3:
		s._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_eq(p.lines_left(), 2, "three of my own lines counted")
	s._on_welcome(_welcome_data())
	assert_eq(p.lines_left(), 5, "a welcome that does not say how many I sent starts again from 0 (the server refuses the excess)")
	s._on_welcome(_welcome_data({"my_chat": 3}))
	assert_eq(p.lines_left(), 2, "a welcome with my_chat: 3 leaves two")
	s._on_welcome(_welcome_data({"my_chat": 5}))
	assert_true(not p.is_enabled(), "my_chat 5: closed")
	var playing: Dictionary = _welcome_data()
	playing["state"]["meeting"] = null
	playing["state"]["phase"] = "playing"
	s._on_welcome(playing)
	assert_eq(s.chat_left(), 5, "a welcome into the round forgets the count of the meeting before")
	return true


func test_clue_victims_and_living_keep_out_seats_out_of_the_picker() -> bool:
	var s: Object = _discussion_session()
	s._on_event({"t": "meeting_started", "cause": "report", "by": 1,
		"clues": [{"pile": 1, "zone": "market", "t": 10.0, "waste": null, "victim": 3}]})
	var p: Object = _picker(s)
	assert_eq(p.seat_options(), [1] as Array[int], "the victim of a clue is out from the start of the meeting")
	var l: Object = _discussion_session()
	l._on_event({"t": "meeting_started", "cause": "report", "by": 1, "clues": [], "living": [0, 3]})
	assert_eq(_picker(l).seat_options(), [3] as Array[int], "living lists who may be named")
	assert_true(l.seat_out(1) and not l.seat_out(3), "the seats living leaves out are out for the vote cards too")
	var r: Object = _session()
	r._on_welcome(_welcome_data({"living": [0, 3]}))
	assert_eq(_picker(r).seat_options(), [3] as Array[int], "a rejoin welcome with living")
	return true


## A picker open in the scene while chat closes under it (a refusal, the last line echoed).
func test_scene_closes_the_picker_when_chat_closes_under_it() -> bool:
	var s: Object = _discussion_session()
	var saved: Dictionary = {}
	for f in ["seat_id", "seats", "out_seats", "is_out", "phase", "meeting", "map_id", "phrases_used"]:
		saved[f] = Session.get(f)
		Session.set(f, s.get(f))
	var m: Control = track(load("res://scenes/meeting/meeting.tscn").instantiate()) as Control
	m._ready()
	m._on_say()
	assert_true(m._picking and _find_picker(m._left) != null, "the picker is open")
	Session.phrases_used = Phrases.LIMIT
	m._on_event({"t": "action_rejected", "action": "chat_phrase", "reason": "phrase_limit"})
	assert_true(not m._picking and _find_picker(m._left) == null, "a refusal closes it")
	assert_true(m._say_note.text.contains(m.tr("REJECT_PHRASE_LIMIT")), "and says so for a moment: " + m._say_note.text)
	Session.phrases_used = 4
	m._on_say()
	assert_true(m._picking, "reopened with one line left")
	Session.phrases_used = Phrases.LIMIT
	m._on_event({"t": "chat", "seat": 0, "id": "hmm", "args": []})
	assert_true(not m._picking and _find_picker(m._left) == null, "the last line echoed closes it too")
	for f in saved:
		Session.set(f, saved[f])
	return true


func _find_picker(node: Node) -> Node:
	if node.get_script() == PhrasePicker:
		return node
	for c in node.get_children():
		var found: Node = _find_picker(c)
		if found != null:
			return found
	return null


## The tapped button frees itself inside its own `pressed`: no engine error, the next step shows.
func test_a_tap_that_rebuilds_the_buttons_logs_no_error() -> bool:
	var s: Object = _discussion_session()
	var p: Object = PhrasePicker.new()
	p.session = s
	(Engine.get_main_loop() as SceneTree).root.add_child(p)
	var kinds: Array = p.find_children("*", "Button", true, false)
	assert_true(kinds.size() >= 8, "kinds step: six kinds plus Back and Send")
	var accuse: Button = kinds[0] as Button
	accuse.pressed.emit()
	assert_eq(p.kind, "accuse", "the tap chose the kind")
	await (Engine.get_main_loop() as SceneTree).process_frame
	assert_true(not is_instance_valid(accuse) or accuse.get_parent() == null, "the old button is gone")
	var phrase: Button = null
	for b in p.find_children("*", "Button", true, false):
		if (b as Button).text.begins_with("I saw"):
			phrase = b
			break
	assert_true(phrase != null, "the phrase step lists the phrases")
	phrase.pressed.emit()
	assert_eq(p.phrase_id, "saw_dump", "the tap chose the phrase")
	await (Engine.get_main_loop() as SceneTree).process_frame
	p.queue_free()
	await (Engine.get_main_loop() as SceneTree).process_frame
	return true


func test_picker_offers_only_living_seats_and_not_self() -> bool:
	var s: Object = _discussion_session()
	var p: Object = _picker(s)
	assert_eq(p.seat_options(), [1, 3] as Array[int], "not me (0), not the seat that is out (2)")
	var zones: Array[String] = p.zone_options()
	zones.sort()
	assert_eq(zones, ["depot", "garden", "market"] as Array[String], "the map zones")
	return true


func test_picker_sends_ints_and_zone_ids_only_when_every_slot_is_filled() -> bool:
	var s: Object = _discussion_session()
	var p: Object = _picker(s)
	_sent = []
	p.send_requested.connect(_on_send)
	p.choose_kind("accuse")
	p.choose_phrase("saw_dump")
	assert_true(not p.can_send(), "two slots empty")
	p.send()
	assert_eq(_sent.size(), 0, "nothing sent yet")
	p.choose_slot(3)
	assert_true(not p.can_send(), "one slot empty")
	p.choose_slot("depot")
	assert_true(p.can_send(), "all filled")
	p.send()
	assert_eq(_sent.size(), 1, "sent once")
	assert_eq(_sent[0][0], "saw_dump", "id")
	var args: Array = _sent[0][1]
	assert_eq(args, [3, "depot"], "seat first, zone second")
	assert_eq(typeof(args[0]), TYPE_INT, "the seat is an int, the server reads numbers")
	p.choose_kind("filler")
	p.choose_phrase("hmm")
	assert_true(p.can_send(), "a phrase with no slot can go at once")
	s.phrases_used = Phrases.LIMIT
	p.send()
	assert_eq(_sent.size(), 1, "a closed picker never sends")
	return true
