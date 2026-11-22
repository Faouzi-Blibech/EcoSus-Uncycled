extends Minigame
## Read the sensor value and key it in on the pad. A wrong entry clears; the right one finishes the step.

const KEY_SIZE: Vector2 = Vector2(64, 48)
const PAD_TOP: float = 136.0
const KEY_GAP: float = 4.0
const PAD_COLUMNS: int = 3
const FALLBACK_LEFT: float = 20.0

@export var digits: int = 3

var reading: String = ""
var entry: String = ""
var _hint: Label = null
var _entry_label: Label = null


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	digits = int(Net.as_dict(Session.tuning.get("minigames")).get("meter_digits", digits))
	reading = _random_reading(digits)
	_hint = _add_label(Vector2(20, 20), tr("MG_METER_HINT"))
	_add_label(Vector2(20, 52), tr("MG_METER_READING") % reading)
	_entry_label = _add_label(Vector2(20, 84), "")
	_build_pad()
	_refresh()


## A random value of exactly `count` digits (no leading zero), as text.
static func _random_reading(count: int) -> String:
	var lo: int = int(pow(10.0, count - 1))
	var hi: int = int(pow(10.0, count)) - 1
	return str(randi_range(lo, hi))


func _add_label(pos: Vector2, text: String) -> Label:
	var l: Label = Label.new()
	l.position = pos
	l.text = text
	add_child(l)
	return l


func _build_pad() -> void:
	var left: float = FALLBACK_LEFT
	if size.x > 0.0:
		left = (size.x - (KEY_SIZE.x * PAD_COLUMNS + KEY_GAP * (PAD_COLUMNS - 1))) * 0.5
	var keys: Array = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "clear", "0"]
	for i in keys.size():
		var b: Button = Button.new()
		b.size = KEY_SIZE
		b.custom_minimum_size = KEY_SIZE
		b.position = Vector2(left + (i % PAD_COLUMNS) * (KEY_SIZE.x + KEY_GAP), PAD_TOP + (i / PAD_COLUMNS) * (KEY_SIZE.y + KEY_GAP))
		# Digits are cream keys, Clear is the coral one.
		b.theme_type_variation = "CoralButton" if keys[i] == "clear" else "CreamButton"
		if keys[i] == "clear":
			b.text = tr("MG_METER_CLEAR")
			b.pressed.connect(clear_entry)
		else:
			b.text = keys[i]
			b.pressed.connect(press_digit.bind(keys[i]))
		add_child(b)


func press_digit(d: String) -> void:
	if entry.length() >= digits:
		return
	_hint.text = tr("MG_METER_HINT")
	entry += d
	if entry.length() < digits:
		_refresh()
		return
	if entry == reading:
		_refresh()
		finish_step()
		return
	clear_entry()
	_hint.text = tr("MG_METER_WRONG")


func clear_entry() -> void:
	entry = ""
	_refresh()


func _refresh() -> void:
	if _entry_label != null:
		_entry_label.text = entry + "_".repeat(maxi(digits - entry.length(), 0))
