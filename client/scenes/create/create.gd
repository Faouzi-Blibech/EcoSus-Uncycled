extends "res://scenes/entry_page.gd"
## Create a room (Figma "Create Game"): pick the map, then Create. The map art carries its own tick
## and locks. Only TBS is playable: the server has one map and no map choice yet, so the other cards
## say "coming soon" when tapped. No player count: seats are locked at 8 (team decision, 21 Nov).

const MAP_DIR: String = "res://assets/ui/maps/"
## Rects are the Figma frame at 1/3 scale.
const MAPS: Array[Dictionary] = [
	{"id": "tbs", "name_key": "MAP_TBS", "art": "tbs.png", "rect": Rect2(154.0, 91.0, 88.0, 79.0), "playable": true},
	{"id": "insat", "name_key": "MAP_INSAT", "art": "insat.png", "rect": Rect2(252.0, 86.0, 93.0, 84.0), "playable": false},
	{"id": "sidi_bou_said", "name_key": "MAP_SIDI_BOU_SAID", "art": "sidi_bou_said.png", "rect": Rect2(357.0, 91.0, 94.0, 79.0), "playable": false},
	{"id": "soon", "name_key": "MAP_SOON", "art": "coming_soon.png", "rect": Rect2(463.0, 88.0, 96.0, 80.0), "playable": false},
]

const PAPER_RECT: Rect2 = Rect2(17.0, 40.0, 611.0, 335.0)
const MAP_LABEL_RECT: Rect2 = Rect2(60.0, 108.0, 90.0, 40.0)
const BIG_FONT_SIZE: int = 34
const NAME_TOP: float = 172.0
const NAME_FONT_SIZE: int = 22
const CREATE_RECT: Rect2 = Rect2(254.0, 240.0, 132.0, 44.0)
const STATUS_RECT: Rect2 = Rect2(90.0, 320.0, 460.0, 22.0)

var _create_btn: Button


func _build(stage: Control) -> void:
	_add_paper(stage, PAPER_RECT)

	var map_label: Label = PaperKit.label(tr("CREATE_MAP"), BIG_FONT_SIZE)
	map_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	# A longer word (Arabic) widens the label to the left, onto the paper, not under the first card.
	map_label.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	PaperKit.place(map_label, MAP_LABEL_RECT)
	stage.add_child(map_label)
	for m in MAPS:
		_add_card(stage, m)

	_create_btn = PaperKit.button(tr("CREATE_BUTTON"), "green")
	PaperKit.place_button(_create_btn, CREATE_RECT)
	_create_btn.pressed.connect(_on_create)
	stage.add_child(_create_btn)

	PaperKit.place(_status, STATUS_RECT)
	stage.add_child(_status)


func _add_card(stage: Control, m: Dictionary) -> void:
	var rect: Rect2 = m["rect"]
	var art: Texture2D = Assets.texture(MAP_DIR + str(m["art"]))
	var btn: TextureButton = TextureButton.new()
	btn.texture_normal = art
	btn.ignore_texture_size = true
	btn.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	PaperKit.place(btn, rect)
	btn.pressed.connect(_on_card.bind(bool(m["playable"])))
	stage.add_child(btn)
	PaperKit.add_shadow(btn, art, 1.5)
	PaperKit.add_press_squash(btn)

	var name_label: Label = PaperKit.label(tr(str(m["name_key"])), NAME_FONT_SIZE)
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	PaperKit.place(name_label, Rect2(rect.position.x - 12.0, NAME_TOP, rect.size.x + 24.0, 28.0))
	stage.add_child(name_label)


func _on_card(playable: bool) -> void:
	_status.text = "" if playable else tr("MAP_LOCKED")


func _on_create() -> void:
	_send("create", {})


func _apply_busy(busy_now: bool) -> void:
	_create_btn.disabled = busy_now
