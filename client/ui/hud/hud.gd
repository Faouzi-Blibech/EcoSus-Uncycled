class_name Hud
extends CanvasLayer
## In-round HUD: task list, cleanliness meter, match timer, bags carried, the action buttons with
## cooldown rings, the sabotage banner, the role reveal card, toasts and the spectator overlay. It only shows
## what Session holds; actions go through the bound round (RoundScene.perform_action).

## The out player asked to leave the round (button on the spectator overlay).
signal leave_requested

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const CharacterSprite := preload("res://ui/widgets/character_sprite.gd")
const Characters := preload("res://ui/widgets/characters.gd")
const RoleRevealScene: PackedScene = preload("res://ui/hud/role_reveal.tscn")
const SpectatorScene: PackedScene = preload("res://ui/hud/spectator_overlay.tscn")
const GhostChatPanel := preload("res://ui/hud/ghost_chat_panel.gd")

const MARGIN_PX: float = 8.0
const TOAST_BOTTOM_OFFSET_PX: float = -84.0
const SMALL_FONT: int = 11
const DONE_COLOR: Color = Color(0.55, 0.48, 0.42)
## Storybook HUD look (Screens reference): ink text on cream paper, Caroni font.
const INK: Color = Color("3e2b25")
const PAPER: Color = Color("faf2dd")
const PAPER_SHADOW: Color = Color(0.24, 0.17, 0.12, 0.35)
const LEAF: Color = Color("6f9a52")
const CORAL: Color = Color("c8553f")
const CARD_WIDTH_PX: float = 150.0
const AVATAR_PX: float = 34.0
const NOTE_WIDTH_PX: float = 132.0
const BAR_WIDTH_PX: float = 170.0
const CHECKBOX_PX: float = 11.0
const STOPWATCH_PX: float = 16.0
## The round mini map sits under the player card.
const MINIMAP_TOP_PX: float = 66.0
const CHECKBOX_DONE: String = "res://assets/ui/kit/checkbox_checked.png"
const CHECKBOX_OPEN: String = "res://assets/ui/kit/checkbox_empty.png"
const STOPWATCH: String = "res://assets/ui/hud/icon_stopwatch.png"
## Below the pollution bar: the sabotage banner, then the virus countdown.
const BANNER_TOP_PX: float = 58.0
const VIRUS_TIMER_TOP_PX: float = 86.0
## The ping line appears only above this (a good ping is a debug readout, not player information).
const WEAK_PING_MS: int = 250
## Grid slot order (REPORT USE / DUMP HIT / SABOTAGE); each button keeps its slot while hidden.
## FIX shares the USE slot and CLEAN the DUMP slot (the rules never show both), see _build_buttons.
## v2: the Trasher row is RE-POLLUTE and the unlocked ability (virus or gas) where v1 had HIT and DUMP;
## they keep v1's keys (act_hit, act_dump).
const ACTIONS: Array[String] = ["report", "use", "ability", "repollute", "sabotage"]
const ACTION_INPUT: Dictionary = {
	"use": "act_use", "report": "act_report", "repollute": "act_hit", "ability": "act_dump", "sabotage": "act_sabotage",
}
const ACTION_LABEL: Dictionary = {
	"use": "ACT_USE", "report": "ACT_REPORT", "repollute": "ACT_REPOLLUTE", "ability": "ACT_VIRUS", "sabotage": "ACT_SABOTAGE",
}
const SABOTAGE_KINDS: Array[String] = ["machine", "smog"]
## The Machine / Smog sabotage choices are basic-size buttons.
const CHOICE_SIZE: Vector2 = ActionButton.BASIC_SIZE
## Basic actions (every player) get the smaller button; the role abilities keep the big one. FIX
## shares the USE slot, so it is basic too; CLEAN, HEAL and ALERT share special slots.
const BASIC_ACTIONS: Array[String] = ["report", "use"]
## The painted button of each grid action (assets/ui/hud/btn_<art>.png); the ability slot follows
## the unlocked ability (virus or gas).
const ACTION_ART: Dictionary = {
	"use": "use", "report": "report", "repollute": "repollute", "ability": "virus", "sabotage": "sabotage",
}
## What the USE button looks like for each ActionRules use_look kind: [art, label key]. ACT_RECYCLE
## takes the bag count. The meeting has its own button (btn_meeting).
const USE_LOOK: Dictionary = {
	"use": ["use", "ACT_USE"], "clean": ["clean", "ACT_CLEAN"], "recycle": ["recycle", "ACT_RECYCLE"],
	"meeting": ["meeting", "ACT_MEETING"],
}
const BANNER_WIDTH_PX: float = 220.0
## Width of the button grid (two 64 px slots and the gap), used to put the choices beside it.
const GRID_WIDTH_PX: float = 134.0
const PICK_BUTTON_WIDTH_PX: float = 150.0
## REPORT pulses while a body is in reach (scale and red tint, 0..1 by a sine).
const REPORT_PULSE_SCALE: float = 0.1
const REPORT_PULSE_TINT: Color = Color(1.0, 0.45, 0.4)

var _round: RoundScene = null
var _root: Control
var _role_label: Label
var _partner_label: Label
var _task_rows: VBoxContainer
var _tasks_header: Label
var _timer_label: Label
var _meter_label: Label
var _meter_bar: PollutionBar
var _bags_label: Label
## Upgrades owned (backpack, tool, heal, mask), Cleaners only.
var _items_label: Label
var _picker: UpgradePicker
var _ability_picker: AbilityPicker
## The player closed an open offer: it stays closed until the "Pick" button or a new offer.
var _upgrade_later: bool = false
var _ability_later: bool = false
## Reopens a closed upgrade or ability offer (bottom centre).
var _pick_button: Button
var _room_picker: RoomPicker
## The kind the ability button is labelled with ("virus" or "gas"), "" until known.
var _ability_kind: String = ""
## The infected player's countdown, and the HEAL / ALERT buttons a Cleaner gets in the Trasher slots.
var _virus_timer: VirusTimer
var _heal_button: ActionButton
var _alert_button: ActionButton
## DASH: every role, left of the action grid.
var _dash_button: ActionButton
var _ping_label: Label
var _buttons: Dictionary = {}
var _fix_button: ActionButton
var _sabotage_slot: Control
var _sabotage_spare: Control
var _clean_button: ActionButton
var _choices: HBoxContainer
var _banner: SabotageBanner
var _states: Dictionary = {}
## The use_look the USE button shows now ({kind, count}), so it is only redrawn on a change.
var _use_look: Dictionary = {}
var _fire_total: int = -1
var _toast: Toast
var _reveal: RoleReveal
var _reveal_shown: bool = false
var _spectator: SpectatorOverlay
var _shown_cause: String = ""
var _tasks_signature: String = ""
var _name_label: Label
var _avatar_slot: Control
var _avatar_key: String = ""
var _note: PanelContainer
var _minimap: Minimap
var _arrows: MissionArrows
var _minimap_bound: bool = false


func _ready() -> void:
	_build()
	Net.action_rejected.connect(_on_action_rejected)
	Net.sabotage_ended.connect(_on_sabotage_ended)
	Net.event.connect(_on_event)


func bind(round: Node) -> void:
	_round = round as RoundScene


func _build() -> void:
	_root = Control.new()
	var th: Theme = UIKit.theme().duplicate() as Theme
	th.default_font = PaperKit.font()
	_root.theme = th
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)
	UIKit.fill(_root)
	_apply_safe_area()
	# Mission arrows sit under every other HUD widget.
	_arrows = MissionArrows.new()
	_root.add_child(_arrows)
	UIKit.fill(_arrows)

	_spectator = SpectatorScene.instantiate() as SpectatorOverlay
	_spectator.visible = false
	_spectator.leave_pressed.connect(func() -> void: leave_requested.emit())
	_root.add_child(_spectator)

	_build_top_left()
	_build_top_center()
	_build_top_right()
	_build_banner()
	_build_buttons()
	_build_choices()

	_toast = Toast.new()
	_toast.anchor_left = 0.5
	_toast.anchor_right = 0.5
	_toast.anchor_top = 1.0
	_toast.anchor_bottom = 1.0
	_toast.offset_top = TOAST_BOTTOM_OFFSET_PX
	_toast.offset_bottom = TOAST_BOTTOM_OFFSET_PX
	_toast.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_toast.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_root.add_child(_toast)
	# The ghost chat of an out player, above the other HUD parts (it docks at the top while typing on a
	# phone); it hides itself while the local player is in.
	_root.add_child(GhostChatPanel.new())

	# The upgrade cards after a delivery, under the role reveal; hidden until upgrade_offer.
	_virus_timer = VirusTimer.new()
	_virus_timer.anchor_left = 0.5
	_virus_timer.anchor_right = 0.5
	_virus_timer.offset_top = VIRUS_TIMER_TOP_PX
	_virus_timer.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_root.add_child(_virus_timer)
	_virus_timer.show_seconds(-1.0)

	_picker = UpgradePicker.new()
	_picker.picked.connect(_on_upgrade_picked)
	_picker.dismissed.connect(func() -> void: _upgrade_later = true)
	_root.add_child(_picker)
	# The Trasher's ability choice and the gas room choice, same card picker.
	_ability_picker = AbilityPicker.new()
	_ability_picker.picked.connect(_on_ability_picked)
	_ability_picker.dismissed.connect(func() -> void: _ability_later = true)
	_root.add_child(_ability_picker)
	_pick_button = UIKit.make_button("", PICK_BUTTON_WIDTH_PX)
	_pick_button.visible = false
	_pick_button.anchor_left = 0.5
	_pick_button.anchor_right = 0.5
	_pick_button.anchor_top = 1.0
	_pick_button.anchor_bottom = 1.0
	_pick_button.offset_left = -PICK_BUTTON_WIDTH_PX * 0.5
	_pick_button.offset_right = PICK_BUTTON_WIDTH_PX * 0.5
	_pick_button.offset_bottom = -MARGIN_PX
	_pick_button.offset_top = -MARGIN_PX - UIKit.MIN_TOUCH
	_pick_button.pressed.connect(_on_pick_pressed)
	_root.add_child(_pick_button)
	_room_picker = RoomPicker.new()
	_room_picker.picked.connect(_on_room_picked)
	_root.add_child(_room_picker)

	_reveal = RoleRevealScene.instantiate() as RoleReveal
	_reveal.visible = false
	_root.add_child(_reveal)


## On phones the HUD keeps clear of the camera cut-out (the Android build is full screen).
func _apply_safe_area() -> void:
	if not OS.has_feature("mobile"):
		return
	var insets: Vector4 = safe_insets(
		DisplayServer.get_display_safe_area(), DisplayServer.screen_get_size(),
		DisplayServer.window_get_size(), get_viewport().get_visible_rect().size)
	_root.offset_left = insets.x
	_root.offset_top = insets.y
	_root.offset_right = -insets.z
	_root.offset_bottom = -insets.w


func _build_top_left() -> void:
	# The player card: round portrait, name, role tag, the partner for a Trasher.
	var card: PanelContainer = _paper_panel()
	card.position = Vector2(MARGIN_PX, MARGIN_PX)
	card.custom_minimum_size = Vector2(CARD_WIDTH_PX, 0.0)
	_root.add_child(card)
	var row: HBoxContainer = HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card.add_child(row)
	_avatar_slot = Control.new()
	_avatar_slot.custom_minimum_size = Vector2(AVATAR_PX, AVATAR_PX)
	_avatar_slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(_avatar_slot)
	var col: VBoxContainer = UIKit.make_vbox(0)
	col.mouse_filter = Control.MOUSE_FILTER_IGNORE
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(col)
	_name_label = _ink_label(14)
	_name_label.clip_text = true
	_role_label = _ink_label(SMALL_FONT)
	_role_label.add_theme_color_override("font_color", PAPER)
	_role_label.add_theme_stylebox_override("normal", _pill(LEAF))
	_role_label.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	_partner_label = _ink_label(SMALL_FONT - 1)
	for n in [_name_label, _role_label, _partner_label]:
		col.add_child(n)
	_minimap = Minimap.new()
	_minimap.position = Vector2(MARGIN_PX + 4.0, MINIMAP_TOP_PX)
	_root.add_child(_minimap)


## The pollution bar under its paper tab, top centre.
func _build_top_center() -> void:
	var box: VBoxContainer = UIKit.make_vbox(-3, BAR_WIDTH_PX)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.anchor_left = 0.5
	box.anchor_right = 0.5
	box.offset_left = -BAR_WIDTH_PX * 0.5
	box.offset_right = BAR_WIDTH_PX * 0.5
	box.offset_top = MARGIN_PX - 4.0
	_root.add_child(box)
	_meter_label = _ink_label(SMALL_FONT + 1)
	_meter_label.add_theme_stylebox_override("normal", _pill(PAPER, true))
	_meter_label.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_meter_label.z_index = 1
	_meter_bar = PollutionBar.new()
	box.add_child(_meter_label)
	box.add_child(_meter_bar)


## The paper note, top right: tasks (when the mode has them), bags, items and the match clock.
func _build_top_right() -> void:
	_note = _paper_panel()
	_note.anchor_left = 1.0
	_note.anchor_right = 1.0
	_note.offset_left = -MARGIN_PX - NOTE_WIDTH_PX
	_note.offset_right = -MARGIN_PX
	_note.offset_top = MARGIN_PX
	_note.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_root.add_child(_note)
	var box: VBoxContainer = UIKit.make_vbox(1)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_note.add_child(box)
	_tasks_header = _ink_label(15)
	_tasks_header.text = tr("HUD_TASKS")
	_tasks_header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_task_rows = UIKit.make_vbox(0)
	_task_rows.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_bags_label = _ink_label(SMALL_FONT + 1)
	_items_label = _ink_label(SMALL_FONT)
	_items_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var clock: HBoxContainer = HBoxContainer.new()
	clock.alignment = BoxContainer.ALIGNMENT_CENTER
	clock.add_theme_constant_override("separation", 4)
	clock.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if ResourceLoader.exists(STOPWATCH):
		var icon: TextureRect = TextureRect.new()
		icon.texture = load(STOPWATCH) as Texture2D
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.custom_minimum_size = Vector2(STOPWATCH_PX, STOPWATCH_PX)
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		clock.add_child(icon)
	_timer_label = _ink_label(16)
	clock.add_child(_timer_label)
	for n in [_tasks_header, _task_rows, _bags_label, _items_label, clock]:
		box.add_child(n)
	_ping_label = UIKit.make_hud_label(SMALL_FONT)
	_ping_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_ping_label.anchor_left = 1.0
	_ping_label.anchor_right = 1.0
	_ping_label.offset_left = -MARGIN_PX - NOTE_WIDTH_PX
	_ping_label.offset_right = -MARGIN_PX
	_ping_label.offset_top = 96.0
	_root.add_child(_ping_label)


## Cream paper with an ink rim and a soft drop shadow.
static func _paper_panel() -> PanelContainer:
	var p: PanelContainer = PanelContainer.new()
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = PAPER
	box.border_color = INK
	box.set_border_width_all(2)
	box.set_corner_radius_all(9)
	box.shadow_color = PAPER_SHADOW
	box.shadow_size = 3
	box.shadow_offset = Vector2(1.0, 2.0)
	box.content_margin_left = 7.0
	box.content_margin_right = 7.0
	box.content_margin_top = 5.0
	box.content_margin_bottom = 5.0
	box.anti_aliasing = true
	p.add_theme_stylebox_override("panel", box)
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return p


## A rounded tag behind a label (role tag, the "Pollution" tab).
static func _pill(color: Color, rim: bool = false) -> StyleBoxFlat:
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(8)
	if rim:
		box.border_color = INK
		box.set_border_width_all(2)
	box.content_margin_left = 7.0
	box.content_margin_right = 7.0
	box.content_margin_top = 0.0
	box.content_margin_bottom = 1.0
	box.anti_aliasing = true
	return box


## Ink text for the paper panels (the theme gives the Caroni font).
static func _ink_label(font_size: int) -> Label:
	var l: Label = Label.new()
	l.add_theme_font_size_override("font_size", font_size)
	l.add_theme_color_override("font_color", INK)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


func _build_banner() -> void:
	_banner = SabotageBanner.new()
	_banner.anchor_left = 0.5
	_banner.anchor_right = 0.5
	_banner.offset_top = BANNER_TOP_PX
	_banner.custom_minimum_size = Vector2(BANNER_WIDTH_PX, 0.0)
	_banner.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_banner.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_root.add_child(_banner)


func _build_buttons() -> void:
	var grid: GridContainer = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 6)
	grid.add_theme_constant_override("v_separation", 6)
	grid.mouse_filter = Control.MOUSE_FILTER_IGNORE
	grid.anchor_left = 1.0
	grid.anchor_right = 1.0
	grid.anchor_top = 1.0
	grid.anchor_bottom = 1.0
	grid.offset_left = -MARGIN_PX
	grid.offset_right = -MARGIN_PX
	grid.offset_top = -MARGIN_PX
	grid.offset_bottom = -MARGIN_PX
	grid.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	grid.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_root.add_child(grid)
	var slots: Dictionary = {}
	for action in ACTIONS:
		var slot: Control = Control.new()
		var button_size: Vector2 = button_size_for(action)
		slot.custom_minimum_size = button_size
		slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
		grid.add_child(slot)
		slots[action] = slot
		var b: ActionButton = ActionButton.new()
		b.custom_minimum_size = button_size
		b.text = action_text(tr(ACTION_LABEL[action]), _key_text(str(ACTION_INPUT[action])), DisplayServer.is_touchscreen_available())
		b.visible = false
		b.pressed.connect(_on_action_pressed.bind(action))
		b.set_art(str(ACTION_ART[action]))
		slot.add_child(b)
		UIKit.fill(b)
		_buttons[action] = b
	_fix_button = _make_hold_button(slots["use"] as Control, "ACT_FIX", "act_use")
	_fix_button.hold_started.connect(_on_hold_started.bind("fix"))
	_fix_button.hold_ended.connect(_on_hold_ended.bind("fix"))
	_clean_button = _make_hold_button(slots["ability"] as Control, "ACT_CLEAN", "act_clean")
	# A Cleaner never sees RE-POLLUTE or the ability, so HEAL and ALERT take those two slots and keys.
	_heal_button = _make_tap_button(slots["repollute"] as Control, "ACT_HEAL", "act_hit", "heal")
	_alert_button = _make_tap_button(slots["ability"] as Control, "ACT_ALERT", "act_dump", "alert")
	_fix_button.set_art("fix")
	_clean_button.set_art("clean")
	_heal_button.set_art("heal")
	_alert_button.set_art("alert")
	_clean_button.hold_started.connect(_on_hold_started.bind("clean"))
	_clean_button.hold_ended.connect(_on_hold_ended.bind("clean"))
	# An empty last slot keeps the grid two columns wide; both go when the role has no SABOTAGE.
	_sabotage_slot = slots["sabotage"] as Control
	_sabotage_spare = Control.new()
	_sabotage_spare.custom_minimum_size = ActionButton.BUTTON_SIZE
	_sabotage_spare.mouse_filter = Control.MOUSE_FILTER_IGNORE
	grid.add_child(_sabotage_spare)
	_dash_button = ActionButton.new()
	_dash_button.text = action_text(tr("ACT_DASH"), _key_text("act_dash"), DisplayServer.is_touchscreen_available())
	_dash_button.visible = false
	_dash_button.pressed.connect(_on_action_pressed.bind("dash"))
	_dash_button.set_art("dash")
	_dash_button.anchor_left = 1.0
	_dash_button.anchor_right = 1.0
	_dash_button.anchor_top = 1.0
	_dash_button.anchor_bottom = 1.0
	_dash_button.offset_right = -(MARGIN_PX + GRID_WIDTH_PX + 6.0)
	# Dash is basic: the small button, left of the grid, level with the row above the bottom one.
	_dash_button.custom_minimum_size = ActionButton.BASIC_SIZE
	_dash_button.offset_left = _dash_button.offset_right - ActionButton.BASIC_SIZE.x
	_dash_button.offset_bottom = -(MARGIN_PX + ActionButton.BUTTON_SIZE.y + 6.0)
	_dash_button.offset_top = _dash_button.offset_bottom - ActionButton.BASIC_SIZE.y
	_root.add_child(_dash_button)


## The button size of a grid action: basic (every player) or special (the role's ability).
static func button_size_for(action: String) -> Vector2:
	return ActionButton.BASIC_SIZE if BASIC_ACTIONS.has(action) else ActionButton.BUTTON_SIZE


## A tap button that fills `slot`, hidden until the rules show it; pressing it runs `action`.
func _make_tap_button(slot: Control, label_key: String, input_action: String, action: String) -> ActionButton:
	var b: ActionButton = ActionButton.new()
	b.custom_minimum_size = slot.custom_minimum_size
	b.text = action_text(tr(label_key), _key_text(input_action), DisplayServer.is_touchscreen_available())
	b.visible = false
	b.pressed.connect(_on_action_pressed.bind(action))
	slot.add_child(b)
	UIKit.fill(b)
	return b


## A hold button (see ActionButton.hold_mode) that fills `slot`, hidden until the rules show it.
func _make_hold_button(slot: Control, label_key: String, input_action: String) -> ActionButton:
	var b: ActionButton = ActionButton.new()
	b.custom_minimum_size = slot.custom_minimum_size
	b.hold_mode = true
	b.text = action_text(tr(label_key), _key_text(input_action), DisplayServer.is_touchscreen_available())
	b.visible = false
	slot.add_child(b)
	UIKit.fill(b)
	return b


## The Fire / Smog choices that open beside the SABOTAGE button.
func _build_choices() -> void:
	_choices = HBoxContainer.new()
	_choices.add_theme_constant_override("separation", 6)
	_choices.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_choices.anchor_left = 1.0
	_choices.anchor_right = 1.0
	_choices.anchor_top = 1.0
	_choices.anchor_bottom = 1.0
	_choices.offset_right = -(MARGIN_PX + GRID_WIDTH_PX + 6.0)
	_choices.offset_bottom = -MARGIN_PX - (ActionButton.BUTTON_SIZE.y - CHOICE_SIZE.y) * 0.5
	_choices.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_choices.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_choices.visible = false
	_root.add_child(_choices)
	for kind in SABOTAGE_KINDS:
		var b: ActionButton = ActionButton.new()
		b.text = tr("SABOTAGE_" + kind.to_upper())
		b.custom_minimum_size = CHOICE_SIZE
		b.set_art("sabotage_" + kind)
		b.pressed.connect(_on_choice_pressed.bind(kind))
		_choices.add_child(b)


## The button text: the label, plus the keyboard key only where there is a keyboard (not on phones).
static func action_text(label: String, key: String, touchscreen: bool) -> String:
	if touchscreen or key.is_empty():
		return label
	return "%s\n[%s]" % [label, key]


## Insets (left, top, right, bottom) in logical pixels that keep the HUD clear of a camera cut-out or
## rounded corners. `safe` is DisplayServer.get_display_safe_area() in screen pixels; an empty
## rect (unknown) gives no insets.
static func safe_insets(safe: Rect2i, screen_size: Vector2i, window_size: Vector2i, logical: Vector2) -> Vector4:
	if safe.size.x <= 0 or safe.size.y <= 0 or window_size.x <= 0 or window_size.y <= 0:
		return Vector4.ZERO
	var sx: float = logical.x / float(window_size.x)
	var sy: float = logical.y / float(window_size.y)
	return Vector4(
		maxf(float(safe.position.x), 0.0) * sx,
		maxf(float(safe.position.y), 0.0) * sy,
		maxf(float(screen_size.x - safe.end.x), 0.0) * sx,
		maxf(float(screen_size.y - safe.end.y), 0.0) * sy)


## The first keyboard key bound to an input action, as text ("E"); "" when it has none.
static func _key_text(input_action: String) -> String:
	for ev in InputMap.action_get_events(input_action):
		if ev is InputEventKey:
			return OS.get_keycode_string((ev as InputEventKey).physical_keycode)
	return ""


func _unhandled_input(event: InputEvent) -> void:
	# E holds the fire console (USE becomes HOLD) and taps the smog panel; releasing E lets go.
	if event.is_action_pressed("act_use") and bool(Net.as_dict(_states.get("fix")).get("visible", false)):
		_on_hold_started("fix")
		get_viewport().set_input_as_handled()
		return
	if event.is_action_released("act_use"):
		_on_hold_ended("fix")
	# C holds the cleaning of the nearest pile.
	if event.is_action_pressed("act_clean") and bool(_states.get("clean", false)):
		_on_hold_started("clean")
		get_viewport().set_input_as_handled()
		return
	if event.is_action_released("act_clean"):
		_on_hold_ended("clean")
	if event.is_action_pressed("act_dash"):
		_on_action_pressed("dash")
		get_viewport().set_input_as_handled()
		return
	for action in ACTIONS:
		if event.is_action_pressed(str(ACTION_INPUT[action])):
			_on_action_pressed(action)
			get_viewport().set_input_as_handled()
			return


func _on_action_pressed(action: String) -> void:
	# A Cleaner's keys of the Trasher slots run HEAL and ALERT.
	if Session.role != "polluter":
		if action == "repollute":
			action = "heal"
		elif action == "ability":
			action = "alert"
	# The gas asks which room first (the room picker sends it through the round).
	var ability: Dictionary = Net.as_dict(_states.get("ability"))
	if action == "ability" and str(ability.get("kind", "")) == "gas":
		if bool(ability.get("ready", false)) and _round != null:
			_room_picker.open(_round.gas_room_ids())
		return
	if action == "sabotage":
		_choices.visible = not _choices.visible and bool(Net.as_dict(_states.get("sabotage")).get("ready", false))
		return
	if _round != null:
		_round.perform_action(action)


func _on_choice_pressed(kind: String) -> void:
	_choices.visible = false
	if _round != null:
		_round.perform_action("sabotage_" + kind)


func _on_hold_started(action: String) -> void:
	if _round != null:
		_round.begin_hold(action)


func _on_hold_ended(action: String) -> void:
	if _round != null:
		_round.end_hold(action)


func _process(_delta: float) -> void:
	_bind_minimap()
	_arrows.update_from(_round)
	_update_header()
	_update_tasks()
	_update_buttons()
	_update_pickers()
	_virus_timer.show_seconds(Session.infected_s)
	_update_banner()
	_update_reveal()
	_update_spectator()


func _update_header() -> void:
	_role_label.visible = not Session.role.is_empty()
	if _role_label.visible:
		_role_label.text = tr("ROLE_" + Session.role.to_upper())
		_role_label.add_theme_stylebox_override("normal", _pill(CORAL if Session.role == "polluter" else LEAF))
	if Session.partner_seat >= 0:
		_partner_label.text = tr("HUD_PARTNER") % Session.seat_name(Session.partner_seat)
	else:
		_partner_label.text = ""
	_partner_label.visible = not _partner_label.text.is_empty()
	_name_label.text = Session.seat_name(Session.seat_id) if Session.seat_id >= 0 else ""
	_update_avatar()
	# The meter counts cleanliness; the bar and its label show what is left: the pollution.
	var pollution: float = clampf(100.0 - Session.meter, 0.0, 100.0)
	_meter_label.text = tr("HUD_POLLUTION") % pollution
	_meter_bar.value = pollution
	var left: int = maxi(ceili(Session.deadline_t - Session.clock_t), 0) if Session.deadline_t > 0.0 else 0
	_timer_label.text = tr("HUD_TIMER") % [floori(left / 60.0), left % 60] if Session.deadline_t > 0.0 else ""
	# Only a Cleaner has bags (cap > 0); the server never sends a Trasher's.
	_bags_label.visible = Session.bag_cap > 0
	_bags_label.text = tr("HUD_BAGS") % [Session.bags, Session.bag_cap] if Session.bag_cap > 0 else ""
	var items: String = owned_items_text(Session.bag_cap, Session.has_tool, Session.heals, Session.masks)
	_items_label.visible = not items.is_empty()
	_items_label.text = items
	# v2 has no per-player task list (spots replace it): no empty "Tasks" header.
	_tasks_header.visible = not Session.my_tasks.is_empty()
	_ping_label.text = tr("HUD_PING") if Net.ping_ms > WEAK_PING_MS else ""


## One label per task: "Name 1/2 (Zone)", greyed when done. Rebuilt only when the data changes.
## The mini map follows the local player once the round has its map.
func _bind_minimap() -> void:
	if _minimap_bound or _round == null or _round.map() == null:
		return
	_minimap_bound = true
	_minimap.player_position = _round.local_position_px
	_minimap.bind_map(_round.map())


## The card's round portrait: the seat's character (its lobby skin) in the seat colour.
func _update_avatar() -> void:
	var skin: String = str(Session.seat_info(Session.seat_id).get("skin", "")) if Session.seat_id >= 0 else ""
	var id: String = skin if Characters.has(skin) else Characters.DEFAULT_ID
	var colour: Color = Session.seat_color(Session.seat_id) if Session.seat_id >= 0 else Color.WHITE
	var key: String = "%s/%s" % [id, colour.to_html()]
	if key == _avatar_key:
		return
	_avatar_key = key
	for c in _avatar_slot.get_children():
		_avatar_slot.remove_child(c)
		c.queue_free()
	_avatar_slot.add_child(CharacterSprite.avatar(id, colour, Rect2(Vector2.ZERO, Vector2(AVATAR_PX, AVATAR_PX))))


func _update_tasks() -> void:
	var signature: String = str(Session.my_tasks)
	if signature == _tasks_signature:
		return
	_tasks_signature = signature
	_tasks_header.visible = not Session.my_tasks.is_empty()
	for child in _task_rows.get_children():
		_task_rows.remove_child(child)
		child.queue_free()
	for entry in Session.my_tasks:
		var t: Dictionary = Net.as_dict(entry)
		var done: bool = int(t.get("steps_done", 0)) >= int(t.get("steps", 1))
		var row: HBoxContainer = HBoxContainer.new()
		row.add_theme_constant_override("separation", 3)
		row.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var box: TextureRect = TextureRect.new()
		box.texture = Assets.texture(CHECKBOX_DONE if done else CHECKBOX_OPEN)
		box.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		box.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		box.custom_minimum_size = Vector2(CHECKBOX_PX, CHECKBOX_PX)
		box.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var l: Label = _ink_label(SMALL_FONT)
		l.text = task_line(t, _round.map() if _round != null else null)
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		if done:
			l.add_theme_color_override("font_color", DONE_COLOR)
		row.add_child(box)
		row.add_child(l)
		_task_rows.add_child(row)


## "Name", "Name 1/2", with " (Zone)" when the console sits inside a zone of the map.
static func task_line(task: Dictionary, map: MapRoot) -> String:
	var line: String = TranslationServer.translate(StringName("TASK_" + str(task.get("type", "")).to_upper()))
	var steps: int = int(task.get("steps", 1))
	if steps > 1:
		line += " " + TranslationServer.translate(&"HUD_TASK_STEPS") % [int(task.get("steps_done", 0)), steps]
	if map != null:
		var m: MapMarker = map.marker(str(task.get("console", "")))
		var zone: MapMarker = map.zone_at(m.position) if m != null else null
		if zone != null and not zone.name_key.is_empty():
			line += " " + TranslationServer.translate(&"HUD_TASK_ZONE") % TranslationServer.translate(StringName(zone.name_key.to_upper()))
	return line


func _update_buttons() -> void:
	var states: Dictionary = _round.action_states() if _round != null else ActionRules.button_states({})
	_states = states
	_set_simple(states, "use")
	var use_wait: float = float(states.get("use_wait", 0.0))
	var use_b: ActionButton = _buttons["use"] as ActionButton
	use_b.disabled = use_wait > 0.0
	_apply_use_look(use_b, Net.as_dict(states.get("use_look")))
	use_b.set_ring(use_wait, float(Net.as_dict(Session.tuning.get("meeting")).get("emergency_min_since_meeting_s", Session.EMERGENCY_MIN_SINCE_FALLBACK_S)))
	_set_simple(states, "report")
	_pulse_report()
	_set_cooldown(states, "repollute", "trasher", "repollute_cooldown_s")
	_update_ability(Net.as_dict(states["ability"]))
	_heal_button.visible = bool(states["heal"])
	_alert_button.visible = bool(states["alert"])
	_set_cooldown(states, "sabotage", "sabotage", "shared_cooldown_s")
	var dash: Dictionary = Net.as_dict(states.get("dash"))
	_dash_button.visible = bool(dash.get("visible", false))
	_dash_button.disabled = not bool(dash.get("ready", false))
	_dash_button.set_ring(float(dash.get("cooldown", 0.0)), float(Net.as_dict(Session.tuning.get("dash")).get("cooldown_s", 0.0)))
	_update_fix(Net.as_dict(states["fix"]))
	# Only polluters have the third row; citizens keep REPORT and USE near the thumb.
	_sabotage_slot.visible = Session.role == "polluter"
	_sabotage_spare.visible = _sabotage_slot.visible
	_update_clean(bool(states["clean"]))
	_choices.visible = _choices.visible and bool(Net.as_dict(states["sabotage"]).get("ready", false))


## USE takes the art and label of what it will do here (clean, recycle n bags, call a meeting).
func _apply_use_look(use_b: ActionButton, look: Dictionary) -> void:
	if look == _use_look:
		return
	_use_look = look
	var face: Array = use_look_face(look)
	use_b.set_art(str(face[0]))
	use_b.text = action_text(str(face[1]), _key_text("act_use"), DisplayServer.is_touchscreen_available())


## [art, translated label] of the USE button for an ActionRules use_look; unknown kinds look like USE.
static func use_look_face(look: Dictionary) -> Array:
	var kind: String = str(look.get("kind", "use"))
	var entry: Array = USE_LOOK.get(kind, USE_LOOK["use"]) as Array
	var label: String = TranslationServer.translate(StringName(str(entry[1])))
	if kind == "recycle":
		label = label % int(look.get("count", 0))
	return [str(entry[0]), label]


## FIX: a hold button for the fire and the jammed machine ("Hold"), a tap for the smog panel ("Fix").
func _update_fix(fix: Dictionary) -> void:
	_fix_button.visible = bool(fix["visible"])
	var kind: String = str(fix["kind"])
	var hold: bool = kind == "fire" or kind == "machine"
	var label: String = tr("ACT_FIX_HOLD" if hold else "ACT_FIX")
	_fix_button.text = action_text(label, _key_text("act_use"), DisplayServer.is_touchscreen_available())
	var repair: Dictionary = {}
	if _round != null and kind == "machine":
		repair = _round.machine_repair_progress()
	elif _round != null and kind != "fire":
		repair = _round.repair_progress()
	if bool(repair.get("active", false)):
		_fix_button.set_ring(float(repair["elapsed"]), float(repair["duration"]))
	else:
		_fix_button.set_ring(0.0, 1.0)


## CLEAN: a hold button whose ring fills over the duration the server gave (clean_ok).
func _update_clean(offered: bool) -> void:
	_clean_button.visible = offered
	var progress: Dictionary = _round.clean_progress() if _round != null else {}
	if bool(progress.get("active", false)):
		_clean_button.set_ring(float(progress["elapsed"]), float(progress["duration"]))
	else:
		_clean_button.set_ring(0.0, 1.0)


func _update_banner() -> void:
	# The machine jam is not in Session.sabotage (that object is smog); it shows when no smog runs.
	var jam: bool = Session.sabotage.is_empty() and Session.machine_jammed
	var sabotage: Dictionary = {"kind": "machine"} if jam else Session.sabotage
	var repair: Dictionary = {}
	var repairing: bool = false
	if _round != null:
		repair = _round.machine_repair_progress() if jam else _round.repair_progress()
		repairing = _round.is_holding_machine() if jam else _round.is_repairing()
	var fraction: float = float(repair["elapsed"]) / float(repair["duration"]) if bool(repair.get("active", false)) and float(repair["duration"]) > 0.0 else -1.0
	_banner.show_sabotage(sabotage, _count_fire_consoles(), repairing, fraction)


## How many fire consoles the map has (the "held x/y" total); counted once.
func _count_fire_consoles() -> int:
	if _fire_total < 0 and _round != null and _round.map() != null:
		_fire_total = 0
		for m in _round.map().markers():
			if m.kind == "fire_fix":
				_fire_total += 1
	return maxi(_fire_total, 0)


func _pulse_report() -> void:
	var b: ActionButton = _buttons["report"] as ActionButton
	var k: float = 0.0
	if b.visible:
		k = 0.5 + 0.5 * sin(float(Time.get_ticks_msec()) / 1000.0 * 6.0)
	b.pivot_offset = b.size * 0.5
	b.scale = Vector2.ONE * (1.0 + REPORT_PULSE_SCALE * k)
	b.self_modulate = Color.WHITE.lerp(REPORT_PULSE_TINT, k)


func _set_simple(states: Dictionary, action: String) -> void:
	(_buttons[action] as ActionButton).visible = bool(states[action])


func _set_cooldown(states: Dictionary, action: String, tuning_group: String, tuning_key: String) -> void:
	var b: ActionButton = _buttons[action] as ActionButton
	var s: Dictionary = states[action]
	b.visible = bool(s["visible"])
	b.disabled = not bool(s["ready"])
	var total: float = float(Net.as_dict(Session.tuning.get(tuning_group)).get(tuning_key, 0.0))
	b.set_ring(float(s["cooldown"]), total)


## The reveal card shows while the server says the phase is the intro.
func _update_reveal() -> void:
	var on: bool = Session.phase == "intro" and not Session.role.is_empty()
	if on and not _reveal_shown:
		var partner: String = Session.seat_name(Session.partner_seat) if Session.partner_seat >= 0 else ""
		_reveal.setup(Session.role, partner)
	_reveal_shown = on
	_reveal.visible = on


func _update_spectator() -> void:
	if Session.is_out and (not _spectator.visible or _shown_cause != Session.out_cause):
		_shown_cause = Session.out_cause
		_spectator.set_cause(_shown_cause)
	_spectator.visible = Session.is_out
	if Session.is_out:
		_spectator.set_status(Session.meter, Session.phase, SpectatorOverlay.left_seconds(Session.deadline_t, Session.clock_t))


func _on_event(data: Dictionary) -> void:
	match str(data.get("t", "")):
		"spot_cleaned":
			_toast.show_text(tr("HUD_SPOT_CLEANED"))
		"delivered":
			_toast.show_text(tr("HUD_DELIVERED") % [int(data.get("bags", 0)), float(data.get("meter", Session.meter))])
		"repollute_ok":
			_toast.show_text(tr("REPOLLUTE_OK"))
		"healed":
			_toast.show_text(tr("HUD_HEALED"))
		"mask_used":
			_toast.show_text(tr("HUD_MASK_USED"))
		"gas_warning":
			_toast.show_text(tr("GAS_WARNING") % [tr(gas_room_key(str(data.get("room", "")))), ceili(float(data.get("in_s", 0.0)))])
		"virus_planted":
			_toast.show_text(tr("VIRUS_PLANTED"))
		"ability_ok":
			_toast.show_text(tr("ABILITY_OK") % tr("ABILITY_" + str(data.get("ability", "")).to_upper()))
		"upgrade_ok":
			_toast.show_text(tr("UPGRADE_OK") % tr("UPGRADE_" + str(data.get("item", "")).to_upper()))


## The open offers come back after a meeting or a rejoin: Session keeps them until a pick (the server
## keeps the upgrade offer; the ability choice is rebuilt from the snapshot after a rejoin).
## A closed offer waits behind the "Pick" button until the player reopens it.
func _update_pickers() -> void:
	if Session.upgrade_offer.is_empty():
		_upgrade_later = false
	if Session.ability_choices.is_empty():
		_ability_later = false
	var playing: bool = Session.phase == "playing" and not Session.is_out
	var upgrade_waits: bool = _upgrade_later and not Session.upgrade_offer.is_empty()
	var ability_waits: bool = _ability_later and not Session.ability_choices.is_empty()
	_pick_button.visible = playing and (upgrade_waits or ability_waits)
	if _pick_button.visible:
		_pick_button.text = tr("HUD_PICK_ABILITY") if ability_waits else tr("HUD_PICK_UPGRADE")
	if not playing:
		return
	if not Session.upgrade_offer.is_empty() and not _picker.is_open() and not _upgrade_later:
		_picker.open(_as_strings(Session.upgrade_offer))
	if not Session.ability_choices.is_empty() and not _ability_picker.is_open() and not _ability_later:
		_ability_picker.open(_as_strings(Session.ability_choices))


func _on_pick_pressed() -> void:
	_upgrade_later = false
	_ability_later = false


## "gas_market" -> "GAS_ROOM_MARKET" (same names as the room picker).
static func gas_room_key(room: String) -> String:
	return "GAS_ROOM_" + room.trim_prefix("gas_").to_upper()


static func _as_strings(items: Array) -> Array[String]:
	var out: Array[String] = []
	for i in items:
		out.append(str(i))
	return out


## The ability button: hidden until the unlock, then labelled VIRUS or GAS with its cooldown ring.
func _update_ability(ability: Dictionary) -> void:
	var b: ActionButton = _buttons["ability"] as ActionButton
	b.visible = bool(ability.get("visible", false))
	b.disabled = not bool(ability.get("ready", false))
	var kind: String = str(ability.get("kind", ""))
	if kind.is_empty():
		return
	if kind != _ability_kind:
		_ability_kind = kind
		b.text = action_text(tr("ACT_" + kind.to_upper()), _key_text("act_dump"), DisplayServer.is_touchscreen_available())
		b.set_art(kind)
	var total: float = float(Net.as_dict(Session.tuning.get("trasher")).get(kind + "_cooldown_s", 0.0))
	b.set_ring(float(ability.get("cooldown", 0.0)), total)


func _on_ability_picked(ability: String) -> void:
	if _round != null:
		_round.pick_ability(ability)


func _on_room_picked(room: String) -> void:
	if _round != null:
		_round.send_gas(room)


func _on_upgrade_picked(item: String) -> void:
	if _round != null:
		_round.pick_upgrade(item)


## "Items: Backpack, Tool, ..." for the upgrades a Cleaner owns; "" when none (and for Trashers:
## the server sends them no cap, tool, heals or masks).
static func owned_items_text(bag_cap: int, has_tool: bool, heals: int, masks: int) -> String:
	var names: PackedStringArray = []
	if bag_cap > 1:
		names.append(TranslationServer.translate(&"UPGRADE_BACKPACK"))
	if has_tool:
		names.append(TranslationServer.translate(&"UPGRADE_TOOL"))
	if heals > 0:
		names.append(TranslationServer.translate(&"UPGRADE_HEAL"))
	if masks > 0:
		names.append(TranslationServer.translate(&"UPGRADE_MASK"))
	if names.is_empty():
		return ""
	return TranslationServer.translate(&"HUD_ITEMS") % ", ".join(names)


func _on_sabotage_ended(data: Dictionary) -> void:
	var key: String = ended_toast_key(str(data.get("result", "")))
	if not key.is_empty():
		_toast.show_text(tr(key))


## The toast key for a sabotage_ended result; "" for none ("cleared": a meeting ended the fire).
static func ended_toast_key(result: String) -> String:
	match result:
		"fixed":
			return "SABOTAGE_FIXED"
		"expired":
			return "SABOTAGE_EXPIRED"
	return ""


## A refused clean_cancel is the server answering a cancel for a job that already ended: no toast.
## A refused ghost line has its note in the ghost panel.
static func shows_rejection(data: Dictionary) -> bool:
	return not ["clean_cancel", "chat_text", "chat_phrase"].has(str(data.get("action", "")))


## The window lost focus: no key release will come, so a key hold ends here.
func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT or what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		_on_hold_ended("fix")
		_on_hold_ended("clean")


func _on_action_rejected(data: Dictionary) -> void:
	if not shows_rejection(data):
		return
	_toast.show_text(UIKit.reason_text("REJECT_", str(data.get("reason", "")), "REJECT_UNKNOWN"))
