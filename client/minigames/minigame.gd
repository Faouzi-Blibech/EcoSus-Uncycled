class_name Minigame
extends Control
## Base of every mini-game. The subclass calls finish_step() when the player did the step; the
## base emits step_done once, not before min_seconds after begin(), and never for a fake.

signal step_done(task_id: int, step: int)
signal closed()

var task_id: int = -1
var task_type: String = ""
var step: int = 0
var fake: bool = false
## Returns seconds. Tests replace it.
var clock: Callable = Callable()

const WAIT_LABEL_TOP: float = -40.0

var _wait_label: Label = null
var _min_seconds: float = 0.0
var _begun_at: float = 0.0
var _wanted: bool = false
var _sent: bool = false


func begin(task: Dictionary, step_index: int, is_fake: bool, min_seconds: float) -> void:
	task_id = int(task.get("id", -1))
	task_type = str(task.get("type", ""))
	step = step_index
	fake = is_fake
	_min_seconds = min_seconds
	_begun_at = _now()
	_wanted = false
	_sent = false


func finish_step() -> void:
	if _sent:
		return
	_wanted = true
	poll()


func poll() -> void:
	if not _wanted or _sent:
		return
	if _now() - _begun_at < _min_seconds:
		_show_wait()
		return
	_sent = true
	if fake:
		closed.emit()
		return
	step_done.emit(task_id, step)


## The step is done but the server's minimum time has not passed: say so, with the seconds left, so
## the player does not take the held step for a frozen game. Fakes show it too.
func _show_wait() -> void:
	if _wait_label == null:
		_wait_label = Label.new()
		_wait_label.name = "WaitLabel"
		_wait_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_wait_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_wait_label)
		_wait_label.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
		_wait_label.offset_top = WAIT_LABEL_TOP
	var left: float = _min_seconds - (_now() - _begun_at)
	_wait_label.text = tr("MG_CHECKING") % maxi(ceili(left), 1)
	_wait_label.visible = true


func cancel() -> void:
	_sent = true
	_wanted = false
	closed.emit()


## How far a drag event moved: mouse motion (the first touch arrives as emulated mouse motion) or a
## ScreenDrag of a later finger, which is not emulated. Zero for anything else, so the first
## touch is never counted twice.
static func drag_delta(event: InputEvent) -> Vector2:
	if event is InputEventMouseMotion:
		return (event as InputEventMouseMotion).relative
	if event is InputEventScreenDrag and (event as InputEventScreenDrag).index != 0:
		return (event as InputEventScreenDrag).relative
	return Vector2.ZERO


func _process(_delta: float) -> void:
	poll()


func _now() -> float:
	if clock.is_valid():
		return float(clock.call())
	return Time.get_ticks_msec() / 1000.0
