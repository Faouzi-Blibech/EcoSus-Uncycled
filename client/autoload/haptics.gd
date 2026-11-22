extends Node
## Phone buzz on game events: a meeting is called (short), the local player is infected or caught
## in gas (longer). Listens to Net.event, so each server message buzzes once; snapshots never do.
## Input.vibrate_handheld works on Android (VIBRATE permission in the export preset) and on web
## where the browser allows it; elsewhere it does nothing.

## The defaults, in milliseconds.
const MEETING_MS: int = 120
const HIT_MS: int = 400

## Replaceable so tests never call the real API.
var vibrate_fn: Callable = func(ms: int) -> void: Input.vibrate_handheld(ms)
var enabled_fn: Callable = func() -> bool: return Settings.haptics_enabled


func _ready() -> void:
	Net.event.connect(handle_message)


## One server message; buzzes for meeting_started, infected and you_are_out caused by gas.
func handle_message(msg: Dictionary) -> void:
	match str(msg.get("t", "")):
		"meeting_started":
			_buzz(MEETING_MS)
		"infected":
			_buzz(HIT_MS)
		"you_are_out":
			if str(msg.get("cause", "")) == "gas":
				_buzz(HIT_MS)


func _buzz(ms: int) -> void:
	if enabled_fn.call():
		vibrate_fn.call(ms)
