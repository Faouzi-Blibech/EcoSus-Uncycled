extends "res://tests/test_case.gd"
## A rejected action reaches the player as the toast text of its reason.


## A node of the live tree to hang the HUD on (the Toast tween needs the tree).
func _host() -> Node:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	return tree.current_scene


func test_action_rejected_sets_the_toast_text() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var host: Node = _host()
	assert_true(host != null, "a live scene to host the HUD")
	if host == null:
		return true
	var hud: Hud = track(Hud.new()) as Hud
	host.add_child(hud)
	hud._on_action_rejected({"reason": "on_cooldown"})
	var toast: Toast = hud._toast
	assert_eq(toast.text, "Not ready yet, wait a moment.", "known reason")
	assert_true(toast.visible, "shown")
	hud._on_action_rejected({"reason": "some_new_reason"})
	assert_eq(toast.text, "Action refused.", "unknown reason falls back")
	return true
