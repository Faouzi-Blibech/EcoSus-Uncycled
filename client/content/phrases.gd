extends RefCounted
## The meeting chat phrases. CATALOGUE is a copy of content/phrases.json (the server reads that
## file; this client has no filesystem access to it once exported, like the map scene vs
## content/maps/*.json). tests/test_phrase_picker.gd fails when the two differ.
## The text of a phrase is the i18n key CHAT_<ID>, one %s per slot, in slot order.

## Phrases one seat may send per meeting (server rule, discussion stage only).
const LIMIT: int = 5
const KINDS: Array[String] = ["accuse", "alibi", "question", "react", "vote", "filler"]
const SLOT_SEAT: String = "seat"
const SLOT_ZONE: String = "zone"
const MAP_SCENE_PATH: String = "res://world/maps/%s.tscn"
const UNFILLED: String = "___"
## What an id this client does not know (a newer server) reads as.
const UNKNOWN_ID: String = "hmm"
static var _warned: Dictionary = {}

const CATALOGUE: Array[Dictionary] = [
	{"id": "sus", "kind": "accuse", "slots": ["seat"]},
	{"id": "saw_dump", "kind": "accuse", "slots": ["seat", "zone"]},
	{"id": "saw_hit", "kind": "accuse", "slots": ["seat", "zone"]},
	{"id": "near_pile", "kind": "accuse", "slots": ["seat", "zone"]},
	{"id": "i_was", "kind": "alibi", "slots": ["zone"]},
	{"id": "with", "kind": "alibi", "slots": ["seat", "zone"]},
	{"id": "where", "kind": "question", "slots": ["seat"]},
	{"id": "agree", "kind": "react", "slots": ["seat"]},
	{"id": "doubt", "kind": "react", "slots": ["seat"]},
	{"id": "not_me", "kind": "react", "slots": []},
	{"id": "lying", "kind": "react", "slots": ["seat"]},
	{"id": "skip_no_proof", "kind": "vote", "slots": []},
	{"id": "vote", "kind": "vote", "slots": ["seat"]},
	{"id": "hmm", "kind": "filler", "slots": []},
	{"id": "idk", "kind": "filler", "slots": []},
]


static func all() -> Array[Dictionary]:
	return CATALOGUE


static func of_kind(kind: String) -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	for info in CATALOGUE:
		if info["kind"] == kind:
			out.append(info)
	return out


## The catalogue entry, or an empty dictionary for an unknown id.
static func info(id: String) -> Dictionary:
	for entry in CATALOGUE:
		if entry["id"] == id:
			return entry
	return {}


static func zone_name(zone: String) -> String:
	var key: String = "ZONE_" + zone.to_upper()
	var text: String = TranslationServer.translate(StringName(key))
	return zone if text == key else text


## The phrase as a chat line, in the current language. name_of(seat: int) -> String names a seat.
## A missing or non-numeric seat arg shows UNFILLED; an unknown id reads as a neutral "hmm" (one warning per id).
static func render(id: String, args: Array, name_of: Callable) -> String:
	var entry: Dictionary = info(id)
	var key: String = "CHAT_" + id.to_upper()
	var text: String = TranslationServer.translate(StringName(key))
	if entry.is_empty() or text == key:
		if not _warned.has(id):
			_warned[id] = true
			push_warning("Phrases: no catalogue entry or CHAT_ text for \"%s\"" % id)
		return TranslationServer.translate(StringName("CHAT_" + UNKNOWN_ID.to_upper()))
	var parts: Array[String] = []
	var slots: Array = entry["slots"]
	for i in slots.size():
		var numeric: bool = i < args.size() and (typeof(args[i]) == TYPE_INT or typeof(args[i]) == TYPE_FLOAT)
		if i >= args.size() or args[i] == null or (slots[i] == SLOT_SEAT and not numeric):
			parts.append(UNFILLED)
		elif slots[i] == SLOT_SEAT:
			parts.append(str(name_of.call(int(args[i]))))
		else:
			parts.append(zone_name(str(args[i])))
	return text % parts


## Zone ids of a map scene, in scene order; empty when the scene cannot be loaded.
static func zone_ids(map_id: String) -> Array[String]:
	var out: Array[String] = []
	var packed: PackedScene = load(MAP_SCENE_PATH % map_id) as PackedScene
	if packed == null:
		return out
	var root: Node = packed.instantiate()
	if root is MapRoot:
		for m in (root as MapRoot).markers():
			if m.kind == "zone":
				out.append(m.marker_id)
	root.free()
	return out
