extends RefCounted
## Cream-paper look shared by the home, create, join and lobby screens (the designers' UI kit in
## assets/ui/kit). Static helpers only, like UIKit. Every texture goes through Assets, so missing art
## falls back to a placeholder.

const UIKit := preload("res://ui/widgets/ui_kit.gd")

const KIT_DIR: String = "res://assets/ui/kit/"
const BG_PATH: String = "res://assets/ui/home/background.jpg"
const FONT_PATH: String = "res://assets/fonts/caroni_regular.otf"
const SHADOW_SHADER: String = "res://ui/widgets/soft_shadow.gdshader"

const STAGE_SIZE: Vector2 = Vector2(640.0, 360.0)
const INK: Color = Color(0.17, 0.15, 0.13)
const SOFT_INK: Color = Color(0.42, 0.36, 0.3)
const ERROR_INK: Color = Color(0.65, 0.2, 0.12)
const BG_FALLBACK: Color = Color(0.97, 0.94, 0.85)
## Cream ink for text on the dark (purple) notes.
const CREAM: Color = Color(0.97, 0.94, 0.85)

## Icons draw at ICON_SIZE inside a UIKit.MIN_TOUCH square, so the tap target stays 48 px.
const ICON_SIZE: float = 40.0
const ICON_MARGIN: float = 6.0
const PRESS_SCALE: float = 0.95
const TWEEN_S: float = 0.08

const SHADOW_OFFSET: Vector2 = Vector2(2.0, 4.0)
const SHADOW_BLUR: float = 4.0
const SHADOW_PAD: float = 10.0
const SHADOW_COLOR: Color = Color(0.2, 0.14, 0.08, 0.35)

## Kit textures are exported at 3 texture pixels per logical pixel; nine-patches are drawn at that
## density (3x size, 1/3 scale) so their corners keep the art's proportions.
const DENSITY: float = 3.0
const PANEL_MARGIN_PX: int = 60
const BUTTON_FONT_SIZE: int = 22
const BUTTON_PAD_X: float = 18.0

static var _font_lang: String = ""
static var _font: Font = null


## Caroni handwriting; in Arabic it falls back to the Arabic font Settings uses.
static func font() -> Font:
	if _font != null and _font_lang == Settings.language:
		return _font
	_font_lang = Settings.language
	var variation: FontVariation = FontVariation.new()
	if ResourceLoader.exists(FONT_PATH):
		variation.base_font = load(FONT_PATH) as Font
	if Settings.language == "ar" and ResourceLoader.exists(Settings.ARABIC_FONT_PATH):
		var fallbacks: Array[Font] = [load(Settings.ARABIC_FONT_PATH) as Font]
		variation.fallbacks = fallbacks
	_font = variation
	return _font


static func tex(file: String) -> Texture2D:
	return Assets.texture(KIT_DIR + file)


## Paper background that covers the whole screen.
static func background(parent: Control) -> void:
	UIKit.background(parent, BG_FALLBACK)
	var bg: TextureRect = TextureRect.new()
	bg.texture = Assets.texture(BG_PATH)
	bg.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	bg.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(bg)
	UIKit.fill(bg)


## Fixed 640x360 stage centred on screen; wider phones get extra paper at the sides.
## Always left to right, so the drawn composition matches the mockup in Arabic too (Arabic text
## still reads right to left inside it).
static func stage(parent: Control) -> Control:
	var s: Control = Control.new()
	s.layout_direction = Control.LAYOUT_DIRECTION_LTR
	s.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(s)
	s.set_anchors_preset(Control.PRESET_CENTER)
	s.offset_left = -STAGE_SIZE.x / 2.0
	s.offset_top = -STAGE_SIZE.y / 2.0
	s.offset_right = STAGE_SIZE.x / 2.0
	s.offset_bottom = STAGE_SIZE.y / 2.0
	return s


## Top-left anchored rect. Offsets, not position: setting position on a control with no parent
## yet mirrors it when the language is Arabic (against a zero-width parent), which threw the home
## art off screen.
static func place(c: Control, rect: Rect2) -> void:
	c.set_anchors_preset(Control.PRESET_TOP_LEFT)
	c.offset_left = rect.position.x
	c.offset_top = rect.position.y
	c.offset_right = rect.end.x
	c.offset_bottom = rect.end.y


static func label(text: String, font_size: int, color: Color = INK, handwritten: bool = true) -> Label:
	var l: Label = UIKit.make_label(text, font_size, color)
	if handwritten:
		l.add_theme_font_override("font", font())
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


static func picture(texture: Texture2D, rect: Rect2) -> TextureRect:
	var t: TextureRect = TextureRect.new()
	t.texture = texture
	t.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	t.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	place(t, rect)
	return t


## Nine-patch drawn at DENSITY so the corners stay the art's size. Add children to a sibling, not to it.
static func panel(file: String, rect: Rect2, margin_px: int = PANEL_MARGIN_PX) -> NinePatchRect:
	var p: NinePatchRect = NinePatchRect.new()
	p.texture = tex(file)
	p.patch_margin_left = margin_px
	p.patch_margin_right = margin_px
	p.patch_margin_top = margin_px
	p.patch_margin_bottom = margin_px
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	p.position = rect.position
	p.size = rect.size * DENSITY
	p.scale = Vector2.ONE / DENSITY
	return p


## Width / height of a texture (1 when missing).
static func aspect_of(texture: Texture2D) -> float:
	if texture == null or texture.get_height() <= 0:
		return 1.0
	return float(texture.get_width()) / float(texture.get_height())


## The largest rect with the given aspect that fits inside rect, centred in it.
static func aspect_fit(rect: Rect2, aspect: float) -> Rect2:
	var s: Vector2 = Vector2(rect.size.x, rect.size.x / aspect)
	if s.y > rect.size.y:
		s = Vector2(rect.size.y * aspect, rect.size.y)
	return Rect2(rect.get_center() - s / 2.0, s)


## Places c in rect at the art's own proportions (never stretched).
static func place_fit(c: Control, rect: Rect2, aspect: float) -> void:
	place(c, aspect_fit(rect, aspect))


## Pill button from the kit: "green", "cream" or "coral". Place it with place_button so the pill
## keeps the art's proportions.
static func button(text: String, kind: String = "green") -> Button:
	var b: Button = Button.new()
	b.text = text
	b.clip_text = true
	b.set_meta("aspect", aspect_of(tex("button_%s.png" % kind)))
	b.add_theme_font_override("font", font())
	b.add_theme_font_size_override("font_size", BUTTON_FONT_SIZE)
	for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color"]:
		b.add_theme_color_override(state, INK)
	b.add_theme_color_override("font_disabled_color", SOFT_INK)
	b.add_theme_stylebox_override("normal", _button_box("button_%s.png" % kind))
	b.add_theme_stylebox_override("hover", _button_box("button_%s.png" % kind))
	b.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	b.add_theme_stylebox_override("pressed", _button_box("button_%s_pressed.png" % kind))
	b.add_theme_stylebox_override("disabled", _button_box("button_grey_disabled.png"))
	add_press_squash(b)
	return b


## Fits a kit button in rect at the pill art's proportions.
static func place_button(b: Button, rect: Rect2) -> void:
	place_fit(b, rect, float(b.get_meta("aspect", 3.0)))


static func _button_box(file: String) -> StyleBoxTexture:
	var box: StyleBoxTexture = StyleBoxTexture.new()
	box.texture = tex(file)
	box.content_margin_left = BUTTON_PAD_X
	box.content_margin_right = BUTTON_PAD_X
	return box


## Text field on the kit's paper strip.
static func line_edit(placeholder: String, font_size: int, max_len: int = 0) -> LineEdit:
	var e: LineEdit = LineEdit.new()
	e.placeholder_text = placeholder
	e.max_length = max_len
	e.alignment = HORIZONTAL_ALIGNMENT_CENTER
	e.custom_minimum_size = Vector2(0.0, UIKit.MIN_TOUCH)
	e.add_theme_font_override("font", font())
	e.add_theme_font_size_override("font_size", font_size)
	e.add_theme_color_override("font_color", INK)
	e.add_theme_color_override("font_placeholder_color", SOFT_INK)
	e.add_theme_color_override("caret_color", INK)
	var box: StyleBoxTexture = StyleBoxTexture.new()
	box.texture = tex("field_input.png")
	box.set_content_margin_all(8.0)
	e.add_theme_stylebox_override("normal", box)
	e.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	return e


## Invisible 48 px tap square with the round kit icon centred inside. file is under assets/ui/...
static func icon_button(parent: Control, path: String, tooltip: String) -> TextureButton:
	var btn: TextureButton = TextureButton.new()
	btn.custom_minimum_size = Vector2(UIKit.MIN_TOUCH, UIKit.MIN_TOUCH)
	btn.size = btn.custom_minimum_size
	btn.tooltip_text = tooltip
	parent.add_child(btn)
	var art: TextureRect = picture(Assets.texture(path), Rect2())
	btn.add_child(art)
	var inset: float = (UIKit.MIN_TOUCH - ICON_SIZE) / 2.0
	art.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	art.offset_left = inset
	art.offset_top = inset
	art.offset_right = -inset
	art.offset_bottom = -inset
	add_shadow(art, art.texture)
	add_press_squash(btn)
	return btn


## Anchors a 48 px button to the top-left (right=false) or top-right screen corner; slot stacks down.
static func pin_icon(btn: Control, right: bool, slot: int) -> void:
	var top: float = ICON_MARGIN + float(slot) * UIKit.MIN_TOUCH
	if right:
		btn.set_anchors_preset(Control.PRESET_TOP_RIGHT)
		btn.offset_left = -ICON_MARGIN - UIKit.MIN_TOUCH
		btn.offset_right = -ICON_MARGIN
	else:
		btn.set_anchors_preset(Control.PRESET_TOP_LEFT)
		btn.offset_left = ICON_MARGIN
		btn.offset_right = ICON_MARGIN + UIKit.MIN_TOUCH
	btn.offset_top = top
	btn.offset_bottom = top + UIKit.MIN_TOUCH


## Squashes a little while pressed.
static func add_press_squash(btn: BaseButton) -> void:
	btn.resized.connect(func() -> void: btn.pivot_offset = btn.size / 2.0)
	btn.button_down.connect(func() -> void: _scale_to(btn, PRESS_SCALE))
	btn.button_up.connect(func() -> void: _scale_to(btn, 1.0))


static func _scale_to(btn: BaseButton, s: float) -> void:
	if btn.disabled or not btn.is_inside_tree():
		return
	btn.create_tween().tween_property(btn, "scale", Vector2(s, s), TWEEN_S)


## Soft shadow drawn behind target (a child, so it follows the press squash). Skipped without the shader.
static func add_shadow(target: Control, texture: Texture2D, strength: float = 1.0) -> void:
	if texture == null or not ResourceLoader.exists(SHADOW_SHADER):
		return
	var offset: Vector2 = SHADOW_OFFSET * strength
	var blur_px: float = SHADOW_BLUR * strength
	var pad_px: float = SHADOW_PAD * strength
	var mat: ShaderMaterial = ShaderMaterial.new()
	mat.shader = load(SHADOW_SHADER) as Shader
	mat.set_shader_parameter("shadow_color", SHADOW_COLOR)
	var shadow: TextureRect = TextureRect.new()
	shadow.texture = texture
	shadow.material = mat
	shadow.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	shadow.stretch_mode = TextureRect.STRETCH_SCALE
	shadow.show_behind_parent = true
	shadow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	target.add_child(shadow)
	# The shadow sits under the art as drawn: a keep-aspect target letterboxes its texture.
	var keeps_aspect: bool = _keeps_aspect(target)
	var update: Callable = func() -> void:
		var area: Rect2 = Rect2(Vector2.ZERO, target.size)
		if area.size.x <= 0.0 or area.size.y <= 0.0:
			return
		if keeps_aspect:
			area = aspect_fit(area, aspect_of(texture))
		shadow.position = area.position + offset - Vector2(pad_px, pad_px)
		shadow.size = area.size + Vector2(pad_px, pad_px) * 2.0
		mat.set_shader_parameter("pad", Vector2(pad_px / shadow.size.x, pad_px / shadow.size.y))
		mat.set_shader_parameter("blur", Vector2(blur_px / area.size.x, blur_px / area.size.y))
	target.resized.connect(update)
	update.call()


static func _keeps_aspect(target: Control) -> bool:
	if target is TextureRect:
		return (target as TextureRect).stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	if target is TextureButton:
		return (target as TextureButton).stretch_mode == TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	return false


static var _dense: Dictionary = {}
static var _theme: Theme = null
static var _theme_lang: String = ""


## A kit texture that reports its logical size (texture pixels / DENSITY) but keeps every pixel, so a
## StyleBoxTexture's margins are logical pixels and the corners draw at the art's real size.
static func dense(file: String) -> Texture2D:
	if _dense.has(file):
		return _dense[file] as Texture2D
	var src: Texture2D = tex(file)
	var img: Image = src.get_image() if src != null else null
	if img == null:
		return src
	var t: ImageTexture = ImageTexture.create_from_image(img)
	t.set_size_override(Vector2i((Vector2(img.get_size()) / DENSITY).round()))
	_dense[file] = t
	return t


## Nine-slice box over a kit texture (margins in logical px; only the middle stretches).
static func box(file: String, margin: float, content: Vector2 = Vector2(10.0, 6.0)) -> StyleBoxTexture:
	var b: StyleBoxTexture = StyleBoxTexture.new()
	b.texture = dense(file)
	b.set_texture_margin_all(margin)
	b.content_margin_left = content.x
	b.content_margin_right = content.x
	b.content_margin_top = content.y
	b.content_margin_bottom = content.y
	return b


## The paper look for screens built from plain controls: Caroni ink text, cream pill buttons,
## the paper input strip, paper panels. UIKit.theme() returns it, so every UIKit screen shares it.
static func theme() -> Theme:
	if _theme != null and _theme_lang == Settings.language:
		return _theme
	_theme_lang = Settings.language
	var th: Theme = Theme.new()
	th.default_font = font()
	th.default_font_size = UIKit.FONT_SIZE
	th.set_color("font_color", "Label", INK)
	for type in ["Button", "OptionButton", "MenuButton", "CheckBox", "CheckButton"]:
		for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color"]:
			th.set_color(state, type, INK)
		th.set_color("font_disabled_color", type, SOFT_INK)
		th.set_stylebox("focus", type, StyleBoxEmpty.new())
	# Buttons are green (go / confirm) unless marked: theme_type_variation "CreamButton" for
	# cancel, close and back, "CoralButton" for skip and danger (UIKit.make_button's kind).
	_pill_styles(th, "Button", "green")
	_pill_styles(th, "OptionButton", "cream")
	_pill_styles(th, "MenuButton", "cream")
	for kind in BUTTON_KINDS:
		var variation: String = variation_of(kind)
		th.set_type_variation(variation, "Button")
		_pill_styles(th, variation, kind)
	for type in ["CheckBox", "CheckButton"]:
		for state in ["normal", "hover", "pressed", "hover_pressed", "disabled"]:
			th.set_stylebox(state, type, StyleBoxEmpty.new())
	th.set_icon("checked", "CheckBox", dense("checkbox_checked.png"))
	th.set_icon("unchecked", "CheckBox", dense("checkbox_empty.png"))
	var field: StyleBoxTexture = box("field_input.png", 14.0, Vector2(14.0, 6.0))
	th.set_stylebox("normal", "LineEdit", field)
	th.set_stylebox("focus", "LineEdit", StyleBoxEmpty.new())
	th.set_stylebox("read_only", "LineEdit", field)
	th.set_color("font_color", "LineEdit", INK)
	th.set_color("font_placeholder_color", "LineEdit", SOFT_INK)
	th.set_color("caret_color", "LineEdit", INK)
	th.set_color("font_uneditable_color", "LineEdit", SOFT_INK)
	var paper: StyleBoxTexture = box("panel_paper.png", 20.0, Vector2(16.0, 14.0))
	th.set_stylebox("panel", "PanelContainer", paper)
	th.set_stylebox("panel", "Panel", paper)
	th.set_stylebox("panel", "PopupMenu", paper)
	for state in ["font_color", "font_hover_color"]:
		th.set_color(state, "PopupMenu", INK)
	th.set_font("font", "PopupMenu", font())
	_theme = th
	return _theme


const BUTTON_KINDS: Array[String] = ["green", "cream", "coral"]


## Theme type variation of a pill colour: "cream" -> "CreamButton".
static func variation_of(kind: String) -> String:
	return kind.capitalize() + "Button"


static func _pill_styles(th: Theme, type: String, kind: String) -> void:
	var pad: Vector2 = Vector2(BUTTON_PAD_X, 4.0)
	var normal: StyleBoxTexture = box("button_%s.png" % kind, 18.0, pad)
	var held: StyleBoxTexture = box("button_%s_pressed.png" % kind, 18.0, pad)
	th.set_stylebox("normal", type, normal)
	th.set_stylebox("hover", type, normal)
	th.set_stylebox("pressed", type, held)
	th.set_stylebox("hover_pressed", type, held)
	th.set_stylebox("disabled", type, box("button_grey_disabled.png", 18.0, pad))
	th.set_stylebox("focus", type, StyleBoxEmpty.new())
	# Ink reads on all three pills (the green and coral art are light enough).
	for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color"]:
		th.set_color(state, type, INK)
	th.set_color("font_disabled_color", type, SOFT_INK)


## A seat colour dark enough to read as text on paper (seat colours are made for the dark map).
static func seat_ink(seat_color: Color) -> Color:
	return seat_color.darkened(0.45) if seat_color.get_luminance() > 0.35 else seat_color


## A pick-one toggle (a language, a vote card): a cream pill that turns green while chosen.
static func choice(btn: Button) -> void:
	btn.theme_type_variation = variation_of("cream")
	var picked: StyleBoxTexture = box("button_green.png", 18.0, Vector2(BUTTON_PAD_X, 4.0))
	btn.add_theme_stylebox_override("pressed", picked)
	btn.add_theme_stylebox_override("hover_pressed", picked)
