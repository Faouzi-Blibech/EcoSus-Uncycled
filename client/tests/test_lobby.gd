extends "res://tests/test_case.gd"
## Lobby: taken colours are dimmed, free seats are listed, a refused colour or a failed Start says so.

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
	l._open_picker()
	var swatches: Array = l._picker._swatches
	assert_true(not (swatches[0] as Button).disabled, "my own colour")
	assert_true((swatches[3] as Button).disabled, "Bo has colour 3")
	assert_true((swatches[5] as Button).disabled, "the bot has colour 5")
	assert_true(not (swatches[1] as Button).disabled, "a free colour")
	_restore()
	return true


func test_free_seats_are_listed_up_to_the_room_size() -> bool:
	var saved_size: int = Session.room_size
	Session.room_size = 6
	var l: Control = _lobby()
	assert_eq(l._count.text, "3/6", "three seated of six")
	assert_eq(l._filled, [true, true, true, false, false, false] as Array[bool], "a tick per taken seat, then three free seats")
	Session.room_size = saved_size
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


# --- Skins (plan L3). Unlocks are data from the scan service, so the tests set Profile.skins. ---

func _with_unlocked(ids: Array[String]) -> Dictionary:
	var before: Dictionary = {"skins": Profile.skins.duplicate(), "loaded": Profile.loaded, "skin": Profile.skin}
	Profile.skins = ids
	Profile.loaded = true
	return before


func _restore_profile(before: Dictionary) -> void:
	Profile.skins = before["skins"]
	Profile.loaded = before["loaded"]
	Profile.skin = before["skin"]


func test_characters_unlock_from_data() -> bool:
	var C: GDScript = load("res://ui/widgets/characters.gd")
	var unlocked: Array[String] = ["robot"]
	assert_true(C.is_unlocked(C.DEFAULT_ID, [] as Array[String]), "the default is always unlocked")
	assert_true(C.is_unlocked("robot", unlocked), "listed by the scan service")
	assert_true(not C.is_unlocked("eagle", unlocked), "not listed")
	assert_eq(C.wire_id(C.DEFAULT_ID), "", "the default is sent as an empty skin")
	assert_eq(C.wire_id("robot"), "robot", "others as their id")
	assert_true(C.has("rabbit") and not C.has("no_art_for_this"), "art lookup")
	return true


func test_a_seat_shows_its_skin_when_there_is_art() -> bool:
	var lobby_script: GDScript = load("res://scenes/lobby/lobby.gd")
	assert_eq(lobby_script._character_of({"skin": "robot"}), "robot", "server skin with art")
	assert_eq(lobby_script._character_of({"skin": "future_skin"}), "sprout", "no art yet -> default")
	assert_eq(lobby_script._character_of({}), "sprout", "no skin -> default")
	return true


func test_saved_skin_is_worn_once_the_profile_confirms_it() -> bool:
	var lobby_script: GDScript = load("res://scenes/lobby/lobby.gd")
	var unlocked: Array[String] = ["robot"]
	assert_eq(lobby_script.skin_to_wear("robot", unlocked, true), "robot", "saved and unlocked")
	assert_eq(lobby_script.skin_to_wear("eagle", unlocked, true), "", "saved but locked now -> default")
	assert_eq(lobby_script.skin_to_wear("robot", [] as Array[String], false), null, "profile not loaded yet -> wait")
	assert_eq(lobby_script.skin_to_wear("", [] as Array[String], false), "", "default needs no profile")
	return true


func test_picker_locks_follow_the_unlocked_list_and_taps_pick() -> bool:
	var before: Dictionary = _with_unlocked(["robot"] as Array[String])
	var l: Control = _lobby()
	l._open_picker()
	var picker: Control = l._picker
	assert_eq((picker._cards["robot"]["avatar"] as Control).modulate, Color.WHITE, "unlocked card is bright")
	assert_true((picker._cards["eagle"]["avatar"] as Control).modulate != Color.WHITE, "locked card is dimmed")
	assert_true((picker._cards["sprout"]["badge"] as TextureRect).visible, "check mark on the worn default")
	assert_true(not (picker._cards["robot"]["badge"] as TextureRect).visible, "no badge on an unlocked card not worn")

	var picked: Array[String] = []
	picker.skin_picked.connect(func(id: String) -> void: picked.append(id))
	var tap: InputEventMouseButton = InputEventMouseButton.new()
	tap.button_index = MOUSE_BUTTON_LEFT
	tap.pressed = true
	picker._on_card_input(tap, "robot")
	picker._on_card_input(tap, "sprout")
	picker._on_card_input(tap, "eagle")
	assert_eq(picked, ["robot", ""] as Array[String], "unlocked cards pick; the default as \"\"; locked ones do not")
	assert_eq(picker._hint.text, "That character is still locked. Keep your scan streak going!", "locked tap explains")
	_restore()
	_restore_profile(before)
	return true


func test_refused_skin_shows_a_line() -> bool:
	var l: Control = _lobby()
	l._on_action_rejected({"action": "set_skin", "reason": "target_invalid"})
	assert_eq(l._toast.text, "That character is still locked. Keep your scan streak going!", "skin refused")
	_restore()
	return true
