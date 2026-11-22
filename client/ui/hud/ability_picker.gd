class_name AbilityPicker
extends CardPicker
## Modal card row where the Trasher picks one ability (virus or gas). Close hides it; the HUD's
## "Pick ability" button opens it again.


func _init() -> void:
	title_key = "ABILITY_TITLE"
	known_items = ["virus", "gas"]
	icon_format = "res://assets/icons/ability_%s.png"
	label_prefix = "ABILITY_"
	cancelable = true
	cancel_key = "COMMON_CLOSE"
