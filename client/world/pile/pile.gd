class_name Pile
extends Node2D
## A waste pile or a body pile on the floor. The server sends only what the player can see.

const TEXTURE_PATH: String = "res://assets/piles/pile_%s.png"
const STATE_COLORS: Dictionary = {
	"fresh": Color(1.0, 1.0, 1.0),
	"reported": Color(1.0, 0.7, 0.35),
}

var pile_id: int = -1
## How many times the texture was loaded (a repeated setup with the same data must not load again).
var texture_loads: int = 0
var _kind: String = ""
var _state: String = ""


## Safe before or after add_child. Does nothing when kind and state are unchanged.
func setup(id: int, kind: String, state: String) -> void:
	pile_id = id
	if kind == _kind and state == _state:
		return
	_kind = kind
	_state = state
	var sprite: Sprite2D = get_node("Sprite") as Sprite2D
	sprite.texture = Assets.texture(TEXTURE_PATH % _kind)
	texture_loads += 1
	sprite.modulate = STATE_COLORS.get(_state, Color.WHITE)
