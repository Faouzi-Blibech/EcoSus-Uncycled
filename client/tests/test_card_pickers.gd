extends "res://tests/test_case.gd"
## The ability and room pickers: filtering, one pick, cancel, and closing when play ends.


func _ability() -> AbilityPicker:
	return track(AbilityPicker.new()) as AbilityPicker


func _room() -> RoomPicker:
	return track(RoomPicker.new()) as RoomPicker


func _items(list: Array[String]) -> Array[String]:
	return list


func _has_cancel(p: CardPicker) -> bool:
	return _button(p, "COMMON_CANCEL") != null


func _button(p: CardPicker, key: String) -> Button:
	for n in p.find_children("*", "Button", true, false):
		if (n as Button).text == p.tr(key):
			return n as Button
	return null


func test_ability_skips_unknown_and_picks_once() -> bool:
	var p: AbilityPicker = _ability()
	var got: Array[String] = []
	p.picked.connect(func(item: String) -> void: got.append(item))
	p.open(_items(["virus", "gas", "laser"]))
	assert_eq(p._row.get_child_count(), 2, "two cards")
	var card: Button = p._row.get_child(1) as Button
	card.pressed.emit()
	card.pressed.emit()
	assert_eq(got, ["gas"] as Array[String], "picked gas once")
	assert_true(not p.is_open(), "closed")
	return true


## The ability choice can be closed (the HUD reopens it): Close emits dismissed, no pick.
func test_ability_close_dismisses_without_a_pick() -> bool:
	var p: AbilityPicker = _ability()
	var got: Array[String] = []
	var dismissed: Array[bool] = []
	p.picked.connect(func(item: String) -> void: got.append(item))
	p.dismissed.connect(func() -> void: dismissed.append(true))
	p.open(_items(["virus", "gas"]))
	var close: Button = _button(p, "COMMON_CLOSE")
	assert_true(close != null, "close button")
	close.pressed.emit()
	assert_true(not p.is_open(), "closed")
	assert_eq(got.size(), 0, "no pick")
	assert_eq(dismissed.size(), 1, "dismissed once")
	p.open(_items(["virus", "gas"]))
	assert_eq(p._row.get_child_count(), 2, "reopens with both cards")
	return true


func test_room_cards_use_room_labels() -> bool:
	var p: RoomPicker = _room()
	p.open(_items(["gas_market", "gas_dark"]))
	assert_eq(p._row.get_child_count(), 2, "two cards")
	assert_eq((p._row.get_child(0) as Button).text, p.tr("GAS_ROOM_MARKET"), "market")
	assert_eq((p._row.get_child(1) as Button).text, p.tr("GAS_ROOM_DARK"), "dark")
	assert_eq(p.label_key("gas_depot"), "GAS_ROOM_DEPOT", "depot key")
	return true


func test_room_cancel_closes_without_pick() -> bool:
	var p: RoomPicker = _room()
	var got: Array[String] = []
	p.picked.connect(func(item: String) -> void: got.append(item))
	p.open(_items(["gas_market"]))
	var cancel: Button = null
	for n in p.find_children("*", "Button", true, false):
		if (n as Button).text == p.tr("COMMON_CANCEL"):
			cancel = n as Button
	assert_true(cancel != null, "cancel exists")
	assert_true(cancel.custom_minimum_size.y >= 48.0, "cancel touch height")
	cancel.pressed.emit()
	assert_true(not p.is_open(), "closed")
	assert_eq(got.size(), 0, "nothing picked")
	return true


func test_room_card_press_emits_once() -> bool:
	var p: RoomPicker = _room()
	var got: Array[String] = []
	p.picked.connect(func(item: String) -> void: got.append(item))
	p.open(_items(["gas_market", "gas_dark"]))
	var card: Button = p._row.get_child(1) as Button
	card.pressed.emit()
	card.pressed.emit()
	assert_eq(got, ["gas_dark"] as Array[String], "picked gas_dark once")
	return true


func test_both_close_on_meeting() -> bool:
	var a: AbilityPicker = _ability()
	a.open(_items(["virus"]))
	a._on_meeting_started({})
	assert_true(not a.is_open(), "ability closed")
	var r: RoomPicker = _room()
	r.open(_items(["gas_market"]))
	r._on_meeting_started({})
	assert_true(not r.is_open(), "room closed")
	return true
