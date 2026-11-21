extends "res://tests/test_case.gd"
## The infected countdown label and the gas-room tint.


func test_whole_seconds_rounds_up_and_never_goes_negative() -> bool:
	assert_eq(VirusTimer.whole_seconds(23.2), 24, "23.2")
	assert_eq(VirusTimer.whole_seconds(0.3), 1, "0.3")
	assert_eq(VirusTimer.whole_seconds(0.0), 0, "0.0")
	assert_eq(VirusTimer.whole_seconds(-2.0), 0, "negative")
	return true


func test_urgent_in_the_last_ten_seconds() -> bool:
	assert_true(VirusTimer.is_urgent(10.0), "10.0")
	assert_true(not VirusTimer.is_urgent(10.5), "10.5")
	assert_true(not VirusTimer.is_urgent(-1.0), "-1.0")
	return true


func test_timer_hides_when_not_infected() -> bool:
	load("res://i18n/i18n_loader.gd").load_csv()
	TranslationServer.set_locale("en")
	var timer: VirusTimer = track(VirusTimer.new()) as VirusTimer
	timer.show_seconds(-1.0)
	assert_true(not timer.visible, "hidden below 0")
	timer.show_seconds(23.2)
	assert_true(timer.visible, "shown")
	assert_true(timer.text.contains("24"), "text has 24: %s" % timer.text)
	return true


func test_gas_alpha_grows_toward_landing() -> bool:
	assert_near(GasOverlay.alpha_for(3.0, 3.0), 0.15, 0.001, "start")
	assert_near(GasOverlay.alpha_for(0.0, 3.0), 0.6, 0.001, "landed")
	assert_near(GasOverlay.alpha_for(1.5, 3.0), 0.375, 0.001, "middle")
	assert_near(GasOverlay.alpha_for(5.0, 3.0), 0.15, 0.001, "clamped high")
	assert_near(GasOverlay.alpha_for(-1.0, 3.0), 0.6, 0.001, "clamped low")
	assert_near(GasOverlay.alpha_for(1.0, 0.0), 0.6, 0.001, "no warning")
	return true


func test_gas_overlay_shows_and_clears() -> bool:
	var gas: GasOverlay = track(GasOverlay.new()) as GasOverlay
	gas.show_room(Rect2(1, 1, 8, 6), 3.0, 3.0)
	assert_true(gas.visible, "visible")
	gas.clear()
	assert_true(not gas.visible, "hidden")
	return true
