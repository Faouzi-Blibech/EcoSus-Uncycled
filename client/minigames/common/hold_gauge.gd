class_name HoldGauge
extends Control
## Fills while the pointer is held on it; releasing inside the green band succeeds, otherwise it resets.

signal released(in_band: bool)

@export var fill_per_s: float = 0.5
@export var band_lo: float = 0.6
@export var band_hi: float = 0.85

var value: float = 0.0
var holding: bool = false
## Set by an in-band release: the step is done, so the gauge stops taking presses.
var locked: bool = false


func _ready() -> void:
	var cfg: Dictionary = Net.as_dict(Session.tuning.get("minigames"))
	fill_per_s = float(cfg.get("gauge_fill_per_s", fill_per_s))
	band_lo = float(cfg.get("gauge_band_lo", band_lo))
	band_hi = float(cfg.get("gauge_band_hi", band_hi))
	set_process(false)


func in_band() -> bool:
	return value >= band_lo and value <= band_hi


func press() -> void:
	if locked:
		return
	holding = true
	value = 0.0
	set_process(true)


func release() -> void:
	if not holding:
		return
	holding = false
	set_process(false)
	var ok: bool = in_band()
	locked = ok
	if not ok:
		value = 0.0
	queue_redraw()
	released.emit(ok)


func _process(delta: float) -> void:
	value = minf(value + fill_per_s * delta, 1.0)
	queue_redraw()


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton or event is InputEventScreenTouch:
		if event.is_pressed():
			press()
		else:
			release()


func _draw() -> void:
	var h: float = size.y
	draw_rect(Rect2(Vector2.ZERO, size), Color(0.42, 0.36, 0.3))
	draw_rect(Rect2(0.0, h * (1.0 - band_hi), size.x, h * (band_hi - band_lo)), Color(0.45, 0.7, 0.3))
	var fill: Color = Color(0.62, 0.85, 0.45, 0.9) if locked else Color(0.97, 0.94, 0.85, 0.85)
	draw_rect(Rect2(0.0, h * (1.0 - value), size.x, h * value), fill)
