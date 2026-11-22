extends RefCounted
## A gold coin drawn in code (no art file): the currency box and the shop button use it.

const SIZE: int = 64
const GOLD: Color = Color(0.96, 0.76, 0.26)
const RIM: Color = Color(0.72, 0.5, 0.1)
const SHINE: Color = Color(1.0, 0.93, 0.62)

static var _cached: ImageTexture = null


static func texture() -> ImageTexture:
	if _cached != null:
		return _cached
	var img: Image = Image.create(SIZE, SIZE, false, Image.FORMAT_RGBA8)
	var c: Vector2 = Vector2(SIZE, SIZE) / 2.0 - Vector2(0.5, 0.5)
	var r: float = SIZE / 2.0 - 1.0
	for y in range(SIZE):
		for x in range(SIZE):
			var d: float = Vector2(x, y).distance_to(c)
			if d > r:
				continue
			var col: Color = RIM if d > r * 0.8 else GOLD
			# A soft shine in the upper left and an inner ring, so it reads as a coin at 20 px.
			if d <= r * 0.8 and Vector2(x, y).distance_to(c - Vector2(r, r) * 0.3) < r * 0.28:
				col = SHINE
			if absf(d - r * 0.6) < 1.2:
				col = RIM
			col.a = clampf(r - d + 0.5, 0.0, 1.0)
			img.set_pixel(x, y, col)
	_cached = ImageTexture.create_from_image(img)
	return _cached
