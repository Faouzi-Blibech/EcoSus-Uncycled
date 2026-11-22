extends Control
## Shown while Net retries the socket (0.5, 1, 2, 4 s backoff, 30 s total) and rejoins with the token.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")

var _status: Label


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)

	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)
	var box: VBoxContainer = UIKit.make_vbox(10, 360.0)
	center.add_child(box)

	var title: Label = UIKit.make_label(tr("RECONNECT_TITLE"), 24)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)
	_status = UIKit.make_label(tr("RECONNECT_WAITING"), 16)
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_status)

	var back: Button = UIKit.make_button(tr("RECONNECT_BACK"), 120.0, "cream")
	back.pressed.connect(_on_back)
	box.add_child(back)

	Net.reconnecting.connect(_on_reconnecting)
	Net.welcome.connect(_on_welcome)
	Net.reconnect_failed.connect(_on_failed)

	Net.start_reconnect()


## Gives up: a player who wants out should not wait the full 30 s.
func _on_back() -> void:
	Net.send("leave")
	Net.cancel_reconnect()
	Session.clear_room()
	Router.go(get_tree(), Router.MENU)


func _on_reconnecting(_attempt: int) -> void:
	_status.text = tr("RECONNECT_TRYING")


func _on_welcome(_data: Dictionary) -> void:
	Router.route_after_welcome(get_tree())


func _on_failed() -> void:
	var notice: String = Session.lost_seat_notice()
	Session.clear_room()
	Session.pending_notice = notice
	Router.go(get_tree(), Router.MENU)
