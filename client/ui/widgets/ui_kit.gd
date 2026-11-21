extends RefCounted
## Small helpers to build UI in code (scenes are kept as bare roots). Touch targets >= 48 px
## in the 640x360 logical space.

const MIN_TOUCH: float = 48.0
const BG_COLOR: Color = Color(0.09, 0.14, 0.12)
const FONT_SIZE: int = 14

static var _theme: Theme = null


static func theme() -> Theme:
	if _theme == null:
		_theme = Theme.new()
		_theme.default_font_size = FONT_SIZE
	return _theme


static func fill(c: Control) -> void:
	c.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)


static func background(parent: Control, color: Color = BG_COLOR) -> ColorRect:
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(rect)
	fill(rect)
	return rect


static func make_label(text: String, font_size: int = FONT_SIZE, color: Color = Color.WHITE) -> Label:
	var l: Label = Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", font_size)
	l.add_theme_color_override("font_color", color)
	return l


static func make_button(text: String, min_width: float = 120.0) -> Button:
	var b: Button = Button.new()
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
	var l: Label = make_label("", font_size)
	l.add_theme_color_override("font_outline_color", Color.BLACK)
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
