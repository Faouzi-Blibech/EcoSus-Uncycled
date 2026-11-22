class_name HoldGauge
extends Control
## Fills while the pointer is held on it; releasing inside the green band succeeds, otherwise it resets.
## Drawn with the kit's bar standing upright (assets/minigames/gauge_track.png): water rises inside,
## the release band is leaf green with ink edges, and the water turns green on a success.

const TRACK_PATH: String = "res://assets/minigames/gauge_track.png"
## The track art is drawn at 2 texture pixels per logical pixel; its round ends stay this tall.
const TRACK_DENSITY: float = 2.0
const TRACK_CAP_PX: float = 24.0
## The inside of the track (where water and band go), as a fraction of the gauge's width.
const INSET_FRAC: float = 0.22
const WATER: Color = Color(0.62, 0.8, 0.86)
const WATER_DONE: Color = Color(0.54, 0.67, 0.42)
const BAND: Color = Color(0.54, 0.67, 0.42, 0.55)
const INK: Color = Color(0.24, 0.17, 0.15)
const BAND_EDGE_PX: float = 2.0

static var _track: StyleBoxTexture = null

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
	var track: StyleBoxTexture = _track_box()
	if track != null:
		track.draw(get_canvas_item(), Rect2(Vector2.ZERO, size))
	# Water and band live inside the track's rim.
	var inset: float = size.x * INSET_FRAC
	var inner: Rect2 = Rect2(Vector2(inset, inset), size - Vector2(inset, inset) * 2.0)
	var h: float = inner.size.y
	var band: Rect2 = Rect2(inner.position.x, inner.position.y + h * (1.0 - band_hi), inner.size.x, h * (band_hi - band_lo))
	draw_rect(band, BAND)
	var water_h: float = h * value
	if water_h > 0.0:
		var water: StyleBoxFlat = StyleBoxFlat.new()
		water.bg_color = WATER_DONE if locked else WATER
		water.set_corner_radius_all(int(inner.size.x * 0.5))
		water.draw(get_canvas_item(), Rect2(inner.position.x, inner.end.y - water_h, inner.size.x, water_h))
	for y in [band.position.y, band.end.y]:
		draw_line(Vector2(inner.position.x - 3.0, y), Vector2(inner.end.x + 3.0, y), INK, BAND_EDGE_PX)


## The upright kit bar as a nine-slice box (its round ends keep their size; only the middle stretches).
static func _track_box() -> StyleBoxTexture:
	if _track != null or not ResourceLoader.exists(TRACK_PATH):
		return _track
	var img: Image = (load(TRACK_PATH) as Texture2D).get_image()
	var tex: ImageTexture = ImageTexture.create_from_image(img)
	tex.set_size_override(Vector2i((Vector2(img.get_size()) / TRACK_DENSITY).round()))
	_track = StyleBoxTexture.new()
	_track.texture = tex
	_track.texture_margin_top = TRACK_CAP_PX
	_track.texture_margin_bottom = TRACK_CAP_PX
	return _track
