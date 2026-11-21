extends "res://tests/test_case.gd"
## Protocol 2 (Cleaners vs Trashers) fields in Session: snapshot, rejoin welcome, new match.
## Snapshots here skip `me.pos`: Net turns positions into Vector2 before Session sees them.

const TEST_SAVE: String = "user://test_session_v2.cfg"
const TEST_EJECTIONS: String = "user://test_ejections_v2.cfg"


func _session() -> Object:
	var s: Object = track(load("res://autoload/session.gd").new())
	s.save_path = TEST_SAVE
	s.ejections_path = TEST_EJECTIONS
	return s


func _cleanup_save() -> void:
	for path in [TEST_SAVE, TEST_EJECTIONS]:
		if FileAccess.file_exists(path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(path))


func _cleaner_snapshot() -> Dictionary:
	return {
		"t": "snapshot", "tick": 10, "s": 5.0, "phase": "playing", "meter": 30.0,
		"me": {"vision": 4.0, "emergencies_left": 2,
			"bags": 1, "cap": 1, "tool": false, "heals": 0, "masks": 1, "infected_s": 24.0},
		"spots": [{"id": "market_bins", "state": "dirty"}, {"id": "depot_sink", "state": "clean"}],
		"machine": {"jammed": false},
		"gas": null,
	}


func _trasher_snapshot() -> Dictionary:
	return {
		"t": "snapshot", "tick": 11, "s": 5.05, "phase": "playing", "meter": 20.0,
		"me": {"vision": 5.0, "emergencies_left": 2,
			"repollutes": 2, "ability": "virus",
			"cd": {"repollute": 4.5, "virus": 0.0, "gas": 0.0, "sabotage": 10.0}},
		"spots": [{"id": "market_bins", "state": "clean"}],
		"machine": {"jammed": true},
		"gas": {"room": "gas_market", "in_s": 2.1},
	}


func test_client_speaks_protocol_2() -> bool:
	assert_eq(Net.PROTOCOL_VERSION, 2, "protocol version sent in hello")
	return true


func test_cleaner_snapshot_fields() -> bool:
	var s: Object = _session()
	s._on_snapshot(_cleaner_snapshot())
	assert_near(s.meter, 30.0, 0.001, "meter is cleanliness")
	assert_eq(s.spots, {"market_bins": "dirty", "depot_sink": "clean"}, "spots by id")
	assert_eq(s.bags, 1, "bags")
	assert_eq(s.bag_cap, 1, "cap")
	assert_eq(s.has_tool, false, "tool")
	assert_eq(s.heals, 0, "heals")
	assert_eq(s.masks, 1, "masks")
	assert_near(s.infected_s, 24.0, 0.001, "infected countdown")
	assert_eq(s.machine_jammed, false, "machine runs")
	assert_eq(s.gas_room, "", "no gas warning")
	assert_eq(s.repollutes, 0, "a Cleaner has no re-pollute count")
	assert_eq(s.ability, "", "a Cleaner has no ability")
	return true


func test_trasher_snapshot_fields() -> bool:
	var s: Object = _session()
	s._on_snapshot(_trasher_snapshot())
	assert_eq(s.repollutes, 2, "re-pollutes")
	assert_eq(s.ability, "virus", "ability")
	assert_near(float(s.cooldowns.get("repollute", -1.0)), 4.5, 0.001, "repollute cooldown")
	assert_near(float(s.cooldowns.get("sabotage", -1.0)), 10.0, 0.001, "sabotage cooldown")
	assert_eq(s.bags, 0, "a Trasher has no bags")
	assert_eq(s.bag_cap, 0, "cap 0 means no bag counter")
	assert_near(s.infected_s, -1.0, 0.001, "a Trasher is never infected")
	assert_eq(s.machine_jammed, true, "machine jammed")
	assert_eq(s.gas_room, "gas_market", "gas room")
	assert_near(s.gas_in_s, 2.1, 0.001, "gas warning time")
	return true


func test_missing_fields_mean_not_mine() -> bool:
	var s: Object = _session()
	s._on_snapshot(_cleaner_snapshot())
	var healed: Dictionary = _cleaner_snapshot()
	healed["me"].erase("infected_s")
	healed["gas"] = null
	s._on_snapshot(healed)
	assert_near(s.infected_s, -1.0, 0.001, "no infected_s after a heal")
	s._on_snapshot(_trasher_snapshot())
	var calm: Dictionary = _trasher_snapshot()
	calm["gas"] = null
	calm["me"]["ability"] = null
	s._on_snapshot(calm)
	assert_eq(s.gas_room, "", "gas over")
	assert_near(s.gas_in_s, 0.0, 0.001, "no gas time")
	assert_eq(s.ability, "", "null ability")
	return true


func test_rejoin_welcome_restores_v2_fields() -> bool:
	var s: Object = _session()
	s._on_welcome({
		"seat": 1, "room": "ABCD", "token": "tok", "phase": "playing",
		"state": {"phase": "playing", "role": "citizen", "status": "active", "tasks": [], "poison": null,
			"bags": 2, "cap": 3, "tool": true, "heals": 1, "masks": 0, "infected_s": 12.0},
	})
	assert_eq(s.bags, 2, "bags")
	assert_eq(s.bag_cap, 3, "backpack cap")
	assert_eq(s.has_tool, true, "tool")
	assert_eq(s.heals, 1, "heals")
	assert_near(s.infected_s, 12.0, 0.001, "infection survives a rejoin")
	_cleanup_save()
	return true


func test_new_match_resets_v2_fields() -> bool:
	var s: Object = _session()
	s._on_snapshot(_trasher_snapshot())
	s._on_match_started({"role": "citizen", "spawn": Vector2.ZERO})
	assert_eq(s.spots, {}, "spots")
	assert_eq(s.repollutes, 0, "re-pollutes")
	assert_eq(s.ability, "", "ability")
	assert_eq(s.machine_jammed, false, "machine")
	assert_eq(s.gas_room, "", "gas")
	assert_near(s.infected_s, -1.0, 0.001, "infection")
	_cleanup_save()
	return true
