class_name RoleReveal
extends Control
## The intro card: role, goal, and for a polluter the partner and the signature waste.

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const BACKDROP: Color = Color(0.04, 0.07, 0.06, 0.92)
const CITIZEN_COLOR: Color = Color(0.55, 0.9, 0.6)
const POLLUTER_COLOR: Color = Color(0.95, 0.45, 0.35)
const TITLE_SIZE: int = 32

var _title: Label
var _goal: Label
var _partner: Label
var _signature: Label


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	UIKit.fill(self)
	UIKit.background(self, BACKDROP)
	var box: VBoxContainer = UIKit.make_vbox(10)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	add_child(box)
	UIKit.fill(box)
	_title = UIKit.make_label("", TITLE_SIZE)
	_goal = UIKit.make_label("")
	_partner = UIKit.make_label("")
	_signature = UIKit.make_label("")
	for l in [_title, _goal, _partner, _signature]:
		(l as Label).horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		(l as Label).autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		box.add_child(l)


## partner_name and signature may be empty (citizens never get them).
func setup(role: String, partner_name: String, signature: String) -> void:
	var polluter: bool = role == "polluter"
	_title.text = tr("ROLE_" + role.to_upper())
	_title.add_theme_color_override("font_color", POLLUTER_COLOR if polluter else CITIZEN_COLOR)
	_goal.text = tr("REVEAL_GOAL_POLLUTER" if polluter else "REVEAL_GOAL_CITIZEN")
	_partner.text = tr("HUD_PARTNER") % partner_name if not partner_name.is_empty() else ""
	_signature.text = tr("REVEAL_SIGNATURE") % tr("WASTE_" + signature.to_upper()) if not signature.is_empty() else ""
