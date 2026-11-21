extends Control
## Home page: two big art buttons (Create Game, Join Game) and three small ones (profile, scan, settings).
## Join asks for the 4-letter code in a popup. A missing name opens the profile popup first.
## Layout follows the designers' 1920x1080 mockup at 1/3 scale on a centred 640x360 stage.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const SettingsPanel := preload("res://ui/widgets/settings_panel.gd")

const ART_DIR: String = "res://assets/ui/home/"
const FONT_PATH: String = "res://assets/fonts/caroni_regular.otf"

const CODE_LENGTH: int = 4
const BUSY_TIMEOUT_S: float = 5.0

const STAGE_SIZE: Vector2 = Vector2(640.0, 360.0)
const LOGO_RECT: Rect2 = Rect2(250.0, 10.0, 140.0, 53.0)
const CREATE_RECT: Rect2 = Rect2(63.0, 82.0, 234.0, 207.0)
const JOIN_RECT: Rect2 = Rect2(330.0, 84.0, 257.0, 205.0)
const CAPTION_TOP: float = 292.0
const CAPTION_HEIGHT: float = 40.0
const CAPTION_FONT_SIZE: int = 34
const STATUS_TOP: float = 334.0
## Icons draw at ICON_SIZE inside a UIKit.MIN_TOUCH square, so the tap target stays 48 px.
const ICON_SIZE: float = 40.0
const ICON_MARGIN: float = 6.0
const ICON_GAP: float = 0.0
const SHADOW_SHADER: String = "res://ui/widgets/soft_shadow.gdshader"
const SHADOW_OFFSET: Vector2 = Vector2(2.0, 4.0)
const SHADOW_BLUR: float = 4.0
const SHADOW_PAD: float = 10.0
const SHADOW_COLOR: Color = Color(0.2, 0.14, 0.08, 0.35)
## The two big circles cast a larger shadow so it reads at their size.
const BIG_SHADOW_SCALE: float = 2.0
const PRESS_SCALE: float = 0.95
const TWEEN_S: float = 0.08

const INK: Color = Color(0.17, 0.15, 0.13)
const ERROR_INK: Color = Color(0.65, 0.2, 0.12)
const BG_FALLBACK: Color = Color(0.97, 0.94, 0.85)
const BUSY_MODULATE: Color = Color(1.0, 1.0, 1.0, 0.5)
const POPUP_BG: Color = Color(0.13, 0.19, 0.15)
const POPUP_BORDER: Color = Color(0.97, 0.94, 0.85)
const POPUP_BORDER_PX: int = 2
const POPUP_RADIUS_PX: int = 10

var _create_btn: TextureButton
var _join_btn: TextureButton
var _status: Label
var _panel: SettingsPanel = null
var _busy: bool = false
var _font: Font = null

## Open popup (join code, profile or scan); null when none.
var _popup: Control = null
var _popup_error: Label = null
var _code_edit: LineEdit = null
var _name_edit: LineEdit = null
var _popup_join_btn: Button = null
## Run after the profile popup saves a name (Create or Join pressed with no name).
var _after_name: Callable = Callable()


func _ready() -> void:
	if Net.state != Net.State.OPEN:
		Router.go(get_tree(), Router.BOOT)
		return

	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self, BG_FALLBACK)
	if ResourceLoader.exists(FONT_PATH):
		_font = load(FONT_PATH) as Font

	var bg: TextureRect = TextureRect.new()
	bg.texture = Assets.texture(ART_DIR + "background.jpg")
	bg.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	bg.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)
	UIKit.fill(bg)

	# Fixed 640x360 stage centred on screen; wider phones get extra paper at the sides.
	var stage: Control = Control.new()
	stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(stage)
	stage.set_anchors_preset(Control.PRESET_CENTER)
	stage.offset_left = -STAGE_SIZE.x / 2.0
	stage.offset_top = -STAGE_SIZE.y / 2.0
	stage.offset_right = STAGE_SIZE.x / 2.0
	stage.offset_bottom = STAGE_SIZE.y / 2.0

	var logo: TextureRect = TextureRect.new()
	logo.texture = Assets.texture(ART_DIR + "logo.png")
	logo.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	logo.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	logo.mouse_filter = Control.MOUSE_FILTER_IGNORE
	logo.position = LOGO_RECT.position
	logo.size = LOGO_RECT.size
	stage.add_child(logo)
	_add_shadow(logo, logo.texture)

	_create_btn = _make_art_button(stage, "create.png", CREATE_RECT, tr("MENU_CREATE_GAME"))
	_create_btn.pressed.connect(_on_create)
	_join_btn = _make_art_button(stage, "join.png", JOIN_RECT, tr("MENU_JOIN_GAME"))
	_join_btn.pressed.connect(_open_join_popup)

	_status = _make_label("", UIKit.FONT_SIZE, ERROR_INK, false)
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status.position = Vector2(0.0, STATUS_TOP)
	_status.size = Vector2(STAGE_SIZE.x, STAGE_SIZE.y - STATUS_TOP)
	stage.add_child(_status)

	# Small round buttons in the screen corners (not the stage, so they hug the real edges).
	var profile_btn: TextureButton = _make_icon_button("user.png", tr("MENU_PROFILE"))
	profile_btn.pressed.connect(_open_profile_popup)
	_pin_icon(profile_btn, false, 0)
	var settings_btn: TextureButton = _make_icon_button("settings.png", tr("COMMON_SETTINGS"))
	settings_btn.pressed.connect(_open_settings)
	_pin_icon(settings_btn, true, 0)
	var scan_btn: TextureButton = _make_icon_button("tasks.png", tr("MENU_SCAN"))
	scan_btn.pressed.connect(_open_scan_popup)
	_pin_icon(scan_btn, false, 1)

	if Session.pending_notice != "":
		_status.text = tr(Session.pending_notice)
		Session.pending_notice = ""

	Net.welcome.connect(_on_welcome)
	Net.join_failed.connect(_on_join_failed)
	Net.error.connect(_on_error)
	Net.disconnected.connect(_on_disconnected)


# --- Building blocks ---------------------------------------------------------------------------

func _make_label(text: String, font_size: int, color: Color, art_font: bool) -> Label:
	var l: Label = UIKit.make_label(text, font_size, color)
	if art_font and _font != null:
		l.add_theme_font_override("font", _font)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


## Big picture button with its handwritten caption underneath.
func _make_art_button(parent: Control, file: String, rect: Rect2, caption: String) -> TextureButton:
	var btn: TextureButton = TextureButton.new()
	btn.texture_normal = Assets.texture(ART_DIR + file)
	btn.ignore_texture_size = true
	btn.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	btn.position = rect.position
	btn.size = rect.size
	parent.add_child(btn)
	_add_shadow(btn, btn.texture_normal, BIG_SHADOW_SCALE)
	_add_juice(btn)

	var label: Label = _make_label(caption, CAPTION_FONT_SIZE, INK, true)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.position = Vector2(rect.position.x, CAPTION_TOP)
	label.size = Vector2(rect.size.x, CAPTION_HEIGHT)
	parent.add_child(label)
	return btn


## Invisible 48 px tap square with the smaller icon art centred inside.
func _make_icon_button(file: String, tooltip: String) -> TextureButton:
	var btn: TextureButton = TextureButton.new()
	btn.custom_minimum_size = Vector2(UIKit.MIN_TOUCH, UIKit.MIN_TOUCH)
	btn.tooltip_text = tooltip
	add_child(btn)
	var art: TextureRect = TextureRect.new()
	art.texture = Assets.texture(ART_DIR + file)
	art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	art.mouse_filter = Control.MOUSE_FILTER_IGNORE
	btn.add_child(art)
	var inset: float = (UIKit.MIN_TOUCH - ICON_SIZE) / 2.0
	art.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	art.offset_left = inset
	art.offset_top = inset
	art.offset_right = -inset
	art.offset_bottom = -inset
	_add_shadow(art, art.texture)
	_add_juice(btn)
	return btn


## Anchors an icon to the top-left (right=false) or top-right corner; slot stacks downwards.
func _pin_icon(btn: TextureButton, right: bool, slot: int) -> void:
	var top: float = ICON_MARGIN + float(slot) * (UIKit.MIN_TOUCH + ICON_GAP)
	if right:
		btn.set_anchors_preset(Control.PRESET_TOP_RIGHT)
		btn.offset_left = -ICON_MARGIN - UIKit.MIN_TOUCH
		btn.offset_right = -ICON_MARGIN
	else:
		btn.set_anchors_preset(Control.PRESET_TOP_LEFT)
		btn.offset_left = ICON_MARGIN
		btn.offset_right = ICON_MARGIN + UIKit.MIN_TOUCH
	btn.offset_top = top
	btn.offset_bottom = top + UIKit.MIN_TOUCH


## Soft shadow drawn behind target (a child, so it follows the press squash). Skipped without the shader.
func _add_shadow(target: Control, tex: Texture2D, strength: float = 1.0) -> void:
	if not ResourceLoader.exists(SHADOW_SHADER):
		return
	var offset: Vector2 = SHADOW_OFFSET * strength
	var blur_px: float = SHADOW_BLUR * strength
	var pad_px: float = SHADOW_PAD * strength
	var mat: ShaderMaterial = ShaderMaterial.new()
	mat.shader = load(SHADOW_SHADER) as Shader
	mat.set_shader_parameter("shadow_color", SHADOW_COLOR)
	var shadow: TextureRect = TextureRect.new()
	shadow.texture = tex
	shadow.material = mat
	shadow.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	shadow.stretch_mode = TextureRect.STRETCH_SCALE
	shadow.show_behind_parent = true
	shadow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	target.add_child(shadow)
	shadow.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	shadow.offset_left = offset.x - pad_px
	shadow.offset_top = offset.y - pad_px
	shadow.offset_right = offset.x + pad_px
	shadow.offset_bottom = offset.y + pad_px
	var update: Callable = func() -> void:
		var s: Vector2 = target.size
		if s.x <= 0.0 or s.y <= 0.0:
			return
		var full: Vector2 = s + Vector2(pad_px, pad_px) * 2.0
		mat.set_shader_parameter("pad", Vector2(pad_px / full.x, pad_px / full.y))
		mat.set_shader_parameter("blur", Vector2(blur_px / s.x, blur_px / s.y))
	target.resized.connect(update)
	update.call()


## Squashes a little while pressed.
func _add_juice(btn: TextureButton) -> void:
	btn.resized.connect(func() -> void: btn.pivot_offset = btn.size / 2.0)
	btn.button_down.connect(_scale_to.bind(btn, PRESS_SCALE))
	btn.button_up.connect(_scale_to.bind(btn, 1.0))


func _scale_to(btn: TextureButton, s: float) -> void:
	if btn.disabled:
		return
	btn.create_tween().tween_property(btn, "scale", Vector2(s, s), TWEEN_S)


# --- Popups ------------------------------------------------------------------------------------

## Dimmed modal with a titled panel; returns the box to fill. Replaces any open popup.
func _open_popup(title: String) -> VBoxContainer:
	_close_popup()
	_popup = Control.new()
	_popup.theme = UIKit.theme()
	_popup.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_popup)
	UIKit.fill(_popup)
	UIKit.background(_popup, Color(0.0, 0.0, 0.0, 0.6))

	var center: CenterContainer = CenterContainer.new()
	_popup.add_child(center)
	UIKit.fill(center)
	var panel: PanelContainer = PanelContainer.new()
	var style: StyleBoxFlat = StyleBoxFlat.new()
	style.bg_color = POPUP_BG
	style.border_color = POPUP_BORDER
	style.set_border_width_all(POPUP_BORDER_PX)
	style.set_corner_radius_all(POPUP_RADIUS_PX)
	panel.add_theme_stylebox_override("panel", style)
	center.add_child(panel)
	var margin: MarginContainer = MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 12)
	panel.add_child(margin)

	var box: VBoxContainer = UIKit.make_vbox(8, 300.0)
	margin.add_child(box)
	box.add_child(UIKit.make_label(title, 20))
	return box


## Error line plus the bottom button row (Cancel/Close on the right). Returns the row.
func _finish_popup(box: VBoxContainer, close_key: String) -> HBoxContainer:
	_popup_error = UIKit.make_label("", UIKit.FONT_SIZE, Color(1.0, 0.6, 0.5))
	_popup_error.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_popup_error)
	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	box.add_child(row)
	var close_btn: Button = UIKit.make_button(tr(close_key))
	close_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	close_btn.pressed.connect(_close_popup)
	row.add_child(close_btn)
	return row


func _close_popup() -> void:
	if _popup != null:
		_popup.queue_free()
	_popup = null
	_popup_error = null
	_code_edit = null
	_name_edit = null
	_popup_join_btn = null


func _open_join_popup() -> void:
	if _busy:
		return
	if not _has_name():
		_ask_name(_open_join_popup)
		return
	var box: VBoxContainer = _open_popup(tr("MENU_JOIN_TITLE"))
	_code_edit = UIKit.make_line_edit(tr("MENU_CODE_PLACEHOLDER"), "", CODE_LENGTH)
	_code_edit.text_submitted.connect(func(_t: String) -> void: _on_join())
	box.add_child(_code_edit)
	var row: HBoxContainer = _finish_popup(box, "COMMON_CANCEL")
	_popup_join_btn = UIKit.make_button(tr("MENU_JOIN"))
	_popup_join_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_popup_join_btn.pressed.connect(_on_join)
	row.add_child(_popup_join_btn)
	row.move_child(_popup_join_btn, 0)
	_code_edit.grab_focus()


func _open_profile_popup() -> void:
	var box: VBoxContainer = _open_popup(tr("MENU_PROFILE"))
	box.add_child(UIKit.make_label(tr("SETTINGS_NAME")))
	_name_edit = UIKit.make_line_edit(tr("MENU_NAME_PLACEHOLDER"), Settings.player_name, 16)
	_name_edit.text_submitted.connect(func(_t: String) -> void: _on_profile_save())
	box.add_child(_name_edit)
	var skins: Label = UIKit.make_label(tr("MENU_PROFILE_SKINS"))
	skins.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(skins)
	var row: HBoxContainer = _finish_popup(box, "COMMON_CANCEL")
	var save_btn: Button = UIKit.make_button(tr("COMMON_SAVE"))
	save_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	save_btn.pressed.connect(_on_profile_save)
	row.add_child(save_btn)
	row.move_child(save_btn, 0)
	_name_edit.grab_focus()


func _open_scan_popup() -> void:
	var box: VBoxContainer = _open_popup(tr("MENU_SCAN"))
	var text: Label = UIKit.make_label(tr("MENU_SCAN_TEXT"))
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(text)
	_finish_popup(box, "COMMON_CLOSE")


## Opens the profile popup with a hint; then runs next once a name is saved.
func _ask_name(next: Callable) -> void:
	_open_profile_popup()
	_popup_error.text = tr("MENU_NAME_REQUIRED")
	_after_name = next


func _on_profile_save() -> void:
	var player: String = _name_edit.text.strip_edges()
	if player.is_empty():
		_popup_error.text = tr("MENU_NAME_REQUIRED")
		return
	Settings.player_name = player
	Settings.save()
	_close_popup()
	var next: Callable = _after_name
	_after_name = Callable()
	if next.is_valid():
		next.call()


# --- Create / join (unchanged protocol) --------------------------------------------------------

func _has_name() -> bool:
	return not Settings.player_name.strip_edges().is_empty()


func _show_status(text: String) -> void:
	if _popup_error != null:
		_popup_error.text = text
	else:
		_status.text = text


func _on_create() -> void:
	if _busy:
		return
	if not _has_name():
		_ask_name(_on_create)
		return
	_set_busy(true)
	Net.send("create", {"name": Settings.player_name.strip_edges()})


func _on_join() -> void:
	if _busy or _code_edit == null:
		return
	var code: String = _code_edit.text.strip_edges().to_upper()
	if code.length() != CODE_LENGTH:
		_show_status(tr("MENU_BAD_CODE"))
		return
	_set_busy(true)
	Net.send("join", {"code": code, "name": Settings.player_name.strip_edges()})


func _set_busy(busy: bool) -> void:
	_busy = busy
	for btn in [_create_btn, _join_btn]:
		(btn as TextureButton).disabled = busy
		(btn as TextureButton).modulate = BUSY_MODULATE if busy else Color.WHITE
	if _popup_join_btn != null:
		_popup_join_btn.disabled = busy
	if busy:
		_show_status(tr("MENU_WORKING"))
		get_tree().create_timer(BUSY_TIMEOUT_S).timeout.connect(_on_busy_timeout)


func _on_busy_timeout() -> void:
	if _busy:
		_set_busy(false)
		_show_status(tr("MENU_NO_ANSWER"))


func _on_welcome(_data: Dictionary) -> void:
	Router.route_after_welcome(get_tree())


func _on_join_failed(data: Dictionary) -> void:
	_set_busy(false)
	_show_status(UIKit.reason_text("JOIN_FAILED_", str(data.get("reason", "")), "JOIN_FAILED_UNKNOWN"))


func _on_error(data: Dictionary) -> void:
	_set_busy(false)
	_show_status(tr("BOOT_ERROR") % str(data.get("code", "")))


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())


func _open_settings() -> void:
	if _panel != null:
		return
	_close_popup()
	_panel = SettingsPanel.new()
	_panel.closed.connect(_on_settings_closed)
	add_child(_panel)


func _on_settings_closed(applied: bool) -> void:
	if applied:
		get_tree().reload_current_scene()
		return
	_panel.queue_free()
	_panel = null
