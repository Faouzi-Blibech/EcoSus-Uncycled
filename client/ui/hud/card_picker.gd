class_name CardPicker
extends Control
## Modal card row: the player picks one item (or cancels, when allowed). Subclasses set the config vars in _init().

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const CARD_SIZE: Vector2 = Vector2(116.0, 96.0)
const GAP: int = 8
const DIM: Color = Color(0.0, 0.0, 0.0, 0.55)
const TITLE_SIZE: int = 18
const CANCEL_MIN_HEIGHT: float = 48.0

## Emitted once with the chosen item name.
signal picked(item: String)

## Translation key of the title.
var title_key: String = ""
## Accepted items; empty accepts any item.
var known_items: Array[String] = []
## Card icons are drawn at most this wide (the art keeps its ratio).
const ICON_MAX_WIDTH: int = 48

## Icon path format with one %s for the item; empty means no icon.
var icon_format: String = ""
## Prefix of the card label key.
var label_prefix: String = ""
## When true, a Cancel button closes the picker without emitting picked.
var cancelable: bool = false

var _built: bool = false
var _row: HBoxContainer
var _open: bool = false


func _ready() -> void:
	_ensure_built()
	Net.meeting_started.connect(_on_meeting_started)
	Net.you_are_out.connect(_on_you_are_out)
	Net.game_over.connect(_on_game_over)


## Translation key of the card label for an item.
func label_key(item: String) -> String:
	return label_prefix + item.to_upper()


## Shows one card per accepted item, in the given order; replaces any cards already shown.
func open(items: Array[String]) -> void:
	_ensure_built()
	_clear_cards()
	var first: Button = null
	for item in items:
		if not known_items.is_empty() and not known_items.has(item):
			continue
		var card: Button = _make_card(item)
		_row.add_child(card)
		if first == null:
			first = card
	_open = true
	visible = true
	if first != null and is_inside_tree():
		first.grab_focus()


## Hides the picker and frees the cards.
func close() -> void:
	_clear_cards()
	_open = false
	visible = false


func is_open() -> bool:
	return _open


func _ensure_built() -> void:
	if _built:
		return
	_built = true
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	UIKit.fill(self)
	UIKit.background(self, DIM)
	var center: CenterContainer = CenterContainer.new()
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)
	UIKit.fill(center)
	var panel: PanelContainer = PanelContainer.new()
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	center.add_child(panel)
	var margin: MarginContainer = MarginContainer.new()
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 12)
	panel.add_child(margin)
	var box: VBoxContainer = UIKit.make_vbox(10)
	margin.add_child(box)
	var title: Label = UIKit.make_label(tr(title_key), TITLE_SIZE)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_child(title)
	_row = HBoxContainer.new()
	_row.add_theme_constant_override("separation", GAP)
	_row.alignment = BoxContainer.ALIGNMENT_CENTER
	_row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_child(_row)
	if cancelable:
		var cancel: Button = UIKit.make_button(tr("COMMON_CANCEL"), CARD_SIZE.x)
		cancel.custom_minimum_size.y = maxf(cancel.custom_minimum_size.y, CANCEL_MIN_HEIGHT)
		cancel.pressed.connect(close)
		box.add_child(cancel)


func _make_card(item: String) -> Button:
	var card: Button = UIKit.make_button(tr(label_key(item)), CARD_SIZE.x)
	card.custom_minimum_size = CARD_SIZE
	if not icon_format.is_empty():
		card.icon = Assets.texture(icon_format % item)
		card.expand_icon = true
		card.add_theme_constant_override("icon_max_width", ICON_MAX_WIDTH)
		card.icon_alignment = HORIZONTAL_ALIGNMENT_CENTER
		card.vertical_icon_alignment = VERTICAL_ALIGNMENT_TOP
	card.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	card.pressed.connect(_on_card_pressed.bind(item))
	return card


func _clear_cards() -> void:
	for c in _row.get_children():
		_row.remove_child(c)
		c.queue_free()


func _on_card_pressed(item: String) -> void:
	if not _open:
		return
	close()
	picked.emit(item)


func _on_meeting_started(_data: Dictionary) -> void:
	close()


func _on_you_are_out(_data: Dictionary) -> void:
	close()


func _on_game_over(_data: Dictionary) -> void:
	close()
