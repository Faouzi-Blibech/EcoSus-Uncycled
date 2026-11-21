extends Control
## Menu: player name, create a room, join by 4-letter code, settings.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const SettingsPanel := preload("res://ui/widgets/settings_panel.gd")

const CODE_LENGTH: int = 4
const BUSY_TIMEOUT_S: float = 5.0

var _name_edit: LineEdit
var _code_edit: LineEdit
var _create_btn: Button
var _join_btn: Button
var _status: Label
var _panel: SettingsPanel = null
var _busy: bool = false


func _ready() -> void:
	if Net.state != Net.State.OPEN:
		Router.go(get_tree(), Router.BOOT)
		return

	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self)

	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)

	var box: VBoxContainer = UIKit.make_vbox(8, 320.0)
	center.add_child(box)

	var title: Label = UIKit.make_label(tr("BOOT_TITLE"), 30)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)

	_name_edit = UIKit.make_line_edit(tr("MENU_NAME_PLACEHOLDER"), Settings.player_name, 16)
	box.add_child(_name_edit)

	_create_btn = UIKit.make_button(tr("MENU_CREATE"))
	_create_btn.pressed.connect(_on_create)
	box.add_child(_create_btn)

	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	box.add_child(row)
	_code_edit = UIKit.make_line_edit(tr("MENU_CODE_PLACEHOLDER"), "", CODE_LENGTH)
	_code_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_code_edit.text_submitted.connect(_on_code_submitted)
	row.add_child(_code_edit)
	_join_btn = UIKit.make_button(tr("MENU_JOIN"), 110.0)
	_join_btn.pressed.connect(_on_join)
	row.add_child(_join_btn)

	_status = UIKit.make_label("", 14, Color(1.0, 0.75, 0.5))
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.custom_minimum_size = Vector2(320.0, 20.0)
	box.add_child(_status)

	var settings_btn: Button = UIKit.make_button(tr("COMMON_SETTINGS"))
	settings_btn.pressed.connect(_open_settings)
	box.add_child(settings_btn)

	if Session.pending_notice != "":
		_status.text = tr(Session.pending_notice)
		Session.pending_notice = ""

	Net.welcome.connect(_on_welcome)
	Net.join_failed.connect(_on_join_failed)
	Net.error.connect(_on_error)
	Net.disconnected.connect(_on_disconnected)


func _read_name() -> String:
	var player: String = _name_edit.text.strip_edges()
	if player.is_empty():
		_status.text = tr("MENU_NAME_REQUIRED")
	else:
		Settings.player_name = player
		Settings.save()
	return player


func _on_create() -> void:
	if _busy:
		return
	var player: String = _read_name()
	if player.is_empty():
		return
	_set_busy(true)
	Net.send("create", {"name": player})


func _on_code_submitted(_text: String) -> void:
	_on_join()


func _on_join() -> void:
	if _busy:
		return
	var player: String = _read_name()
	if player.is_empty():
		return
	var code: String = _code_edit.text.strip_edges().to_upper()
	if code.length() != CODE_LENGTH:
		_status.text = tr("MENU_BAD_CODE")
		return
	_set_busy(true)
	Net.send("join", {"code": code, "name": player})


func _set_busy(busy: bool) -> void:
	_busy = busy
	_create_btn.disabled = busy
	_join_btn.disabled = busy
	if busy:
		_status.text = tr("MENU_WORKING")
		get_tree().create_timer(BUSY_TIMEOUT_S).timeout.connect(_on_busy_timeout)


func _on_busy_timeout() -> void:
	if _busy:
		_set_busy(false)
		_status.text = tr("MENU_NO_ANSWER")


func _on_welcome(_data: Dictionary) -> void:
	Router.route_after_welcome(get_tree())


func _on_join_failed(data: Dictionary) -> void:
	_set_busy(false)
	_status.text = UIKit.reason_text("JOIN_FAILED_", str(data.get("reason", "")), "JOIN_FAILED_UNKNOWN")


func _on_error(data: Dictionary) -> void:
	_set_busy(false)
	_status.text = tr("BOOT_ERROR") % str(data.get("code", ""))


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())


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
