extends Minigame
## Hold the gauge and release in the green band to rinse the bottles; a miss resets the gauge.

const GAUGE_SCENE: PackedScene = preload("res://minigames/common/hold_gauge.tscn")
const GAUGE_TOP: float = 90.0
const FALLBACK_WIDTH: float = 640.0
## Fallbacks when Session.tuning lacks the keys; equal to content/tuning.json.
const FILL_PER_S: float = 0.5
const BAND_LO: float = 0.6
const BAND_HI: float = 0.85
const SINK_ART: String = "res://assets/spots/rinse_bottles_%s.png"
const SINK_SIZE: Vector2 = Vector2(150.0, 150.0)
const SINK_GAP: float = 16.0

var _hint: Label = null
var _gauge: HoldGauge = null
var _sink: TextureRect = null


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_hint = Label.new()
	_hint.position = Vector2(20, 20)
	_hint.text = tr("MG_RINSE_HINT")
	add_child(_hint)
	_gauge = GAUGE_SCENE.instantiate() as HoldGauge
	_gauge.position = Vector2(((size.x if size.x > 0.0 else FALLBACK_WIDTH) - _gauge.size.x) * 0.5, GAUGE_TOP)
	add_child(_gauge)
	# The gauge has just read the shared gauge_* keys in its own _ready; this game has its own.
	var cfg: Dictionary = Net.as_dict(Session.tuning.get("minigames"))
	_gauge.fill_per_s = float(cfg.get("rinse_fill_per_s", FILL_PER_S))
	_gauge.band_lo = float(cfg.get("rinse_band_lo", BAND_LO))
	_gauge.band_hi = float(cfg.get("rinse_band_hi", BAND_HI))
	_gauge.released.connect(_on_gauge_released)
	# The sink being rinsed, beside the gauge (the spot's own art; it turns clean on a success).
	_sink = TextureRect.new()
	_sink.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_sink.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_sink.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sink.size = SINK_SIZE
	_sink.position = _gauge.position + Vector2(-SINK_SIZE.x - SINK_GAP, _gauge.size.y - SINK_SIZE.y)
	_set_sink("dirty")
	add_child(_sink)


func _set_sink(state: String) -> void:
	var path: String = SINK_ART % state
	if ResourceLoader.exists(path):
		_sink.texture = load(path) as Texture2D


func _on_gauge_released(in_band: bool) -> void:
	if in_band:
		_set_sink("clean")
		finish_step()
	else:
		_hint.text = tr("MG_RINSE_RETRY")
