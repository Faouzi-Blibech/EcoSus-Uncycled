extends "res://tests/test_case.gd"
## The meeting scene while voting: another seat's vote must not rebuild the cards under a finger.

const SESSION_FIELDS: Array[String] = ["seat_id", "seats", "out_seats", "is_out", "phase", "meeting"]

var _saved: Dictionary = {}


func _voting_scene() -> Control:
	for f in SESSION_FIELDS:
		_saved[f] = Session.get(f).duplicate(true) if Session.get(f) is Array or Session.get(f) is Dictionary else Session.get(f)
	Session.seat_id = 0
	Session.is_out = false
	Session.out_seats = {}
	Session.phase = "meeting"
	Session.seats = [
		{"seat": 0, "name": "Ana", "color": 0, "human": true},
		{"seat": 1, "name": "Bo", "color": 1, "human": true},
		{"seat": 2, "name": "Cy", "color": 2, "human": true},
	]
	Session.meeting = {"stage": "voting", "voted": [] as Array[int], "votes": {}, "clues": []}
	var m: Control = track(load("res://scenes/meeting/meeting.tscn").instantiate()) as Control
	m._ready()
	return m


func _restore() -> void:
	for f in SESSION_FIELDS:
		Session.set(f, _saved[f])


func test_another_seats_vote_updates_its_tag_and_keeps_the_cards() -> bool:
	var m: Control = _voting_scene()
	var card: Button = m._cards[2] as Button
	var confirm: Button = m._confirm_btn
	m._on_card_pressed(2)
	Session.meeting["voted"] = [1] as Array[int]
	m._on_event({"t": "vote_cast", "seat": 1})
	assert_true(m._cards[2] == card and card.get_parent() != null, "the card under the finger is still there")
	assert_true(m._confirm_btn == confirm and confirm.get_parent() != null, "so is Confirm")
	assert_true(card.button_pressed, "and the selection is kept")
	assert_true((m._cards[1] as Button).text.contains(m.tr("MEETING_VOTED")), "the voter's card shows the tag")
	_restore()
	return true


func test_my_own_vote_still_rebuilds_the_view() -> bool:
	var m: Control = _voting_scene()
	var confirm: Button = m._confirm_btn
	Session.meeting["voted"] = [0] as Array[int]
	m._on_event({"t": "vote_cast", "seat": 0})
	assert_true(m._confirm_btn != confirm, "my vote locks the cards: rebuilt")
	_restore()
	return true


func _texts(node: Node) -> Array[String]:
	var found: Array[String] = []
	if node is Label:
		found.append((node as Label).text)
	for c in node.get_children():
		found.append_array(_texts(c))
	return found


func _has_icon(node: Node) -> bool:
	if node is TextureRect and (node as TextureRect).texture != null:
		return true
	for c in node.get_children():
		if _has_icon(c):
			return true
	return false


func test_clue_card_shows_zone_time_waste_and_victim() -> bool:
	var m: Control = _voting_scene()
	var waste: Control = m._clue_card({"zone": "market", "t": 125.0, "waste": "plastic", "victim": null})
	var text: String = " | ".join(_texts(waste))
	assert_true(text.contains(m.tr("ZONE_MARKET")), "zone name: " + text)
	assert_true(text.contains("2:05"), "match time as m:ss: " + text)
	assert_true(text.contains(m.tr("WASTE_PLASTIC")), "waste name: " + text)
	assert_true(_has_icon(waste), "waste icon")
	var victim: Control = m._clue_card({"zone": "garden", "t": 61.0, "waste": null, "victim": 1})
	var vtext: String = " | ".join(_texts(victim))
	assert_true(vtext.contains("Bo"), "victim name: " + vtext)
	assert_true(not _has_icon(victim), "no waste, no icon")
	waste.free()
	victim.free()
	_restore()
	return true


func test_ejection_view_says_how_many_polluters_remain() -> bool:
	var m: Control = _voting_scene()
	var saved_tuning: Dictionary = Session.tuning
	var saved_count: int = Session.ejected_polluters
	Session.tuning = {"polluters": 2}
	Session.ejected_polluters = 1
	Session.phase = "ejection"
	m._ejection = {"seat": 2, "was_polluter": true}
	m._rebuild()
	assert_true(" | ".join(_texts(m._left)).contains(m.tr("EJECTION_LEFT_ONE") % 1), "one remains")
	Session.ejected_polluters = -1
	m._rebuild()
	assert_true(not " | ".join(_texts(m._left)).contains(m.tr("EJECTION_LEFT_ONE") % 1), "unknown count: no line")
	Session.tuning = saved_tuning
	Session.ejected_polluters = saved_count
	_restore()
	return true


## The splash paints the body of the reported pile's victim; another victim's clue is only a fallback.
func test_report_victim_is_the_reported_piles_clue() -> bool:
	var script: GDScript = load("res://scenes/meeting/meeting.gd")
	var clues: Array = [{"pile": 3, "zone": "a", "victim": 4}, {"pile": 8, "zone": "b", "victim": 6}]
	assert_eq(script.report_victim({"pile": 8, "clues": clues}), 6, "the reported pile")
	assert_eq(script.report_victim({"pile": 99, "clues": clues}), 4, "unknown pile: first victim")
	assert_eq(script.report_victim({"pile": 1, "clues": [{"pile": 1, "zone": "a"}]}), -1, "no victim")
	return true


func test_report_splash_shows_once_per_meeting() -> bool:
	var m: Control = _voting_scene()
	Session.meeting["cause"] = "report"
	Session.meeting["pile"] = 3
	Session.meeting["clues"] = [{"pile": 3, "zone": "a", "victim": 1}]
	Session.meeting["splash"] = true
	var before: int = m.get_child_count()
	m._show_report_splash()
	assert_eq(m.get_child_count(), before + 1, "splash added")
	m._show_report_splash()
	assert_eq(m.get_child_count(), before + 1, "not twice")
	_restore()
	return true
