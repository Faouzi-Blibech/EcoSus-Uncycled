class_name Toast
extends Label
## One short line that fades out by itself (rejected actions).

const HOLD_S: float = 1.6
const FADE_S: float = 0.4
## A paper slip with ink text, like the rest of the HUD.
const BACKDROP: Color = Color("faf2dd")
const INK: Color = Color("3e2b25")

var _tween: Tween = null


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_theme_color_override("font_color", INK)
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = BACKDROP
	box.border_color = INK
	box.set_border_width_all(2)
	box.set_corner_radius_all(8)
	box.shadow_color = Color(0.24, 0.17, 0.12, 0.35)
	box.shadow_size = 3
	box.anti_aliasing = true
	box.content_margin_left = 10.0
	box.content_margin_right = 10.0
	box.content_margin_top = 4.0
	box.content_margin_bottom = 5.0
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
