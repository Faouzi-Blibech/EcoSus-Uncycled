extends Node
## Loads textures and sounds. A missing file never crashes: it logs a warning and returns a
## coloured placeholder (texture) or an empty stream (sound).

const PLACEHOLDER_SIZE: int = 32

var _placeholders: Dictionary = {}


func texture(path: String) -> Texture2D:
	if ResourceLoader.exists(path):
		var res: Resource = load(path)
		if res is Texture2D:
			return res as Texture2D
	push_warning("Assets: missing texture %s, using placeholder" % path)
	return _placeholder(path)


func sound(path: String) -> AudioStream:
	if ResourceLoader.exists(path):
		var res: Resource = load(path)
		if res is AudioStream:
			return res as AudioStream
	push_warning("Assets: missing sound %s, using silence" % path)
	return AudioStreamWAV.new()


func _placeholder(path: String) -> Texture2D:
	if _placeholders.has(path):
		return _placeholders[path] as Texture2D
	var img: Image = Image.create(PLACEHOLDER_SIZE, PLACEHOLDER_SIZE, false, Image.FORMAT_RGBA8)
	var hue: float = float(path.hash() % 360) / 360.0
	img.fill(Color.from_hsv(hue, 0.5, 0.9))
	var tex: ImageTexture = ImageTexture.create_from_image(img)
	_placeholders[path] = tex
	return tex
