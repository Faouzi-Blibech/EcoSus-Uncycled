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
## Where the messages go (the Net autoload; a test passes a recorder).
var net: Object = Net
## A touch device (phone, tablet): the box docks at the top while it has focus. A test sets it.
var touch: bool = ChatRules.is_touch_device()

var _toggle_btn: Button
var _done_btn: Button
var _compact: bool = false
var _lines: VBoxContainer
var _scroll: ScrollContainer
var _say_btn: Button
var _type_row: HBoxContainer
var _picker: PhrasePicker = null
## The history entries behind the labels, oldest first (the very dictionaries of Session.ghost_chat).
var _drawn: Array = []
var _flash_left: float = 0.0
var _awaiting_echo: bool = false
var _await_left: float = 0.0
var _lift: float = 0.0


func _ready() -> void:
	visible = false
	grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_place()
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
	text_edit.focus_entered.connect(_on_box_focus.bind(true))
	text_edit.focus_exited.connect(_on_box_focus.bind(false))
	_type_row.add_child(text_edit)
	# Send and Done never take the focus: a tap on them must not close the keyboard (and so change the
	# layout) before the tap is read.
	send_btn = UIKit.make_button(tr("CHAT_SEND"), SEND_WIDTH)
	send_btn.focus_mode = Control.FOCUS_NONE
	send_btn.pressed.connect(send)
	_type_row.add_child(send_btn)
	_done_btn = UIKit.make_button(tr("CHAT_DONE"), SEND_WIDTH)
	_done_btn.focus_mode = Control.FOCUS_NONE
	_done_btn.visible = false
	_done_btn.pressed.connect(_on_done)
	_type_row.add_child(_done_btn)
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
	var show_it: bool = ChatRules.is_out(Session)
	if visible != show_it:
		visible = show_it
	if not show_it:
		return
	_sync_lines()
	var can: bool = ChatRules.can_ghost(Session)
	text_edit.editable = can
	send_btn.disabled = not can or _awaiting_echo or Session.chat_gap_left() > 0.0
	_say_btn.disabled = not can or _picker != null


## Draws the history that came since the last call. The history is capped (the oldest line goes when a
## new one comes), so the labels follow the entries, not a count: find the last drawn entry in the
## list, add what is after it, then drop the labels of entries that left the list.
func _sync_lines() -> void:
	var list: Array = Session.ghost_chat
	if not _drawn.is_empty() and not list.is_empty() and is_same(list.back(), _drawn.back()):
		return
	var start: int = 0
	if not _drawn.is_empty():
		start = -1
		for i in range(list.size() - 1, -1, -1):
			if is_same(list[i], _drawn.back()):
				start = i + 1
				break
		if start < 0:
			# The history was replaced (a welcome, a new match): draw it again.
			for c in _lines.get_children():
				_lines.remove_child(c)
				c.queue_free()
			_drawn.clear()
			start = 0
	var grew: bool = false
	for i in range(start, list.size()):
		if typeof(list[i]) == TYPE_DICTIONARY:
			_lines.add_child(_line_label(list[i]))
			_drawn.append(list[i])
			grew = true
	while _drawn.size() > list.size():
		_drawn.pop_front()
		var oldest: Node = _lines.get_child(0)
		_lines.remove_child(oldest)
		oldest.queue_free()
	if grew and is_inside_tree():
		_scroll_to_end.call_deferred()


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
	net.send_chat_text(text)
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
	_place()


func _close_picker() -> void:
	if _picker == null:
		return
	_picker.queue_free()
	_picker = null
	_set_chat_visible(true)
	_place()


func _set_chat_visible(on: bool) -> void:
	for c in [_scroll, note, _type_row, _say_btn, _toggle_btn]:
		(c as Control).visible = on


func _on_phrase_chosen(id: String, args: Array) -> void:
	net.send_chat_phrase(id, args)


## The box gained or lost the focus: on a touch device the soft keyboard is up or gone.
func _on_box_focus(entered: bool) -> void:
	if touch:
		_set_compact(entered)


func _on_done() -> void:
	if text_edit.has_focus():
		text_edit.release_focus()
	_set_compact(false)
	if DisplayServer.has_feature(DisplayServer.FEATURE_VIRTUAL_KEYBOARD):
		DisplayServer.virtual_keyboard_hide()


## The compact typing layout: the soft keyboard covers the bottom half of a landscape phone and the web
## build cannot say how high it is, so while the box has focus the panel docks at the top of the
## screen: the box and its note first, the history right under them, nothing else.
func _set_compact(on: bool) -> void:
	if on == _compact:
		return
	_compact = on
	_toggle_btn.visible = not on
	_say_btn.visible = not on
	_done_btn.visible = on
	if on:
		ChatRules.arrange(body, [_type_row, note, _scroll])
	else:
		ChatRules.arrange(body, [_scroll, note, _type_row, _say_btn])
		_lift = 0.0
	_place()


## Bottom-right corner, MARGIN_PX in. The phrase picker needs more room, so while it is open the panel
## spans the screen; the compact typing layout docks it at the top, full width.
func _place() -> void:
	if _compact:
		anchor_left = 0.0
		anchor_right = 1.0
		anchor_top = 0.0
		anchor_bottom = 0.0
		grow_vertical = Control.GROW_DIRECTION_END
		offset_left = MARGIN_PX
		offset_right = -MARGIN_PX
		offset_top = MARGIN_PX
		offset_bottom = MARGIN_PX
		custom_minimum_size = Vector2.ZERO
		return
	var span: bool = _picker != null
	anchor_left = 0.0 if span else 1.0
	anchor_right = 1.0
	anchor_top = 1.0
	anchor_bottom = 1.0
	grow_vertical = Control.GROW_DIRECTION_BEGIN
	offset_left = MARGIN_PX if span else -PANEL_WIDTH - MARGIN_PX
	offset_right = -MARGIN_PX
	offset_bottom = -MARGIN_PX - _lift
	offset_top = offset_bottom
	custom_minimum_size = Vector2(0.0 if span else PANEL_WIDTH, PICKER_HEIGHT if span else 0.0)


## An extra for platforms that report the keyboard height (the web build reports 0, so touch devices
## use the compact layout): lift the panel above the keyboard while the box has focus.
func _keep_box_above_keyboard() -> void:
	var lift: float = 0.0
	# Only where a soft keyboard exists: elsewhere the call warns every frame (desktop).
	if text_edit.has_focus() and not _compact and DisplayServer.has_feature(DisplayServer.FEATURE_VIRTUAL_KEYBOARD):
		lift = ChatRules.lift_px(DisplayServer.virtual_keyboard_get_height(), DisplayServer.window_get_size().y,
				get_viewport_rect().size.y)
	if not is_equal_approx(lift, _lift):
		_lift = lift
		offset_bottom = -MARGIN_PX - _lift
		offset_top = offset_bottom
