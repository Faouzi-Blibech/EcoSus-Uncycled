extends "res://tests/test_case.gd"
## Net._handle_text routes in-match messages to typed signals and still emits event.

var _got: Array[Dictionary] = []
var _events: Array[Dictionary] = []
var _any: int = 0


func _on_got_any(_d: Variant = null) -> void:
	_any += 1


func _on_got(d: Dictionary) -> void:
	_got.append(d)


func _on_event(d: Dictionary) -> void:
	_events.append(d)


func test_use_ok_emits_typed_signal() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.use_ok.connect(_on_got)
	n.event.connect(_on_event)
	n._handle_text('{"t":"use_ok","console":"market_bins","kind":"task","task":3,"type":"sort_waste","step":0}')
	assert_eq(_got.size(), 1, "typed emitted once")
	assert_eq(str(_got[0].get("console")), "market_bins", "console")
	assert_eq(_events.size(), 1, "event still emitted")
	return true


func test_cue_pos_converted_to_px() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.cue.connect(_on_got)
	n._handle_text('{"t":"cue","kind":"dump","seat":2,"pos":[2,3]}')
	assert_eq(_got.size(), 1, "cue emitted")
	assert_eq(_got[0].get("pos"), Vector2(64, 96), "pos px")
	return true


func test_unknown_type_only_event() -> bool:
	var n: Object = track(load("res://autoload/net.gd").new())
	n.event.connect(_on_event)
	for sig in ["use_ok", "cue", "chat", "game_over", "connected", "ready"]:
		n.connect(sig, _on_got_any)
	n._handle_text('{"t":"foo_bar"}')
	n._handle_text('{"t":"connected"}')
	n._handle_text('{"t":"ready"}')
	assert_eq(_events.size(), 3, "event emitted for each")
	assert_eq(str(_events[0].get("t")), "foo_bar", "t")
	assert_eq(_any, 0, "no typed or built-in signal fired")
	return true
