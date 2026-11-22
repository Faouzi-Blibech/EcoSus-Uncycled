class_name SpotView
extends Sprite2D
## A cleaning spot on the map: its mini-game's art, dirty or clean. Missing art falls back to the
## Assets placeholder; a dirty spot is tinted so the two states differ even on placeholders.

const ART_PATH: String = "res://assets/spots/%s_%s.png"
const DIRTY_TINT: Color = Color(0.75, 0.6, 0.45)
## Painted spot art is scaled uniformly so its longer side is this many tiles.
const SIZE_TILES: float = 1.5

var spot_id: String = ""
var art_type: String = ""
var state: String = ""


func setup(id: String, type: String, pos: Vector2) -> void:
	spot_id = id
	art_type = type
	position = pos


## "clean" or anything else (dirty: every spot starts dirty). Reloads only on a change.
func show_state(new_state: String) -> void:
	var s: String = "clean" if new_state == "clean" else "dirty"
	if s == state:
		return
	state = s
	texture = Assets.texture(ART_PATH % [art_type, s])
	var longest: float = maxf(float(texture.get_width()), float(texture.get_height()))
	var k: float = minf(1.0, Net.tiles_to_px(SIZE_TILES) / longest)
	scale = Vector2(k, k)
	modulate = Color.WHITE if s == "clean" else DIRTY_TINT
