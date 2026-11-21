extends RefCounted
## Eight clients stand still until the match deadline. Run against a server whose content has a
## short match.deadline_s. PASS on game_over with winner "polluters" and reason "deadline".

const Room := preload("res://tools/probe/scenarios/scenario_room.gd")
## Slack on top of the deadline for the last tick and the message to arrive.
const SLACK_S: float = 15.0
## The deadline may fire a tick early; anything earlier than this is not the deadline.
const EARLY_TOLERANCE_S: float = 1.0


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var room := Room.new()
	var result: Dictionary = _play(room, url, grid, deadline_ms)
	room.close_all()
	return result


static func _play(room: Room, url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	var err: String = room.open(url, grid, deadline_ms)
	if not err.is_empty():
		return {"pass": false, "reason": err}
	var limit_s: float = float(room.tuning.get("match", {}).get("deadline_s", 0.0))
	if limit_s <= 0.0:
		return {"pass": false, "reason": "welcome.tuning has no match.deadline_s"}
	var overs: Array[Dictionary] = room.wait_each(room.everyone(), "game_over", limit_s + SLACK_S)
	if overs.is_empty():
		return {"pass": false, "reason": "no game_over for every client within %.0f s (deadline_s %.0f)" % [limit_s + SLACK_S, limit_s]}
	var over: Dictionary = overs[0]
	if str(over.get("winner")) != "polluters" or str(over.get("reason")) != "deadline":
		return {"pass": false, "reason": "game_over was winner %s reason %s, expected polluters / deadline" % [str(over.get("winner")), str(over.get("reason"))]}
	var elapsed_s: float = float(Time.get_ticks_msec() - room.playing_since_ms) / 1000.0
	var match_t: float = float(over.get("match_t", 0.0))
	if elapsed_s < limit_s - EARLY_TOLERANCE_S or match_t < limit_s - EARLY_TOLERANCE_S:
		return {"pass": false, "reason": "game_over came too early to be the deadline: %.1f s after the first playing snapshot, match_t %.1f, match.deadline_s %.0f" % [elapsed_s, match_t, limit_s]}
	for entry in over.get("timeline", []):
		var kind: String = str(entry.get("kind"))
		if kind == "meeting" or kind == "ejection":
			return {"pass": false, "reason": "timeline has a %s entry, so a vote may have ended the match, not the deadline" % kind}
	return {"pass": true, "evidence": "game_over winner=%s reason=%s %.1f s after the first playing snapshot (match.deadline_s %.0f, match_t %.1f), no meeting or ejection in the timeline; %d of %d clients got game_over" % [str(over["winner"]), str(over["reason"]), elapsed_s, limit_s, match_t, overs.size(), Room.PLAYERS]}
