extends "res://tests/test_case.gd"
## Pure meeting rules: who can be voted for, who may vote, the timer text.

const VoteRules := preload("res://scenes/meeting/vote_rules.gd")


func test_votable_excludes_out() -> bool:
	var seats: Array = [{"seat": 0}, {"seat": 1}, {"seat": 2}, {"seat": 3}]
	var got: Array[int] = VoteRules.votable_seats(seats, {2: true})
	assert_eq(got, [0, 1, 3] as Array[int], "out seat removed, self kept")
	return true


func test_can_vote_rules() -> bool:
	assert_true(VoteRules.can_vote(false, "voting", false), "active, voting, not voted")
	assert_true(not VoteRules.can_vote(true, "voting", false), "out cannot vote")
	assert_true(not VoteRules.can_vote(false, "discussion", false), "not during discussion")
	assert_true(not VoteRules.can_vote(false, "voting", true), "only once")
	return true


func test_mmss() -> bool:
	assert_eq(VoteRules.mmss(130.4), "2:10", "130.4")
	assert_eq(VoteRules.mmss(5.0), "0:05", "5.0")
	assert_eq(VoteRules.mmss(-3.0), "0:00", "negative clamps")
	return true


func test_show_timer() -> bool:
	assert_true(VoteRules.show_timer("meeting", "discussion"), "discussion")
	assert_true(VoteRules.show_timer("meeting", "voting"), "voting")
	assert_true(not VoteRules.show_timer("meeting", "results"), "results has no timer")
	assert_true(not VoteRules.show_timer("ejection", "voting"), "ejection has no timer")
	assert_true(not VoteRules.show_timer("playing", "discussion"), "other phase")
	return true


func test_can_show_tally() -> bool:
	assert_true(not VoteRules.can_show_tally({}), "rejoin during results has no payload")
	assert_true(VoteRules.can_show_tally({"votes": {"0": 1}, "tie": false}), "real result")
	return true


func test_unlocks_vote() -> bool:
	assert_true(VoteRules.unlocks_vote({"t": "action_rejected", "action": "vote", "reason": "wrong_phase"}), "vote")
	assert_true(not VoteRules.unlocks_vote({"action": "use"}), "other action")
	assert_true(not VoteRules.unlocks_vote({"reason": "x"}), "missing action")
	return true


func test_ejection_reveal() -> bool:
	assert_eq(VoteRules.ejection_reveal(null), "MEETING_EJECTED", "role not revealed")
	assert_eq(VoteRules.ejection_reveal(true), "EJECTION_WAS_POLLUTER", "polluter")
	assert_eq(VoteRules.ejection_reveal(false), "EJECTION_WAS_NOT_POLLUTER", "not polluter")
	return true


func test_voters_by_target_groups_and_skips() -> bool:
	var got: Dictionary = load("res://scenes/meeting/vote_rules.gd").voters_by_target({"3": 1, "0": 1, "2": "skip", "1": 2, "4": null})
	assert_eq(got[1], [0, 3], "two votes for seat 1, sorted")
	assert_eq(got[2], [1], "one vote for seat 2")
	assert_eq(got[-1], [2, 4], "skip and timed-out under -1")
	return true
