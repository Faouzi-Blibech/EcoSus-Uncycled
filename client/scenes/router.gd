extends RefCounted
## Scene paths and routing helpers. Scenes never hard-code each other's paths.

const BOOT: String = "res://scenes/boot/boot.tscn"
const MENU: String = "res://scenes/menu/menu.tscn"
const CREATE: String = "res://scenes/create/create.tscn"
const JOIN: String = "res://scenes/join/join.tscn"
const LOBBY: String = "res://scenes/lobby/lobby.tscn"
const ROUND: String = "res://scenes/round/round.tscn"
const MEETING: String = "res://scenes/meeting/meeting.tscn"
const RESULTS: String = "res://scenes/results/results.tscn"
const PLACEHOLDER: String = "res://scenes/placeholder/placeholder.tscn"
const RECONNECTING: String = "res://scenes/reconnect/reconnecting.tscn"


## Phases shown by the round scene. "intro" is the 5 s role reveal: same scene, movement locked.
const ROUND_PHASES: Array[String] = ["intro", "playing"]


## Phases of a match in progress (not the lobby, not the finished match).
const MATCH_PHASES: Array[String] = ["intro", "playing", "meeting", "ejection"]


static func go(tree: SceneTree, path: String) -> void:
	tree.change_scene_to_file.call_deferred(path)


static func is_match_phase(phase: String) -> bool:
	return MATCH_PHASES.has(phase)


static func is_round_phase(phase: String) -> bool:
	return ROUND_PHASES.has(phase)


## Scene for a phase string ("lobby", "intro", "playing", "meeting", "ejection", "ended").
static func scene_for_phase(phase: String) -> String:
	match phase:
		"lobby":
			return LOBBY
		"meeting", "ejection":
			return MEETING
		"ended":
			return RESULTS
		"intro", "playing":
			return ROUND
		_:
			return PLACEHOLDER


## Used after any welcome (create, join, rejoin): Session already holds the phase.
static func route_after_welcome(tree: SceneTree) -> void:
	go(tree, scene_for_phase(Session.phase))


## Socket closed while the scene was showing: reconnect if we hold a seat, else start over.
static func on_disconnected(tree: SceneTree) -> void:
	if Session.has_room():
		go(tree, RECONNECTING)
	else:
		go(tree, BOOT)
