class_name GuideOverlay
extends Control
## What to do now, for new players: a small objectives card on the left (the main quest and the
## next step for the player's role) and, while a Cleaner carries bags, a gold arrow around the
## player pointing at the Recycling Machine. The camera keeps the player centred, so the arrow
## circles the middle of the screen. The round calls update_state() every frame.

const PaperKit := preload("res://ui/widgets/paper_kit.gd")

const CARD_POS: Vector2 = Vector2(8.0, 112.0)
## The HUD's player card width (Hud.CARD_WIDTH_PX), so the left column lines up.
const CARD_WIDTH: float = 150.0
const TEXT_SIZE: int = 9
const TITLE_SIZE: int = 10
const ARROW_RADIUS_PX: float = 56.0
const ARROW_LEN_PX: float = 26.0
const ARROW_HALF_WIDTH_PX: float = 10.0
## A short shaft behind the head, so it reads as an arrow at any angle.
const SHAFT_LEN_PX: float = 16.0
const SHAFT_WIDTH_PX: float = 6.0
## Hide the arrow this close to the machine (px, world): the player is there.
const NEAR_PX: float = 72.0
const GOLD: Color = Color(1.0, 0.8, 0.15)
const GOLD_EDGE: Color = Color(0.45, 0.3, 0.0)

var _card: PanelContainer
var _text: RichTextLabel
var _arrow_dir: Vector2 = Vector2.ZERO
var _t: float = 0.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_preset(Control.PRESET_FULL_RECT)
	# The HUD's storybook look: cream paper card, ink text, Caroni font. This node sits beside the
	# HUD's themed root, not under it, so it sets the font itself.
	_card = Hud._paper_panel()
	_card.position = CARD_POS
	_card.custom_minimum_size = Vector2(CARD_WIDTH, 0.0)
	add_child(_card)
	_text = RichTextLabel.new()
	_text.bbcode_enabled = true
	_text.fit_content = true
	_text.scroll_active = false
	_text.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_text.custom_minimum_size = Vector2(CARD_WIDTH - 14.0, 0.0)
	_text.add_theme_font_override("normal_font", PaperKit.font())
	_text.add_theme_font_override("bold_font", PaperKit.font())
	_text.add_theme_font_size_override("normal_font_size", TEXT_SIZE)
	_text.add_theme_font_size_override("bold_font_size", TITLE_SIZE)
	_text.add_theme_color_override("default_color", Hud.INK)
	_card.add_child(_text)


## The next step's strings.csv key for a role and bag count ("" when out of the round).
static func step_key(role: String, bags: int, bag_cap: int, is_out: bool) -> String:
	if is_out:
		return ""
	if role == "polluter":
		return "GUIDE_TRASHER_STEP"
	if bags <= 0:
		return "GUIDE_CLEAN"
	if bag_cap > 0 and bags >= bag_cap:
		return "GUIDE_BAG_FULL"
	return "GUIDE_DELIVER"


## True when the gold arrow should show: a Cleaner with bags, not already at the machine.
static func wants_arrow(role: String, bags: int, is_out: bool, to_machine: Vector2) -> bool:
	return not is_out and role != "polluter" and bags > 0 and to_machine.length() > NEAR_PX


## to_machine: world vector from the player to the machine (ZERO when unknown).
func update_state(role: String, bags: int, bag_cap: int, is_out: bool, playing: bool, to_machine: Vector2) -> void:
	var step: String = step_key(role, bags, bag_cap, is_out)
	_card.visible = playing and not step.is_empty()
	if _card.visible:
		var main_key: String = "GUIDE_MAIN_TRASHER" if role == "polluter" else "GUIDE_MAIN_CLEANER"
		_text.text = "[b]%s[/b]\n%s" % [tr(main_key), tr(step)]
	_arrow_dir = to_machine.normalized() if playing and wants_arrow(role, bags, is_out, to_machine) else Vector2.ZERO
	queue_redraw()


func _process(delta: float) -> void:
	_t += delta
	if _arrow_dir != Vector2.ZERO:
		queue_redraw()


func _draw() -> void:
	if _arrow_dir == Vector2.ZERO:
		return
	var center: Vector2 = get_viewport_rect().size / 2.0
	var pulse: float = 1.0 + 0.12 * sin(_t * TAU * 1.5)
	var tip: Vector2 = center + _arrow_dir * (ARROW_RADIUS_PX + ARROW_LEN_PX) * pulse
	var base: Vector2 = center + _arrow_dir * ARROW_RADIUS_PX * pulse
	var side: Vector2 = _arrow_dir.orthogonal() * ARROW_HALF_WIDTH_PX
	var tail: Vector2 = base - _arrow_dir * SHAFT_LEN_PX
	draw_line(tail, base, GOLD_EDGE, SHAFT_WIDTH_PX + 3.0)
	draw_line(tail, base, GOLD, SHAFT_WIDTH_PX)
	var pts: PackedVector2Array = PackedVector2Array([tip, base + side, base - side])
	draw_colored_polygon(pts, GOLD)
	pts.append(tip)
	draw_polyline(pts, GOLD_EDGE, 2.0, true)
