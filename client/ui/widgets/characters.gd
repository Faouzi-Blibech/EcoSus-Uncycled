extends RefCounted
## Playable characters: the art of each skin. Walk frames live in assets/characters/<id>/walk_<n>.png;
## frame times come from the designers' GIFs. The sprout is the default (it takes the seat colour) and
## is always available. Which others a player has unlocked is data, not code: the scan service lists
## them (Profile.skins, from content/skins.json), so the skin set can change without touching this list.
## "face": where the face is in a walk frame (0..1), and "zoom": the frame width per avatar circle
## width, so a round profile picture shows the face. "faces_right": which way the walk frames
## look (the world mirrors them to face the way the player walks).

const DIR: String = "res://assets/characters/%s/walk_%d.png"
const DEFAULT_ID: String = "sprout"

const LIST: Array[Dictionary] = [
	{"id": "sprout", "name_key": "CHAR_SPROUT", "frame_ms": [100, 100, 100, 100], "tint": true, "face": Vector2(0.5, 0.42), "zoom": 1.15, "faces_right": false},
	{"id": "robot", "name_key": "CHAR_ROBOT", "frame_ms": [100, 100, 200], "tint": false, "face": Vector2(0.5, 0.25), "zoom": 1.35, "faces_right": true},
	{"id": "eagle", "name_key": "CHAR_EAGLE", "frame_ms": [100, 200, 100, 200, 100, 100], "tint": false, "face": Vector2(0.55, 0.24), "zoom": 1.45, "faces_right": true},
	{"id": "rabbit", "name_key": "CHAR_RABBIT", "frame_ms": [100, 200, 100, 200], "tint": false, "face": Vector2(0.5, 0.3), "zoom": 1.35, "faces_right": false},
]


## True when there is art for this id (an unknown id is drawn as the default).
static func has(id: String) -> bool:
	for c in LIST:
		if c["id"] == id:
			return true
	return false


## The default is always unlocked; any other character when the player's unlocked list has it.
static func is_unlocked(id: String, unlocked: Array[String]) -> bool:
	return id == DEFAULT_ID or unlocked.has(id)


## The skin id sent with set_skin: "" for the default character.
static func wire_id(id: String) -> String:
	return "" if id == DEFAULT_ID else id


static func info(id: String) -> Dictionary:
	for c in LIST:
		if c["id"] == id:
			return c
	return LIST[0]


static func frames(id: String) -> Array[Texture2D]:
	var out: Array[Texture2D] = []
	var c: Dictionary = info(id)
	for i in range((c["frame_ms"] as Array).size()):
		out.append(Assets.texture(DIR % [c["id"], i]))
	return out
