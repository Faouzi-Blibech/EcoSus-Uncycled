class_name Avatar
extends Node2D
## One player as seen in the world: a body disc tinted by the seat colour, the seat icon
## (so colour is never the only cue) and the name. Used for the local player and the remote ones.

const ICON_PATH: String = "res://assets/player/icons/icon_%02d.png"
const RADIUS_TILES: float = 0.3
const ICON_PX: float = 12.0
const OUT_ALPHA: float = 0.45
const OUT_DARKEN: float = 0.5
## The poison meter runs 0..100 on the wire.
const POISON_MAX: float = 100.0
const POISON_COLOR: Color = Color(0.55, 0.85, 0.2)
const POISON_BLEND_MAX: float = 0.6
const OUTLINE_COLOR: Color = Color(0.0, 0.0, 0.0, 0.7)
const OUTLINE_WIDTH: float = 1.5

var seat: int = -1
var _color: Color = Color.WHITE
var _out: bool = false
var _poison: int = 0
var _name: String = ""
var _icon_index: int = 0
var _radius_px: float = 0.0

@onready var _icon: Sprite2D = $Icon
@onready var _label: Label = $NameLabel


func _ready() -> void:
	_radius_px = Net.tiles_to_px(RADIUS_TILES)
	_label.position.y = -_radius_px - _label.custom_minimum_size.y - 2.0
	_apply()


## A seat's name can arrive after its avatar exists (bots are named at match start).
func set_display_name(display_name: String) -> void:
	if display_name == _name:
		return
	_name = display_name
	if is_node_ready():
		_label.text = _name


func setup(seat_id: int, display_name: String, color: Color, icon_index: int) -> void:
	seat = seat_id
	_name = display_name
	_color = color
	_icon_index = icon_index
	if is_node_ready():
		_apply()


func set_out(out: bool) -> void:
	if out == _out:
		return
	_out = out
	modulate.a = OUT_ALPHA if out else 1.0
	queue_redraw()


## Own avatar only: tints the body towards green as the poison level rises.
func set_poison_tint(level: int) -> void:
	if level == _poison:
		return
	_poison = level
	queue_redraw()


func _apply() -> void:
	_label.text = _name
	var tex: Texture2D = Assets.texture(ICON_PATH % _icon_index)
	_icon.texture = tex
	var biggest: float = maxf(tex.get_width(), tex.get_height())
	_icon.scale = Vector2.ONE * (ICON_PX / biggest)
	queue_redraw()


func _draw() -> void:
	var body: Color = _color
	if _poison > 0:
		var t: float = clampf(float(_poison) / POISON_MAX, 0.0, 1.0) * POISON_BLEND_MAX
		body = body.lerp(POISON_COLOR, t)
	if _out:
		body = body.darkened(OUT_DARKEN)
	draw_circle(Vector2.ZERO, _radius_px, body)
	draw_arc(Vector2.ZERO, _radius_px, 0.0, TAU, 20, OUTLINE_COLOR, OUTLINE_WIDTH)
