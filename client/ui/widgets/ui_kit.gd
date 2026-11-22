extends RefCounted
## Small helpers to build UI in code (scenes are kept as bare roots). Touch targets >= 48 px
## in the 640x360 logical space.

const MIN_TOUCH: float = 48.0
const BG_COLOR: Color = Color(0.09, 0.14, 0.12)
const FONT_SIZE: int = 14

## Ink for text on paper (same as PaperKit.INK).
const INK: Color = Color(0.17, 0.15, 0.13)


## The shared paper look (PaperKit.theme): our kit art for buttons, fields and panels. Loaded at
## run time because PaperKit preloads this script.
static func theme() -> Theme:
	return load("res://ui/widgets/paper_kit.gd").theme()


static func fill(c: Control) -> void:
	c.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)


static func background(parent: Control, color: Color = BG_COLOR) -> ColorRect:
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(rect)
	fill(rect)
	return rect


static func make_label(text: String, font_size: int = FONT_SIZE, color: Color = INK) -> Label:
	var l: Label = Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", font_size)
	l.add_theme_color_override("font_color", color)
	return l


## kind: the pill colour, "green" (go, the default), "cream" (cancel, close, back) or "coral"
## (skip, danger).
static func make_button(text: String, min_width: float = 120.0, kind: String = "green") -> Button:
	var b: Button = Button.new()
	if kind != "green":
		b.theme_type_variation = kind.capitalize() + "Button"
	b.text = text
	b.custom_minimum_size = Vector2(min_width, MIN_TOUCH)
	return b


static func make_line_edit(placeholder: String, text: String = "", max_len: int = 0) -> LineEdit:
	var e: LineEdit = LineEdit.new()
	e.placeholder_text = placeholder
	e.text = text
	e.max_length = max_len
	e.custom_minimum_size = Vector2(0.0, MIN_TOUCH)
	return e


## Outlined label for the in-game HUD (readable over any floor colour).
static func make_hud_label(font_size: int = FONT_SIZE) -> Label:
	# Storybook colours: paper-cream text with an ink outline.
	var l: Label = make_label("", font_size, Color("faf2dd"))
	l.add_theme_color_override("font_outline_color", Color("3e2b25"))
	l.add_theme_constant_override("outline_size", 4)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


static func make_vbox(separation: int = 8, min_width: float = 0.0) -> VBoxContainer:
	var v: VBoxContainer = VBoxContainer.new()
	v.add_theme_constant_override("separation", separation)
	v.custom_minimum_size = Vector2(min_width, 0.0)
	return v


## Translates a keyed reason such as "no_room" with a prefix; falls back to fallback_key.
static func reason_text(prefix: String, reason: String, fallback_key: String) -> String:
	var key: String = prefix + reason.to_upper()
	var text: String = TranslationServer.translate(StringName(key))
	if text == key:
		return TranslationServer.translate(StringName(fallback_key))
	return text
