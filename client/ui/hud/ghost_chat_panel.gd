extends PanelContainer
## The ghost chat of an out player: typed lines and phrases of the other out players, at any time of
## the match (also outside meetings). Collapsible. It shows only while the local player is out, and
## the lines come from Session.ghost_chat (which only an out seat ever receives). Lines are Labels:
## whatever is typed shows as typed, never as BBCode.

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const Phrases := preload("res://content/phrases.gd")
const PhrasePicker := preload("res://scenes/meeting/phrase_picker.gd")
const ChatRules := preload("res://scenes/meeting/chat_rules.gd")

const MUTED: Color = Color(0.7, 0.8, 0.75)
const BACKGROUND: Color = Color(0.07, 0.11, 0.1, 1.0)
## The panel and its margin: the spectator overlay keeps this much of the right side free.
const PANEL_WIDTH: float = 250.0
const PANEL_PADDING_PX: int = 4
const PICKER_HEIGHT: float = 270.0
const LINES_HEIGHT: float = 70.0
const SEND_WIDTH: float = 64.0
const MARGIN_PX: float = 6.0
const FLASH_S: float = 2.0
const ECHO_WAIT_S: float = 3.0

## Everything but the header; hidden while collapsed.
var body: VBoxContainer
## The one short note for a refused line.
var note: Label
var text_edit: LineEdit
var send_btn: Button

var _toggle_btn: Button
var _lines: VBoxContainer
var _scroll: ScrollContainer
var _say_btn: Button
var _type_row: HBoxContainer
var _picker: PhrasePicker = null
var _shown: int = 0
var _flash_left: float = 0.0
var _awaiting_echo: bool = false
var _await_left: float = 0.0
var _lift: float = 0.0


func _ready() -> void:
	visible = false
	grow_horizontal = Control.GROW_DIRECTION_BEGIN
	grow_vertical = Control.GROW_DIRECTION_BEGIN
	anchor_top = 1.0
	anchor_bottom = 1.0
	anchor_right = 1.0
	_place(false)
	var style: StyleBoxFlat = StyleBoxFlat.new()
	style.bg_color = BACKGROUND
	style.set_content_margin_all(PANEL_PADDING_PX)
	add_theme_stylebox_override("panel", style)

	var box: VBoxContainer = UIKit.make_vbox(2)
	box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(box)
	_toggle_btn = UIKit.make_button(tr("GHOST_CHAT_TITLE"), 0.0)
	_toggle_btn.custom_minimum_size = Vector2(0.0, 32.0)
	_toggle_btn.pressed.connect(toggle)
	box.add_child(_toggle_btn)
	body = UIKit.make_vbox(2)
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	box.add_child(body)
	_scroll = ScrollContainer.new()
	_scroll.custom_minimum_size = Vector2(0.0, LINES_HEIGHT)
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	body.add_child(_scroll)
	_lines = UIKit.make_vbox(2)
	_lines.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_scroll.add_child(_lines)
	note = _wrapped("", MUTED)
	body.add_child(note)
	_type_row = HBoxContainer.new()
	_type_row.add_theme_constant_override("separation", 4)
	body.add_child(_type_row)
	text_edit = UIKit.make_line_edit(tr("CHAT_TYPE_HINT"), "", Session.text_max_chars())
	text_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text_edit.keep_editing_on_text_submit = true
	text_edit.text_submitted.connect(_on_text_submitted)
	_type_row.add_child(text_edit)
	send_btn = UIKit.make_button(tr("CHAT_SEND"), SEND_WIDTH)
	send_btn.pressed.connect(send)
	_type_row.add_child(send_btn)
	_say_btn = UIKit.make_button(tr("CHAT_SAY"), 0.0)
	_say_btn.pressed.connect(_open_picker)
	body.add_child(_say_btn)

	Net.action_rejected.connect(_on_rejected)
	Net.event.connect(_on_event)


func _process(delta: float) -> void:
	if _flash_left > 0.0:
		_flash_left -= delta
		if _flash_left <= 0.0:
			note.text = ""
	if _awaiting_echo:
		_await_left -= delta
		if _await_left <= 0.0:
			_awaiting_echo = false
	sync()
	_keep_box_above_keyboard()


## Shows or hides the panel by the session (out players only) and adds the lines that came since.
func sync() -> void:
	var show_it: bool = Session.is_out
	if visible != show_it:
		visible = show_it
	if not show_it:
		return
	if Session.ghost_chat.size() < _shown:
		for c in _lines.get_children():
			_lines.remove_child(c)
			c.queue_free()
		_shown = 0
	var grew: bool = Session.ghost_chat.size() > _shown
	while _shown < Session.ghost_chat.size():
		var line: Variant = Session.ghost_chat[_shown]
		if typeof(line) == TYPE_DICTIONARY:
			_lines.add_child(_line_label(line))
		_shown += 1
	if grew and is_inside_tree():
		_scroll_to_end.call_deferred()
	var can: bool = ChatRules.can_ghost(Session)
	text_edit.editable = can
	send_btn.disabled = not can or _awaiting_echo or Session.chat_gap_left() > 0.0
	_say_btn.disabled = not can or _picker != null


func _scroll_to_end() -> void:
	if is_inside_tree():
		_scroll.scroll_vertical = int(_scroll.get_v_scroll_bar().max_value)


func toggle() -> void:
	body.visible = not body.visible
	if not body.visible:
		_close_picker()


## A typed line may go now: text, an out seat, and neither an unanswered line nor the gap in the way.
func can_send(text: String) -> bool:
	return not text.strip_edges().is_empty() and ChatRules.can_ghost(Session) and not _awaiting_echo \
			and Session.chat_gap_left() <= 0.0


## Sends the typed line; the box keeps it until my own line comes back.
func send() -> void:
	var text: String = text_edit.text.strip_edges()
	if not can_send(text):
		return
	Net.send_chat_text(text)
	_awaiting_echo = true
	_await_left = ECHO_WAIT_S
	sync()


func _on_text_submitted(_text: String) -> void:
	send()


func _on_event(data: Dictionary) -> void:
	if str(data.get("t", "")) == "chat" and bool(data.get("ghost", false)) and data.has("text") \
			and int(data.get("seat", -1)) == Session.seat_id and _awaiting_echo:
		_awaiting_echo = false
		text_edit.text = ""


## A refused ghost line: one short note, replacing the last.
func _on_rejected(data: Dictionary) -> void:
	var action: String = str(data.get("action", ""))
	if action != "chat_text" and action != "chat_phrase":
		return
	if action == "chat_text":
		_awaiting_echo = false
	note.text = tr(ChatRules.note_key(str(data.get("reason", ""))))
	_flash_left = FLASH_S


func _line_label(data: Dictionary) -> Label:
	var seat: int = int(data.get("seat", -1))
	var text: String
	if data.has("text"):
		text = str(data.get("text"))
	else:
		text = Phrases.render(str(data.get("id", "")), Net.as_array(data.get("args")), Callable(self, "_name_of"))
	return _wrapped(tr("MEETING_CHAT_LINE") % [_name_of(seat), text], Session.seat_color(seat))


func _name_of(seat: int) -> String:
	var n: String = Session.seat_name(seat)
	return n if n != "" else str(seat)


func _wrapped(text: String, color: Color) -> Label:
	var label: Label = UIKit.make_label(text, 12, color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label


# ---------------------------------------------------------------- phrases

## The phrase picker needs more room than the panel: while it is open the panel spans the screen.
func _open_picker() -> void:
	if _picker != null or not ChatRules.can_ghost(Session):
		return
	_picker = PhrasePicker.new()
	_picker.ghost = true
	_picker.send_requested.connect(_on_phrase_chosen)
	_picker.closed.connect(_close_picker)
	_set_chat_visible(false)
	body.add_child(_picker)
	_place(true)


func _close_picker() -> void:
	if _picker == null:
		return
	_picker.queue_free()
	_picker = null
	_set_chat_visible(true)
	_place(false)


func _set_chat_visible(on: bool) -> void:
	for c in [_scroll, note, _type_row, _say_btn, _toggle_btn]:
		(c as Control).visible = on


func _on_phrase_chosen(id: String, args: Array) -> void:
	Net.send_chat_phrase(id, args)


## Bottom-right corner, MARGIN_PX in; spanning (the picker is open) stretches it to the left edge.
func _place(span: bool) -> void:
	anchor_left = 0.0 if span else 1.0
	offset_left = MARGIN_PX if span else -PANEL_WIDTH - MARGIN_PX
	offset_right = -MARGIN_PX
	offset_bottom = -MARGIN_PX - _lift
	offset_top = offset_bottom
	custom_minimum_size = Vector2(0.0 if span else PANEL_WIDTH, PICKER_HEIGHT if span else 0.0)


func _keep_box_above_keyboard() -> void:
	var lift: float = 0.0
	if text_edit.has_focus():
		lift = ChatRules.lift_px(DisplayServer.virtual_keyboard_get_height(), DisplayServer.window_get_size().y,
				get_viewport_rect().size.y)
	if not is_equal_approx(lift, _lift):
		_lift = lift
		offset_bottom = -MARGIN_PX - _lift
		offset_top = offset_bottom
