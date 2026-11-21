extends RefCounted
## Same as meeting_bots, but the client never calls a meeting and never talks: it only reads the first meeting the bots start.

const Base := preload("res://tools/probe/scenarios/meeting_bots.gd")
const DEFAULT_TIMEOUT_S: float = 420.0


static func run(url: String, grid: GridPath, deadline_ms: int) -> Dictionary:
	return Base.play(url, grid, deadline_ms, false)
