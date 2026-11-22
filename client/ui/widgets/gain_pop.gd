extends Control
## A green "+10" that floats up and fades, with a burst of gold sparkles: shown when points go up.
## Frees itself when done. Usage: GainPop.spawn(parent, position, "+10").

const DURATION_S: float = 1.6
const RISE_PX: float = 46.0
const SPARKS: int = 12
const SPARK_REACH_PX: float = 46.0
const GREEN: Color = Color(0.35, 0.9, 0.45)
const SPARK: Color = Color(1.0, 0.83, 0.3)

var _t: float = 0.0
var _label: Label


static func spawn(parent: Node, at: Vector2, text: String) -> Control:
	var pop: Control = (load("res://ui/widgets/gain_pop.gd") as GDScript).new()
	pop.position = at
	parent.add_child(pop)
	pop.call("start", text)
	return pop


func start(text: String) -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	z_index = 50
	_label = Label.new()
	_label.text = text
	_label.add_theme_font_size_override("font_size", 30)
	_label.add_theme_color_override("font_color", GREEN)
	_label.add_theme_color_override("font_outline_color", Color(0.05, 0.2, 0.08))
	_label.add_theme_constant_override("outline_size", 6)
	_label.position = Vector2(-24.0, -20.0)
	add_child(_label)


func _process(delta: float) -> void:
	_t += delta
	var k: float = clampf(_t / DURATION_S, 0.0, 1.0)
	if _label != null:
		_label.position.y = -20.0 - RISE_PX * k
		_label.modulate.a = 1.0 - k * k
		_label.scale = Vector2.ONE * (1.0 + 0.25 * sin(minf(k * 4.0, 1.0) * PI))
	queue_redraw()
	if k >= 1.0:
		queue_free()


func _draw() -> void:
	var k: float = clampf(_t / (DURATION_S * 0.6), 0.0, 1.0)
	if k >= 1.0:
		return
	for i in range(SPARKS):
		var a: float = TAU * float(i) / SPARKS
		var p: Vector2 = Vector2(cos(a), sin(a)) * SPARK_REACH_PX * k
		var col: Color = SPARK
		col.a = 1.0 - k
		draw_circle(p, 3.5 * (1.0 - k) + 1.0, col)
