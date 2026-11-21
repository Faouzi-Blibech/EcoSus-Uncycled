extends TextureRect
## One character playing its walk cycle. Tintable characters (the sprout) take the seat colour.

const Characters := preload("res://ui/widgets/characters.gd")
const TINT_SHADER: String = "res://ui/widgets/sprout_tint.gdshader"
const AVATAR_BG: Color = Color(0.97, 0.94, 0.87)
const SELF_PATH: String = "res://ui/widgets/character_sprite.gd"

var playing: bool = true
var _frames: Array[Texture2D] = []
var _frame_ms: Array = []
var _index: int = 0
var _elapsed_ms: float = 0.0


func _init() -> void:
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func setup(id: String, colour: Color) -> void:
	var c: Dictionary = Characters.info(id)
	_frames = Characters.frames(id)
	_frame_ms = c["frame_ms"]
	_index = 0
	_elapsed_ms = 0.0
	texture = _frames[0] if not _frames.is_empty() else null
	if bool(c["tint"]) and ResourceLoader.exists(TINT_SHADER):
		var mat: ShaderMaterial = ShaderMaterial.new()
		mat.shader = load(TINT_SHADER) as Shader
		mat.set_shader_parameter("tint", colour)
		material = mat
	else:
		material = null


## Round profile picture: the character's face, zoomed and clipped to a circle of the given rect
## (a square), on a cream disc.
static func avatar(id: String, colour: Color, rect: Rect2) -> Control:
	var disc: Panel = Panel.new()
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = AVATAR_BG
	box.set_corner_radius_all(int(ceilf(rect.size.x / 2.0)))
	box.anti_aliasing = true
	disc.add_theme_stylebox_override("panel", box)
	disc.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
	disc.mouse_filter = Control.MOUSE_FILTER_IGNORE
	disc.position = rect.position
	disc.size = rect.size

	var c: Dictionary = Characters.info(id)
	var sprite: TextureRect = (load(SELF_PATH) as GDScript).new()
	sprite.call("setup", id, colour)
	sprite.set("playing", false)
	var frame_aspect: float = 1.0
	if sprite.texture != null:
		frame_aspect = float(sprite.texture.get_width()) / float(sprite.texture.get_height())
	var w: float = rect.size.x * float(c.get("zoom", 1.0))
	var s: Vector2 = Vector2(w, w / frame_aspect)
	var face: Vector2 = c.get("face", Vector2(0.5, 0.5))
	sprite.position = rect.size / 2.0 - Vector2(face.x * s.x, face.y * s.y)
	sprite.size = s
	disc.add_child(sprite)
	return disc


func set_colour(colour: Color) -> void:
	if material is ShaderMaterial:
		(material as ShaderMaterial).set_shader_parameter("tint", colour)


func _process(delta: float) -> void:
	if not playing or _frames.size() < 2:
		return
	_elapsed_ms += delta * 1000.0
	while _elapsed_ms >= float(_frame_ms[_index]):
		_elapsed_ms -= float(_frame_ms[_index])
		_index = (_index + 1) % _frames.size()
	texture = _frames[_index]
