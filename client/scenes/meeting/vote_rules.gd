extends RefCounted
## Pure meeting rules (no nodes), so they can be tested headless.


## Seats that can receive a vote: every seat not out (self included).
static func votable_seats(seats: Array, out_seats: Dictionary) -> Array[int]:
	var result: Array[int] = []
	for s in seats:
		if typeof(s) != TYPE_DICTIONARY:
			continue
		var seat: int = int((s as Dictionary).get("seat", -1))
		if seat >= 0 and not bool(out_seats.get(seat, false)):
			result.append(seat)
	return result


static func can_vote(me_out: bool, stage: String, already_voted: bool) -> bool:
	return not me_out and stage == "voting" and not already_voted


## Seconds to "m:ss", clamped at 0.
static func mmss(t: float) -> String:
	var total: int = maxi(int(t), 0)
	return "%d:%02d" % [total / 60, total % 60]


## The countdown is shown only while the meeting still has a server deadline to show (the server
## can close voting early, so results and ejection must not keep counting down).
static func show_timer(phase: String, stage: String) -> bool:
	return phase == "meeting" and (stage == "discussion" or stage == "voting")


## A meeting_result payload is needed for a tally; a rejoin during results has none.
static func can_show_tally(result: Dictionary) -> bool:
	return not result.is_empty()


## An action_rejected for the vote releases the local "already voted" lock.
static func unlocks_vote(rejection: Dictionary) -> bool:
	return str(rejection.get("action", "")) == "vote"


## Translation key for the ejection outcome. was_polluter is null when roles are not revealed.
static func ejection_reveal(was_polluter: Variant) -> String:
	if was_polluter == null:
		return "MEETING_EJECTED"
	if was_polluter == true:
		return "EJECTION_WAS_POLLUTER"
	return "EJECTION_WAS_NOT_POLLUTER"


## Translation key of the "N polluters remain" line; "" when the count is unknown (-1) or nobody is left.
static func polluters_left_key(left: int) -> String:
	if left < 1:
		return ""
	return "EJECTION_LEFT_ONE" if left == 1 else "EJECTION_LEFT_MANY"
