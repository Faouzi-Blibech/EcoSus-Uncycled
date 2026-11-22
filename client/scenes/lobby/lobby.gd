extends Control
## Lobby (Figma "Game Created"): the seat list on a paper sheet (avatar, name, a tick per taken
## seat, a crown over the host), the player count, the room code (tap to copy), your own character
## walking in place (tap it or the leaf to choose character and colour), and Start for the host.
## Bots fill the free seats when the host starts.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const Characters := preload("res://ui/widgets/characters.gd")
const CharacterSprite := preload("res://ui/widgets/character_sprite.gd")
const CharacterPicker := preload("res://ui/widgets/character_picker.gd")

const TOAST_BOTTOM_OFFSET_PX: float = -16.0
const LEAF_PATH: String = "res://assets/ui/home/tasks.png"

## Rects are the Figma frame at 1/3 scale.
const PAPER_RECT: Rect2 = Rect2(-513.0, -19.0, 781.0, 414.0)
const ROW_LEFT: float = 11.0
const ROW_TOP: float = 37.0
const ROW_BOTTOM: float = 352.0
const ROW_W: float = 151.0
const ROW_H_MAX: float = 38.0
## player_slot.png is SLOT_TEX_H px tall; each bar is drawn at SLOT_TEX_H / row height density, so the
## circle and right pill keep their shape at any row height.
const SLOT_TEX_H: float = 108.0
const SLOT_MARGIN_LEFT_PX: int = 108
const SLOT_MARGIN_RIGHT_PX: int = 160
const NAME_RAISE: float = 3.0
## Inside a 38 px row: name pill and avatar, scaled with the row height.
const PILL_X: float = 40.0
const PILL_W: float = 101.0
const PILL_H: float = 14.0
const PILL_COLOUR: Color = Color(0.89, 0.84, 0.77)
const AVATAR_X: float = 8.0
const AVATAR_SIZE: float = 22.0
const NAME_FONT_SIZE: int = 14
const CHECK_X: float = 166.0
const CHECK_SIZE: float = 25.0
const CROWN_SIZE: Vector2 = Vector2(22.0, 20.0)
const CROWN_OFFSET: Vector2 = Vector2(6.0, -9.0)

const COUNT_RECT: Rect2 = Rect2(262.0, 8.0, 60.0, 32.0)
const COUNT_FONT_SIZE: int = 28
const CODE_RECT: Rect2 = Rect2(540.0, 10.0, 89.0, 27.0)
const CODE_FONT_SIZE: int = 20
const BACK_POS: Vector2 = Vector2(486.0, 0.0)
const ME_RECT: Rect2 = Rect2(382.0, 105.0, 111.0, 148.0)
const LEAF_POS: Vector2 = Vector2(590.0, 254.0)
const START_RECT: Rect2 = Rect2(500.0, 308.0, 132.0, 44.0)
const WAIT_RECT: Rect2 = Rect2(330.0, 308.0, 300.0, 44.0)
const WAIT_FONT_SIZE: int = 16

var _stage: Control
var _rows: Control
var _count: Label
var _me: CharacterSprite
var _start_btn: Button
var _wait_label: Label
var _toast: Toast
var _picker: CharacterPicker = null
## One flag per listed seat slot: true when a player sits there (the tick).
var _filled: Array[bool] = []


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)
	_stage = PaperKit.stage(self)

	var paper: TextureRect = PaperKit.picture(PaperKit.tex("panel_paper_hd.png"), PAPER_RECT)
	_stage.add_child(paper)
	_rows = Control.new()
	_rows.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_stage.add_child(_rows)

	_count = PaperKit.label("", COUNT_FONT_SIZE)
	PaperKit.place(_count, COUNT_RECT)
	_stage.add_child(_count)

	var code_btn: TextureButton = TextureButton.new()
	code_btn.texture_normal = PaperKit.tex("field_input.png")
	code_btn.ignore_texture_size = true
	code_btn.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	code_btn.tooltip_text = tr("LOBBY_COPY")
	PaperKit.place(code_btn, CODE_RECT)
	code_btn.pressed.connect(_on_copy)
	_stage.add_child(code_btn)
	PaperKit.add_shadow(code_btn, code_btn.texture_normal)
	var code: Label = PaperKit.label(Session.room_code, CODE_FONT_SIZE)
	code.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	code.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	PaperKit.place(code, CODE_RECT.grow(6.0))
	_stage.add_child(code)

	var back: TextureButton = PaperKit.icon_button(_stage, PaperKit.KIT_DIR + "icon_back.png", tr("LOBBY_LEAVE"))
	back.position = BACK_POS
	back.pressed.connect(_on_leave)

	_me = CharacterSprite.new()
	_me.setup(Characters.DEFAULT_ID, Color.WHITE)
	_me.mouse_filter = Control.MOUSE_FILTER_STOP
	_me.gui_input.connect(_on_me_input)
	PaperKit.place(_me, ME_RECT)
	_stage.add_child(_me)
	PaperKit.add_shadow(_me, _me.texture)

	var leaf: TextureButton = PaperKit.icon_button(_stage, LEAF_PATH, tr("LOBBY_CHARACTER"))
	leaf.position = LEAF_POS
	leaf.pressed.connect(_open_picker)

	_start_btn = PaperKit.button(tr("LOBBY_START_SHORT"), "green")
	PaperKit.place_button(_start_btn, START_RECT)
	_start_btn.pressed.connect(_on_start)
	_stage.add_child(_start_btn)
	_wait_label = PaperKit.label(tr("LOBBY_WAITING_HOST"), WAIT_FONT_SIZE, PaperKit.SOFT_INK)
	_wait_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_wait_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_wait_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	PaperKit.place(_wait_label, WAIT_RECT)
	_stage.add_child(_wait_label)

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
	Profile.changed.connect(_on_profile_changed)
	Profile.refresh()

	_refresh()
	_wear_saved_skin()


func _refresh() -> void:
	var seats: Array = _seat_dicts()
	var seat_count: int = maxi(Session.room_size, seats.size())
	_count.text = "%d/%d" % [seats.size(), seat_count]
	_refresh_rows(seats, seat_count)

	var me: Dictionary = Session.seat_info(Session.seat_id)
	_me.setup(_character_of(me), Session.color_of(int(me.get("color", -1))))

	var host: bool = Session.is_host()
	_start_btn.visible = host
	_wait_label.visible = not host
	if _picker != null:
		_picker.refresh(_my_colour(), taken_colors(Session.seats, Session.seat_id), _character_of(me), Profile.skins)


func _seat_dicts() -> Array:
	var out: Array = []
	for s in Session.seats:
		if typeof(s) == TYPE_DICTIONARY:
			out.append(s)
	return out


func _my_colour() -> int:
	return int(Session.seat_info(Session.seat_id).get("color", -1))


## The colours other seats already use (the server refuses them).
static func taken_colors(seats: Array, my_seat: int) -> Array[int]:
	var taken: Array[int] = []
	for s in seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var d: Dictionary = s
		if int(d.get("seat", -1)) == my_seat:
			continue
		taken.append(int(d.get("color", -1)))
	return taken


## The character a seat shows: its skin from the server (which only accepts ids from
## content/skins.json), or the default sprout when it has none or there is no art for it.
static func _character_of(d: Dictionary) -> String:
	var skin: String = str(d.get("skin", ""))
	return skin if Characters.has(skin) else Characters.DEFAULT_ID


## The skin to send when entering a room: the saved choice once the scan service confirms it is
## unlocked; "" (default) when it is not; null while the profile has not answered yet.
static func skin_to_wear(saved: String, unlocked: Array[String], profile_loaded: bool) -> Variant:
	if saved == "" or saved == Characters.DEFAULT_ID:
		return ""
	if not profile_loaded:
		return null
	return saved if unlocked.has(saved) else ""


## Put the saved character on this seat if it differs from what the server shows.
func _wear_saved_skin() -> void:
	var wanted: Variant = skin_to_wear(Profile.skin, Profile.skins, Profile.loaded or Profile.unlock_all)
	if wanted == null:
		return
	var current: String = str(Session.seat_info(Session.seat_id).get("skin", ""))
	if str(wanted) != current:
		Net.send("set_skin", {"skin": str(wanted)})


func _on_profile_changed() -> void:
	_wear_saved_skin()
	_refresh()


# --- Seat list ---------------------------------------------------------------------------------

func _refresh_rows(seats: Array, seat_count: int) -> void:
	for child in _rows.get_children():
		_rows.remove_child(child)
		child.queue_free()
	_filled.clear()
	var row_h: float = minf(ROW_H_MAX, (ROW_BOTTOM - ROW_TOP) / float(maxi(seat_count, 1)))
	var k: float = row_h / ROW_H_MAX
	for i in range(seat_count):
		var rect: Rect2 = Rect2(ROW_LEFT, ROW_TOP + i * row_h, ROW_W, row_h - 2.0 * k)
		var bar: NinePatchRect = PaperKit.panel("player_slot.png", rect, 0)
		bar.patch_margin_left = SLOT_MARGIN_LEFT_PX
		bar.patch_margin_right = SLOT_MARGIN_RIGHT_PX
		var density: float = SLOT_TEX_H / rect.size.y
		bar.size = rect.size * density
		bar.scale = Vector2.ONE / density
		_rows.add_child(bar)

		var pill: Panel = Panel.new()
		var box: StyleBoxFlat = StyleBoxFlat.new()
		box.bg_color = PILL_COLOUR
		box.set_corner_radius_all(int(PILL_H * k / 2.0))
		pill.add_theme_stylebox_override("panel", box)
		pill.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var pill_rect: Rect2 = Rect2(rect.position.x + PILL_X * k, rect.get_center().y - PILL_H * k / 2.0, PILL_W, PILL_H * k)
		PaperKit.place(pill, pill_rect)
		_rows.add_child(pill)

		var filled: bool = i < seats.size()
		_filled.append(filled)
		var check: String = "checkbox_checked.png" if filled else "checkbox_empty.png"
		var check_size: float = CHECK_SIZE * k
		_rows.add_child(PaperKit.picture(PaperKit.tex(check), Rect2(CHECK_X, rect.get_center().y - check_size / 2.0, check_size, check_size)))
		if filled:
			_add_player(seats[i], rect, pill_rect, k)


func _add_player(d: Dictionary, rect: Rect2, pill_rect: Rect2, k: float) -> void:
	var avatar_size: float = AVATAR_SIZE * k
	var avatar_rect: Rect2 = Rect2(rect.position.x + AVATAR_X * k, rect.get_center().y - avatar_size / 2.0, avatar_size, avatar_size)
	_rows.add_child(CharacterSprite.avatar(_character_of(d), Session.color_of(int(d.get("color", -1))), avatar_rect))

	var player: String = str(d.get("name", ""))
	if not bool(d.get("human", true)):
		player += " (" + tr("LOBBY_BOT") + ")"
	var name_label: Label = PaperKit.label(player, NAME_FONT_SIZE)
	name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	name_label.clip_text = true
	# The handwriting's line is taller than the pill: give the label room and centre it on the pill.
	PaperKit.place(name_label, Rect2(pill_rect.position.x + 6.0, pill_rect.get_center().y - 12.0 - NAME_RAISE, pill_rect.size.x - 10.0, 24.0))
	_rows.add_child(name_label)

	if int(d.get("seat", -1)) == Session.host_seat:
		var crown_pos: Vector2 = rect.position + CROWN_OFFSET * k
		_rows.add_child(PaperKit.picture(PaperKit.tex("icon_crown.png"), Rect2(crown_pos, CROWN_SIZE * k)))


# --- Actions -----------------------------------------------------------------------------------

func _on_me_input(event: InputEvent) -> void:
	var tap: bool = event is InputEventMouseButton and (event as InputEventMouseButton).pressed \
		and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT
	if tap or (event is InputEventScreenTouch and (event as InputEventScreenTouch).pressed):
		_open_picker()


func _open_picker() -> void:
	if _picker != null:
		return
	_picker = CharacterPicker.new()
	_picker.colour_picked.connect(_on_colour_picked)
	_picker.skin_picked.connect(_on_skin_picked)
	_picker.closed.connect(_close_picker)
	add_child(_picker)
	_refresh()


func _close_picker() -> void:
	if _picker != null:
		_picker.queue_free()
	_picker = null


func _on_colour_picked(index: int) -> void:
	Net.send("set_color", {"color": index})


## id is "" for the default character. Saved so the next rooms start with it.
func _on_skin_picked(id: String) -> void:
	Net.send("set_skin", {"skin": id})
	Profile.choose_skin(id)


func _on_copy() -> void:
	DisplayServer.clipboard_set(Session.room_code)
	_toast.show_text(tr("LOBBY_COPIED"))


func _on_start() -> void:
	Net.send("start")


func _on_leave() -> void:
	Net.send("leave")
	Session.clear_room()
	Router.go(get_tree(), Router.MENU)


## A colour the server refused (taken a moment ago by someone else), or a skin it does not know.
func _on_action_rejected(data: Dictionary) -> void:
	var action: String = str(data.get("action", ""))
	if action == "set_color":
		_toast.show_text(tr("LOBBY_COLOR_TAKEN"))
	elif action == "set_skin":
		_toast.show_text(tr("CHAR_LOCKED"))


func _on_error(data: Dictionary) -> void:
	if str(data.get("code", "")) == "start_failed":
		_toast.show_text(tr("LOBBY_START_FAILED"))


func _on_lobby(_data: Dictionary) -> void:
	_refresh()


func _on_match_started(_data: Dictionary) -> void:
	Router.go(get_tree(), Router.ROUND)


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())
