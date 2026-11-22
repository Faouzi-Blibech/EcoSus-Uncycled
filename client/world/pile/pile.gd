class_name Pile
extends Node2D
## A waste pile or a body pile on the floor. The server sends only what the player can see.
## A body is drawn as a fallen player in the victim's seat colour (BodyArt); when it is the one the
## player can report, a pulsing ring and a "!" bubble mark it.

const TEXTURE_PATH: String = "res://assets/piles/pile_%s.png"
const STATE_COLORS: Dictionary = {
	"fresh": Color(1.0, 1.0, 1.0),
	"reported": Color(1.0, 0.7, 0.35),
}
## A body whose victim is unknown (older server).
const UNKNOWN_BODY_COLOR: Color = Color(0.6, 0.6, 0.6)
const RING_RADIUS_PX: float = 17.0
const ALERT_OFFSET_PX: Vector2 = Vector2(0.0, -20.0)

var pile_id: int = -1
## How many times the texture was loaded (a repeated setup with the same data must not load again).
var texture_loads: int = 0
## True while this body is the one the REPORT button would report.
var reportable: bool = false
var _kind: String = ""
var _state: String = ""
var _color: Color = UNKNOWN_BODY_COLOR
var _t: float = 0.0


## Safe before or after add_child. Does nothing when kind, state and colour are unchanged.
func setup(id: int, kind: String, state: String, color: Color = UNKNOWN_BODY_COLOR) -> void:
	pile_id = id
	if kind == _kind and state == _state and color == _color:
		return
	_kind = kind
	_state = state
	_color = color
	var sprite: Sprite2D = get_node("Sprite") as Sprite2D
	sprite.visible = not is_body()
	if not is_body():
		sprite.texture = Assets.texture(TEXTURE_PATH % _kind)
		texture_loads += 1
	sprite.modulate = STATE_COLORS.get(_state, Color.WHITE)
	queue_redraw()


func is_body() -> bool:
	return _kind == "body"


func set_reportable(on: bool) -> void:
	if on == reportable:
		return
	reportable = on
	set_process(on)
	queue_redraw()


func _ready() -> void:
	set_process(reportable)


func _process(delta: float) -> void:
	_t += delta
	queue_redraw()


func _draw() -> void:
	if not is_body():
		return
	if reportable:
		var pulse: float = 0.5 + 0.5 * sin(_t * 6.0)
		draw_arc(Vector2.ZERO, RING_RADIUS_PX + pulse * 3.0, 0.0, TAU, 32, Color(BodyArt.ALERT_RED, 0.4 + 0.5 * pulse), 2.0)
	BodyArt.draw_body(self, Vector2.ZERO, _color, 1.0)
	if reportable:
		BodyArt.draw_alert(self, ALERT_OFFSET_PX, 1.0, _t)
