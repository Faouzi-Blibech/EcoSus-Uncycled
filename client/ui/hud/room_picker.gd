class_name RoomPicker
extends CardPicker
## Modal card row where the Trasher picks the room to gas; can be cancelled.


func _init() -> void:
	title_key = "GAS_ROOM_TITLE"
	icon_format = ""
	cancelable = true


func label_key(item: String) -> String:
	return "GAS_ROOM_" + item.trim_prefix("gas_").to_upper()
