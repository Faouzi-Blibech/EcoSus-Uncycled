extends Control
## "Choose your character" overlay. The sprout is the default and takes the seat colour; the other
## characters are locked for now (they unlock with the scan streak). Emits colour_picked when a free
## colour is tapped and closed when dismissed. The owner calls refresh() on every lobby change.

signal colour_picked(index: int)
signal closed

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const Characters := preload("res://ui/widgets/characters.gd")
const CharacterSprite := preload("res://ui/widgets/character_sprite.gd")

const COLOUR_COUNT: int = 8
const DIM: Color = Color(0.0, 0.0, 0.0, 0.45)
const PANEL_RECT: Rect2 = Rect2(50.0, 14.0, 540.0, 332.0)
const TITLE_RECT: Rect2 = Rect2(50.0, 26.0, 540.0, 40.0)
const TITLE_FONT_SIZE: int = 30
const CLOSE_POS: Vector2 = Vector2(540.0, 16.0)
const CARD_SIZE: Vector2 = Vector2(92.0, 131.0)
const CARD_TOP: float = 70.0
const CARD_GAP: float = 18.0
## The round photo spot of card_avatar.png (centre 149,130, inner radius 88 of 300 px) at card size.
const CARD_AVATAR: Rect2 = Rect2(19.0, 13.0, 54.0, 54.0)
const NAME_FONT_SIZE: int = 18
const BADGE_SIZE: float = 30.0
const LOCKED_SPRITE: Color = Color(0.3, 0.27, 0.24, 0.9)
const SWATCH_SIZE: float = 44.0
const SWATCH_GAP: float = 4.0
const SWATCH_TOP: float = 220.0
const HINT_RECT: Rect2 = Rect2(80.0, 266.0, 480.0, 26.0)
const HINT_FONT_SIZE: int = 12
const CONFIRM_RECT: Rect2 = Rect2(254.0, 294.0, 132.0, 44.0)
const TAKEN_ALPHA: float = 0.3

var _swatches: Array[Button] = []
var _sprout: TextureRect = null
var _hint: Label


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	UIKit.fill(self)
	UIKit.background(self, DIM)
	var stage: Control = PaperKit.stage(self)
	stage.add_child(PaperKit.panel("panel_paper.png", PANEL_RECT))

	var title: Label = PaperKit.label(tr("CHAR_TITLE"), TITLE_FONT_SIZE)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	PaperKit.place(title, TITLE_RECT)
	stage.add_child(title)

	var close_btn: TextureButton = PaperKit.icon_button(stage, PaperKit.KIT_DIR + "icon_close.png", tr("COMMON_CLOSE"))
	close_btn.position = CLOSE_POS
	close_btn.pressed.connect(_close)

	var n: int = Characters.LIST.size()
	var x: float = (PaperKit.STAGE_SIZE.x - (CARD_SIZE.x * n + CARD_GAP * (n - 1))) / 2.0
	for c in Characters.LIST:
		_add_card(stage, c, x)
		x += CARD_SIZE.x + CARD_GAP

	var row_w: float = SWATCH_SIZE * COLOUR_COUNT + SWATCH_GAP * (COLOUR_COUNT - 1)
	var sx: float = (PaperKit.STAGE_SIZE.x - row_w) / 2.0
	for i in range(COLOUR_COUNT):
		var sw: Button = Button.new()
		PaperKit.place(sw, Rect2(sx + i * (SWATCH_SIZE + SWATCH_GAP), SWATCH_TOP, SWATCH_SIZE, SWATCH_SIZE))
		sw.pressed.connect(func() -> void: colour_picked.emit(i))
		stage.add_child(sw)
		_swatches.append(sw)

	_hint = PaperKit.label(tr("CHAR_UNLOCK_HINT"), HINT_FONT_SIZE, PaperKit.SOFT_INK, false)
	_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hint.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	PaperKit.place(_hint, HINT_RECT)
	stage.add_child(_hint)

	var confirm: Button = PaperKit.button(tr("COMMON_CONFIRM"), "green")
	PaperKit.place_button(confirm, CONFIRM_RECT)
	confirm.pressed.connect(_close)
	stage.add_child(confirm)


func _add_card(stage: Control, c: Dictionary, x: float) -> void:
	var locked: bool = bool(c["locked"])
	var card: TextureRect = PaperKit.picture(PaperKit.tex("card_avatar.png"), Rect2(Vector2(x, CARD_TOP), CARD_SIZE))
	stage.add_child(card)
	PaperKit.add_shadow(card, card.texture)

	var avatar: Control = CharacterSprite.avatar(str(c["id"]), Color.WHITE, CARD_AVATAR)
	card.add_child(avatar)
	if locked:
		avatar.modulate = LOCKED_SPRITE
	if str(c["id"]) == Characters.DEFAULT_ID:
		_sprout = avatar.get_child(0) as TextureRect

	var badge_file: String = "icon_lock.png" if locked else "icon_confirm.png"
	card.add_child(PaperKit.picture(PaperKit.tex(badge_file), Rect2(CARD_SIZE.x - BADGE_SIZE + 6.0, -8.0, BADGE_SIZE, BADGE_SIZE)))

	var name_label: Label = PaperKit.label(tr(str(c["name_key"])), NAME_FONT_SIZE)
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	PaperKit.place(name_label, Rect2(0.0, CARD_SIZE.y - 28.0, CARD_SIZE.x, 24.0))
	card.add_child(name_label)


## my_colour: the seat's colour index; taken: colours other seats use (the server refuses them).
func refresh(my_colour: int, taken: Array[int]) -> void:
	if _sprout != null:
		_sprout.call("set_colour", Session.color_of(my_colour))
	for i in range(_swatches.size()):
		var sw: Button = _swatches[i]
		var is_taken: bool = taken.has(i)
		sw.disabled = is_taken
		sw.modulate = Color(1.0, 1.0, 1.0, TAKEN_ALPHA) if is_taken else Color.WHITE
		var box: StyleBoxFlat = StyleBoxFlat.new()
		box.bg_color = Session.color_of(i)
		box.set_corner_radius_all(int(SWATCH_SIZE / 2.0))
		box.border_color = PaperKit.INK
		box.set_border_width_all(4 if i == my_colour else 2)
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			sw.add_theme_stylebox_override(state, box)


func _close() -> void:
	closed.emit()
