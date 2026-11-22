extends Minigame
## Step 0: drag the handle around the pile for full turns. Step 1: hold the gauge, release in the green band.

const HANDLE_SIZE: Vector2 = Vector2(64, 64)
const RADIUS: float = 90.0
const CENTRE_Y: float = 190.0
const FALLBACK_WIDTH: float = 640.0
const GAUGE_SCENE: PackedScene = preload("res://minigames/common/hold_gauge.tscn")

@export var turns_needed: int = 3

var _centre: Vector2 = Vector2.ZERO
var _handle: Panel = null
var _last_angle: float = 0.0
var _total_angle: float = 0.0
var _dragging: bool = false
var _hint: Label = null


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_centre = Vector2((size.x if size.x > 0.0 else FALLBACK_WIDTH) * 0.5, CENTRE_Y)
	turns_needed = int(Net.as_dict(Session.tuning.get("minigames")).get("compost_turns", turns_needed))
	_hint = Label.new()
	_hint.position = Vector2(20, 20)
	add_child(_hint)
	if step == 0:
		_build_turn()
	else:
		_build_hold()


func _build_turn() -> void:
	_hint.text = tr("MG_COMPOST_TURN_HINT") % turns_needed
	_handle = Panel.new()
	_handle.size = HANDLE_SIZE
	_handle.position = _centre + Vector2(RADIUS, 0.0) - HANDLE_SIZE * 0.5
	dress(_handle, "compost_handle")
	_handle.gui_input.connect(_on_handle_input)
	add_child(_handle)


func _handle_centre() -> Vector2:
	return _handle.position + HANDLE_SIZE * 0.5


func _on_handle_input(event: InputEvent) -> void:
	if event is InputEventMouseButton or event is InputEventScreenTouch:
		_dragging = event.is_pressed()
		if _dragging:
			_last_angle = (_handle_centre() - _centre).angle()
	elif _dragging:
		drag_handle_to(_handle_centre() + drag_delta(event))


## Moves the handle towards p, projected on the circle; the turns are counted from the angle walked.
func drag_handle_to(p: Vector2) -> void:
	var a: float = (p - _centre).angle()
	_total_angle += angle_difference(_last_angle, a)
	_last_angle = a
	_handle.position = _centre + Vector2.from_angle(a) * RADIUS - HANDLE_SIZE * 0.5
	if absf(_total_angle) >= TAU * turns_needed:
		_dragging = false
		finish_step()


func _build_hold() -> void:
	_hint.text = tr("MG_COMPOST_WATER_HINT")
	var g: HoldGauge = GAUGE_SCENE.instantiate() as HoldGauge
	g.position = _centre - Vector2(24.0, 100.0)
	g.released.connect(_on_gauge_released)
	add_child(g)


func _on_gauge_released(in_band: bool) -> void:
	if in_band:
		finish_step()
