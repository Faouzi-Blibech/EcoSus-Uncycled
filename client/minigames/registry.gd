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
	var key: String = base_type(task_type)
	return "" if key.is_empty() else SCENES[key]


## The mini-game key of a task type: itself, or the longest key it starts with plus "_"
## ("sort_waste_depot" -> "sort_waste"); "" when none. Spot art is named after it.
static func base_type(task_type: String) -> String:
	if SCENES.has(task_type):
		return task_type
	var best: String = ""
	for k in SCENES:
		var key: String = k
		if task_type.begins_with(key + "_") and key.length() > best.length():
			best = key
	return best
