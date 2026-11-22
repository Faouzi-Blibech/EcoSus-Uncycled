class_name RoleReveal
extends Control
## The intro card: role, goal, and for a polluter the partner.

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const BACKDROP: Color = Color(0.0, 0.0, 0.0, 0.6)
## Cleaners read their card on the mint note, Trashers on the purple one (cream ink).
const CITIZEN_COLOR: Color = Color(0.2, 0.45, 0.18)
const POLLUTER_COLOR: Color = Color(1.0, 0.72, 0.55)
const POLLUTER_INK: Color = Color(0.97, 0.94, 0.85)
const CARD_WIDTH: float = 360.0
const TITLE_SIZE: int = 32

var _title: Label
var _goal: Label
var _partner: Label
var _card: PanelContainer


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self, BACKDROP)
	var center: CenterContainer = CenterContainer.new()
	add_child(center)
	UIKit.fill(center)
	_card = PanelContainer.new()
	center.add_child(_card)
	var box: VBoxContainer = UIKit.make_vbox(10, CARD_WIDTH)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	_card.add_child(box)
	_title = UIKit.make_label("", TITLE_SIZE)
	_goal = UIKit.make_label("")
	_partner = UIKit.make_label("")
	for l in [_title, _goal, _partner]:
		(l as Label).horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		(l as Label).autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		box.add_child(l)


## partner_name may be empty (citizens never get one).
func setup(role: String, partner_name: String) -> void:
	var polluter: bool = role == "polluter"
	_title.text = tr("ROLE_" + role.to_upper())
	_title.add_theme_color_override("font_color", POLLUTER_COLOR if polluter else CITIZEN_COLOR)
	_goal.text = tr("REVEAL_GOAL_POLLUTER" if polluter else "REVEAL_GOAL_CITIZEN")
	_partner.text = tr("HUD_PARTNER") % partner_name if not partner_name.is_empty() else ""
	_card.add_theme_stylebox_override("panel", PaperKit.box("note_purple.png" if polluter else "note_mint.png", 24.0, Vector2(28.0, 26.0)))
	for l in [_goal, _partner]:
		(l as Label).add_theme_color_override("font_color", POLLUTER_INK if polluter else PaperKit.INK)
