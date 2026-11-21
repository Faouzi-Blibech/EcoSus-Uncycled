class_name Hud
extends CanvasLayer
## In-round HUD: task list, cleanliness meter, match timer, bags carried, the action buttons with
## cooldown rings, the sabotage banner, the role reveal card, toasts and the spectator overlay. It only shows
## what Session holds; actions go through the bound round (RoundScene.perform_action).

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const RoleRevealScene: PackedScene = preload("res://ui/hud/role_reveal.tscn")
const SpectatorScene: PackedScene = preload("res://ui/hud/spectator_overlay.tscn")
const GhostChatPanel := preload("res://ui/hud/ghost_chat_panel.gd")

const MARGIN_PX: float = 8.0
const TOP_RIGHT_WIDTH_PX: float = 170.0
const TOAST_BOTTOM_OFFSET_PX: float = -84.0
const SMALL_FONT: int = 11
const DONE_COLOR: Color = Color(0.6, 0.6, 0.6)
const METER_COLOR: Color = Color(0.3, 0.8, 0.35)
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
const CHOICE_SIZE: Vector2 = Vector2(56.0, 48.0)
const BANNER_WIDTH_PX: float = 220.0
## Width of the button grid (two 64 px slots and the gap), used to put the choices beside it.
const GRID_WIDTH_PX: float = 134.0

var _round: RoundScene = null
var _root: Control
var _role_label: Label
var _partner_label: Label
var _task_rows: VBoxContainer
var _tasks_header: Label
var _timer_label: Label
var _meter_label: Label
var _meter_bar: ProgressBar
var _bags_label: Label
## Upgrades owned (backpack, tool, heal, mask), Cleaners only.
var _items_label: Label
var _picker: UpgradePicker
var _ability_picker: AbilityPicker
var _room_picker: RoomPicker
## The kind the ability button is labelled with ("virus" or "gas"), "" until known.
var _ability_kind: String = ""
## The infected player's countdown, and the HEAL / ALERT buttons a Cleaner gets in the Trasher slots.
var _virus_timer: VirusTimer
var _heal_button: ActionButton
var _alert_button: ActionButton
var _ping_label: Label
var _buttons: Dictionary = {}
var _fix_button: ActionButton
var _sabotage_slot: Control
var _sabotage_spare: Control
var _clean_button: ActionButton
var _choices: HBoxContainer
var _banner: SabotageBanner
var _states: Dictionary = {}
var _fire_total: int = -1
var _toast: Toast
var _reveal: RoleReveal
var _reveal_shown: bool = false
var _spectator: SpectatorOverlay
var _shown_cause: String = ""
var _tasks_signature: String = ""


func _ready() -> void:
	_build()
	Net.action_rejected.connect(_on_action_rejected)
	Net.sabotage_ended.connect(_on_sabotage_ended)
	Net.event.connect(_on_event)


func bind(round: Node) -> void:
	_round = round as RoundScene


func _build() -> void:
	_root = Control.new()
	_root.theme = UIKit.theme()
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)
	UIKit.fill(_root)
	_apply_safe_area()

	_spectator = SpectatorScene.instantiate() as SpectatorOverlay
	_spectator.visible = false
	_root.add_child(_spectator)

	_build_top_left()
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
	_virus_timer.offset_top = 52.0
	_virus_timer.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_root.add_child(_virus_timer)
	_virus_timer.show_seconds(-1.0)

	_picker = UpgradePicker.new()
	_picker.picked.connect(_on_upgrade_picked)
	_root.add_child(_picker)
	# The Trasher's ability choice and the gas room choice, same card picker.
	_ability_picker = AbilityPicker.new()
	_ability_picker.picked.connect(_on_ability_picked)
	_root.add_child(_ability_picker)
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
	var box: VBoxContainer = UIKit.make_vbox(0)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.position = Vector2(MARGIN_PX, MARGIN_PX - 2.0)
	_root.add_child(box)
	_role_label = UIKit.make_hud_label(SMALL_FONT)
	_partner_label = UIKit.make_hud_label(SMALL_FONT)
	_tasks_header = UIKit.make_hud_label()
	_tasks_header.text = tr("HUD_TASKS")
	_task_rows = UIKit.make_vbox(0)
	_task_rows.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for n in [_role_label, _partner_label, _tasks_header, _task_rows]:
		box.add_child(n)


func _build_top_right() -> void:
	var box: VBoxContainer = UIKit.make_vbox(2, TOP_RIGHT_WIDTH_PX)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.anchor_left = 1.0
	box.anchor_right = 1.0
	box.offset_left = -MARGIN_PX
	box.offset_right = -MARGIN_PX
	box.offset_top = MARGIN_PX - 2.0
	box.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_root.add_child(box)

	_timer_label = UIKit.make_hud_label(18)
	_timer_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_meter_label = UIKit.make_hud_label(SMALL_FONT)
	_meter_bar = _make_bar(METER_COLOR)
	_bags_label = UIKit.make_hud_label(SMALL_FONT)
	_bags_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_items_label = UIKit.make_hud_label(SMALL_FONT)
	_items_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_items_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_ping_label = UIKit.make_hud_label(SMALL_FONT)
	_ping_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	for n in [_timer_label, _meter_label, _meter_bar, _bags_label, _items_label, _ping_label]:
		box.add_child(n)


func _build_banner() -> void:
	_banner = SabotageBanner.new()
	_banner.anchor_left = 0.5
	_banner.anchor_right = 0.5
	_banner.offset_top = MARGIN_PX - 2.0
	_banner.custom_minimum_size = Vector2(BANNER_WIDTH_PX, 0.0)
	_banner.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_banner.grow_horizontal = Control.GROW_DIRECTION_BOTH
	_root.add_child(_banner)


func _make_bar(color: Color) -> ProgressBar:
	var bar: ProgressBar = ProgressBar.new()
	bar.min_value = 0.0
	bar.max_value = 100.0
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0.0, 10.0)
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var fill: StyleBoxFlat = StyleBoxFlat.new()
	fill.bg_color = color
	bar.add_theme_stylebox_override("fill", fill)
	return bar


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
		slot.custom_minimum_size = ActionButton.BUTTON_SIZE
		slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
		grid.add_child(slot)
		slots[action] = slot
		var b: ActionButton = ActionButton.new()
		b.text = action_text(tr(ACTION_LABEL[action]), _key_text(str(ACTION_INPUT[action])), DisplayServer.is_touchscreen_available())
		b.visible = false
		b.pressed.connect(_on_action_pressed.bind(action))
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
	_clean_button.hold_started.connect(_on_hold_started.bind("clean"))
	_clean_button.hold_ended.connect(_on_hold_ended.bind("clean"))
	# An empty last slot keeps the grid two columns wide; both go when the role has no SABOTAGE.
	_sabotage_slot = slots["sabotage"] as Control
	_sabotage_spare = Control.new()
	_sabotage_spare.custom_minimum_size = ActionButton.BUTTON_SIZE
	_sabotage_spare.mouse_filter = Control.MOUSE_FILTER_IGNORE
	grid.add_child(_sabotage_spare)


## A tap button that fills `slot`, hidden until the rules show it; pressing it runs `action`.
func _make_tap_button(slot: Control, label_key: String, input_action: String, action: String) -> ActionButton:
	var b: ActionButton = ActionButton.new()
	b.text = action_text(tr(label_key), _key_text(input_action), DisplayServer.is_touchscreen_available())
	b.visible = false
	b.pressed.connect(_on_action_pressed.bind(action))
	slot.add_child(b)
	UIKit.fill(b)
	return b


## A hold button (see ActionButton.hold_mode) that fills `slot`, hidden until the rules show it.
func _make_hold_button(slot: Control, label_key: String, input_action: String) -> ActionButton:
	var b: ActionButton = ActionButton.new()
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
		var b: Button = Button.new()
		b.text = tr("SABOTAGE_" + kind.to_upper())
		b.custom_minimum_size = CHOICE_SIZE
		b.focus_mode = Control.FOCUS_NONE
		b.add_theme_font_size_override("font_size", 12)
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
	_update_header()
	_update_tasks()
	_update_buttons()
	_update_pickers()
	_virus_timer.show_seconds(Session.infected_s)
	_update_banner()
	_update_reveal()
	_update_spectator()


func _update_header() -> void:
	if not Session.role.is_empty():
		_role_label.text = tr("HUD_ROLE") % tr("ROLE_" + Session.role.to_upper())
	else:
		_role_label.text = ""
	if Session.partner_seat >= 0:
		_partner_label.text = tr("HUD_PARTNER") % Session.seat_name(Session.partner_seat)
	else:
		_partner_label.text = ""
	_meter_label.text = tr("HUD_METER") % Session.meter
	_meter_bar.value = Session.meter
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
		var l: Label = UIKit.make_hud_label(SMALL_FONT + 1)
		l.text = task_line(t, _round.map() if _round != null else null)
		if int(t.get("steps_done", 0)) >= int(t.get("steps", 1)):
			l.add_theme_color_override("font_color", DONE_COLOR)
		_task_rows.add_child(l)


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
	_set_simple(states, "report")
	_set_cooldown(states, "repollute", "trasher", "repollute_cooldown_s")
	_update_ability(Net.as_dict(states["ability"]))
	_heal_button.visible = bool(states["heal"])
	_alert_button.visible = bool(states["alert"])
	_set_cooldown(states, "sabotage", "sabotage", "shared_cooldown_s")
	_update_fix(Net.as_dict(states["fix"]))
	# Only polluters have the third row; citizens keep REPORT and USE near the thumb.
	_sabotage_slot.visible = Session.role == "polluter"
	_sabotage_spare.visible = _sabotage_slot.visible
	_update_clean(bool(states["clean"]))
	_choices.visible = _choices.visible and bool(Net.as_dict(states["sabotage"]).get("ready", false))


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
func _update_pickers() -> void:
	if Session.phase != "playing" or Session.is_out:
		return
	if not Session.upgrade_offer.is_empty() and not _picker.is_open():
		_picker.open(_as_strings(Session.upgrade_offer))
	if not Session.ability_choices.is_empty() and not _ability_picker.is_open():
		_ability_picker.open(_as_strings(Session.ability_choices))


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
