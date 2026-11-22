extends Control
## Full-screen splash when a body is reported: red bursts, the fallen player drawn big in its seat
## colour, "Fallen player reported!" and who found whom. Pops in, holds, fades, then frees itself;
## the meeting screen is underneath. Text and colours come from the caller (setup).

const BodyArt := preload("res://world/pile/body_art.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")

const HOLD_S: float = 1.6
const FADE_S: float = 0.35
const POP_S: float = 0.25
const BODY_SCALE: float = 3.2
const RAYS: int = 14
const BACKDROP: Color = Color(0.12, 0.02, 0.03, 0.92)
const RAY_COLOR: Color = Color(0.7, 0.1, 0.1, 0.35)
const TITLE_COLOR: Color = Color(1.0, 0.85, 0.3)

var _color: Color = Color(0.6, 0.6, 0.6)
var _t: float = 0.0
var _title: Label
var _sub: Label


## `victim_color` paints the body; `subtitle` is the "who found whom" line ("" for none).
func setup(victim_color: Color, title: String, subtitle: String) -> void:
	_color = victim_color
	UIKit.fill(self)
	mouse_filter = Control.MOUSE_FILTER_STOP
	var box: VBoxContainer = UIKit.make_vbox(4)
	box.alignment = BoxContainer.ALIGNMENT_END
	add_child(box)
	UIKit.fill(box)
	box.offset_bottom = -40.0
	_title = UIKit.make_label(title, 26, TITLE_COLOR)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_title)
	_sub = UIKit.make_label(subtitle, 14, Color.WHITE)
	_sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_sub)


func _ready() -> void:
	modulate.a = 0.0
	var tw: Tween = create_tween()
	tw.tween_property(self, "modulate:a", 1.0, POP_S)
	tw.tween_interval(HOLD_S)
	tw.tween_property(self, "modulate:a", 0.0, FADE_S)
	tw.tween_callback(queue_free)


func _process(delta: float) -> void:
	_t += delta
	queue_redraw()


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), BACKDROP)
	var center: Vector2 = Vector2(size.x * 0.5, size.y * 0.42)
	# Slowly turning red rays behind the body.
	var reach: float = size.length()
	for i in RAYS:
		var a: float = _t * 0.6 + TAU * float(i) / float(RAYS)
		var half: float = PI / float(RAYS) * 0.5
		draw_colored_polygon(PackedVector2Array([
			center,
			center + Vector2.from_angle(a - half) * reach,
			center + Vector2.from_angle(a + half) * reach,
		]), RAY_COLOR)
	# The body pops in (overshoot), then sits still; the "!" bubble bobs over it.
	var pop: float = clampf(_t / POP_S, 0.0, 1.0)
	var s: float = BODY_SCALE * (1.0 + 0.25 * sin(pop * PI))
	BodyArt.draw_body(self, center, _color, s)
	BodyArt.draw_alert(self, center + Vector2(0.0, -20.0 * s), s * 0.8, _t)
