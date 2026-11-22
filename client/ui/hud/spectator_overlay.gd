class_name SpectatorOverlay
extends Control
## Shown instead of the world once the local player is out: the round goes on without them.
## It tells them why they are out, how the round stands (pollution, phase, time left) and, during
## a meeting, which stage it is in. The server sends no world and no players to an out seat.

const UIKit := preload("res://ui/widgets/ui_kit.gd")
const PaperKit := preload("res://ui/widgets/paper_kit.gd")
const BACKDROP: Color = Color(0.0, 0.0, 0.0, 0.55)
const METER_COLOR: Color = Color(0.45, 0.7, 0.3)
const MUTED: Color = PaperKit.SOFT_INK
const TITLE_SIZE: int = 28
const METER_WIDTH: float = 220.0
## The ghost chat panel sits at the right edge (ghost_chat_panel.gd PANEL_WIDTH plus its margin): the
## text is centred in the space left of it.
const GHOST_PANEL_RESERVED_PX: float = 262.0

var _cause: Label
var _meter_label: Label
var _meter_bar: ProgressBar
var _phase: Label
var _time: Label


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self, BACKDROP)
	var box: VBoxContainer = UIKit.make_vbox(6)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(box)
	UIKit.fill(box)
	box.offset_right = -GHOST_PANEL_RESERVED_PX
	# The words sit on the purple taped note (cream ink), over a dimmed map.
	var center: CenterContainer = CenterContainer.new()
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)
	UIKit.fill(center)
	center.offset_right = -GHOST_PANEL_RESERVED_PX
	var note: PanelContainer = PanelContainer.new()
	note.mouse_filter = Control.MOUSE_FILTER_IGNORE
	note.add_theme_stylebox_override("panel", PaperKit.box("note_purple.png", 24.0, Vector2(30.0, 26.0)))
	center.add_child(note)
	remove_child(box)
	UIKit.fill(box)
	note.add_child(box)
	var title: Label = UIKit.make_label(tr("SPECTATOR_TITLE"), TITLE_SIZE)
	_cause = UIKit.make_label("")
	_meter_label = UIKit.make_label("", 14, MUTED)
	_meter_bar = _make_meter_bar()
	_phase = UIKit.make_label("")
	_time = UIKit.make_label("", 14, MUTED)
	var hint: Label = UIKit.make_label(tr("SPECTATOR_HINT"), 12, MUTED)
	for l in [title, _cause, _meter_label, _phase, _time, hint]:
		(l as Label).horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		(l as Label).mouse_filter = Control.MOUSE_FILTER_IGNORE
		box.add_child(l)
	# The bar sits under its label, centred.
	box.add_child(_meter_bar)
	box.move_child(_meter_bar, _meter_label.get_index() + 1)
	for l in [title, _cause, _phase]:
		(l as Label).add_theme_color_override("font_color", PaperKit.CREAM)
	for l in [_meter_label, _time, hint]:
		(l as Label).add_theme_color_override("font_color", PaperKit.CREAM.darkened(0.15))


func _make_meter_bar() -> ProgressBar:
	var bar: ProgressBar = ProgressBar.new()
	bar.min_value = 0.0
	bar.max_value = 100.0
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(METER_WIDTH, 10.0)
	bar.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var fill: StyleBoxFlat = StyleBoxFlat.new()
	fill.bg_color = METER_COLOR
	fill.set_corner_radius_all(5)
	bar.add_theme_stylebox_override("fill", fill)
	var track: StyleBoxFlat = StyleBoxFlat.new()
	track.bg_color = PaperKit.CREAM
	track.set_corner_radius_all(5)
	bar.add_theme_stylebox_override("background", track)
	return bar


## cause is the you_are_out cause ("poison", "vote") or "" when unknown (rejoined as out).
func set_cause(cause: String) -> void:
	_cause.text = tr("SPECTATOR_CAUSE_" + cause.to_upper()) if not cause.is_empty() else ""


## The round at a glance. phase is the snapshot phase, left_s the seconds to the deadline or -1
## when there is none. Meetings are not shown here: an out player is taken to the meeting scene
## itself (Router), which has the stage and the timer.
func set_status(meter: float, phase: String, left_s: int) -> void:
	_meter_label.text = tr("HUD_METER") % meter
	_meter_bar.value = meter
	_phase.text = tr(phase_key(phase)) if not phase.is_empty() else ""
	_time.text = tr("HUD_TIMER") % [floori(left_s / 60.0), left_s % 60] if left_s >= 0 else ""


## The players' "Find the polluters!" is no line for someone who is out: say the round goes on.
static func phase_key(phase: String) -> String:
	return "SPECTATOR_PHASE_PLAYING" if phase == "playing" else "PHASE_" + phase.to_upper()


## Seconds left on the deadline clock the HUD timer counts (clock_t up to deadline_t, never match_t); -1 without one.
static func left_seconds(deadline_t: float, clock_t: float) -> int:
	if deadline_t <= 0.0:
		return -1
	return maxi(ceili(deadline_t - clock_t), 0)
