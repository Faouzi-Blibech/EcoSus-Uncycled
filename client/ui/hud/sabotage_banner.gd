class_name SabotageBanner
extends Label
## The line under the timer while a sabotage runs: what it is, the fire countdown and how many fire
## consoles are held. It shows what Session holds; the round decides nothing here.

const FIRE_COLOR: Color = Color(0.45, 0.12, 0.08, 0.8)
const SMOG_COLOR: Color = Color(0.3, 0.3, 0.3, 0.8)

var _box: StyleBoxFlat = StyleBoxFlat.new()


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_theme_font_size_override("font_size", 12)
	add_theme_color_override("font_outline_color", Color.BLACK)
	add_theme_constant_override("outline_size", 3)
	_box.set_content_margin_all(5.0)
	add_theme_stylebox_override("normal", _box)
	visible = false


## `sabotage` is Session.sabotage, `fire_total` the number of fire consoles on the map.
func show_sabotage(sabotage: Dictionary, fire_total: int, repairing: bool, repair_fraction: float = -1.0) -> void:
	visible = not sabotage.is_empty()
	if not visible:
		return
	text = text_for(sabotage, fire_total, repairing, repair_fraction)
	_box.bg_color = FIRE_COLOR if str(sabotage.get("kind", "")) == "fire" else SMOG_COLOR


## The banner text: fire shows the countdown (m:ss) and the held count, smog the repair state, with the
## progress in percent when `repair_fraction` (0..1) is given.
static func text_for(sabotage: Dictionary, fire_total: int, repairing: bool, repair_fraction: float = -1.0) -> String:
	if str(sabotage.get("kind", "")) == "fire":
		var left: int = maxi(ceili(float(sabotage.get("countdown", 0.0))), 0)
		var held: int = Net.as_array(sabotage.get("held")).size()
		return "%s\n%s" % [
			TranslationServer.translate(&"SABOTAGE_BANNER_FIRE") % ("%d:%02d" % [left / 60, left % 60]),
			TranslationServer.translate(&"SABOTAGE_BANNER_FIRE_HELD") % [held, fire_total]]
	var line: String = TranslationServer.translate(&"SABOTAGE_BANNER_SMOG")
	if repairing:
		line += "\n" + TranslationServer.translate(&"SABOTAGE_REPAIRING")
		if repair_fraction >= 0.0:
			line += " %d%%" % roundi(clampf(repair_fraction, 0.0, 1.0) * 100.0)
	return line
