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


## Time ran out with the pollution bar not full: the Trashers win by the clock, and the screen says so loudly.
func test_deadline_reason_is_loud_and_explicit() -> bool:
	assert_true(Results.reason_font_size("deadline") > Results.reason_font_size("pollution_full"), "the deadline reason is bigger than the others")
	assert_true(tr("RESULT_DEADLINE").contains("not clean"), "it says the campus was not clean")
	assert_true(tr("RESULT_DEADLINE").contains("Trashers win"), "it says the Trashers win")
	return true
