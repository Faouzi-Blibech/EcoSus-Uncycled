extends "res://tests/test_case.gd"
## The upgrade picker offers the server's items, emits one pick, and closes when play ends.


func _picker() -> UpgradePicker:
	return track(UpgradePicker.new()) as UpgradePicker


func _cards(p: UpgradePicker) -> Array[Node]:
	return p._row.get_children()


func _items(list: Array[String]) -> Array[String]:
	return list


func test_open_makes_one_card_per_item() -> bool:
	var p: UpgradePicker = _picker()
	p.open(_items(["heal", "mask"]))
	assert_eq(_cards(p).size(), 2, "two cards")
	assert_true(p.is_open(), "open")
	return true


func test_press_emits_once_and_closes() -> bool:
	var p: UpgradePicker = _picker()
	var got: Array[String] = []
	p.picked.connect(func(item: String) -> void: got.append(item))
	p.open(_items(["heal", "mask"]))
	var card: Button = _cards(p)[1] as Button
	card.pressed.emit()
	assert_eq(got, ["mask"] as Array[String], "picked mask once")
	assert_true(not p.is_open(), "closed")
	card.pressed.emit()
	assert_eq(got.size(), 1, "second press ignored")
	return true


func test_unknown_items_are_skipped() -> bool:
	var p: UpgradePicker = _picker()
	p.open(_items(["backpack", "jetpack"]))
	assert_eq(_cards(p).size(), 1, "one card")
	return true


func test_second_open_replaces_cards() -> bool:
	var p: UpgradePicker = _picker()
	p.open(_items(["heal", "mask"]))
	p.open(_items(["tool"]))
	assert_eq(_cards(p).size(), 1, "replaced")
	return true


func test_leaving_play_closes() -> bool:
	var p: UpgradePicker = _picker()
	p.open(_items(["heal"]))
	p._on_meeting_started({})
	assert_true(not p.is_open(), "meeting")
	p.open(_items(["heal"]))
	p._on_you_are_out({})
	assert_true(not p.is_open(), "out")
	p.open(_items(["heal"]))
	p._on_game_over({})
	assert_true(not p.is_open(), "game over")
	assert_eq(_cards(p).size(), 0, "cards freed")
	return true


func test_cards_are_touch_sized() -> bool:
	var p: UpgradePicker = _picker()
	p.open(_items(["backpack", "tool", "heal", "mask"]))
	assert_eq(_cards(p).size(), 4, "four cards")
	for c in _cards(p):
		var size: Vector2 = (c as Control).custom_minimum_size
		assert_true(size.x >= 48.0 and size.y >= 48.0, "min size %s" % str(size))
	return true
