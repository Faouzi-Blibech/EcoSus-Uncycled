extends Control
## Modal settings overlay: name, language, server URL, scan service URL. Emits closed(applied). The caller reloads
## its scene when applied is true (language may have changed) and frees the panel.

signal closed(applied: bool)

const UIKit := preload("res://ui/widgets/ui_kit.gd")

const COLUMN_WIDTH_PX: float = 270.0

var _name_edit: LineEdit
var _lang: OptionButton
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
	_lang = OptionButton.new()
	_lang.custom_minimum_size = Vector2(0.0, UIKit.MIN_TOUCH)
	for code in Settings.LANGUAGES:
		_lang.add_item(tr("LANG_" + code.to_upper()))
	var idx: int = Settings.LANGUAGES.find(Settings.language)
	_lang.select(maxi(idx, 0))
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
	Settings.language = Settings.LANGUAGES[maxi(_lang.selected, 0)]
	Settings.server_url = url
	Settings.scan_url = scan if not follow else Settings.default_scan_url()
	Settings.apply_language()
	Settings.save()
	Profile.refresh()
	closed.emit(true)


func _on_cancel() -> void:
	closed.emit(false)
