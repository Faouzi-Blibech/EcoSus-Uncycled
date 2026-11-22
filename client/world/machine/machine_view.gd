class_name MachineView
extends Sprite2D
## The Recycling Machine on the map: idle, working for a moment after a delivery, jammed while
## the server says so (Session.machine_jammed). Its base stands on the machine marker; the art is
## scaled uniformly to WIDTH_TILES.

const ART_PATH: String = "res://assets/props/machine_%s.png"
const WIDTH_TILES: float = 3.0
const WORKING_S: float = 1.5
## Where the machine's base stands relative to its marker (tiles, + is down): the marker is the
## use spot in front of the machine pad, the machine stands on the pad just above it.
const BASE_BELOW_TILES: float = -0.5

var state: String = ""
var _working_s: float = 0.0


func _ready() -> void:
	centered = true
	Net.event.connect(_on_event)
	_show("idle")


func _process(delta: float) -> void:
	_working_s = maxf(_working_s - delta, 0.0)
	var want: String = "jammed" if Session.machine_jammed else ("working" if _working_s > 0.0 else "idle")
	_show(want)


func _show(new_state: String) -> void:
	if new_state == state:
		return
	state = new_state
	texture = Assets.texture(ART_PATH % state)
	var k: float = Net.tiles_to_px(WIDTH_TILES) / float(texture.get_width())
	scale = Vector2(k, k)
	# y-sort by the base: the sprite's origin is the base, the picture rises above it.
	offset = Vector2(0.0, -texture.get_height() * 0.5)


func _on_event(data: Dictionary) -> void:
	if str(data.get("t", "")) == "delivered":
		_working_s = WORKING_S


## Puts the base on the marker (px).
func place(marker_px: Vector2) -> void:
	position = marker_px + Vector2(0.0, Net.tiles_to_px(BASE_BELOW_TILES))
