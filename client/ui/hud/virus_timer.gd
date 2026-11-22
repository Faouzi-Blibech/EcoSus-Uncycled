class_name VirusTimer
extends Label
## A big red countdown shown only to the infected player. It shows what Session holds.

const URGENT_SECONDS: float = 10.0
const PULSE_SPEED: float = 8.0
const PULSE_MIN: float = 0.55

var _urgent: bool = false
var _time: float = 0.0


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	autowrap_mode = TextServer.AUTOWRAP_OFF
	add_theme_font_size_override("font_size", 18)
	add_theme_color_override("font_color", Color("c8553f"))
	add_theme_color_override("font_outline_color", Color("faf2dd"))
	add_theme_constant_override("outline_size", 4)
	visible = false


## Hidden when `seconds` < 0, otherwise shows the whole seconds left.
func show_seconds(seconds: float) -> void:
	visible = seconds >= 0.0
	_urgent = is_urgent(seconds)
	if not visible:
		return
	text = tr("HUD_INFECTED") % VirusTimer.whole_seconds(seconds)
	if not _urgent:
		modulate.a = 1.0


func _process(delta: float) -> void:
	if not _urgent or not visible:
		return
	_time += delta
	modulate.a = lerpf(PULSE_MIN, 1.0, 0.5 + 0.5 * sin(_time * PULSE_SPEED))


## Seconds rounded up, never below 0.
static func whole_seconds(seconds: float) -> int:
	return maxi(ceili(seconds), 0)


## True in the last 10 seconds (and not yet expired below 0).
static func is_urgent(seconds: float) -> bool:
	return seconds >= 0.0 and seconds <= URGENT_SECONDS
