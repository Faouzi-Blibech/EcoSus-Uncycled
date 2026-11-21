class_name MinigameRegistry
extends RefCounted
## Task type -> mini-game scene.

const SCENES: Dictionary = {
	"sort_waste": "res://minigames/sort_waste/sort_waste.tscn",
	"turn_compost": "res://minigames/turn_compost/turn_compost.tscn",
	"check_meter": "res://minigames/check_meter/check_meter.tscn",
	"crush_cans": "res://minigames/crush_cans/crush_cans.tscn",
	"rinse_bottles": "res://minigames/rinse_bottles/rinse_bottles.tscn",
	"deliver_glass": "res://minigames/deliver_glass/deliver_glass.tscn",
}


## The key equal to task_type, or the longest key k with task_type starting with "k_"; else "".
static func scene_for(task_type: String) -> String:
	if SCENES.has(task_type):
		return SCENES[task_type]
	var best: String = ""
	for k in SCENES:
		var key: String = k
		if task_type.begins_with(key + "_") and key.length() > best.length():
			best = key
	if best.is_empty():
		return ""
	return SCENES[best]
