extends Control
## Lobby: room code, seat list (from `lobby` messages), colour picker, Start (host only), Leave.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")

const COLOR_COUNT: int = 8
const TOAST_BOTTOM_OFFSET_PX: float = -16.0

var _seat_list: VBoxContainer
var _swatches: Array[Button] = []
var _start_btn: Button
var _wait_label: Label
var _toast: Toast


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self)

	var margin: MarginContainer = MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 12)
	add_child(margin)
	UIKit.fill(margin)

	var cols: HBoxContainer = HBoxContainer.new()
	cols.add_theme_constant_override("separation", 16)
	margin.add_child(cols)

	# Left column: room code and seats.
	var left: VBoxContainer = UIKit.make_vbox(4)
	left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cols.add_child(left)
	left.add_child(UIKit.make_label(tr("LOBBY_CODE"), 14, Color(0.7, 0.8, 0.75)))
	left.add_child(UIKit.make_label(Session.room_code, 48))
	_seat_list = UIKit.make_vbox(2)
	left.add_child(_seat_list)

	# Right column: colours, start, leave.
	var right: VBoxContainer = UIKit.make_vbox(8, 210.0)
	cols.add_child(right)
	right.add_child(UIKit.make_label(tr("LOBBY_COLOR"), 14, Color(0.7, 0.8, 0.75)))
	var grid: GridContainer = GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	right.add_child(grid)
	for i in range(COLOR_COUNT):
		var sw: Button = Button.new()
		sw.custom_minimum_size = Vector2(UIKit.MIN_TOUCH, UIKit.MIN_TOUCH)
		sw.pressed.connect(_on_color_pressed.bind(i))
		grid.add_child(sw)
		_swatches.append(sw)

	_start_btn = UIKit.make_button(tr("LOBBY_START"))
	_start_btn.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_start_btn.pressed.connect(_on_start)
	right.add_child(_start_btn)
	_wait_label = UIKit.make_label(tr("LOBBY_WAITING_HOST"), 12, Color(0.7, 0.8, 0.75))
	_wait_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_wait_label.custom_minimum_size = Vector2(200.0, 0.0)
	right.add_child(_wait_label)
	var leave_btn: Button = UIKit.make_button(tr("LOBBY_LEAVE"))
	leave_btn.pressed.connect(_on_leave)
	right.add_child(leave_btn)

	_toast = Toast.new()
	_toast.anchor_left = 0.5
	_toast.anchor_right = 0.5
	_toast.anchor_top = 1.0
	_toast.anchor_bottom = 1.0
	_toast.offset_top = TOAST_BOTTOM_OFFSET_PX
	_toast.offset_bottom = TOAST_BOTTOM_OFFSET_PX
	_toast.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_toast.grow_vertical = Control.GROW_DIRECTION_BEGIN
	add_child(_toast)

	Net.lobby.connect(_on_lobby)
	Net.action_rejected.connect(_on_action_rejected)
	Net.error.connect(_on_error)
	Net.match_started.connect(_on_match_started)
	Net.disconnected.connect(_on_disconnected)

	_refresh()


func _refresh() -> void:
	for child in _seat_list.get_children():
		_seat_list.remove_child(child)
		child.queue_free()

	for s in Session.seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var d: Dictionary = s
		var seat: int = int(d.get("seat", -1))
		var row: HBoxContainer = HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_seat_list.add_child(row)

		var swatch: ColorRect = ColorRect.new()
		swatch.color = Session.color_of(int(d.get("color", -1)))
		swatch.custom_minimum_size = Vector2(16.0, 16.0)
		row.add_child(swatch)

		var player: String = str(d.get("name", ""))
		if seat == Session.seat_id:
			player += " " + tr("LOBBY_YOU")
		var name_label: Label = UIKit.make_label(player)
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(name_label)

		# Only bots are tagged: a "Player" tag on every human reads like a type field.
		if not bool(d.get("human", true)):
			row.add_child(UIKit.make_label(tr("LOBBY_BOT"), 12, Color(0.7, 0.8, 0.75)))

	var my_color: int = int(Session.seat_info(Session.seat_id).get("color", -1))
	var taken: Array[int] = taken_colors(Session.seats, Session.seat_id)
	for i in range(_swatches.size()):
		_style_swatch(_swatches[i], Session.color_of(i), i == my_color)
		_swatches[i].disabled = taken.has(i)
		_swatches[i].modulate = Color(1.0, 1.0, 1.0, 0.35) if taken.has(i) else Color.WHITE

	var host: bool = Session.is_host()
	_start_btn.visible = host
	_wait_label.visible = not host


## The colours other seats already use (the server refuses them).
static func taken_colors(seats: Array, my_seat: int) -> Array[int]:
	var taken: Array[int] = []
	for s in seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var d: Dictionary = s
		if int(d.get("seat", -1)) != my_seat and int(d.get("color", -1)) >= 0:
			taken.append(int(d.get("color")))
	return taken


func _style_swatch(btn: Button, color: Color, selected: bool) -> void:
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(6)
	box.set_border_width_all(4 if selected else 0)
	box.border_color = Color.WHITE
	for style_name in ["normal", "hover", "pressed", "focus"]:
		btn.add_theme_stylebox_override(style_name, box)


func _on_color_pressed(index: int) -> void:
	Net.send("set_color", {"color": index})


func _on_start() -> void:
	Net.send("start")


func _on_leave() -> void:
	Net.send("leave")
	Session.clear_room()
	Router.go(get_tree(), Router.MENU)


## A colour the server refused (taken a moment ago by someone else).
func _on_action_rejected(data: Dictionary) -> void:
	if str(data.get("action", "")) == "set_color":
		_toast.show_text(tr("LOBBY_COLOR_TAKEN"))


func _on_error(data: Dictionary) -> void:
	if str(data.get("code", "")) == "start_failed":
		_toast.show_text(tr("LOBBY_START_FAILED"))


func _on_lobby(_data: Dictionary) -> void:
	_refresh()


func _on_match_started(_data: Dictionary) -> void:
	Router.go(get_tree(), Router.ROUND)


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())
