extends CanvasLayer
## Covers the screen with "turn your phone sideways" while the window is taller than wide: the game
## is landscape only. Added once by Settings on the web and on phones.

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const TOP_LAYER: int = 100

var _label: Label


static func is_portrait(size: Vector2) -> bool:
	return size.y > size.x


## Only a touchscreen device held upright is covered: a laptop browser in a tall half-screen
## window is still playable.
static func should_cover(size: Vector2, touchscreen: bool) -> bool:
	return touchscreen and is_portrait(size)


func _init() -> void:
	layer = TOP_LAYER
	visible = false


func _ready() -> void:
	var cover: Control = Control.new()
	cover.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(cover)
	UIKit.fill(cover)
	UIKit.background(cover, Color(0.05, 0.1, 0.08, 1.0))
	var center: CenterContainer = CenterContainer.new()
	cover.add_child(center)
	UIKit.fill(center)
	_label = UIKit.make_label(tr("ROTATE_HINT"), 20)
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_label.custom_minimum_size = Vector2(240.0, 0.0)
	center.add_child(_label)
	get_viewport().size_changed.connect(_on_size_changed)
	_on_size_changed()


func _on_size_changed() -> void:
	update_for(get_viewport().get_visible_rect().size, DisplayServer.is_touchscreen_available())


func update_for(size: Vector2, touchscreen: bool) -> void:
	visible = should_cover(size, touchscreen)
