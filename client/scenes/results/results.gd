extends Control
## Results: winner, reason, every seat's role, the timeline, the fact. Built from
## Session.last_game_over at _ready, so a rejoin during "ended" shows the same page.
## No button: the server sends everybody back to the lobby (Net.lobby), and so do we.

const Router := preload("res://scenes/router.gd")
const UIKit := preload("res://ui/widgets/ui_kit.gd")
const VoteRules := preload("res://scenes/meeting/vote_rules.gd")

## The server brings the room back to the lobby after tuning match.return_to_lobby_s; this default
## only applies when the welcome tuning lacks the key. Display only: the real switch is the lobby message.
const DEFAULT_LOBBY_DELAY_S: float = 15.0
const MUTED: Color = Color(0.7, 0.8, 0.75)
const WIN_COLOR: Color = Color(0.55, 0.9, 0.55)
const LOSE_COLOR: Color = Color(0.95, 0.6, 0.45)
const CARD_BG: Color = Color(0.12, 0.22, 0.16)
const CARD_BORDER: Color = Color(0.35, 0.65, 0.4)

var _countdown: Label
var _left_s: float = DEFAULT_LOBBY_DELAY_S


func _ready() -> void:
	theme = UIKit.theme()
	UIKit.fill(self)
	UIKit.background(self)
	var over: Dictionary = Session.last_game_over
	_left_s = lobby_delay_s(Session.tuning)

	var margin: MarginContainer = MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 8)
	add_child(margin)
	UIKit.fill(margin)
	var root: VBoxContainer = UIKit.make_vbox(6)
	margin.add_child(root)

	var winner: String = str(over.get("winner", ""))
	var title: Label = UIKit.make_label(_tr_or("RESULT_WINNER_", winner, "RESULT_REASON_UNKNOWN"), 24, WIN_COLOR if winner == "citizens" else LOSE_COLOR)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(title)
	var reason: Label = UIKit.make_label(_tr_or("RESULT_", str(over.get("reason", "")), "RESULT_REASON_UNKNOWN"), 14, MUTED)
	reason.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(reason)

	var cols: HBoxContainer = HBoxContainer.new()
	cols.add_theme_constant_override("separation", 16)
	cols.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(cols)
	cols.add_child(_roles_column(Net.as_dict(over.get("roles"))))
	cols.add_child(_timeline_column(Net.as_array(over.get("timeline"))))

	var card: Control = _fact_card(over)
	if card != null:
		root.add_child(card)
	_countdown = UIKit.make_label("", 14)
	_countdown.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(_countdown)

	Net.lobby.connect(_on_lobby)
	Net.disconnected.connect(_on_disconnected)
	_show_countdown()


## Seconds until the server returns to the lobby: tuning match.return_to_lobby_s, else the default.
static func lobby_delay_s(tuning: Dictionary) -> float:
	var m: Variant = tuning.get("match")
	if typeof(m) == TYPE_DICTIONARY:
		var v: float = float((m as Dictionary).get("return_to_lobby_s", 0.0))
		if v > 0.0:
			return v
	return DEFAULT_LOBBY_DELAY_S


func _process(delta: float) -> void:
	_left_s = maxf(0.0, _left_s - delta)
	_show_countdown()


func _show_countdown() -> void:
	_countdown.text = tr("RESULT_COUNTDOWN") % ceili(_left_s)


# ---------------------------------------------------------------- views

func _roles_column(roles: Dictionary) -> VBoxContainer:
	var col: VBoxContainer = UIKit.make_vbox(2)
	col.add_child(UIKit.make_label(tr("RESULT_ROLES"), 14, MUTED))
	# Two columns: 8 players fit in 4 rows on a phone screen.
	var grid: GridContainer = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 2)
	col.add_child(grid)
	var seats: Array[int] = []
	for key in roles.keys():
		seats.append(int(key))
	seats.sort()
	for seat in seats:
		var role: String = str(roles.get(str(seat), roles.get(seat, "")))
		var line: Label = UIKit.make_label(tr("RESULT_ROLE_LINE") % [_name_of(seat), _tr_or("ROLE_", role, "RESULT_ROLE_UNKNOWN")], 13, Session.seat_color(seat))
		grid.add_child(line)
	return col


func _timeline_column(timeline: Array) -> VBoxContainer:
	var col: VBoxContainer = UIKit.make_vbox(2)
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	col.add_child(UIKit.make_label(tr("RESULT_TIMELINE"), 14, MUTED))
	var scroll: ScrollContainer = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	col.add_child(scroll)
	var list: VBoxContainer = UIKit.make_vbox(2)
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(list)
	if timeline.is_empty():
		list.add_child(UIKit.make_label(tr("RESULT_NO_TIMELINE"), 12, MUTED))
	for entry in timeline:
		if typeof(entry) != TYPE_DICTIONARY:
			continue
		var l: Label = UIKit.make_label(_timeline_line(entry), 12)
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		list.add_child(l)
	return col


func _timeline_line(entry: Dictionary) -> String:
	var kind: String = str(entry.get("kind", ""))
	var key: String = "RESULT_TL_" + kind.to_upper()
	var text: String = kind
	if tr(key) != key:
		var who: String = _name_of(int(entry.get("seat", -1)))
		if entry.has("target"):
			text = tr(key) % [who, _name_of(int(entry.get("target", -1)))]
		else:
			text = tr(key) % who
	var zone: String = str(entry.get("zone", ""))
	if not zone.is_empty():
		text = tr("RESULT_TL_AT") % [text, _tr_or("ZONE_", zone, "RESULT_REASON_UNKNOWN")]
	return tr("RESULT_TL_LINE") % [VoteRules.mmss(float(entry.get("t", 0.0))), text]


## The fact id is a key ("fact_01" -> FACT_01); anything unknown is shown as the raw text.
func _fact_text(over: Dictionary) -> String:
	var fact: String = str(over.get("fact", ""))
	if fact.is_empty():
		return ""
	return _tr_or("", fact, "RESULT_REASON_UNKNOWN")


## The AI debrief of the match, shown as sent; "" when the server did not send one.
static func debrief_text(over: Dictionary) -> String:
	var text: Variant = over.get("debrief_text")
	return str(text) if text != null else ""


## The fact on a green card with the debrief text under it; null when there is neither.
func _fact_card(over: Dictionary) -> Control:
	var fact: String = _fact_text(over)
	var debrief: String = debrief_text(over)
	if fact.is_empty() and debrief.is_empty():
		return null
	var panel: PanelContainer = PanelContainer.new()
	var style: StyleBoxFlat = StyleBoxFlat.new()
	style.bg_color = CARD_BG
	style.border_color = CARD_BORDER
	style.set_border_width_all(2)
	style.set_corner_radius_all(6)
	style.set_content_margin_all(8)
	panel.add_theme_stylebox_override("panel", style)
	var lines: VBoxContainer = UIKit.make_vbox(4)
	panel.add_child(lines)
	if not fact.is_empty():
		lines.add_child(UIKit.make_label(tr("RESULT_FACT_LABEL"), 12, WIN_COLOR))
		lines.add_child(_wrapped(fact, 12, Color.WHITE))
	if not debrief.is_empty():
		lines.add_child(_wrapped(debrief, 12, MUTED))
	return panel


func _wrapped(text: String, size: int, color: Color) -> Label:
	var label: Label = UIKit.make_label(text, size, color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label


# ---------------------------------------------------------------- helpers

func _name_of(seat: int) -> String:
	var n: String = Session.seat_name(seat)
	return n if n != "" else str(seat)


## Translation of prefix + id; the readable fallback is the raw id, or the fallback key when id is empty.
func _tr_or(prefix: String, id: String, fallback_key: String) -> String:
	if id.is_empty():
		return tr(fallback_key)
	var key: String = prefix + id.to_upper()
	var text: String = tr(key)
	return id if text == key else text


# ---------------------------------------------------------------- events

func _on_lobby(_data: Dictionary) -> void:
	Router.go(get_tree(), Router.LOBBY)


func _on_disconnected() -> void:
	Router.on_disconnected(get_tree())
