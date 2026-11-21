extends RefCounted
## Playable characters. Walk frames live in assets/characters/<id>/walk_<n>.png; frame times come
## from the designers' GIFs. The sprout is the default and the only unlocked one for now: it takes
## the seat colour. The others unlock later with the scan streak (design section 9).
## "face": where the face is in a walk frame (0..1), and "zoom": the frame width per avatar circle
## width, so a round profile picture shows the face.

const DIR: String = "res://assets/characters/%s/walk_%d.png"
const DEFAULT_ID: String = "sprout"

const LIST: Array[Dictionary] = [
	{"id": "sprout", "name_key": "CHAR_SPROUT", "frame_ms": [100, 100, 100, 100], "tint": true, "locked": false, "face": Vector2(0.5, 0.42), "zoom": 1.15},
	{"id": "robot", "name_key": "CHAR_ROBOT", "frame_ms": [100, 100, 200], "tint": false, "locked": true, "face": Vector2(0.5, 0.25), "zoom": 1.35},
	{"id": "eagle", "name_key": "CHAR_EAGLE", "frame_ms": [100, 200, 100, 200, 100, 100], "tint": false, "locked": true, "face": Vector2(0.55, 0.24), "zoom": 1.45},
	{"id": "rabbit", "name_key": "CHAR_RABBIT", "frame_ms": [100, 200, 100, 200], "tint": false, "locked": true, "face": Vector2(0.5, 0.3), "zoom": 1.35},
]


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
