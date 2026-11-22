extends HBoxContainer
## The skin shop on the home page's Profile: one kit card per skin (face in the card's circle,
## name, rarity) with Wear / Wearing, or the streak still needed when locked. Wearing a skin saves
## it as the profile's skin (the lobby puts it on).

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const CharacterSprite := preload("res://ui/widgets/character_sprite.gd")
const Characters := preload("res://ui/widgets/characters.gd")
const Skins := preload("res://content/skins.gd")

const CARD_SIZE: Vector2 = Vector2(100.0, 142.0)
## The portrait circle printed on card_avatar.png, as fractions of the card.
const CIRCLE_CENTRE: Vector2 = Vector2(0.5, 0.29)
const CIRCLE_WIDTH: float = 0.62
const NAME_TOP: float = 0.5
const BUTTON_RECT: Rect2 = Rect2(0.1, 0.74, 0.8, 0.17)
const LOCK_PX: float = 26.0
## The starter (tinted) character is shown in the default seat colour.
const STARTER_COLOUR: Color = Color(0.86, 0.3, 0.26)
const LOCKED_TINT: Color = Color(0.25, 0.22, 0.2)
const RARITY_INK: Dictionary = {
	"starter": Color(0.42, 0.36, 0.3), "common": Color(0.33, 0.49, 0.27),
	"uncommon": Color(0.24, 0.42, 0.6), "rare": Color(0.55, 0.35, 0.62),
}


func _init() -> void:
	add_theme_constant_override("separation", 8)
	alignment = BoxContainer.ALIGNMENT_CENTER


func _ready() -> void:
	rebuild()


func rebuild() -> void:
	for c in get_children():
		c.queue_free()
	for s in Skins.all():
		add_child(_card(s))


func _card(s: Dictionary) -> Control:
	var id: String = str(s["id"])
	var state: String = Skins.status(id, Profile.skins, Profile.skin)
	var card: Control = Control.new()
	card.custom_minimum_size = CARD_SIZE
	card.add_child(PaperKit.picture(PaperKit.tex("card_avatar.png"), Rect2(Vector2.ZERO, CARD_SIZE)))

	var d: float = CARD_SIZE.x * CIRCLE_WIDTH
	var centre: Vector2 = CARD_SIZE * CIRCLE_CENTRE
	var face: Control = CharacterSprite.avatar(id, STARTER_COLOUR, Rect2(centre - Vector2(d, d) * 0.5, Vector2(d, d)))
	if state == "locked":
		face.modulate = LOCKED_TINT
	card.add_child(face)
	if state == "locked":
		card.add_child(PaperKit.picture(PaperKit.tex("icon_lock.png"), Rect2(centre - Vector2(LOCK_PX, LOCK_PX) * 0.5, Vector2(LOCK_PX, LOCK_PX))))

	var info: VBoxContainer = UIKit.make_vbox(-4)
	info.position = Vector2(0.0, CARD_SIZE.y * NAME_TOP)
	info.size = Vector2(CARD_SIZE.x, CARD_SIZE.y * (BUTTON_RECT.position.y - NAME_TOP))
	card.add_child(info)
	var name_label: Label = UIKit.make_label(tr(str(Characters.info(id).get("name_key", ""))), 15)
	var rarity: Label = UIKit.make_label(tr("RARITY_" + str(s["rarity"]).to_upper()), 11, RARITY_INK.get(str(s["rarity"]), PaperKit.SOFT_INK))
	for l in [name_label, rarity]:
		(l as Label).horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		info.add_child(l)

	var b: Button = UIKit.make_button(_button_text(state, int(s["streak"])), 0.0, "green" if state == "wearing" else "cream")
	b.custom_minimum_size = Vector2.ZERO
	b.add_theme_font_size_override("font_size", 12)
	b.position = BUTTON_RECT.position * CARD_SIZE
	b.size = BUTTON_RECT.size * CARD_SIZE
	b.disabled = state == "locked"
	if state == "owned":
		b.pressed.connect(_wear.bind(id))
	card.add_child(b)
	return card


func _button_text(state: String, streak_needed: int) -> String:
	match state:
		"wearing":
			return tr("SHOP_WEARING")
		"owned":
			return tr("SHOP_WEAR")
	return tr("SHOP_STREAK") % [mini(Profile.streak, streak_needed), streak_needed]


func _wear(id: String) -> void:
	Profile.choose_skin("" if id == Skins.STARTER["id"] else id)
	rebuild()
