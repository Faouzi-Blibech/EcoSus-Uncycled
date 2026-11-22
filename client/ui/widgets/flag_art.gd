class_name FlagArt
extends RefCounted
## The language flags, drawn in code (no art files): France for French, England and the US split
## on the diagonal for English (US top left, UK bottom right), Tunisia for Arabic, Sweden for
## Swedish. color_at() gives the flag colour at a point; texture() bakes it once per language.

const WIDTH_PX: int = 90
const HEIGHT_PX: int = 60
## Samples per pixel side: 2 x 2 per pixel smooths the diagonals and the round shapes.
const SAMPLES: int = 2

const FR_BLUE: Color = Color("002395")
const FR_RED: Color = Color("ed2939")
const SE_BLUE: Color = Color("006aa7")
const SE_YELLOW: Color = Color("fecc02")
const TN_RED: Color = Color("e70013")
const UK_BLUE: Color = Color("012169")
const UK_RED: Color = Color("c8102e")
const US_RED: Color = Color("b22234")
const US_BLUE: Color = Color("3c3b6e")

## Tunisia, in units of the white disc radius (the disc is half the flag height across).
const TN_CRESCENT_OUTER: float = 0.75
const TN_CRESCENT_INNER: float = 0.6
const TN_CRESCENT_SHIFT: float = 0.2
const TN_STAR_SHIFT: float = 0.25
const TN_STAR_RADIUS: float = 0.45

static var _textures: Dictionary = {}
static var _star: PackedVector2Array = PackedVector2Array()


## The flag of a language code (en, fr, ar, sv) as a texture; null for an unknown code.
static func texture(lang: String) -> Texture2D:
	if _textures.has(lang):
		return _textures[lang]
	if not ["en", "fr", "ar", "sv"].has(lang):
		return null
	var img: Image = Image.create(WIDTH_PX, HEIGHT_PX, false, Image.FORMAT_RGBA8)
	var n: float = float(SAMPLES * SAMPLES)
	for y in HEIGHT_PX:
		for x in WIDTH_PX:
			var sum: Color = Color(0.0, 0.0, 0.0, 0.0)
			for sy in SAMPLES:
				for sx in SAMPLES:
					var u: float = (float(x) + (float(sx) + 0.5) / SAMPLES) / WIDTH_PX
					var v: float = (float(y) + (float(sy) + 0.5) / SAMPLES) / HEIGHT_PX
					sum += color_at(lang, u, v)
			img.set_pixel(x, y, sum / n)
	var tex: ImageTexture = ImageTexture.create_from_image(img)
	_textures[lang] = tex
	return tex


## Flag colour at (u, v): 0..1 from the top left corner, on a 3:2 flag.
static func color_at(lang: String, u: float, v: float) -> Color:
	match lang:
		"fr":
			return FR_BLUE if u < 1.0 / 3.0 else (Color.WHITE if u < 2.0 / 3.0 else FR_RED)
		"sv":
			return _sweden(u, v)
		"ar":
			return _tunisia(u, v)
		"en":
			# Split on the diagonal from the top right to the bottom left corner.
			return _usa(u, v) if u + v < 1.0 else _union_jack(u, v)
	return Color.MAGENTA


## Nordic cross, 5:2:9 across and 4:2:4 down.
static func _sweden(u: float, v: float) -> Color:
	var x: float = u * 16.0
	var y: float = v * 10.0
	if (x >= 5.0 and x < 7.0) or (y >= 4.0 and y < 6.0):
		return SE_YELLOW
	return SE_BLUE


## Red field, white disc, red crescent opening to the fly, red star in the opening.
static func _tunisia(u: float, v: float) -> Color:
	# Disc radius = a quarter of the height; work in units of it, flag centre at the origin.
	var r: float = 0.25
	var p: Vector2 = Vector2((u - 0.5) * 1.5 / r, (v - 0.5) / r)
	if p.length() > 1.0:
		return TN_RED
	if Geometry2D.is_point_in_polygon(p, _star_points()):
		return TN_RED
	var in_outer: bool = p.length() < TN_CRESCENT_OUTER
	var in_cut: bool = (p - Vector2(TN_CRESCENT_SHIFT, 0.0)).length() < TN_CRESCENT_INNER
	if in_outer and not in_cut:
		return TN_RED
	return Color.WHITE


## Five-pointed star, one point toward the hoist (the crescent).
static func _star_points() -> PackedVector2Array:
	if _star.is_empty():
		var inner: float = TN_STAR_RADIUS * 0.382
		for i in 10:
			var a: float = PI + float(i) * PI / 5.0
			var rad: float = TN_STAR_RADIUS if i % 2 == 0 else inner
			_star.append(Vector2(TN_STAR_SHIFT, 0.0) + Vector2(cos(a), sin(a)) * rad)
	return _star


## 13 stripes, blue canton with 9 rows of stars (6 and 5 alternating).
static func _usa(u: float, v: float) -> Color:
	var canton_w: float = 0.4
	var canton_h: float = 7.0 / 13.0
	if u < canton_w and v < canton_h:
		var cx: float = u / canton_w * 12.0
		var cy: float = v / canton_h * 10.0
		var row: int = clampi(roundi(cy), 1, 9)
		var odd: int = 1 if row % 2 == 1 else 0
		# Even rows of stars sit on odd twelfths, odd rows on even ones.
		var col: float = roundf((cx - odd) / 2.0) * 2.0 + odd
		col = clampf(col, 1.0 if odd == 1 else 2.0, 11.0 if odd == 1 else 10.0)
		if Vector2(cx - col, (cy - row) * 1.2).length() < 0.45:
			return Color.WHITE
		return US_BLUE
	return US_RED if int(v * 13.0) % 2 == 0 else Color.WHITE


## Union Jack on a 60 x 30 grid: blue field, white and counterchanged red saltire, white-edged red cross.
static func _union_jack(u: float, v: float) -> Color:
	var p: Vector2 = Vector2(u * 60.0 - 30.0, v * 30.0 - 15.0)
	if absf(p.x) <= 3.0 or absf(p.y) <= 3.0:
		return UK_RED
	if absf(p.x) <= 5.0 or absf(p.y) <= 5.0:
		return Color.WHITE
	# Distance to the arm of the saltire in this quarter; red on the same turning side in every
	# quarter (the pinwheel), white around it.
	var arm: Vector2 = Vector2(signf(p.x) * 2.0, signf(p.y)).normalized()
	var side: float = arm.x * p.y - arm.y * p.x
	if side > -2.0 and side <= 0.0:
		return UK_RED
	if absf(side) < 3.0:
		return Color.WHITE
	return UK_BLUE
