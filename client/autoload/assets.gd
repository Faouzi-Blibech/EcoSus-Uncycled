extends Node
## Loads textures and sounds. A missing file never crashes: it logs a warning and returns a
## coloured placeholder (texture) or an empty stream (sound).

const PLACEHOLDER_SIZE: int = 32

var _placeholders: Dictionary = {}


## fill: colour of the placeholder when the file is missing (alpha 0 = a colour picked from the path).
func texture(path: String, fill: Color = Color(0, 0, 0, 0)) -> Texture2D:
	if ResourceLoader.exists(path):
		var res: Resource = load(path)
		if res is Texture2D:
			return res as Texture2D
	push_warning("Assets: missing texture %s, using placeholder" % path)
	return _placeholder(path, fill)


func sound(path: String) -> AudioStream:
	if ResourceLoader.exists(path):
		var res: Resource = load(path)
		if res is AudioStream:
			return res as AudioStream
	push_warning("Assets: missing sound %s, using silence" % path)
	return AudioStreamWAV.new()


func _placeholder(path: String, fill: Color) -> Texture2D:
	var key: String = path + fill.to_html()
	if _placeholders.has(key):
		return _placeholders[key] as Texture2D
	var img: Image = Image.create(PLACEHOLDER_SIZE, PLACEHOLDER_SIZE, false, Image.FORMAT_RGBA8)
	var hue: float = float(path.hash() % 360) / 360.0
	img.fill(fill if fill.a > 0.0 else Color.from_hsv(hue, 0.5, 0.9))
	var tex: ImageTexture = ImageTexture.create_from_image(img)
	_placeholders[key] = tex
	return tex
