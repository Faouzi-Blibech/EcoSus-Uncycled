class_name AbilityPicker
extends CardPicker
## Modal card row where the Trasher picks one ability (virus or gas); not cancelable.


func _init() -> void:
	title_key = "ABILITY_TITLE"
	known_items = ["virus", "gas"]
	icon_format = "res://assets/icons/ability_%s.png"
	label_prefix = "ABILITY_"
