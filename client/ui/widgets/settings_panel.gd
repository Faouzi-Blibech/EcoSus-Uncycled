extends Control
## Modal settings overlay: name, language, server URL, scan service URL, and a button to the
## community subreddit. Emits closed(applied). The caller reloads
## its scene when applied is true (language may have changed) and frees the panel.

signal closed(applied: bool)

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const LanguagePicker := preload("res://ui/widgets/language_picker.gd")

const COLUMN_WIDTH_PX: float = 270.0
## Reddit's orange, so the button reads as the subreddit link.
const COMMUNITY_COLOR: Color = Color("ff4500")
const COMMUNITY_PRESSED_COLOR: Color = Color("cc3700")

var _name_edit: LineEdit
var _lang: LanguagePicker
var _url_edit: LineEdit
var _scan_edit: LineEdit
## The scan address shown when the panel opened, when it was the automatic one: left as is, it follows
## a new server address.
var _scan_auto_shown: String = ""
var _error: Label
var _panel: PanelContainer


func _ready() -> void:
	theme = UIKit.theme()
	mouse_filter = Control.MOUSE_FILTER_STOP
	UIKit.fill(self)
	UIKit.background(self, Color(0.0, 0.0, 0.0, 0.8))

	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)

	var panel: PanelContainer = PanelContainer.new()
	_panel = panel
	center.add_child(panel)
	var margin: MarginContainer = MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 12)
	panel.add_child(margin)

	var box: VBoxContainer = UIKit.make_vbox(6)
	margin.add_child(box)
	box.add_child(UIKit.make_label(tr("SETTINGS_TITLE"), 20))

	# Two columns keep the panel inside 360 px: name and language | server address.
	var cols: HBoxContainer = HBoxContainer.new()
	cols.add_theme_constant_override("separation", 16)
	box.add_child(cols)
	var left: VBoxContainer = UIKit.make_vbox(6, COLUMN_WIDTH_PX)
	cols.add_child(left)
	var right: VBoxContainer = UIKit.make_vbox(6, COLUMN_WIDTH_PX)
	cols.add_child(right)

	left.add_child(UIKit.make_label(tr("SETTINGS_NAME")))
	_name_edit = UIKit.make_line_edit(tr("MENU_NAME_PLACEHOLDER"), Settings.player_name, 16)
	left.add_child(_name_edit)

	left.add_child(UIKit.make_label(tr("SETTINGS_LANGUAGE")))
	_lang = LanguagePicker.new(true)
	left.add_child(_lang)

	right.add_child(UIKit.make_label(tr("SETTINGS_SERVER")))
	_url_edit = UIKit.make_line_edit(Settings.default_server_url(), Settings.server_url)
	right.add_child(_url_edit)

	right.add_child(UIKit.make_label(tr("SETTINGS_SCAN")))
	_scan_edit = UIKit.make_line_edit(Settings.default_scan_url(), Settings.scan_url)
	if Settings.scan_url == Settings.default_scan_url():
		_scan_auto_shown = Settings.scan_url
	right.add_child(_scan_edit)

	_error = UIKit.make_label("", 14, Color(1.0, 0.5, 0.5))
	_error.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	right.add_child(_error)

	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	box.add_child(row)
	var save_btn: Button = UIKit.make_button(tr("COMMON_SAVE"))
	save_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	save_btn.pressed.connect(_on_save)
	row.add_child(save_btn)
	var cancel_btn: Button = UIKit.make_button(tr("COMMON_CANCEL"))
	cancel_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cancel_btn.pressed.connect(_on_cancel)
	row.add_child(cancel_btn)
	# In the button row: it adds no height to a panel that must fit 360 px.
	var community_btn: Button = _make_community_button()
	row.add_child(community_btn)
	row.move_child(community_btn, 0)


## Orange button that opens the subreddit in the browser (a new tab on the web build).
func _make_community_button() -> Button:
	var btn: Button = UIKit.make_button(tr("SETTINGS_COMMUNITY"))
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.tooltip_text = Settings.COMMUNITY_URL
	for state in ["normal", "hover", "focus"]:
		btn.add_theme_stylebox_override(state, _flat_box(COMMUNITY_COLOR))
	btn.add_theme_stylebox_override("pressed", _flat_box(COMMUNITY_PRESSED_COLOR))
	for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color"]:
		btn.add_theme_color_override(state, Color.WHITE)
	btn.pressed.connect(func() -> void: OS.shell_open(Settings.COMMUNITY_URL))
	return btn


func _flat_box(color: Color) -> StyleBoxFlat:
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(6)
	box.set_content_margin_all(6)
	return box


func _on_save() -> void:
	var url: String = _url_edit.text.strip_edges()
	if url.is_empty():
		url = Settings.default_server_url()
	if not (url.begins_with("ws://") or url.begins_with("wss://")):
		_error.text = tr("SETTINGS_BAD_URL")
		return
	var typed: String = _scan_edit.text.strip_edges()
	var follow: bool = typed.is_empty() or typed == _scan_auto_shown
	var scan: String = "" if follow else Settings.normalize_scan_url(typed)
	if not follow and scan.is_empty():
		_error.text = tr("SETTINGS_BAD_SCAN_URL")
		return
	Settings.player_name = _name_edit.text.strip_edges()
	Settings.language = _lang.selected()
	Settings.server_url = url
	Settings.scan_url = scan if not follow else Settings.default_scan_url()
	Settings.apply_language()
	Settings.save()
	Profile.refresh()
	closed.emit(true)


func _on_cancel() -> void:
	closed.emit(false)
