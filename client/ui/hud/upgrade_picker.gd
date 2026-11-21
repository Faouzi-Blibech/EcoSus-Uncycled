class_name UpgradePicker
extends CardPicker
## Modal card row offered after a delivery at the Recycling Machine: the player picks one upgrade.


func _init() -> void:
	title_key = "UPGRADE_TITLE"
	known_items = ["backpack", "tool", "heal", "mask"]
	icon_format = "res://assets/icons/upgrade_%s.png"
	label_prefix = "UPGRADE_"
