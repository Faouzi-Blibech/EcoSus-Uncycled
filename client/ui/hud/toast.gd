class_name Toast
extends Label
## One short line that fades out by itself (rejected actions).

const HOLD_S: float = 1.6
const FADE_S: float = 0.4
const BACKDROP: Color = Color(0.0, 0.0, 0.0, 0.6)

var _tween: Tween = null


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_theme_color_override("font_outline_color", Color.BLACK)
	add_theme_constant_override("outline_size", 4)
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = BACKDROP
	box.set_content_margin_all(6.0)
	add_theme_stylebox_override("normal", box)
	visible = false


func show_text(message: String) -> void:
	if _tween != null:
		_tween.kill()
	text = message
	modulate.a = 1.0
	visible = true
	_tween = create_tween()
	_tween.tween_interval(HOLD_S)
	_tween.tween_property(self, "modulate:a", 0.0, FADE_S)
	_tween.tween_callback(hide)
