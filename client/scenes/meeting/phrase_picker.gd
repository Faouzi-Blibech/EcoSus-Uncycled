extends VBoxContainer
## The meeting chat picker: a kind, then a phrase of that kind, then one pick per slot (living
## seats except me, map zones), then Send. One step on screen at a time so it fits a phone, and
## every button is at least UIKit.MIN_TOUCH tall. It only asks: the meeting scene sends.

signal send_requested(id: String, args: Array)
## Back from the first step, or after a send.
signal closed

const Phrases := preload("res://content/phrases.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const ChatRules := preload("res://scenes/meeting/chat_rules.gd")

const SEAT_BUTTON_SIZE: Vector2 = Vector2(140.0, UIKit.MIN_TOUCH)
const ICON_SIZE: int = 32
## Wide enough for the longest phrase with its slots shown as "...", in French too.
const PHRASE_BUTTON_WIDTH: float = 330.0
const ICON_PATH: String = "res://assets/icons/seat_%d.png"
const MUTED: Color = Color(0.7, 0.8, 0.75)

## The session to read (the autoload; a test passes its own).
var session: Object = Session
## An out seat's picker: the line goes to the ghost chat (no phrase count; any seat may be named).
var ghost: bool = false
var kind: String = ""
var phrase_id: String = ""
## Slot picks so far: seat ints and zone id strings, in slot order.
var args: Array = []


## Discussion stage, I am in and have lines left. Used by the scene's Say button too.
static func can_chat(s: Object) -> bool:
	var stage: String = str((s.meeting as Dictionary).get("stage", ""))
	return s.phase == "meeting" and stage == "discussion" and not s.is_out \
			and not s.seat_out(s.seat_id) and s.chat_left() > 0


func _ready() -> void:
	size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_theme_constant_override("separation", 6)
	refresh()


func is_enabled() -> bool:
	return ChatRules.can_ghost(session) if ghost else can_chat(session)


func lines_left() -> int:
	return session.chat_left()


## Living seats other than mine: not out as far as the client knows, and in meeting.living when
## the server lists it (the server refuses chat about a seat that is out). A ghost may name any seat.
func seat_options() -> Array[int]:
	var living: Variant = null if ghost else (session.meeting as Dictionary).get("living")
	var out: Array[int] = []
	for s in session.seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var seat: int = int((s as Dictionary).get("seat", -1))
		if seat < 0 or seat == session.seat_id or (session.seat_out(seat) and not ghost):
			continue
		if typeof(living) == TYPE_ARRAY and not (living as Array).has(seat):
			continue
		out.append(seat)
	return out


func zone_options() -> Array[String]:
	return Phrases.zone_ids(session.map_id)


func choose_kind(k: String) -> void:
	kind = k
	phrase_id = ""
	args = []
	refresh()


func choose_phrase(id: String) -> void:
	if Phrases.info(id).is_empty():
		return
	phrase_id = id
	args = []
	refresh()


## The next slot's value: a seat (int) or a zone id (String), as the slot asks.
func choose_slot(value: Variant) -> void:
	if phrase_id.is_empty() or args.size() >= _slots().size():
		return
	args.append(value)
	refresh()


func can_send() -> bool:
	return is_enabled() and not phrase_id.is_empty() and args.size() == _slots().size()


func send() -> void:
	if not can_send():
		return
	var id: String = phrase_id
	var picked: Array = args.duplicate()
	_reset()
	send_requested.emit(id, picked)
	closed.emit()


## One step back; from the first step it closes the picker.
func back() -> void:
	if not args.is_empty():
		args.pop_back()
	elif not phrase_id.is_empty():
		phrase_id = ""
	elif not kind.is_empty():
		kind = ""
	else:
		closed.emit()
		return
	refresh()


func _reset() -> void:
	kind = ""
	phrase_id = ""
	args = []


func _slots() -> Array:
	return Phrases.info(phrase_id).get("slots", [])


func _name_of(seat: int) -> String:
	var n: String = session.seat_name(seat)
	return n if n != "" else str(seat)


# ---------------------------------------------------------------- view

func refresh() -> void:
	for c in get_children():
		remove_child(c)
		c.queue_free()
	if not is_enabled():
		add_child(_wrapped_label(tr("CHAT_CLOSED"), 14, MUTED))
		add_child(_back_row(false))
		return
	var step_label: Label = _wrapped_label(_prompt(), 14)
	add_child(step_label)
	var scroll: ScrollContainer = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	var flow: HFlowContainer = HFlowContainer.new()
	flow.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	flow.add_theme_constant_override("h_separation", 4)
	flow.add_theme_constant_override("v_separation", 4)
	scroll.add_child(flow)
	_fill_options(flow)
	add_child(_back_row(true))


## What the player is looking at: the phrase so far, or the question of the step.
func _prompt() -> String:
	if not phrase_id.is_empty():
		var line: String = Phrases.render(phrase_id, args, Callable(self, "_name_of"))
		if args.size() < _slots().size():
			var slot: String = _slots()[args.size()]
			return "%s\n%s" % [line, tr("CHAT_PICK_SEAT") if slot == Phrases.SLOT_SEAT else tr("CHAT_PICK_ZONE")]
		return line
	if not kind.is_empty():
		return tr("CHAT_PICK_PHRASE")
	return tr("CHAT_PICK_KIND")


func _fill_options(flow: HFlowContainer) -> void:
	if phrase_id.is_empty():
		if kind.is_empty():
			for k in Phrases.KINDS:
				flow.add_child(_option(tr("CHAT_KIND_" + k.to_upper()), choose_kind.bind(k), 110.0))
		else:
			for info in Phrases.of_kind(kind):
				var id: String = info["id"]
				var text: String = Phrases.render(id, [], Callable(self, "_name_of"))
				var btn: Button = _option(text, choose_phrase.bind(id), PHRASE_BUTTON_WIDTH)
				btn.clip_text = false
				btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
				flow.add_child(btn)
		return
	var slots: Array = _slots()
	if args.size() >= slots.size():
		return
	if slots[args.size()] == Phrases.SLOT_SEAT:
		for seat in seat_options():
			flow.add_child(_seat_button(seat))
	else:
		for zone in zone_options():
			flow.add_child(_option(Phrases.zone_name(zone), choose_slot.bind(zone), 110.0))


func _option(text: String, action: Callable, min_width: float) -> Button:
	var btn: Button = UIKit.make_button(text, min_width)
	btn.clip_text = true
	btn.pressed.connect(action)
	return btn


func _seat_button(seat: int) -> Button:
	var btn: Button = _option(_name_of(seat), choose_slot.bind(seat), SEAT_BUTTON_SIZE.x)
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	var tint: Color = session.seat_color(seat)
	# Only real seat art: a missing icon would show as a plain colour square beside the name.
	var icon_path: String = ICON_PATH % int(session.seat_info(seat).get("color", 0))
	if ResourceLoader.exists(icon_path):
		btn.icon = Assets.texture(icon_path, tint)
	btn.expand_icon = true
	btn.add_theme_constant_override("icon_max_width", ICON_SIZE)
	btn.add_theme_color_override("font_color", tint)
	btn.add_theme_color_override("font_hover_color", tint)
	return btn


func _back_row(with_send: bool) -> HBoxContainer:
	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var back_btn: Button = UIKit.make_button(tr("CHAT_BACK"), 100.0, "cream")
	back_btn.pressed.connect(back)
	row.add_child(back_btn)
	if with_send:
		var send_btn: Button = UIKit.make_button(tr("CHAT_SEND"), 100.0)
		send_btn.disabled = not can_send()
		send_btn.pressed.connect(send)
		row.add_child(send_btn)
		if not ghost:
			var left: Label = UIKit.make_label(tr("CHAT_PHRASES_LEFT") % lines_left(), 12, MUTED)
			left.size_flags_vertical = Control.SIZE_SHRINK_CENTER
			row.add_child(left)
	return row


func _wrapped_label(text: String, size: int, color: Color = Color.WHITE) -> Label:
	var label: Label = UIKit.make_label(text, size, color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label
