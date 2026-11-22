class_name Avatar
extends Node2D
## One player as seen in the world: the seat's character (its lobby skin, the sprout by default)
## playing its walk cycle while it moves, standing on a black shadow, with the name above
## it (the name and the character, not colour alone, tell players apart). Used for the local player
## and the remote ones. The node's origin is the player's position; the feet stand just below it.

const Characters := preload("res://ui/widgets/characters.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const TINT_SHADER: Shader = preload("res://ui/widgets/sprout_tint.gdshader")
## Collision radius (the body is a circle for walls and range checks, whatever its art).
const RADIUS_TILES: float = 0.3
## How tall a character is drawn, in tiles (the art is scaled uniformly to this height).
const HEIGHT_TILES: float = 1.4
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

## Dash look (visual only: the server already moved the seat).
## Start: the body squashes, stretches along the dash and collapses into a ball with a flash and a
## dust puff. Travel: the ball zips along the line with a short fading trail and speed streaks.
## End: the body pops out of the ball, overshoots and settles, with a ring, sparks and a little dust.
const DASH_OUT_S: float = 0.06
const DASH_TRAVEL_S: float = 0.13
const DASH_IN_S: float = 0.10
const DASH_TOTAL_S: float = DASH_OUT_S + DASH_TRAVEL_S + DASH_IN_S
## Start shapes (body x, y factors): the squash first, then the stretch (x along the dash).
const DASH_SQUASH: Vector2 = Vector2(1.35, 0.7)
const DASH_STRETCH: Vector2 = Vector2(1.6, 0.5)
## Share of the start spent squashing, then stretching; the rest collapses into the ball.
const DASH_SQUASH_END: float = 0.35
const DASH_STRETCH_END: float = 0.65
## The body's scale factor when it has become the ball.
const DASH_MIN_SCALE: float = 0.05
## Body brightness at the flash (1 = normal).
const DASH_FLASH: float = 1.8
## The ball: radius, white core share, and its comet shape while it flies (along, across).
const DASH_BALL_PX: float = 7.0
const DASH_BALL_CORE: float = 0.45
const DASH_BALL_COMET: Vector2 = Vector2(1.7, 0.8)
## White flash disc as the body becomes the ball: radius in ball radii at its start and end.
const DASH_FLASH_R: Vector2 = Vector2(0.8, 2.4)
## Short trail: how long a passed place of the ball stays, its alpha when new, its size share.
const DASH_TRAIL_S: float = 0.07
const DASH_TRAIL_ALPHA: float = 0.75
const DASH_TRAIL_SIZE: float = 0.8
## Speed streaks behind the ball: longest length, gap across the line (in ball radii), width, alpha.
const DASH_STREAK_PX: float = 28.0
const DASH_STREAK_GAP: float = 0.6
const DASH_STREAK_WIDTH: float = 1.5
const DASH_STREAK_ALPHA: float = 0.6
const DASH_LIGHTEN: float = 0.35
## Pop: scale at its start, the overshoot, the share spent growing (the rest settles to 1), the
## share the ball takes to vanish into the body, the alpha at its start and the share spent fading in.
const DASH_POP_FROM: float = 0.3
const DASH_POP_SCALE: float = 1.2
const DASH_POP_GROW: float = 0.6
const DASH_POP_BALL: float = 0.3
const DASH_POP_ALPHA: float = 0.3
const DASH_POP_FADE: float = 0.4
## Ground ring of the pop: radius at its start and end, line width, alpha at the start; sparks.
const DASH_BURST_PX: Vector2 = Vector2(5.0, 20.0)
const DASH_BURST_WIDTH: float = 2.0
const DASH_BURST_ALPHA: float = 0.9
const DASH_BURST_SPARKS: int = 8
const DASH_SPARK_PX: float = 2.5
const DASH_SPARK_REACH: float = 1.2
## Dust: start puff length, bit radius at its start and end, spread, rise, alpha, bits per puff.
const DASH_PUFF_S: float = 0.22
const DASH_PUFF_PX: Vector2 = Vector2(2.5, 7.0)
const DASH_PUFF_SPREAD_PX: float = 9.0
const DASH_PUFF_RISE_PX: float = 4.0
const DASH_PUFF_ALPHA: float = 0.7
const DASH_PUFF_BITS: int = 5
const DASH_END_PUFF_BITS: int = 3
const DASH_PUFF_COLOR: Color = Color(0.86, 0.79, 0.66)
## Ground shapes (shadow, ring, dust) sit this far below the origin, in collision radii.
const GROUND_Y: float = 0.6

var seat: int = -1
var _color: Color = Color.WHITE
var _out: bool = false
var _poison: int = 0
var _name: String = ""
var _skin: String = Characters.DEFAULT_ID
var _faces_right: bool = true
var _radius_px: float = 0.0
var _frames: Array[Texture2D] = []
var _frame_ms: Array = []
var _frame: int = 0
var _elapsed_ms: float = 0.0
var _last_pos: Vector2 = Vector2.INF
var _still_s: float = STILL_AFTER_S
## The body's resting scale and place (set by _apply); the dash look always returns to them.
var _body_scale: Vector2 = Vector2.ONE
var _body_pos: Vector2 = Vector2.ZERO
## Seconds into the dash look, -1 when none plays.
var _fx_t: float = -1.0
## Where the dash started (global px) and which way it went.
var _fx_start: Vector2 = Vector2.ZERO
var _fx_dir: Vector2 = Vector2.RIGHT
## True when this node slides along the line itself (own player); false when it already stands at
## the end (remote seats): then the dot flies from the start to it.
var _fx_node_travels: bool = false
## The ball's recent places (global px) and when it was there (seconds into the look).
var _fx_trail: PackedVector2Array = PackedVector2Array()
var _fx_trail_t: PackedFloat32Array = PackedFloat32Array()

@onready var _body: Sprite2D = $Body
@onready var _label: Label = $NameLabel


func _ready() -> void:
	_radius_px = Net.tiles_to_px(RADIUS_TILES)
	_label.add_theme_font_override("font", PaperKit.font())
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
func setup(seat_id: int, display_name: String, color: Color, skin: String = "") -> void:
	seat = seat_id
	_name = display_name
	_color = color
	_skin = skin if Characters.has(skin) else Characters.DEFAULT_ID
	if is_node_ready():
		_apply()


func set_out(out: bool) -> void:
	if out == _out:
		return
	_out = out
	stop_dash()
	modulate.a = OUT_ALPHA if out else 1.0
	_tint_body()
	queue_redraw()


## Own avatar only: tints the body towards green as the poison level rises.
func set_poison_tint(level: int) -> void:
	if level == _poison:
		return
	_poison = level
	_tint_body()


## Starts the dash look. from_offset: the start point relative to this node now (zero when this
## node is about to slide there itself); dir: the dash direction; node_travels: see _fx_node_travels.
## A dash already playing keeps its start: a remote jump can span several interpolated frames.
func play_dash(from_offset: Vector2, dir: Vector2, node_travels: bool) -> void:
	if not is_node_ready() or is_dashing():
		return
	_fx_start = global_position + from_offset
	_fx_dir = dir.normalized() if dir != Vector2.ZERO else Vector2.RIGHT
	_fx_node_travels = node_travels
	_fx_trail.clear()
	_fx_trail_t.clear()
	_fx_t = 0.0
	_shape_body()
	queue_redraw()


func is_dashing() -> bool:
	return _fx_t >= 0.0


## Ends the dash look at once: the body, its flash and the name tag go back to rest.
func stop_dash() -> void:
	if _fx_t < 0.0:
		return
	_fx_t = -1.0
	_fx_trail.clear()
	_fx_trail_t.clear()
	_body.visible = true
	_body.scale = _body_scale
	_body.position = _body_pos
	_body.modulate = Color.WHITE
	_set_tag_alpha(1.0)
	queue_redraw()


## Advances the dash look by `delta` seconds (every frame from _process; tests call it directly).
func step_dash(delta: float) -> void:
	if _fx_t < 0.0:
		return
	_fx_t += delta
	if _fx_t >= DASH_TOTAL_S:
		stop_dash()
		return
	if _fx_t >= DASH_OUT_S and _fx_t < DASH_OUT_S + DASH_TRAVEL_S:
		_fx_trail.append(_ball_global())
		_fx_trail_t.append(_fx_t)
	while not _fx_trail_t.is_empty() and _fx_t - _fx_trail_t[0] >= DASH_TRAIL_S:
		_fx_trail.remove_at(0)
		_fx_trail_t.remove_at(0)
	_shape_body()
	queue_redraw()


func _notification(what: int) -> void:
	# Hidden mid-dash (seat out of sight, world hidden): end the look so it never shows half done.
	if what == NOTIFICATION_VISIBILITY_CHANGED and not visible:
		stop_dash()


## The body, its flash and the name tag at the current moment of the dash look.
func _shape_body() -> void:
	var factor: Vector2 = Vector2.ONE
	var flash: float = 1.0
	var alpha: float = 1.0
	var tag: float = 0.0
	var offset: Vector2 = Vector2.ZERO
	if _fx_t < DASH_OUT_S:
		# Squash, stretch along the dash, collapse into the ball, at the start point.
		var u: float = _fx_t / DASH_OUT_S
		var stretch: Vector2 = _along(DASH_STRETCH)
		if u < DASH_SQUASH_END:
			factor = Vector2.ONE.lerp(DASH_SQUASH, u / DASH_SQUASH_END)
		elif u < DASH_STRETCH_END:
			factor = DASH_SQUASH.lerp(stretch, (u - DASH_SQUASH_END) / (DASH_STRETCH_END - DASH_SQUASH_END))
		else:
			factor = stretch.lerp(Vector2.ONE * DASH_MIN_SCALE, (u - DASH_STRETCH_END) / (1.0 - DASH_STRETCH_END))
		flash = lerpf(1.0, DASH_FLASH, u)
		# A remote node already stands at the end: its tag would show there before the body.
		tag = 1.0 - u if _fx_node_travels else 0.0
		offset = _start_local()
	elif _fx_t < DASH_OUT_S + DASH_TRAVEL_S:
		_body.visible = false
		_set_tag_alpha(0.0)
		return
	else:
		# Pop out of the ball: grow past full size, settle back, fade in.
		var u: float = _in_progress()
		var k: float
		if u < DASH_POP_GROW:
			k = lerpf(DASH_POP_FROM, DASH_POP_SCALE, _ease_out(u / DASH_POP_GROW))
		else:
			k = lerpf(DASH_POP_SCALE, 1.0, smoothstep(0.0, 1.0, (u - DASH_POP_GROW) / (1.0 - DASH_POP_GROW)))
		factor = Vector2.ONE * k
		flash = lerpf(DASH_FLASH, 1.0, u)
		alpha = lerpf(DASH_POP_ALPHA, 1.0, minf(u / DASH_POP_FADE, 1.0))
		tag = u
	_body.visible = true
	_body.scale = _body_scale * factor
	_body.position = _body_pos + offset
	_body.modulate = Color(flash, flash, flash, alpha)
	_set_tag_alpha(tag)


## A shape given as (along, across) turned to the dash: x along a sideways dash, y along an up/down one.
func _along(v: Vector2) -> Vector2:
	return v if absf(_fx_dir.x) >= absf(_fx_dir.y) else Vector2(v.y, v.x)


func _set_tag_alpha(a: float) -> void:
	_label.modulate.a = a


## The dash start point in this node's space.
func _start_local() -> Vector2:
	return to_local(_fx_start)


## Where the ball is now (global px): at the start, then along the line, then at the body.
func _ball_global() -> Vector2:
	var here: Vector2 = global_position + _body_pos
	if _fx_t < DASH_OUT_S:
		return _fx_start + _body_pos
	if _fx_node_travels or _fx_t >= DASH_OUT_S + DASH_TRAVEL_S:
		return here
	return (_fx_start + _body_pos).lerp(here, _ease_out(_travel_progress()))


## 0..1 through the travel phase.
func _travel_progress() -> float:
	return clampf((_fx_t - DASH_OUT_S) / DASH_TRAVEL_S, 0.0, 1.0)


## 0..1 through the pop phase.
func _in_progress() -> float:
	return clampf((_fx_t - DASH_OUT_S - DASH_TRAVEL_S) / DASH_IN_S, 0.0, 1.0)


static func _ease_out(x: float) -> float:
	return 1.0 - (1.0 - x) * (1.0 - x)


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
	_body_scale = _body.scale
	_body_pos = _body.position
	if bool(c["tint"]):
		var mat: ShaderMaterial = ShaderMaterial.new()
		mat.shader = TINT_SHADER
		mat.set_shader_parameter("tint", _color)
		_body.material = mat
	else:
		_body.material = null
	_tint_body()

	_label.text = _name
	_place_tag()
	queue_redraw()


## Name above the head.
func _place_tag() -> void:
	var head_y: float = _radius_px - Net.tiles_to_px(HEIGHT_TILES)
	_label.position.y = head_y - _label.custom_minimum_size.y - NAME_GAP_PX


func _tint_body() -> void:
	var c: Color = Color.WHITE
	if _poison > 0:
		var t: float = clampf(float(_poison) / POISON_MAX, 0.0, 1.0) * POISON_BLEND_MAX
		c = c.lerp(POISON_COLOR, t)
	if _out:
		c = c.darkened(OUT_DARKEN)
	_body.self_modulate = c


func _process(delta: float) -> void:
	step_dash(delta)
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
	# The shadow the character stands on (it follows the body through a dash).
	var shadow_at: Vector2 = Vector2.ZERO
	var shadow_k: float = 1.0
	if is_dashing():
		_draw_dash()
		if _fx_t < DASH_OUT_S:
			shadow_at = _start_local()
			shadow_k = 1.0 - _fx_t / DASH_OUT_S
		elif _fx_t < DASH_OUT_S + DASH_TRAVEL_S:
			shadow_k = 0.0
		else:
			shadow_k = _in_progress()
	if shadow_k <= 0.0:
		return
	# A plain black shadow for every seat (the name and the character tell players apart).
	var body: Color = Color.BLACK
	body.a = SHADOW_ALPHA * shadow_k
	var outline: Color = SHADOW_OUTLINE
	outline.a *= shadow_k
	draw_set_transform(shadow_at + _ground(), 0.0, Vector2(1.0, SHADOW_SQUASH))
	draw_circle(Vector2.ZERO, _radius_px * 1.3, body)
	draw_arc(Vector2.ZERO, _radius_px * 1.3, 0.0, TAU, 24, outline, 1.0)
	draw_set_transform(Vector2.ZERO)


## Where the feet touch the ground, from the node's origin.
func _ground() -> Vector2:
	return Vector2(0.0, _radius_px * GROUND_Y)


## Dust, the short trail, the ball with its flash and streaks, and the pop's ring and sparks.
func _draw_dash() -> void:
	var light: Color = _color.lightened(DASH_LIGHTEN)
	var start: Vector2 = _start_local()
	_draw_dust(start + _ground(), _fx_t / DASH_PUFF_S, DASH_PUFF_BITS)
	for i in _fx_trail.size():
		var age: float = (_fx_t - _fx_trail_t[i]) / DASH_TRAIL_S
		var tc: Color = light
		tc.a = DASH_TRAIL_ALPHA * (1.0 - age)
		draw_circle(to_local(_fx_trail[i]), DASH_BALL_PX * DASH_TRAIL_SIZE * (1.0 - age), tc)
	var ball: Vector2 = to_local(_ball_global())
	if _fx_t < DASH_OUT_S:
		var u: float = _fx_t / DASH_OUT_S
		if u >= DASH_STRETCH_END:
			var g: float = (u - DASH_STRETCH_END) / (1.0 - DASH_STRETCH_END)
			_draw_ball(ball, DASH_BALL_PX * g, Vector2.ONE, light)
			var fc: Color = Color.WHITE
			fc.a = 1.0 - g
			draw_circle(ball, DASH_BALL_PX * lerpf(DASH_FLASH_R.x, DASH_FLASH_R.y, g), fc)
	elif _fx_t < DASH_OUT_S + DASH_TRAVEL_S:
		var back: Vector2 = -_fx_dir
		var across: Vector2 = Vector2(-back.y, back.x) * DASH_BALL_PX * DASH_STREAK_GAP
		var length: float = minf(DASH_STREAK_PX, ball.distance_to(start + _body_pos))
		var sc: Color = light
		sc.a = DASH_STREAK_ALPHA
		for side: float in [-1.0, 1.0]:
			var from: Vector2 = ball + across * side
			draw_line(from, from + back * length, sc, DASH_STREAK_WIDTH)
		_draw_ball(ball, DASH_BALL_PX, DASH_BALL_COMET, light)
	else:
		var u: float = _in_progress()
		if u < DASH_POP_BALL:
			_draw_ball(ball, DASH_BALL_PX * (1.0 - u / DASH_POP_BALL), Vector2.ONE, light)
		var r: float = lerpf(DASH_BURST_PX.x, DASH_BURST_PX.y, _ease_out(u))
		var bc: Color = light
		bc.a = DASH_BURST_ALPHA * (1.0 - u)
		draw_set_transform(_ground(), 0.0, Vector2(1.0, SHADOW_SQUASH))
		draw_arc(Vector2.ZERO, r, 0.0, TAU, 24, bc, DASH_BURST_WIDTH)
		for i in DASH_BURST_SPARKS:
			var a: float = TAU * float(i) / float(DASH_BURST_SPARKS)
			draw_circle(Vector2(cos(a), sin(a)) * r * DASH_SPARK_REACH, DASH_SPARK_PX * (1.0 - u), bc)
		draw_set_transform(Vector2.ZERO)
		_draw_dust(_ground(), u, DASH_END_PUFF_BITS)


## The ball: seat colour with a white core; `shape` stretches it along the dash (comet).
func _draw_ball(at: Vector2, radius: float, shape: Vector2, color: Color) -> void:
	if radius <= 0.0:
		return
	draw_set_transform(at, _fx_dir.angle(), shape)
	draw_circle(Vector2.ZERO, radius, color)
	draw_circle(Vector2.ZERO, radius * DASH_BALL_CORE, Color.WHITE)
	draw_set_transform(Vector2.ZERO)


## A dust puff on the ground at `at`, `p` 0..1 through it: bits spread, rise and fade.
func _draw_dust(at: Vector2, p: float, bits: int) -> void:
	if p < 0.0 or p >= 1.0:
		return
	var dust: Color = DASH_PUFF_COLOR
	dust.a = DASH_PUFF_ALPHA * (1.0 - p)
	for i in bits:
		var a: float = TAU * (float(i) + 0.5) / float(bits)
		var spread: Vector2 = Vector2(cos(a), sin(a) * SHADOW_SQUASH) * DASH_PUFF_SPREAD_PX * _ease_out(p)
		draw_circle(at + spread + Vector2(0.0, -DASH_PUFF_RISE_PX * p), lerpf(DASH_PUFF_PX.x, DASH_PUFF_PX.y, p), dust)
