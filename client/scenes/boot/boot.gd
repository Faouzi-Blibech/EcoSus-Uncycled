extends Control
## Boot: connect, hello, version check, then route (menu, or straight back into a saved seat).
## Shows an "update needed" screen on proto_mismatch and a retry screen when unreachable.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const SettingsPanel := preload("res://ui/widgets/settings_panel.gd")
const LOADING_ART: String = "res://assets/ui/loading_screen.jpg"

var _status: Label
var _buttons: HBoxContainer
var _mismatch: bool = false
var _panel: SettingsPanel = null


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)
	var has_art: bool = ResourceLoader.exists(LOADING_ART)
	if has_art:
		var art: TextureRect = TextureRect.new()
		art.texture = Assets.texture(LOADING_ART)
		art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
		art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(art)
		UIKit.fill(art)

	# The art carries the logo at the bottom, so the status sits in the clear sky at the top.
	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)
	if has_art:
		center.anchor_bottom = 0.0
		center.offset_top = 12.0
		center.offset_bottom = 12.0

	var box: VBoxContainer = UIKit.make_vbox(12, 400.0)
	center.add_child(box)

	if not has_art:
		var title: Label = UIKit.make_label(tr("BOOT_TITLE"), 36)
		title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		box.add_child(title)

	_status = UIKit.make_hud_label(16)
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_status)

	_buttons = HBoxContainer.new()
	_buttons.add_theme_constant_override("separation", 8)
	_buttons.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_child(_buttons)
	var retry: Button = UIKit.make_button(tr("BOOT_RETRY"))
	retry.pressed.connect(_start)
	_buttons.add_child(retry)
	var settings_btn: Button = UIKit.make_button(tr("COMMON_SETTINGS"))
	settings_btn.pressed.connect(_open_settings)
	_buttons.add_child(settings_btn)

	Net.hello_ok.connect(_on_hello_ok)
	Net.error.connect(_on_error)
	Net.welcome.connect(_on_welcome)
	Net.join_failed.connect(_on_join_failed)
	Net.connect_failed.connect(_on_offline)
	Net.disconnected.connect(_on_offline)

	_start()


func _start() -> void:
	_mismatch = false
	_buttons.visible = false
	_status.text = tr("BOOT_CONNECTING")
	Net.connect_to(Settings.server_url)


func _on_hello_ok(_data: Dictionary) -> void:
	# Saved seat (app restarted mid-room): try to take it back, else go to the menu.
	if Session.load_saved():
		Net.send("join", {
			"code": Session.room_code,
			"name": Settings.player_name,
			"token": Session.reconnect_token,
		})
	else:
		Router.go(get_tree(), Router.MENU)


func _on_welcome(_data: Dictionary) -> void:
	Router.route_after_welcome(get_tree())


func _on_join_failed(_data: Dictionary) -> void:
	var notice: String = Session.lost_seat_notice()
	Session.clear_room()
	Session.pending_notice = notice
	Router.go(get_tree(), Router.MENU)


func _on_error(data: Dictionary) -> void:
	var code: String = str(data.get("code", ""))
	if code == "proto_mismatch":
		_mismatch = true
		_status.text = tr("BOOT_UPDATE_NEEDED")
		_buttons.visible = false
		return
	_status.text = tr("BOOT_ERROR") % code
	_buttons.visible = true


func _on_offline() -> void:
	if _mismatch:
		return
	_status.text = tr("BOOT_OFFLINE") % Settings.server_url
	_buttons.visible = true


func _open_settings() -> void:
	if _panel != null:
		return
	_panel = SettingsPanel.new()
	_panel.closed.connect(_on_settings_closed)
	add_child(_panel)


func _on_settings_closed(applied: bool) -> void:
	if applied:
		get_tree().reload_current_scene()
		return
	_panel.queue_free()
	_panel = null
