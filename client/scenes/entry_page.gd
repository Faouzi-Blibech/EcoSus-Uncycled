extends Control
## Shared base of the create and join pages: paper background, back button, status line, and the
## create/join request with its busy state. Subclasses build their content in _build(stage).

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")

const BUSY_TIMEOUT_S: float = 5.0
const STATUS_FONT_SIZE: int = 16
const LOGO_PATH: String = "res://assets/ui/home/logo.png"
## Figma "Create Game" / "jOIN gAME" frames at 1/3 scale.
const LOGO_RECT: Rect2 = Rect2(281.0, 2.0, 93.0, 52.0)
const PANEL_FILE: String = "panel_paper_hd.png"

var _status: Label
var _busy: bool = false


func _ready() -> void:
	if Net.state != Net.State.OPEN:
		Router.go(get_tree(), Router.BOOT)
		return
	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)
	var stage: Control = PaperKit.stage(self)
	_status = PaperKit.label("", STATUS_FONT_SIZE, PaperKit.ERROR_INK, false)
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var logo: TextureRect = PaperKit.picture(Assets.texture(LOGO_PATH), LOGO_RECT)
	stage.add_child(logo)
	PaperKit.add_shadow(logo, logo.texture)
	_build(stage)
	var back: TextureButton = PaperKit.icon_button(self, PaperKit.KIT_DIR + "icon_back.png", tr("COMMON_BACK"))
	back.pressed.connect(_on_back)
	PaperKit.pin_icon(back, false, 0)

	Net.welcome.connect(_on_welcome)
	Net.join_failed.connect(_on_join_failed)
	Net.error.connect(_on_error)
	Net.disconnected.connect(_on_disconnected)


## Subclasses add their widgets to stage and place _status.
func _build(_stage: Control) -> void:
	pass


## The big paper sheet, at its own proportions inside rect.
func _add_paper(stage: Control, rect: Rect2) -> void:
	var paper: TextureRect = PaperKit.picture(PaperKit.tex(PANEL_FILE), rect)
	stage.add_child(paper)


## Subclasses enable or disable their own buttons.
func _apply_busy(_busy_now: bool) -> void:
	pass


func _send(type: String, payload: Dictionary) -> void:
	if _busy:
		return
	_set_busy(true)
	payload["name"] = Settings.player_name.strip_edges()
	Net.send(type, payload)


func _set_busy(busy: bool) -> void:
	_busy = busy
	_apply_busy(busy)
	if busy:
		_status.text = tr("MENU_WORKING")
		get_tree().create_timer(BUSY_TIMEOUT_S).timeout.connect(_on_busy_timeout)


func _on_busy_timeout() -> void:
	if _busy:
		_set_busy(false)
		_status.text = tr("MENU_NO_ANSWER")


func _on_back() -> void:
	Router.go(get_tree(), Router.MENU)


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
