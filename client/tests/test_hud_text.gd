extends "res://tests/test_case.gd"
## Player-facing HUD text: key hints only where there is a keyboard, ping only when it is bad, no
## debug phase line, and the safe-area insets for punch-hole phones.


func _host() -> Node:
	return (Engine.get_main_loop() as SceneTree).current_scene


func _hud() -> Hud:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var hud: Hud = track(Hud.new()) as Hud
	_host().add_child(hud)
	return hud


func test_key_hint_is_shown_without_a_touchscreen_only() -> bool:
	assert_eq(Hud.action_text("Use", "E", false), "Use\n[E]", "keyboard")
	assert_eq(Hud.action_text("Use", "E", true), "Use", "phone: no key hint")
	assert_eq(Hud.action_text("Use", "", false), "Use", "no key bound")
	return true


func test_trasher_buttons_are_repollute_and_the_ability() -> bool:
	var hud: Hud = _hud()
	var label: String = str(TranslationServer.translate(&"ACT_REPOLLUTE"))
	assert_true((hud._buttons["repollute"] as Button).text.begins_with(label), "RE-POLLUTE label")
	assert_true(hud._buttons.has("ability"), "ability slot")
	assert_true(not hud._buttons.has("hit") and not hud._buttons.has("dump"), "no v1 buttons")
	return true


func test_ping_shows_only_when_the_connection_is_weak() -> bool:
	var hud: Hud = _hud()
	var saved: int = Net.ping_ms
	Net.ping_ms = 40
	hud._update_header()
	assert_eq(hud._ping_label.text, "", "a good ping is not shown")
	Net.ping_ms = Hud.WEAK_PING_MS + 50
	hud._update_header()
	assert_eq(hud._ping_label.text, "Weak connection", "a bad ping is")
	Net.ping_ms = saved
	return true


func test_safe_insets_scale_the_notch_to_logical_pixels() -> bool:
	var insets: Vector4 = Hud.safe_insets(Rect2i(120, 0, 2280, 1080), Vector2i(2400, 1080), Vector2i(2400, 1080), Vector2(640.0, 360.0))
	assert_near(insets.x, 32.0, 0.01, "left")
	assert_near(insets.y, 0.0, 0.01, "top")
	assert_near(insets.z, 0.0, 0.01, "right")
	assert_near(insets.w, 0.0, 0.01, "bottom")
	var none: Vector4 = Hud.safe_insets(Rect2i(0, 0, 0, 0), Vector2i(2400, 1080), Vector2i(2400, 1080), Vector2(640.0, 360.0))
	assert_eq(none, Vector4.ZERO, "an empty safe area means no insets")
	return true


func test_match_timer_counts_the_deadline_clock_and_ignores_meeting_time() -> bool:
	var hud: Hud = _hud()
	var saved: Array = [Session.match_t, Session.clock_t, Session.deadline_t]
	Session._on_snapshot({"match_t": 300.0, "clock_t": 100.0, "deadline_t": 600.0})
	hud._update_header()
	assert_eq(hud._timer_label.text, "8:20", "600 s minus 100 s of active play")
	Session._on_snapshot({"match_t": 420.0, "clock_t": 100.0, "deadline_t": 600.0})
	hud._update_header()
	assert_eq(hud._timer_label.text, "8:20", "a 120 s meeting does not move the timer")
	Session.match_t = saved[0]
	Session.clock_t = saved[1]
	Session.deadline_t = saved[2]
	return true


func test_banner_shows_fire_countdown_and_held_count() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var fire: Dictionary = {"kind": "fire", "countdown": 31.2, "held": ["fire_a"]}
	assert_eq(SabotageBanner.text_for(fire, 2, false), "FIRE! Out of control in 0:32\nHold every fire console together (1/2)", "m:ss rounded up, held of total")
	assert_eq(SabotageBanner.text_for({"kind": "fire", "countdown": 65.0, "held": []}, 2, false).split("\n")[0], "FIRE! Out of control in 1:05", "minutes")
	var smog: Dictionary = {"kind": "smog"}
	assert_true(not SabotageBanner.text_for(smog, 2, false).contains("Repairing"), "no repair line yet")
	assert_true(SabotageBanner.text_for(smog, 2, true).ends_with("Repairing..."), "open-ended repairing line")
	return true


func test_hud_banner_follows_the_session_sabotage() -> bool:
	var hud: Hud = _hud()
	var saved: Dictionary = Session.sabotage
	Session.sabotage = {"kind": "fire", "countdown": 40.0, "held": []}
	hud._process(0.0)
	assert_true(hud._banner.visible, "banner shown while a fire burns")
	assert_true(hud._banner.text.contains("0:40"), "countdown from the session")
	Session.sabotage = {}
	hud._process(0.0)
	assert_true(not hud._banner.visible, "banner gone when it ends")
	Session.sabotage = saved
	return true


func test_sabotage_ended_toast_text_per_result() -> bool:
	assert_eq(Hud.ended_toast_key("fixed"), "SABOTAGE_FIXED", "fixed")
	assert_eq(Hud.ended_toast_key("expired"), "SABOTAGE_EXPIRED", "expired")
	assert_eq(Hud.ended_toast_key("cleared"), "", "a meeting clearing the fire is no failure: no toast")
	return true


func test_cancel_rejections_show_no_toast() -> bool:
	assert_true(not Hud.shows_rejection({"action": "clean_cancel", "reason": "target_invalid"}), "a cancel after a good clean")
	assert_true(Hud.shows_rejection({"action": "clean_start", "reason": "out_of_range"}), "a refused start is shown")
	assert_true(Hud.shows_rejection({"action": "fix", "reason": "not_allowed"}), "a refused fix is shown")
	return true


func test_sabotage_row_is_only_there_for_polluters() -> bool:
	var hud: Hud = _hud()
	var saved: String = Session.role
	Session.role = "citizen"
	hud._process(0.0)
	assert_true(not hud._sabotage_slot.visible, "no empty bottom row for a citizen")
	Session.role = "polluter"
	hud._process(0.0)
	assert_true(hud._sabotage_slot.visible, "a polluter has the row")
	Session.role = saved
	return true


func test_banner_repair_line_shows_the_progress_in_percent() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var smog: Dictionary = {"kind": "smog"}
	assert_true(SabotageBanner.text_for(smog, 2, true, 0.67).ends_with("Repairing... 67%"), "two thirds")
	assert_true(SabotageBanner.text_for(smog, 2, true, 0.0).ends_with("Repairing... 0%"), "just started")
	assert_true(SabotageBanner.text_for(smog, 2, true, 1.4).ends_with("Repairing... 100%"), "never above 100")
	assert_true(not SabotageBanner.text_for(smog, 2, false, 0.5).contains("Repairing"), "no line when not repairing")
	return true


## An out player sees the same time left as everyone: the deadline clock, not match time (a meeting
## does not eat it), also right after a rejoin.
func test_spectator_time_left_uses_the_deadline_clock() -> bool:
	var hud: Hud = _hud()
	var saved: Array = [Session.match_t, Session.clock_t, Session.deadline_t, Session.is_out]
	Session.match_t = 400.0
	Session.clock_t = 100.0
	Session.deadline_t = 600.0
	Session.is_out = true
	hud._update_spectator()
	var shown: String = hud._spectator._time.text
	Session.match_t = saved[0]
	Session.clock_t = saved[1]
	Session.deadline_t = saved[2]
	Session.is_out = saved[3]
	assert_eq(shown, "8:20", "600 s minus 100 s of active play, not minus 400 s of match time")
	return true


func test_machine_jam_banner_and_repair_percent() -> bool:
	var jam: Dictionary = {"kind": "machine"}
	assert_eq(SabotageBanner.text_for(jam, 0, false), str(TranslationServer.translate(&"SABOTAGE_BANNER_MACHINE")), "jam line")
	assert_true(SabotageBanner.text_for(jam, 0, true, 0.5).ends_with(" 50%"), "repair percent")
	return true
