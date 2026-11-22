class_name GasBanner
extends Control
## Toxic gas warning at the top of the screen while a gas warning runs: a live countdown for everyone,
## and a blinking red "LEAVE THE ROOM NOW" for players inside the gassed room. The round calls
## show_warning() on every snapshot (the server sends the seconds left); between snapshots the
## countdown runs here so it never stalls.

const TOP_PX: float = 58.0
const HEIGHT_PX: float = 40.0
const DANGER: Color = Color(0.95, 0.15, 0.12)
const WARN: Color = Color(1.0, 0.45, 0.2)
const BLINK_HZ: float = 3.0

var _label: Label
var _left_s: float = 0.0
var _inside: bool = false
var _t: float = 0.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_preset(Control.PRESET_TOP_WIDE)
	offset_top = TOP_PX
	offset_bottom = TOP_PX + HEIGHT_PX
	_label = Label.new()
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_label.add_theme_color_override("font_outline_color", Color(0.1, 0.0, 0.0))
	_label.add_theme_constant_override("outline_size", 6)
	_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_label)
	visible = false


## left_s: seconds until the gas lands (server); inside: the local player is in the gassed room.
func show_warning(left_s: float, inside: bool) -> void:
	_left_s = maxf(left_s, 0.0)
	_inside = inside
	visible = true
	_refresh()


func clear() -> void:
	visible = false


## Whole seconds shown: 3.2 s left reads 4, so the count reaches 0 when the gas lands.
static func seconds_shown(left_s: float) -> int:
	return int(ceil(maxf(left_s, 0.0)))


func _process(delta: float) -> void:
	if not visible:
		return
	_t += delta
	_left_s = maxf(_left_s - delta, 0.0)
	_refresh()


func _refresh() -> void:
	var n: int = seconds_shown(_left_s)
	if _inside:
		_label.text = tr("GAS_LEAVE_NOW") % n
		_label.add_theme_font_size_override("font_size", 26)
		_label.add_theme_color_override("font_color", DANGER)
		_label.modulate.a = 0.55 + 0.45 * absf(sin(_t * PI * BLINK_HZ))
	else:
		_label.text = tr("GAS_INCOMING") % n
		_label.add_theme_font_size_override("font_size", 18)
		_label.add_theme_color_override("font_color", WARN)
		_label.modulate.a = 1.0
