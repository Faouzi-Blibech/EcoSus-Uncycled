extends Control
## Home page: two big art buttons (Create Game, Join Game) and three small ones (profile, scan,
## settings). Create and Join open their own pages; a missing name opens the profile popup first.
## Layout follows the designers' 1920x1080 mockup at 1/3 scale on a centred 640x360 stage.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const SettingsPanel := preload("res://ui/widgets/settings_panel.gd")

const ART_DIR: String = "res://assets/ui/home/"

const LOGO_RECT: Rect2 = Rect2(250.0, 10.0, 140.0, 53.0)
const CREATE_RECT: Rect2 = Rect2(63.0, 82.0, 234.0, 207.0)
const JOIN_RECT: Rect2 = Rect2(330.0, 84.0, 257.0, 205.0)
const CAPTION_TOP: float = 292.0
const CAPTION_HEIGHT: float = 40.0
const CAPTION_FONT_SIZE: int = 34
const STATUS_TOP: float = 334.0
## The two big circles cast a larger shadow so it reads at their size.
const BIG_SHADOW_SCALE: float = 2.0


var _status: Label
var _panel: SettingsPanel = null

## Open popup (profile or scan); null when none.
var _popup: Control = null
var _popup_error: Label = null
var _name_edit: LineEdit = null
## Scan popup parts (null when it is closed).
var _scan_stats: Label = null
var _scan_status: Label = null
## Run after the profile popup saves a name (Create or Join pressed with no name).
var _after_name: Callable = Callable()


func _ready() -> void:
	if Net.state != Net.State.OPEN:
		Router.go(get_tree(), Router.BOOT)
		return

	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)
	Profile.changed.connect(_show_scan_stats)
	Profile.scan_opened.connect(_on_scan_opened)
	Profile.scan_failed.connect(_on_scan_failed)
	Profile.scan_done.connect(_on_scan_done)
	var stage: Control = PaperKit.stage(self)

	var logo: TextureRect = PaperKit.picture(Assets.texture(ART_DIR + "logo.png"), LOGO_RECT)
	stage.add_child(logo)
	PaperKit.add_shadow(logo, logo.texture)

	var create_btn: TextureButton = _make_art_button(stage, "create.png", CREATE_RECT, tr("MENU_CREATE_GAME"))
	create_btn.pressed.connect(_go_with_name.bind(Router.CREATE))
	var join_btn: TextureButton = _make_art_button(stage, "join.png", JOIN_RECT, tr("MENU_JOIN_GAME"))
	join_btn.pressed.connect(_go_with_name.bind(Router.JOIN))

	_status = PaperKit.label("", UIKit.FONT_SIZE, PaperKit.ERROR_INK, false)
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	PaperKit.place(_status, Rect2(0.0, STATUS_TOP, PaperKit.STAGE_SIZE.x, PaperKit.STAGE_SIZE.y - STATUS_TOP))
	stage.add_child(_status)

	var profile_btn: TextureButton = PaperKit.icon_button(self, ART_DIR + "user.png", tr("MENU_PROFILE"))
	profile_btn.pressed.connect(_open_profile_popup)
	PaperKit.pin_icon(profile_btn, false, 0)
	var scan_btn: TextureButton = PaperKit.icon_button(self, ART_DIR + "tasks.png", tr("MENU_SCAN"))
	scan_btn.pressed.connect(_open_scan_popup)
	PaperKit.pin_icon(scan_btn, false, 1)
	var settings_btn: TextureButton = PaperKit.icon_button(self, ART_DIR + "settings.png", tr("COMMON_SETTINGS"))
	settings_btn.pressed.connect(_open_settings)
	PaperKit.pin_icon(settings_btn, true, 0)

	if Session.pending_notice != "":
		_status.text = tr(Session.pending_notice)
		Session.pending_notice = ""

	Net.disconnected.connect(_on_disconnected)


## Big picture button with its handwritten caption underneath.
func _make_art_button(parent: Control, file: String, rect: Rect2, caption: String) -> TextureButton:
	var btn: TextureButton = TextureButton.new()
	btn.texture_normal = Assets.texture(ART_DIR + file)
	btn.ignore_texture_size = true
	btn.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	PaperKit.place(btn, rect)
	parent.add_child(btn)
	PaperKit.add_shadow(btn, btn.texture_normal, BIG_SHADOW_SCALE)
	PaperKit.add_press_squash(btn)

	var label: Label = PaperKit.label(caption, CAPTION_FONT_SIZE)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	PaperKit.place(label, Rect2(rect.position.x, CAPTION_TOP, rect.size.x, CAPTION_HEIGHT))
	parent.add_child(label)
	return btn


func _has_name() -> bool:
	return not Settings.player_name.strip_edges().is_empty()


## Create and Join need a name: ask for it first, then open the page.
func _go_with_name(scene: String) -> void:
	if not _has_name():
		_ask_name(_go_with_name.bind(scene))
		return
	Router.go(get_tree(), scene)


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
	# The paper panel comes from the shared theme (PaperKit.theme).
	var panel: PanelContainer = PanelContainer.new()
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
	_popup_error = UIKit.make_label("", UIKit.FONT_SIZE, PaperKit.ERROR_INK)
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
	_name_edit = null
	_scan_stats = null
	_scan_status = null


func _open_profile_popup() -> void:
	var box: VBoxContainer = _open_popup(tr("MENU_PROFILE"))
	box.add_child(UIKit.make_label(tr("SETTINGS_NAME")))
	_name_edit = UIKit.make_line_edit(tr("MENU_NAME_PLACEHOLDER"), Settings.player_name, 16)
	_name_edit.text_submitted.connect(func(_t: String) -> void: _on_profile_save())
	box.add_child(_name_edit)
	var skins: Label = UIKit.make_label(tr("MENU_PROFILE_SKINS"))
	skins.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(skins)
	if Profile.loaded:
		box.add_child(UIKit.make_label(scan_stats_text(Profile.points, Profile.streak)))
	var row: HBoxContainer = _finish_popup(box, "COMMON_CANCEL")
	var save_btn: Button = UIKit.make_button(tr("COMMON_SAVE"))
	save_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	save_btn.pressed.connect(_on_profile_save)
	row.add_child(save_btn)
	row.move_child(save_btn, 0)
	_name_edit.grab_focus()


## Scan: points and streak from the photo service, a button that starts a scan (the web build
## leaves for the scan page in the same tab, Android opens the browser, desktop shows a QR code).
func _open_scan_popup() -> void:
	var box: VBoxContainer = _open_popup(tr("MENU_SCAN"))
	var text: Label = UIKit.make_label(tr("MENU_SCAN_TEXT"))
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(text)
	_scan_stats = UIKit.make_label("")
	box.add_child(_scan_stats)
	_scan_status = UIKit.make_label("", UIKit.FONT_SIZE, PaperKit.SOFT_INK)
	_scan_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_scan_status.visible = false
	box.add_child(_scan_status)
	var row: HBoxContainer = _finish_popup(box, "COMMON_CLOSE")
	var scan_btn: Button = UIKit.make_button(tr("MENU_SCAN_START"))
	scan_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scan_btn.pressed.connect(_on_scan_pressed)
	row.add_child(scan_btn)
	row.move_child(scan_btn, 0)
	_show_scan_stats()
	Profile.refresh()


static func scan_stats_text(points: int, streak: int) -> String:
	return tr_static("MENU_SCAN_STATS") % [points, streak]


static func tr_static(key: String) -> String:
	return str(TranslationServer.translate(StringName(key)))


func _show_scan_stats() -> void:
	if _scan_stats != null:
		_scan_stats.text = scan_stats_text(Profile.points, Profile.streak) if Profile.loaded else ""


func _on_scan_pressed() -> void:
	_set_scan_status(tr("MENU_SCAN_WAIT"))
	if _popup_error != null:
		_popup_error.text = ""
	Profile.start_scan()


## The camera page is open on this phone: take the photo there, then come back.
func _on_scan_opened() -> void:
	_set_scan_status(tr("MENU_SCAN_OPENED"))


func _on_scan_failed() -> void:
	if _scan_status != null:
		_set_scan_status("")
		_popup_error.text = tr("MENU_SCAN_OFFLINE")


func _on_scan_done() -> void:
	if _scan_status != null:
		_set_scan_status(tr("MENU_SCAN_DONE"))


## The status line only takes room when it says something.
func _set_scan_status(text: String) -> void:
	if _scan_status != null:
		_scan_status.text = text
		_scan_status.visible = not text.is_empty()


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
