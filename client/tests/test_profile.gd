extends "res://tests/test_case.gd"
## Profile (autoload): the player id kept on the device, the /profile fields, and how each platform
## opens a scan link. No network: the parsing and the rules are tested directly.

const PROFILE_SCRIPT: String = "res://autoload/profile.gd"
const TEST_PATH: String = "user://test_profile.cfg"
const UUID_RE: String = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"


func _profile() -> Object:
	return track(load(PROFILE_SCRIPT).new())


func _cleanup() -> void:
	if FileAccess.file_exists(TEST_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_PATH))


func test_player_id_is_a_uuid_created_once_and_kept() -> bool:
	_cleanup()
	var first: String = _profile().load_or_create_id(TEST_PATH)
	var regex: RegEx = RegEx.create_from_string(UUID_RE)
	assert_true(regex.search(first) != null, "uuid v4: " + first)
	var again: String = _profile().load_or_create_id(TEST_PATH)
	assert_eq(again, first, "reloaded, not recreated")
	_cleanup()
	return true


func test_new_uuids_differ() -> bool:
	var script: GDScript = load(PROFILE_SCRIPT)
	assert_true(script.new_uuid() != script.new_uuid(), "random")
	return true


func test_apply_profile_reads_points_streak_and_skins() -> bool:
	var p: Object = _profile()
	p.apply_profile({"points": 30, "streak": 2, "skins": ["eco_novice"], "scans_today": 1})
	assert_eq(p.points, 30, "points")
	assert_eq(p.streak, 2, "streak")
	assert_eq(p.skins, ["eco_novice"], "skins")
	return true


func test_selected_skin_falls_back_when_not_unlocked() -> bool:
	var p: Object = _profile()
	p.skin = "planet_legend"
	p.apply_profile({"points": 10, "streak": 1, "skins": ["eco_novice"]})
	assert_eq(p.skin, "", "not unlocked: no skin")
	p.skin = "eco_novice"
	p.apply_profile({"points": 10, "streak": 1, "skins": ["eco_novice"]})
	assert_eq(p.skin, "eco_novice", "unlocked: kept")
	return true


## Web: the same tab goes to the scan page (its Back button returns to the game). Android: the browser.
## No QR anywhere: the photo is taken on the device that runs the game.
func test_open_mode_per_platform() -> bool:
	var script: GDScript = load(PROFILE_SCRIPT)
	assert_eq(script.open_mode(true, false), "same_tab", "web")
	assert_eq(script.open_mode(true, true), "same_tab", "web on a phone")
	assert_eq(script.open_mode(false, true), "browser", "android app")
	assert_eq(script.open_mode(false, false), "browser", "windows test build: same as the app, no QR")
	return true


func test_scan_changed_when_points_or_streak_move() -> bool:
	var script: GDScript = load(PROFILE_SCRIPT)
	assert_true(not script.scan_counted({"points": 10, "streak": 1}, {"points": 10, "streak": 1}), "same")
	assert_true(script.scan_counted({"points": 10, "streak": 1}, {"points": 20, "streak": 1}), "points up")
	assert_true(script.scan_counted({"points": 30, "streak": 1}, {"points": 30, "streak": 2}), "streak up")
	assert_true(script.scan_counted({"points": 30, "scans_today": 3}, {"points": 30, "scans_today": 4}), "a 4th scan scores no points but counts")
	return true
