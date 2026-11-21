extends Minigame
## Step 0: drag the crate up to lift it. Step 1: drag the bottles into the bank, one by one.

const CRATE_SIZE: Vector2 = Vector2(160, 72)
const CRATE_Y: float = 250.0
const BANK_SIZE: Vector2 = Vector2(200, 80)
const BANK_Y: float = 70.0
const BOTTLE_SIZE: Vector2 = Vector2(72, 64)
const BOTTLES_TOP: float = 190.0
const VIEW_SIZE: Vector2 = Vector2(640.0, 360.0)
const BOUNCE_S: float = 0.25
const FALLBACK_GAP: float = 12.0

@export var lift_px: float = 80.0
@export var bottle_count: int = 4

var _crate: Panel = null
var _crate_home_y: float = 0.0
var _dragging: Control = null
var _bank: Panel = null
var _remaining: int = 0
var _lifted: bool = false


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var cfg: Dictionary = Net.as_dict(Session.tuning.get("minigames"))
	lift_px = float(cfg.get("glass_lift_px", lift_px))
	bottle_count = int(cfg.get("glass_bottles", bottle_count))
	var hint: Label = Label.new()
	hint.position = Vector2(20, 20)
	add_child(hint)
	if step == 0:
		hint.text = tr("MG_GLASS_LIFT_HINT")
		_build_crate()
	else:
		hint.text = tr("MG_GLASS_DROP_HINT") % bottle_count
		_build_bank()
		_build_bottles()


func _centre_x(box_w: float) -> float:
	if size.x > 0.0:
		return (size.x - box_w) * 0.5
	return FALLBACK_GAP


func _label_in(p: Control, key: String) -> void:
	var l: Label = Label.new()
	l.text = tr(key)
	l.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	p.add_child(l)


func _build_crate() -> void:
	_crate = Panel.new()
	_crate.size = CRATE_SIZE
	_crate_home_y = CRATE_Y
	_crate.position = Vector2(_centre_x(CRATE_SIZE.x), CRATE_Y)
	_label_in(_crate, "MG_GLASS_CRATE")
	_crate.gui_input.connect(_on_crate_input)
	add_child(_crate)


func _on_crate_input(event: InputEvent) -> void:
	if event is InputEventMouseButton or event is InputEventScreenTouch:
		_dragging = _crate if event.is_pressed() else null
	elif _dragging == _crate:
		drag_crate(drag_delta(event).y)


## Moves the crate by dy pixels (up is negative), never below its start; done once lifted lift_px.
func drag_crate(dy: float) -> void:
	if _lifted or _crate == null:
		return
	_crate.position.y = clampf(_crate.position.y + dy, _crate_home_y - lift_px, _crate_home_y)
	if _crate_home_y - _crate.position.y >= lift_px:
		_lifted = true
		_dragging = null
		finish_step()


func _build_bank() -> void:
	_bank = Panel.new()
	_bank.size = BANK_SIZE
	_bank.position = Vector2(_centre_x(BANK_SIZE.x), BANK_Y)
	_bank.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_label_in(_bank, "MG_GLASS_BANK")
	add_child(_bank)


## Lays the bottles out in centred rows that wrap at the screen width; the boxes shrink when the
## rows would run past the bottom of the screen (a large tuned count).
func _build_bottles() -> void:
	_remaining = bottle_count
	var w: float = size.x if size.x > 0.0 else VIEW_SIZE.x
	var per_row: int = clampi(floori((w - FALLBACK_GAP) / (BOTTLE_SIZE.x + FALLBACK_GAP)), 1, bottle_count)
	var rows: int = ceili(float(bottle_count) / per_row)
	var k: float = minf(1.0, (VIEW_SIZE.y - FALLBACK_GAP - BOTTLES_TOP) / (rows * (BOTTLE_SIZE.y + FALLBACK_GAP)))
	var box: Vector2 = BOTTLE_SIZE * k
	for i in bottle_count:
		var row: int = i / per_row
		var in_row: int = mini(per_row, bottle_count - row * per_row)
		var gap: float = (w - box.x * in_row) / float(in_row + 1)
		var p: Panel = Panel.new()
		p.size = box
		p.position = Vector2(gap + (i % per_row) * (box.x + gap), BOTTLES_TOP + row * (box.y + FALLBACK_GAP))
		p.set_meta("home", p.position)
		_label_in(p, "MG_GLASS_BOTTLE")
		p.gui_input.connect(_on_bottle_input.bind(p))
		add_child(p)


func _on_bottle_input(event: InputEvent, bottle: Control) -> void:
	if event is InputEventMouseButton or event is InputEventScreenTouch:
		if event.is_pressed() and _dragging == null:
			_dragging = bottle
			bottle.move_to_front()
		elif not event.is_pressed() and _dragging == bottle:
			_dragging = null
			drop_bottle(bottle)
	elif _dragging == bottle:
		bottle.position += drag_delta(event)


## Drops a bottle where it is: into the bank it goes in, anywhere else it bounces home.
func drop_bottle(bottle: Control) -> void:
	var centre: Vector2 = bottle.position + bottle.size * 0.5
	if _bank != null and Rect2(_bank.position, _bank.size).has_point(centre):
		bottle.queue_free()
		_remaining -= 1
		if _remaining == 0:
			finish_step()
		return
	if not bottle.is_inside_tree():
		bottle.position = bottle.get_meta("home")
		return
	var tw: Tween = bottle.create_tween()
	tw.tween_property(bottle, "position", bottle.get_meta("home"), BOUNCE_S)
