class_name MinigameLayer
extends CanvasLayer
## Hosts the open mini-game. Opens on use_ok(kind task), sends the step on step_done, closes on
## meeting / out / game over / walking away from the console.

## Sends a finished step to the server; tests replace it with a counter.
var send_step: Callable = Net.send_task_step
## v2: what a finished spot game sends (spot_done); tests replace it.
var send_spot: Callable = Net.send_spot_done
## Returns seconds for the open game's min_s gate; tests replace it (invalid = real time).
var clock: Callable = Callable()
const TOAST_S: float = 2.0
const TOAST_TOP: float = 12.0

var _current: Minigame = null
var _toast: Label = null
var _toast_left: float = 0.0
var _console_id: String = ""
## The spot being cleaned ("" for a v1 task game or a fake): its game sends spot_done, not task_step.
var _spot_id: String = ""
var _player_pos: Callable = Callable()
var _console_lookup: Callable = Callable()


func _ready() -> void:
	Net.use_ok.connect(_on_use_ok)
	Net.meeting_started.connect(_on_meeting_started)
	Net.you_are_out.connect(_on_you_are_out)
	Net.game_over.connect(_on_game_over)
	(get_node("CloseButton") as Button).pressed.connect(close_all)
	_show_chrome(false)


func set_player_pos_provider(c: Callable) -> void:
	_player_pos = c


func set_console_lookup(c: Callable) -> void:
	_console_lookup = c


## The fake shows the step that belongs at that console (its index in the type's consoles).
func open_fake(task_type: String, console_id: String = "") -> void:
	var consoles: Array = Net.as_array(Net.as_dict(Session.task_types.get(task_type)).get("consoles"))
	_open({"id": -1, "type": task_type}, maxi(consoles.find(console_id), 0), true)
	_console_id = console_id


func close_all() -> void:
	if _current != null:
		var m: Minigame = _current
		_current = null
		m.step_done.disconnect(_on_step_done)
		m.closed.disconnect(close_all)
		m.cancel()
		m.set_process(false)
		m.queue_free()
	_console_id = ""
	_spot_id = ""
	_show_chrome(false)


func is_open() -> bool:
	return _current != null


func _process(delta: float) -> void:
	if _toast_left > 0.0:
		_toast_left -= delta
		if _toast_left <= 0.0 and _toast != null:
			_toast.visible = false
	if _out_of_reach():
		_close_too_far()


## The player left the console's reach: the work is lost, so say why (a short toast on the layer).
func _close_too_far() -> void:
	close_all()
	if _toast == null:
		_toast = Label.new()
		_toast.name = "TooFarToast"
		_toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_toast.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_toast)
		_toast.set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
		_toast.offset_top = TOAST_TOP
	_toast.text = tr("MG_TOO_FAR")
	_toast.visible = true
	_toast_left = TOAST_S


## True when the player is farther from the open game's console than the server's own reach (the
## marker's range, no margin): past it the server answers out_of_range and drops the session.
func _out_of_reach() -> bool:
	if _current == null or _console_id.is_empty():
		return false
	if not _player_pos.is_valid() or not _console_lookup.is_valid():
		return false
	var info: Dictionary = _console_lookup.call(_console_id)
	if info.is_empty():
		return false
	var raw: Variant = _player_pos.call()
	if typeof(raw) != TYPE_VECTOR2:
		return false
	var pos: Vector2 = raw
	var console_pos: Vector2 = info.get("pos_px", pos)
	return pos.distance_to(console_pos) > float(info.get("range_px", 0.0))


func _open(task: Dictionary, step_index: int, is_fake: bool, min_seconds: float = -1.0) -> void:
	close_all()
	var path: String = MinigameRegistry.scene_for(str(task.get("type", "")))
	if path.is_empty():
		push_warning("MinigameLayer: no mini-game for task type '%s'" % str(task.get("type", "")))
		return
	var m: Minigame = (load(path) as PackedScene).instantiate() as Minigame
	m.clock = clock
	var wait: float = min_seconds if min_seconds >= 0.0 else Session.min_s_for(str(task.get("type", "")))
	m.begin(task, step_index, is_fake, wait)
	m.step_done.connect(_on_step_done)
	m.closed.connect(close_all)
	_current = m
	add_child(m)
	move_child(get_node("CloseButton"), -1)
	_show_chrome(true)


func _show_chrome(on: bool) -> void:
	(get_node("Dim") as Control).visible = on
	(get_node("CloseButton") as Control).visible = on


func _on_use_ok(data: Dictionary) -> void:
	if str(data.get("kind", "")) == "spot":
		_open_spot(str(data.get("type", "")), str(data.get("console", "")), int(data.get("step", -1)))
		return
	if str(data.get("kind", "")) != "task":
		return
	var id: int = int(data.get("task", -1))
	var task: Dictionary = Session.task_by_id(id)
	if task.is_empty():
		task = {"id": id, "type": str(data.get("type", ""))}
	var step_index: int = int(data.get("step", task.get("steps_done", 0)))
	_open(task, step_index, false)
	_console_id = str(data.get("console", ""))


## v2: the server opened a clean session on `spot_id`: play the step of its console, then spot_done.
## `step` is the server's use_ok.step (the console's index in its type's consoles, which picks the
## mini-game variant); -1 when missing, then the client finds the same index itself.
func _open_spot(task_type: String, spot_id: String, step: int = -1) -> void:
	if step < 0:
		var consoles: Array = Net.as_array(Net.as_dict(Session.task_types.get(task_type)).get("consoles"))
		step = maxi(consoles.find(spot_id), 0)
	_open({"id": -1, "type": task_type}, step, false, Session.clean_min_s(task_type))
	_console_id = spot_id
	_spot_id = spot_id


func _on_step_done(task_id: int, _step: int) -> void:
	if _out_of_reach():
		_close_too_far()
		return
	if _spot_id.is_empty():
		send_step.call(task_id)
	else:
		send_spot.call(_spot_id)
	close_all()


func _on_meeting_started(_data: Dictionary) -> void:
	close_all()


func _on_you_are_out(_data: Dictionary) -> void:
	close_all()


func _on_game_over(_data: Dictionary) -> void:
	close_all()
