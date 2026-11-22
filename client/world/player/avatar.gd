class_name Avatar
extends Node2D
## One player as seen in the world: the seat's character (its lobby skin, the sprout by default)
## playing its walk cycle while it moves, standing on a seat-coloured shadow, with the seat icon
## and the name above it (so colour is never the only cue). Used for the local player and the
## remote ones. The node's origin is the player's position; the feet stand just below it.

const Characters := preload("res://ui/widgets/characters.gd")
const TINT_SHADER: Shader = preload("res://ui/widgets/sprout_tint.gdshader")
const ICON_PATH: String = "res://assets/player/icons/icon_%02d.png"
## Collision radius (the body is a circle for walls and range checks, whatever its art).
const RADIUS_TILES: float = 0.3
## How tall a character is drawn, in tiles (the art is scaled uniformly to this height).
const HEIGHT_TILES: float = 1.4
const ICON_PX: float = 10.0
const ICON_GAP_PX: float = 2.0
const NAME_GAP_PX: float = 1.0
const OUT_ALPHA: float = 0.45
const OUT_DARKEN: float = 0.5
## The poison meter runs 0..100 on the wire.
const POISON_MAX: float = 100.0
const POISON_COLOR: Color = Color(0.55, 0.85, 0.2)
const POISON_BLEND_MAX: float = 0.6
const SHADOW_ALPHA: float = 0.55
const SHADOW_SQUASH: float = 0.4
const SHADOW_OUTLINE: Color = Color(0.0, 0.0, 0.0, 0.35)
## Moved less than this per frame counts as standing still (px).
const MOVE_EPS_PX: float = 0.05
## Keeps walking this long after the last step, so snapshot gaps do not stutter the cycle (s).
const STILL_AFTER_S: float = 0.15

var seat: int = -1
var _color: Color = Color.WHITE
var _out: bool = false
var _poison: int = 0
var _name: String = ""
var _icon_index: int = 0
var _skin: String = Characters.DEFAULT_ID
var _faces_right: bool = true
var _radius_px: float = 0.0
var _frames: Array[Texture2D] = []
var _frame_ms: Array = []
var _frame: int = 0
var _elapsed_ms: float = 0.0
var _last_pos: Vector2 = Vector2.INF
var _still_s: float = STILL_AFTER_S

@onready var _body: Sprite2D = $Body
@onready var _icon: Sprite2D = $Icon
@onready var _label: Label = $NameLabel


func _ready() -> void:
	_radius_px = Net.tiles_to_px(RADIUS_TILES)
	_apply()


## A seat's name can arrive after its avatar exists (bots are named at match start).
func set_display_name(display_name: String) -> void:
	if display_name == _name:
		return
	_name = display_name
	if is_node_ready():
		_label.text = _name
		_place_tag()


## skin: the seat's lobby skin id ("" or an unknown id draws the default character).
func setup(seat_id: int, display_name: String, color: Color, icon_index: int, skin: String = "") -> void:
	seat = seat_id
	_name = display_name
	_color = color
	_icon_index = icon_index
	_skin = skin if Characters.has(skin) else Characters.DEFAULT_ID
	if is_node_ready():
		_apply()


func set_out(out: bool) -> void:
	if out == _out:
		return
	_out = out
	modulate.a = OUT_ALPHA if out else 1.0
	_tint_body()
	queue_redraw()


## Own avatar only: tints the body towards green as the poison level rises.
func set_poison_tint(level: int) -> void:
	if level == _poison:
		return
	_poison = level
	_tint_body()


## Copies the current pose into a dash afterimage (see dash_ghost.gd).
func dress_ghost(ghost: Node2D) -> void:
	if not is_node_ready():
		return  # not in the tree yet: the ghost keeps its colour disc
	ghost.set("texture", _body.texture)
	ghost.set("flip", _body.flip_h)
	ghost.set("tex_scale", _body.scale)
	ghost.set("tex_offset", _body.position)


func _apply() -> void:
	var c: Dictionary = Characters.info(_skin)
	_frames = Characters.frames(_skin)
	_frame_ms = c["frame_ms"]
	_faces_right = bool(c.get("faces_right", true))
	_frame = 0
	_elapsed_ms = 0.0
	_body.texture = _frames[0] if not _frames.is_empty() else null
	_body.centered = true
	if _body.texture != null:
		var s: float = Net.tiles_to_px(HEIGHT_TILES) / float(_body.texture.get_height())
		_body.scale = Vector2(s, s)
		# Feet at the bottom of the collision circle.
		_body.position = Vector2(0.0, _radius_px - _body.texture.get_height() * s / 2.0)
	if bool(c["tint"]):
		var mat: ShaderMaterial = ShaderMaterial.new()
		mat.shader = TINT_SHADER
		mat.set_shader_parameter("tint", _color)
		_body.material = mat
	else:
		_body.material = null
	_tint_body()

	_label.text = _name
	var tex: Texture2D = Assets.texture(ICON_PATH % _icon_index, _color)
	_icon.texture = tex
	var biggest: float = maxf(tex.get_width(), tex.get_height())
	_icon.scale = Vector2.ONE * (ICON_PX / biggest)
	_place_tag()
	queue_redraw()


## Name above the head, the seat icon just left of the name's text.
func _place_tag() -> void:
	var head_y: float = _radius_px - Net.tiles_to_px(HEIGHT_TILES)
	_label.position.y = head_y - _label.custom_minimum_size.y - NAME_GAP_PX
	var font: Font = _label.get_theme_font("font")
	var size: int = _label.get_theme_font_size("font_size")
	var text_w: float = font.get_string_size(_name, HORIZONTAL_ALIGNMENT_LEFT, -1, size).x if font != null else 0.0
	_icon.position = Vector2(-text_w / 2.0 - ICON_GAP_PX - ICON_PX / 2.0, _label.position.y + _label.custom_minimum_size.y / 2.0)


func _tint_body() -> void:
	var c: Color = Color.WHITE
	if _poison > 0:
		var t: float = clampf(float(_poison) / POISON_MAX, 0.0, 1.0) * POISON_BLEND_MAX
		c = c.lerp(POISON_COLOR, t)
	if _out:
		c = c.darkened(OUT_DARKEN)
	_body.self_modulate = c


func _process(delta: float) -> void:
	var pos: Vector2 = global_position
	if _last_pos == Vector2.INF:
		_last_pos = pos
	var step: Vector2 = pos - _last_pos
	_last_pos = pos
	if step.length() > MOVE_EPS_PX:
		_still_s = 0.0
		if absf(step.x) > MOVE_EPS_PX:
			# Mirror the art when it looks the other way from where the player walks.
			_body.flip_h = (step.x < 0.0) == _faces_right
	else:
		_still_s += delta
	if _frames.size() < 2:
		return
	if _still_s >= STILL_AFTER_S:
		if _frame != 0:
			_frame = 0
			_elapsed_ms = 0.0
			_body.texture = _frames[0]
		return
	_elapsed_ms += delta * 1000.0
	while _elapsed_ms >= float(_frame_ms[_frame]):
		_elapsed_ms -= float(_frame_ms[_frame])
		_frame = (_frame + 1) % _frames.size()
	_body.texture = _frames[_frame]


func _draw() -> void:
	# The seat-coloured shadow the character stands on.
	var body: Color = _color
	if _out:
		body = body.darkened(OUT_DARKEN)
	body.a = SHADOW_ALPHA
	draw_set_transform(Vector2(0.0, _radius_px * 0.6), 0.0, Vector2(1.0, SHADOW_SQUASH))
	draw_circle(Vector2.ZERO, _radius_px * 1.3, body)
	draw_arc(Vector2.ZERO, _radius_px * 1.3, 0.0, TAU, 24, SHADOW_OUTLINE, 1.0)
	draw_set_transform(Vector2.ZERO)
