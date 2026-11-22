extends Control
## Meeting: discussion (clue cards, task bar, chat), voting (tap a card, confirm, or skip),
## results, and the ejection view. Built from Session.meeting at _ready so a rejoin works, then
## follows Net.event (Session handles the same event first, so it is already updated).

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const VoteRules := preload("res://scenes/meeting/vote_rules.gd")
const Phrases := preload("res://content/phrases.gd")
const PhrasePicker := preload("res://scenes/meeting/phrase_picker.gd")
const ChatRules := preload("res://scenes/meeting/chat_rules.gd")
const ReportSplash := preload("res://scenes/meeting/report_splash.gd")

const CARD_SIZE: Vector2 = Vector2(150.0, UIKit.MIN_TOUCH)
const ICON_SIZE: int = 32
const ICON_PATH: String = "res://assets/icons/seat_%d.png"
const WASTE_ICON_PATH: String = "res://assets/waste/%s.png"
const MUTED: Color = Color(0.7, 0.8, 0.75)
const OUT_TINT: Color = Color(0.5, 0.5, 0.5, 0.6)
## How long a refused chat line keeps its note under the chat (one label, so no spam).
const FLASH_S: float = 2.0
## A sent line that is not answered (echo or refusal) in this time frees Send again.
const ECHO_WAIT_S: float = 3.0
const CHAT_WIDTH: float = 230.0
const SEND_WIDTH: float = 64.0
const MARGIN_PX: int = 8

var _title: Label
var _timer: Label
var _left: VBoxContainer
var _chat_panel: VBoxContainer
var _chat_box: VBoxContainer
var _chat_scroll: ScrollContainer
var _say_btn: Button
## One note under the chat: a refusal for a moment, else why chat is closed.
var _say_note: Label
var _counters: Label
var _text_edit: LineEdit
var _send_btn: Button
var _margin: MarginContainer
var _top_row: HBoxContainer
var _chat_title: Label
var _type_row: HBoxContainer
var _done_btn: Button
## Where the messages go (the Net autoload; a test passes a recorder).
var net: Object = Net
## A touch device (phone, tablet): the box docks at the top while it has focus. A test sets it.
var touch: bool = ChatRules.is_touch_device()
## The compact typing layout is on: only the box, the note and the history show, at the top.
var _compact: bool = false
## The picker replaces the clue list in the left column while it is open.
var _picking: bool = false
var _flash_left: float = 0.0
var _flash_text: String = ""
## A typed line was sent and neither its echo nor a refusal came yet: no second send.
var _awaiting_echo: bool = false
var _await_left: float = 0.0
var _gap_open: bool = true
var _lift: float = 0.0
var _selected: int = -2
var _voted_sent: bool = false
## Filled by meeting_result / ejection (Session keeps only the votes).
var _result: Dictionary = {}
var _ejection: Dictionary = {}
var _cards: Dictionary = {}
var _confirm_btn: Button
var _skip_btn: Button


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self)

	_margin = MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		_margin.add_theme_constant_override(side, MARGIN_PX)
	add_child(_margin)
	UIKit.fill(_margin)

	var root: VBoxContainer = UIKit.make_vbox(6)
	_margin.add_child(root)
	_top_row = HBoxContainer.new()
	root.add_child(_top_row)
	_title = UIKit.make_label("", 18)
	_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_top_row.add_child(_title)
	_timer = UIKit.make_label("", 18)
	_top_row.add_child(_timer)

	var cols: HBoxContainer = HBoxContainer.new()
	cols.add_theme_constant_override("separation", 10)
	cols.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(cols)
	_left = UIKit.make_vbox(4)
	_left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cols.add_child(_left)

	_chat_panel = UIKit.make_vbox(2, CHAT_WIDTH)
	cols.add_child(_chat_panel)
	_chat_title = UIKit.make_label(tr("MEETING_CHAT"), 12, MUTED)
	_chat_panel.add_child(_chat_title)
	_chat_scroll = ScrollContainer.new()
	_chat_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_chat_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_chat_panel.add_child(_chat_scroll)
	_chat_box = UIKit.make_vbox(2)
	_chat_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_chat_scroll.add_child(_chat_box)
	_say_note = _clue_line("", 12, MUTED)
	_chat_panel.add_child(_say_note)
	# Two short lines that fit the column: no autowrap (a wrapping label sizes itself badly here).
	_counters = UIKit.make_label("", 12, MUTED)
	_chat_panel.add_child(_counters)
	_type_row = HBoxContainer.new()
	_type_row.add_theme_constant_override("separation", 4)
	_chat_panel.add_child(_type_row)
	_text_edit = UIKit.make_line_edit(tr("CHAT_TYPE_HINT"), "", Session.text_max_chars())
	_text_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_text_edit.keep_editing_on_text_submit = true
	_text_edit.text_submitted.connect(_on_text_submitted)
	_text_edit.focus_entered.connect(_on_box_focus.bind(true))
	_text_edit.focus_exited.connect(_on_box_focus.bind(false))
	_type_row.add_child(_text_edit)
	# Send and Done never take the focus: a tap on them must not close the keyboard (and so change the
	# layout) before the tap is read.
	_send_btn = UIKit.make_button(tr("CHAT_SEND"), SEND_WIDTH)
	_send_btn.focus_mode = Control.FOCUS_NONE
	_send_btn.pressed.connect(_on_send_text)
	_type_row.add_child(_send_btn)
	_done_btn = UIKit.make_button(tr("CHAT_DONE"), SEND_WIDTH)
	_done_btn.focus_mode = Control.FOCUS_NONE
	_done_btn.visible = false
	_done_btn.pressed.connect(_on_done)
	_type_row.add_child(_done_btn)
	_say_btn = UIKit.make_button(tr("CHAT_SAY"), CHAT_WIDTH)
	_say_btn.pressed.connect(_on_say)
	_chat_panel.add_child(_say_btn)

	Net.event.connect(_on_event)
	Net.disconnected.connect(_on_disconnected)
	_rebuild()
	# An out player who comes here sees the ghost chat so far.
	if _me_out():
		for line in Session.ghost_chat:
			if typeof(line) == TYPE_DICTIONARY:
				_add_chat(line)
	_show_report_splash()


## A body report opens the meeting with the splash, once per meeting.
func _show_report_splash() -> void:
	if not bool(Session.meeting.get("splash", false)):
		return
	Session.meeting["splash"] = false
	if str(Session.meeting.get("cause", "")) != "report":
		return
	var victim: int = report_victim(Session.meeting)
	var color: Color = Session.seat_color(victim) if victim >= 0 else Color(0.6, 0.6, 0.6)
	var by: String = _name_of(int(Session.meeting.get("by", -1)))
	var sub: String = tr("MEETING_SPLASH_FOUND") % [by, _name_of(victim)] if victim >= 0 else tr("MEETING_CAUSE_REPORT") % by
	var splash: Control = ReportSplash.new()
	splash.setup(color, tr("MEETING_SPLASH_REPORT"), sub)
	add_child(splash)


## The fallen seat of the reported body (its clue), -1 when the clues do not say.
static func report_victim(meeting: Dictionary) -> int:
	var pile: int = int(meeting.get("pile", -1))
	var fallback: int = -1
	for clue in Net.as_array(meeting.get("clues")):
		if typeof(clue) != TYPE_DICTIONARY or (clue as Dictionary).get("victim") == null:
			continue
		var victim: int = int((clue as Dictionary)["victim"])
		if int((clue as Dictionary).get("pile", -2)) == pile:
			return victim
		if fallback < 0:
			fallback = victim
	return fallback


func _process(delta: float) -> void:
	if _flash_left > 0.0:
		_flash_left -= delta
		if _flash_left <= 0.0:
			_refresh_chat()
	if _awaiting_echo:
		_await_left -= delta
		if _await_left <= 0.0:
			_awaiting_echo = false
			_refresh_chat()
	# Send waits out the gap the server keeps between two lines.
	var gap_open: bool = Session.chat_gap_left() <= 0.0
	if gap_open != _gap_open:
		_gap_open = gap_open
		_refresh_chat()
	_keep_box_above_keyboard()
	# Only discussion and voting have a deadline worth showing; the server can close voting early.
	var ends_at: float = float(Session.meeting.get("ends_at", 0.0))
	var timed: bool = VoteRules.show_timer(Session.phase, _stage())
	_timer.text = VoteRules.mmss(ends_at - Net.local_time()) if timed and ends_at > 0.0 else ""


# ---------------------------------------------------------------- state

func _stage() -> String:
	return str(Session.meeting.get("stage", "discussion"))


func _me_out() -> bool:
	return Session.is_out or Session.seat_out(Session.seat_id)


func _already_voted() -> bool:
	var voted: Array = Session.meeting.get("voted", [])
	return _voted_sent or voted.has(Session.seat_id)


func _name_of(seat: int) -> String:
	var n: String = Session.seat_name(seat)
	return n if n != "" else str(seat)


## Translation by key; the readable fallback is the raw id.
func _tr_or(prefix: String, id: String) -> String:
	var key: String = prefix + id.to_upper()
	var text: String = tr(key)
	return id if text == key else text


# ---------------------------------------------------------------- events

func _on_event(data: Dictionary) -> void:
	match str(data.get("t", "")):
		"meeting_started":
			_selected = -2
			_voted_sent = false
			_result = {}
			_ejection = {}
			_rebuild()
		"voting_started":
			_rebuild()
		"vote_cast":
			_add_note(ChatRules.voted_note(_name_of(int(data.get("seat", -1)))))
			_on_vote_cast(data)
		"chat":
			_add_chat(data)
			if int(data.get("seat", -1)) == Session.seat_id and data.has("text") and _awaiting_echo:
				_awaiting_echo = false
				_text_edit.text = ""
			_chat_state_changed()
		"meeting_result":
			_result = data
			_rebuild()
		"ejection":
			_ejection = data
			_rebuild()
		"action_rejected":
			if VoteRules.unlocks_vote(data):
				_voted_sent = false
				_rebuild()
			elif ["chat_phrase", "chat_text"].has(str(data.get("action", ""))):
				if str(data.get("action", "")) == "chat_text":
					_awaiting_echo = false
				_flash_left = FLASH_S
				_flash_text = tr(ChatRules.note_key(str(data.get("reason", ""))))
				_chat_state_changed()
		"round_resumed", "game_over":
			Router.go(get_tree(), Router.scene_for_phase(Session.phase))


## Another seat's vote only changes its tag: rebuilding would free the card or Confirm button a
## finger is pressing and lose the tap. My own vote locks the view, so that one rebuilds.
func _on_vote_cast(data: Dictionary) -> void:
	var seat: int = int(data.get("seat", -1))
	if seat == Session.seat_id or _stage() != "voting" or not _cards.has(seat):
		_rebuild()
		return
	(_cards[seat] as Button).text = _seat_text(seat, true)


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())


# ---------------------------------------------------------------- views

func _rebuild() -> void:
	for c in _left.get_children():
		_left.remove_child(c)
		c.queue_free()
	_cards = {}
	_confirm_btn = null
	_skip_btn = null
	var ejecting: bool = Session.phase == "ejection"
	# Living players have no chat once the votes are in; an out player's ghost chat goes on (not in the ejection view).
	_chat_panel.visible = not ejecting and (_stage() != "results" or _me_out())
	if ejecting or not _can_say():
		_picking = false
	if not _chat_panel.visible:
		_set_compact(false)
	_refresh_chat()
	if ejecting:
		_title.text = tr("PHASE_EJECTION")
		_build_ejection()
		return
	match _stage():
		"voting":
			_title.text = tr("MEETING_VOTING")
		"results":
			_title.text = tr("MEETING_RESULTS")
		_:
			_title.text = tr("MEETING_DISCUSSION")
	if _picking:
		_build_picker()
		return
	match _stage():
		"voting":
			_build_voting()
		"results":
			_build_results()
		_:
			_build_discussion()


## The phrase picker replaces the left column at any stage (a living player only reaches it in the
## discussion; an out player may send ghost phrases at any time).
func _build_picker() -> void:
	var strip: String = _clue_strip()
	if not strip.is_empty():
		_left.add_child(_clue_line(strip, 12, MUTED))
	var picker: PhrasePicker = PhrasePicker.new()
	picker.ghost = _me_out()
	picker.send_requested.connect(_on_phrase_chosen)
	picker.closed.connect(_on_picker_closed)
	_left.add_child(picker)


func _build_discussion() -> void:
	var by: int = int(Session.meeting.get("by", -1))
	var cause_key: String = "MEETING_CAUSE_REPORT"
	if str(Session.meeting.get("cause", "")) == "emergency":
		cause_key = "MEETING_CAUSE_EMERGENCY"
	_left.add_child(UIKit.make_label(tr(cause_key) % _name_of(by), 14))
	# v2: the cleanliness meter (the v1 task bar is out of play).
	_left.add_child(UIKit.make_label(tr("HUD_METER") % Session.meter, 12, MUTED))
	_left.add_child(UIKit.make_label(tr("MEETING_CLUES"), 12, MUTED))
	var scroll: ScrollContainer = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_left.add_child(scroll)
	var list: VBoxContainer = UIKit.make_vbox(4)
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(list)
	var clues: Array = Net.as_array(Session.meeting.get("clues"))
	if clues.is_empty():
		list.add_child(UIKit.make_label(tr("MEETING_NO_CLUES"), 12, MUTED))
	for clue in clues:
		if typeof(clue) == TYPE_DICTIONARY:
			list.add_child(_clue_card(clue))


## One muted line over the picker: the zones and victims of the clues, so they stay in view while
## a line is built. "" without clues.
func _clue_strip() -> String:
	var parts: Array[String] = []
	for clue in Net.as_array(Session.meeting.get("clues")):
		if typeof(clue) != TYPE_DICTIONARY:
			continue
		var part: String = _tr_or("ZONE_", str((clue as Dictionary).get("zone", "")))
		if (clue as Dictionary).get("victim") != null:
			part = tr("MEETING_CLUE_VICTIM_LINE") % _name_of(int((clue as Dictionary)["victim"])) + " (" + part + ")"
		parts.append(part)
	return "" if parts.is_empty() else tr("MEETING_CLUES") + ": " + ", ".join(parts)


func _clue_card(clue: Dictionary) -> Control:
	var panel: PanelContainer = PanelContainer.new()
	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	panel.add_child(row)
	var waste: Variant = clue.get("waste")
	if waste != null:
		var icon: TextureRect = TextureRect.new()
		icon.texture = Assets.texture(WASTE_ICON_PATH % str(waste))
		icon.custom_minimum_size = Vector2(ICON_SIZE, ICON_SIZE)
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		row.add_child(icon)
	var lines: VBoxContainer = UIKit.make_vbox(0)
	lines.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(lines)
	var zone: String = _tr_or("ZONE_", str(clue.get("zone", "")))
	var when: String = VoteRules.mmss(float(clue.get("t", 0.0)))
	lines.add_child(_clue_line(tr("MEETING_CLUE_PLAIN") % [zone, when], 12))
	if waste != null:
		lines.add_child(_clue_line(tr("MEETING_CLUE_WASTE_LINE") % _tr_or("WASTE_", str(waste)), 12, MUTED))
	if clue.get("victim") != null:
		lines.add_child(_clue_line(tr("MEETING_CLUE_VICTIM_LINE") % _name_of(int(clue.get("victim"))), 12, MUTED))
	return panel


func _clue_line(text: String, size: int, color: Color = Color.WHITE) -> Label:
	var label: Label = UIKit.make_label(text, size, color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label


func _build_voting() -> void:
	var grid: GridContainer = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	_left.add_child(grid)
	var voted: Array = Session.meeting.get("voted", [])
	var can: bool = VoteRules.can_vote(_me_out(), _stage(), _already_voted())
	var votable: Array[int] = VoteRules.votable_seats(Session.seats, Session.out_seats)
	for s in Session.seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var seat: int = int((s as Dictionary).get("seat", -1))
		var card: Button = _seat_card(seat, voted.has(seat), votable.has(seat) and can)
		grid.add_child(card)
		_cards[seat] = card
	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	_left.add_child(row)
	_confirm_btn = UIKit.make_button(tr("MEETING_CONFIRM"), 120.0)
	_confirm_btn.pressed.connect(_on_confirm)
	row.add_child(_confirm_btn)
	_skip_btn = UIKit.make_button(tr("MEETING_SKIP"), 120.0)
	_skip_btn.pressed.connect(_on_skip)
	row.add_child(_skip_btn)
	_skip_btn.disabled = not can
	if _me_out():
		_left.add_child(UIKit.make_label(tr("MEETING_OUT_NOTE"), 12, MUTED))
	elif _already_voted():
		_left.add_child(UIKit.make_label(tr("MEETING_YOU_VOTED"), 12, MUTED))
	_mark_selection()


func _seat_card(seat: int, has_voted: bool, enabled: bool) -> Button:
	var out: bool = Session.seat_out(seat)
	var btn: Button = Button.new()
	btn.custom_minimum_size = CARD_SIZE
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.toggle_mode = true
	var tint: Color = Session.seat_color(seat)
	btn.icon = Assets.texture(ICON_PATH % int(Session.seat_info(seat).get("color", 0)), tint)
	btn.expand_icon = true
	btn.add_theme_constant_override("icon_max_width", ICON_SIZE)
	btn.text = _seat_text(seat, has_voted)
	btn.clip_text = true
	btn.add_theme_color_override("font_color", tint)
	btn.add_theme_color_override("font_disabled_color", tint)
	btn.disabled = not enabled
	if out:
		btn.modulate = OUT_TINT
	btn.pressed.connect(_on_card_pressed.bind(seat))
	return btn


## The card text: the name, tagged "out" or "voted".
func _seat_text(seat: int, has_voted: bool) -> String:
	var text: String = _name_of(seat)
	if Session.seat_out(seat):
		return tr("MEETING_SEAT_TAG") % [text, tr("MEETING_SEAT_OUT")]
	if has_voted:
		return tr("MEETING_SEAT_VOTED_TAG") % [text, tr("MEETING_VOTED")]
	return text


func _mark_selection() -> void:
	for seat in _cards:
		(_cards[seat] as Button).set_pressed_no_signal(int(seat) == _selected)
	if _confirm_btn != null:
		_confirm_btn.disabled = _selected < 0 or not VoteRules.can_vote(_me_out(), _stage(), _already_voted())


func _build_results() -> void:
	# A rejoin during results brings no votes, ejected or tie: say so instead of inventing an outcome.
	if not VoteRules.can_show_tally(_result):
		_left.add_child(UIKit.make_label(tr("MEETING_RESULTS_PENDING"), 18))
		return
	var votes: Dictionary = Net.as_dict(Session.meeting.get("votes"))
	var tally: Dictionary = {}
	var skips: int = 0
	for voter in votes:
		var target: Variant = votes[voter]
		if target == null or typeof(target) == TYPE_STRING:
			skips += 1
		else:
			var t: int = int(target)
			tally[t] = int(tally.get(t, 0)) + 1
	for seat in tally:
		_left.add_child(UIKit.make_label(tr("MEETING_VOTES_FOR") % [_name_of(int(seat)), int(tally[seat])], 14))
	_left.add_child(UIKit.make_label(tr("MEETING_VOTES_SKIP") % skips, 14, MUTED))
	var ejected: Variant = _result.get("ejected")
	var line: String
	if ejected != null:
		line = tr("MEETING_EJECTED") % _name_of(int(ejected))
	elif bool(_result.get("tie", false)):
		line = tr("MEETING_TIE")
	else:
		line = tr("MEETING_NO_EJECTION")
	_left.add_child(UIKit.make_label(line, 18))


func _build_ejection() -> void:
	var box: CenterContainer = CenterContainer.new()
	box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_left.add_child(box)
	var line: String = tr("EJECTION_PENDING")
	if not _ejection.is_empty():
		var who: String = _name_of(int(_ejection.get("seat", -1)))
		# was_polluter is null when the server does not reveal roles on ejection.
		var was_polluter: Variant = _ejection.get("was_polluter")
		line = tr(VoteRules.ejection_reveal(was_polluter)) % who
	var label: Label = UIKit.make_label(line, 22)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	var lines: VBoxContainer = UIKit.make_vbox(8)
	box.add_child(lines)
	lines.add_child(label)
	# The count is only meaningful once the ejection is known and its role was revealed (or the
	# server sent its own count); Session keeps it up to date before this view is built.
	var left_key: String = VoteRules.polluters_left_key(Session.polluters_left())
	if not _ejection.is_empty() and not left_key.is_empty():
		var left: Label = UIKit.make_label(tr(left_key) % Session.polluters_left(), 16, MUTED)
		left.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		lines.add_child(left)


# ---------------------------------------------------------------- chat

func _add_chat(data: Dictionary) -> void:
	var ghost: bool = bool(data.get("ghost", false))
	# Living players never get ghost lines; if one ever came, it stays out of sight.
	if ghost and not _me_out():
		return
	var seat: int = int(data.get("seat", -1))
	var text: String
	if data.has("text"):
		text = str(data.get("text"))
	else:
		text = Phrases.render(str(data.get("id", "")), Net.as_array(data.get("args")), Callable(self, "_name_of"))
	# A Label shows the text as typed: no BBCode, and the text is an argument of the format, never the format.
	var label: Label = UIKit.make_label(tr("MEETING_GHOST_LINE" if ghost else "MEETING_CHAT_LINE") % [_name_of(seat), text], 12)
	label.add_theme_color_override("font_color", Session.seat_color(seat).lerp(MUTED, 0.4) if ghost else Session.seat_color(seat))
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_append_line(label)


## A system line in the chat ("<name> has voted"): muted, not a seat's line.
func _add_note(text: String) -> void:
	var label: Label = UIKit.make_label(text, 12, MUTED)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_append_line(label)


func _append_line(label: Label) -> void:
	_chat_box.add_child(label)
	if not is_inside_tree():
		return
	await get_tree().process_frame
	if not is_inside_tree():
		return
	_chat_scroll.scroll_vertical = int(_chat_scroll.get_v_scroll_bar().max_value)


## Chat opened or closed under me (a line echoed, a refusal): an open picker that can no longer
## send is rebuilt (the rebuild closes it), otherwise only the controls follow.
func _chat_state_changed() -> void:
	if _picking and not _can_say():
		_rebuild()
	else:
		_refresh_chat()


## Phrases (or ghost phrases) may go now.
func _can_say() -> bool:
	return ChatRules.can_ghost(Session) if _me_out() else PhrasePicker.can_chat(Session)


## A typed line (or ghost line) may go now, as far as the stage and the limit say.
func _can_type() -> bool:
	return ChatRules.can_ghost(Session) if _me_out() else ChatRules.can_text(Session)


## The chat controls: each is closed only by its own reason. Say follows the phrase limit, the box and
## Send the typed limit; Send also waits for the answer to my last line and the gap between two lines.
func _refresh_chat() -> void:
	var ghost: bool = _me_out()
	var can_say: bool = _can_say()
	var can_type: bool = _can_type()
	_say_btn.disabled = not can_say or _picking
	_text_edit.editable = can_type
	# The stage moved on or the last line went while the box had focus: hand the screen back.
	if _compact and not can_type:
		_on_done()
	_send_btn.disabled = not can_type or _awaiting_echo or Session.chat_gap_left() > 0.0
	_counters.visible = not ghost and not _compact
	_counters.text = tr("CHAT_PHRASES_LEFT") % Session.chat_left() + "\n" + tr("CHAT_TEXT_LEFT") % Session.text_left()
	if _picking:
		_say_note.text = ""
	elif _flash_left > 0.0:
		_say_note.text = _flash_text
	elif ghost:
		_say_note.text = tr("CHAT_GHOST_HINT")
	elif can_say or can_type:
		_say_note.text = ""
	elif _stage() == "discussion" and Session.chat_left() == 0 and Session.text_left() == 0:
		_say_note.text = tr("CHAT_NONE_LEFT")
	else:
		_say_note.text = tr("CHAT_CLOSED")


## An extra for platforms that report the keyboard height: while the box has focus the view lifts its
## bottom margin by it. The web build reports 0, so touch devices use the compact layout instead.
func _keep_box_above_keyboard() -> void:
	var lift: float = 0.0
	if _text_edit.has_focus() and not _compact:
		lift = ChatRules.lift_px(DisplayServer.virtual_keyboard_get_height(), DisplayServer.window_get_size().y,
				get_viewport_rect().size.y)
	if not is_equal_approx(lift, _lift):
		_lift = lift
		_margin.add_theme_constant_override("margin_bottom", MARGIN_PX + int(lift))


# ---------------------------------------------------------------- input

func _on_say() -> void:
	if not _can_say():
		return
	_picking = true
	_rebuild()


func _on_picker_closed() -> void:
	_picking = false
	_rebuild()


func _on_phrase_chosen(id: String, args: Array) -> void:
	net.send_chat_phrase(id, args)


func _on_text_submitted(_text: String) -> void:
	_on_send_text()


## Send (or Enter): the typed line goes as it is; the box keeps it until my own line comes back,
## so a refusal (too long, too fast) costs no retyping.
func _on_send_text() -> void:
	var text: String = _text_edit.text.strip_edges()
	if text.is_empty() or not _can_type() or _awaiting_echo or Session.chat_gap_left() > 0.0:
		return
	net.send_chat_text(text)
	_awaiting_echo = true
	_await_left = ECHO_WAIT_S
	_refresh_chat()


## The box gained or lost the focus: on a touch device the soft keyboard is up or gone.
func _on_box_focus(entered: bool) -> void:
	if touch:
		_set_compact(entered)


func _on_done() -> void:
	if _text_edit.has_focus():
		_text_edit.release_focus()
	_set_compact(false)
	DisplayServer.virtual_keyboard_hide()


## The compact typing layout: the soft keyboard covers the bottom half of a landscape phone and
## the web build cannot say how high it is, so while the box has focus the box docks at the top of the
## screen with the history right under it, and the title, the clues, the picker and the other controls
## wait until the focus leaves.
func _set_compact(on: bool) -> void:
	if on == _compact:
		return
	_compact = on
	_top_row.visible = not on
	_left.visible = not on
	_chat_title.visible = not on
	_say_btn.visible = not on
	_done_btn.visible = on
	_chat_panel.custom_minimum_size.x = 0.0 if on else CHAT_WIDTH
	_chat_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL if on else Control.SIZE_FILL
	if on:
		ChatRules.arrange(_chat_panel, [_type_row, _say_note, _chat_scroll])
	else:
		ChatRules.arrange(_chat_panel, [_chat_title, _chat_scroll, _say_note, _counters, _type_row, _say_btn])
		_margin.add_theme_constant_override("margin_bottom", MARGIN_PX)
		_lift = 0.0
	_refresh_chat()


func _on_card_pressed(seat: int) -> void:
	_selected = seat
	_mark_selection()


func _on_confirm() -> void:
	if _selected < 0 or not VoteRules.can_vote(_me_out(), _stage(), _already_voted()):
		return
	Net.send_vote(_selected)
	_voted_sent = true
	_rebuild()


func _on_skip() -> void:
	if not VoteRules.can_vote(_me_out(), _stage(), _already_voted()):
		return
	Net.send_vote(-1)
	_voted_sent = true
	_rebuild()
