extends Control
## Stand-in for phases that are not built yet (meeting, ejection, ended). Names the phase,
## shows the meter, and routes back as soon as the server says playing / lobby.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")

var _title: Label
var _meter: Label


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	PaperKit.background(self)

	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)
	var box: VBoxContainer = UIKit.make_vbox(10, 360.0)
	center.add_child(box)

	_title = UIKit.make_label("", 28)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_title)
	var hint: Label = UIKit.make_label(tr("PLACEHOLDER_HINT"), 14, Color(0.7, 0.8, 0.75))
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(hint)
	_meter = UIKit.make_label("", 16)
	_meter.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_meter)

	Net.snapshot.connect(_on_update)
	Net.event.connect(_on_update)
	Net.lobby.connect(_on_lobby)
	Net.match_started.connect(_on_match_started)
	Net.disconnected.connect(_on_disconnected)

	_refresh()


func _refresh() -> void:
	_title.text = tr("PHASE_" + Session.phase.to_upper())
	_meter.text = tr("HUD_METER") % Session.meter


func _on_update(_data: Dictionary) -> void:
	if Router.is_round_phase(Session.phase):
		Router.go(get_tree(), Router.ROUND)
		return
	_refresh()


func _on_lobby(_data: Dictionary) -> void:
	Router.go(get_tree(), Router.LOBBY)


func _on_match_started(_data: Dictionary) -> void:
	Router.go(get_tree(), Router.ROUND)


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())
