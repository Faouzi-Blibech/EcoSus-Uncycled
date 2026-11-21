extends "res://tests/test_case.gd"
## Results screen: the return-to-lobby countdown comes from the welcome tuning.

const Results := preload("res://scenes/results/results.gd")


func test_lobby_delay_reads_tuning() -> bool:
	assert_near(Results.lobby_delay_s({"match": {"return_to_lobby_s": 30}}), 30.0, 0.001, "from tuning")
	return true


func test_lobby_delay_fallback() -> bool:
	assert_near(Results.lobby_delay_s({}), Results.DEFAULT_LOBBY_DELAY_S, 0.001, "empty tuning")
	assert_near(Results.lobby_delay_s({"match": {"return_to_lobby_s": 0}}), Results.DEFAULT_LOBBY_DELAY_S, 0.001, "zero is not a delay")
	return true


func test_debrief_text_only_when_present() -> bool:
	assert_eq(Results.debrief_text({}), "", "absent")
	assert_eq(Results.debrief_text({"debrief_text": null}), "", "null")
	assert_eq(Results.debrief_text({"debrief_text": "Ana dumped twice."}), "Ana dumped twice.", "shown as sent")
	return true
