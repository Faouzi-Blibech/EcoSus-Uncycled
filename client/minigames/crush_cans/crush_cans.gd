extends Minigame
## Tap every can to crush it flat. The step is done when all cans are flat.

const CAN_SIZE: Vector2 = Vector2(88, 96)
const ROW_Y: float = 130.0
const FLAT_SCALE_Y: float = 0.3
const GAP: float = 12.0
const VIEW_SIZE: Vector2 = Vector2(640.0, 360.0)

@export var can_count: int = 5

var _remaining: int = 0
var _cans: Array[Button] = []


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	can_count = int(Net.as_dict(Session.tuning.get("minigames")).get("crush_cans", can_count))
	_remaining = can_count
	var title: Label = Label.new()
	title.text = tr("MG_CANS_HINT") % can_count
	title.position = Vector2(20, 20)
	add_child(title)
	_build_cans()


## Lays the cans out in centred rows that wrap at the screen width; the boxes shrink when the
## rows would run past the bottom of the screen (a large tuned count).
func _build_cans() -> void:
	var w: float = size.x if size.x > 0.0 else VIEW_SIZE.x
	var per_row: int = clampi(floori((w - GAP) / (CAN_SIZE.x + GAP)), 1, can_count)
	var rows: int = ceili(float(can_count) / per_row)
	var k: float = minf(1.0, (VIEW_SIZE.y - GAP - ROW_Y) / (rows * (CAN_SIZE.y + GAP)))
	var box: Vector2 = CAN_SIZE * k
	for i in can_count:
		var row: int = i / per_row
		var in_row: int = mini(per_row, can_count - row * per_row)
		var gap: float = (w - box.x * in_row) / float(in_row + 1)
		var b: Button = Button.new()
		b.text = tr("MG_CANS_CAN")
		b.size = box
		b.custom_minimum_size = box
		b.position = Vector2(gap + (i % per_row) * (box.x + gap), ROW_Y + row * (box.y + GAP))
		b.pivot_offset = Vector2(box.x * 0.5, box.y)
		dress(b, "can")
		b.pressed.connect(crush.bind(i))
		add_child(b)
		_cans.append(b)


## Flattens can `index`; flattening a flat can does nothing.
func crush(index: int) -> void:
	if index < 0 or index >= _cans.size():
		return
	var b: Button = _cans[index]
	if b.disabled:
		return
	b.disabled = true
	# With art the can is squashed (and shows can_flat.png when there is one); without, it says so.
	if b.has_meta("art"):
		dress(b, "can_flat")
	else:
		b.text = tr("MG_CANS_FLAT")
	b.scale.y = FLAT_SCALE_Y
	_remaining -= 1
	if _remaining == 0:
		finish_step()
