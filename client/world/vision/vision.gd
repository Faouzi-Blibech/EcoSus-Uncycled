class_name Vision
extends Node2D
## Fog of war: the world sits at a dim, readable ambient level (the Dark CanvasModulate in
## vision.tscn) and a light with shadows follows the local player. The wall
## tiles carry light occluders, so the lit area stops at walls. The radius is the vision the
## server grants; the client only draws it.

const TWEEN_S: float = 0.3
## The smog haze: a light grey wash over the world.
const HAZE_COLOR: Color = Color(0.78, 0.78, 0.76, 0.3)
const HAZE_FADE_S: float = 1.0

var _tween: Tween = null
var _target_px: float = -1.0
var _haze: ColorRect = null
var _haze_tween: Tween = null
var _haze_on: bool = false

# Looked up on use, not @onready, so a Vision that is not in the tree yet works (tests, Round before _ready ends).
var _light: PointLight2D:
	get:
		return get_node("Light") as PointLight2D
var _dark: CanvasModulate:
	get:
		return get_node("Dark") as CanvasModulate


func _ready() -> void:
	var layer: CanvasLayer = CanvasLayer.new()
	layer.layer = 1
	add_child(layer)
	_haze = ColorRect.new()
	_haze.color = HAZE_COLOR
	_haze.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_haze.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_haze.modulate.a = 0.0
	_haze.visible = false
	layer.add_child(_haze)


## Fades the smog haze in or out; the round calls it every frame with the sabotage kind.
func set_haze(on: bool) -> void:
	if _haze == null or on == _haze_on:
		return
	_haze_on = on
	if _haze_tween != null:
		_haze_tween.kill()
	if on:
		_haze.visible = true
	_haze_tween = create_tween()
	_haze_tween.tween_property(_haze, "modulate:a", 1.0 if on else 0.0, HAZE_FADE_S)
	if not on:
		_haze_tween.tween_callback(_haze.hide)


## Sets the lit radius in pixels: the light is at full strength out to the radius and fades just past
## it (the gradient's full part ends at the radius). The first call applies at once, later ones ease
## over TWEEN_S.
func set_radius_px(r: float) -> void:
	if is_equal_approx(r, _target_px):
		return
	var first: bool = _target_px < 0.0
	_target_px = r
	var full_fraction: float = ((_light.texture as GradientTexture2D).gradient as Gradient).offsets[1]
	var tex_scale: float = r * 2.0 / (float(_light.texture.get_width()) * full_fraction)
	if _tween != null:
		_tween.kill()
	if first:
		_light.texture_scale = tex_scale
		return
	_tween = create_tween()
	_tween.tween_property(_light, "texture_scale", tex_scale, TWEEN_S)


## The ambient level of the world outside the player's light (DarkRooms takes exactly this away).
func ambient() -> Color:
	return _dark.color


## The light radius in tiles. `server_vision` is the snapshot's me.vision, which already counts the dark
## room the server thinks the player stands in (`server_dark`, from the last server position). While the
## local player is in the other state (just walked in or out, the server is ~100 ms behind) the radius
## follows the local position at once with the server's rule: citizen vision eased by smog, and inside
## a dark room the lower of that and vision.citizen * vision.dark_mult. `vision` is tuning.vision.
## Polluters are never affected by dark rooms.
static func light_tiles(server_vision: float, server_dark: bool, local_dark: bool, smog_level: float, vision: Dictionary, is_citizen: bool) -> float:
	if not is_citizen or server_dark == local_dark:
		return server_vision
	var citizen: float = float(vision.get("citizen", 4.0))
	var eased: float = citizen + (float(vision.get("smog_min", 1.0)) - citizen) * smog_level
	if not local_dark:
		return eased
	return minf(eased, citizen * float(vision.get("dark_mult", 0.5)))
