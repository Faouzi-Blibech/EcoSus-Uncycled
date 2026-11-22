extends GridContainer
## The four languages as flag buttons. Named: two per row, each with the language's own name;
## compact: one row of flags (the name is the tooltip). The current one is shown pressed. Emits
## picked(code) when the player taps a language.

signal picked(code: String)

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")

const BUTTON_WIDTH_PX: float = 128.0
const COMPACT_WIDTH_PX: float = 60.0
const FLAG_WIDTH_PX: int = 36

var _group: ButtonGroup = ButtonGroup.new()


func _init(compact: bool = false, current: String = Settings.language) -> void:
	columns = Settings.LANGUAGES.size() if compact else 2
	add_theme_constant_override("h_separation", 8)
	add_theme_constant_override("v_separation", 8)
	for code in Settings.LANGUAGES:
		var lang_name: String = tr("LANG_" + code.to_upper())
		var btn: Button = UIKit.make_button("" if compact else lang_name, COMPACT_WIDTH_PX if compact else BUTTON_WIDTH_PX)
		btn.name = code
		btn.tooltip_text = lang_name
		btn.icon = FlagArt.texture(code)
		btn.expand_icon = true
		btn.add_theme_constant_override("icon_max_width", FLAG_WIDTH_PX)
		btn.alignment = HORIZONTAL_ALIGNMENT_CENTER if compact else HORIZONTAL_ALIGNMENT_LEFT
		btn.toggle_mode = true
		# Cream flags; the current language is the green one.
		PaperKit.choice(btn)
		btn.button_group = _group
		btn.button_pressed = code == current
		btn.pressed.connect(func() -> void: picked.emit(code))
		add_child(btn)


## The language whose button is pressed.
func selected() -> String:
	var btn: BaseButton = _group.get_pressed_button()
	return String(btn.name) if btn != null else Settings.language
