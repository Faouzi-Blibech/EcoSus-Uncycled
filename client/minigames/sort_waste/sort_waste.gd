extends Minigame
## Drag each item into its bin; a wrong drop bounces back. The step is done when all are sorted.

const ITEMS_PATH: String = "res://minigames/sort_waste/items.json"
const ITEM_SIZE: Vector2 = Vector2(104, 52)
const BIN_SIZE: Vector2 = Vector2(144, 64)
const ITEMS_Y: float = 90.0
const BINS_Y: float = 250.0
const BOUNCE_S: float = 0.25
const FALLBACK_GAP: float = 12.0
const VIEW_WIDTH: float = 640.0

@export var item_count: int = 5

var _bins: Array = []
var _items: Array = []
var _bin_nodes: Dictionary = {}
var _remaining: int = 0
var _dragging: Control = null


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	item_count = int(Net.as_dict(Session.tuning.get("minigames")).get("sort_items", item_count))
	var data: Dictionary = _load_items()
	_bins = data.get("bins", [])
	var pool: Array = (data.get("items", []) as Array).duplicate()
	pool.shuffle()
	_items = pool.slice(0, item_count)
	_remaining = _items.size()
	var title: Label = Label.new()
	title.text = tr("MG_SORT_HINT")
	title.position = Vector2(20, 20)
	add_child(title)
	_build_bins()
	_build_items()


func _load_items() -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(ITEMS_PATH))
	if typeof(parsed) != TYPE_DICTIONARY:
		return {}
	return parsed


func _gap(box: Vector2, count: int) -> float:
	if size.x <= 0.0 or count == 0:
		return FALLBACK_GAP
	return (size.x - box.x * count) / float(count + 1)


func _label_in(p: Control, key: String) -> void:
	var l: Label = Label.new()
	l.text = tr(key)
	l.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	p.add_child(l)


func _build_bins() -> void:
	var gap: float = _gap(BIN_SIZE, _bins.size())
	for i in _bins.size():
		var b: Dictionary = _bins[i]
		var p: Panel = Panel.new()
		p.size = BIN_SIZE
		p.position = Vector2(gap + i * (BIN_SIZE.x + gap), BINS_Y)
		p.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_label_in(p, str(b.get("name_key", "")))
		dress(p, "bin_" + str(b.get("id", "")))
		add_child(p)
		_bin_nodes[str(b.get("id", ""))] = p


## Lays the items out in centred rows that wrap at the screen width; the boxes shrink when the rows
## would run into the bins (a large tuned count).
func _build_items() -> void:
	var w: float = size.x if size.x > 0.0 else VIEW_WIDTH
	var per_row: int = clampi(floori((w - FALLBACK_GAP) / (ITEM_SIZE.x + FALLBACK_GAP)), 1, maxi(_items.size(), 1))
	var rows: int = ceili(float(_items.size()) / per_row)
	var k: float = minf(1.0, (BINS_Y - FALLBACK_GAP - ITEMS_Y) / (maxf(rows, 1.0) * (ITEM_SIZE.y + FALLBACK_GAP)))
	var box: Vector2 = ITEM_SIZE * k
	for i in _items.size():
		var it: Dictionary = _items[i]
		var row: int = i / per_row
		var in_row: int = mini(per_row, _items.size() - row * per_row)
		var gap: float = (w - box.x * in_row) / float(in_row + 1)
		var p: Panel = Panel.new()
		p.size = box
		p.position = Vector2(gap + (i % per_row) * (box.x + gap), ITEMS_Y + row * (box.y + FALLBACK_GAP))
		p.set_meta("bin", str(it.get("bin", "")))
		p.set_meta("home", p.position)
		_label_in(p, str(it.get("name_key", "")))
		dress(p, "item_" + str(it.get("id", "")))
		p.gui_input.connect(_on_item_input.bind(p))
		add_child(p)


func _on_item_input(event: InputEvent, item: Control) -> void:
	if event is InputEventMouseButton or event is InputEventScreenTouch:
		if event.is_pressed() and _dragging == null:
			_dragging = item
			item.move_to_front()
		elif not event.is_pressed() and _dragging == item:
			_dragging = null
			drop_item(item)
	elif _dragging == item:
		item.position += drag_delta(event)


## Drops an item where it is: into its own bin it is sorted, anywhere else it bounces home.
func drop_item(item: Control) -> void:
	var centre: Vector2 = item.position + item.size * 0.5
	var target: Control = _bin_nodes.get(str(item.get_meta("bin"))) as Control
	if target != null and Rect2(target.position, target.size).has_point(centre):
		item.queue_free()
		_remaining -= 1
		if _remaining == 0:
			finish_step()
		return
	if not item.is_inside_tree():
		item.position = item.get_meta("home")
		return
	var tw: Tween = item.create_tween()
	tw.tween_property(item, "position", item.get_meta("home"), BOUNCE_S)
