extends Minigame
## Hold the gauge and release in the green band to rinse the bottles; a miss resets the gauge.

const GAUGE_SCENE: PackedScene = preload("res://minigames/common/hold_gauge.tscn")
const GAUGE_TOP: float = 90.0
const FALLBACK_WIDTH: float = 640.0
## Fallbacks when Session.tuning lacks the keys; equal to content/tuning.json.
const FILL_PER_S: float = 0.5
const BAND_LO: float = 0.6
const BAND_HI: float = 0.85

var _hint: Label = null
var _gauge: HoldGauge = null


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


func _on_gauge_released(in_band: bool) -> void:
	if in_band:
		finish_step()
	else:
		_hint.text = tr("MG_RINSE_RETRY")
