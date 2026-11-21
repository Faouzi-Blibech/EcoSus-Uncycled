extends "res://tests/test_case.gd"
## Lobby: taken colours are dimmed, a refused colour or a failed Start says so, no "Ready" column.

const SESSION_FIELDS: Array[String] = ["seat_id", "seats", "host_seat", "room_code"]

var _saved: Dictionary = {}


func _lobby() -> Control:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	for f in SESSION_FIELDS:
		_saved[f] = Session.get(f).duplicate(true) if Session.get(f) is Array else Session.get(f)
	Session.seat_id = 0
	Session.host_seat = 0
	Session.room_code = "ABCD"
	Session.seats = [
		{"seat": 0, "name": "Ana", "color": 0, "human": true, "ready": true},
		{"seat": 1, "name": "Bo", "color": 3, "human": true, "ready": true},
		{"seat": 2, "name": "Bot Cy", "color": 5, "human": false, "ready": true},
	]
	var l: Control = track(load("res://scenes/lobby/lobby.tscn").instantiate()) as Control
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(l)
	return l


func _restore() -> void:
	for f in SESSION_FIELDS:
		Session.set(f, _saved[f])


func test_taken_colours_are_the_other_seats_colours() -> bool:
	var lobby_script: GDScript = load("res://scenes/lobby/lobby.gd")
	assert_eq(lobby_script.taken_colors([{"seat": 0, "color": 0}, {"seat": 1, "color": 3}, {"seat": 2, "color": 5}], 0), [3, 5] as Array[int], "mine is not taken")
	return true


func test_swatches_of_other_seats_are_disabled() -> bool:
	var l: Control = _lobby()
	assert_true(not (l._swatches[0] as Button).disabled, "my own colour")
	assert_true((l._swatches[3] as Button).disabled, "Bo has colour 3")
	assert_true((l._swatches[5] as Button).disabled, "the bot has colour 5")
	assert_true(not (l._swatches[1] as Button).disabled, "a free colour")
	_restore()
	return true


func test_refused_colour_and_failed_start_show_a_line() -> bool:
	var l: Control = _lobby()
	l._on_action_rejected({"action": "set_color", "reason": "not_allowed"})
	assert_eq(l._toast.text, "That colour is taken.", "colour refused")
	l._on_action_rejected({"action": "use", "reason": "out_of_range"})
	assert_eq(l._toast.text, "That colour is taken.", "other actions are not the lobby's business")
	l._on_error({"code": "start_failed"})
	assert_eq(l._toast.text, "Could not start the match. Try again.", "start failed")
	_restore()
	return true
