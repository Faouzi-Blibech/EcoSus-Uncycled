extends "res://tests/test_case.gd"
## Profile (plan L2): the anonymous player id, parsing /profile replies, the skin to wear.

const ProfileScript := preload("res://autoload/profile.gd")
const TEST_PATH: String = "user://test_profile.cfg"


func test_player_id_is_created_once_and_reloaded_equal() -> bool:
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_PATH))
	var first: String = ProfileScript.load_or_create_id(TEST_PATH)
	var again: String = ProfileScript.load_or_create_id(TEST_PATH)
	assert_true(ProfileScript.is_uuid(first), "a version-4 UUID: " + first)
	assert_eq(again, first, "same id after reloading")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_PATH))
	return true


func test_a_broken_saved_id_is_replaced() -> bool:
	var cfg: ConfigFile = ConfigFile.new()
	cfg.set_value("profile", "player_id", "not-a-uuid")
	cfg.save(TEST_PATH)
	var id: String = ProfileScript.load_or_create_id(TEST_PATH)
	assert_true(ProfileScript.is_uuid(id), "replaced with a valid id")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_PATH))
	return true


func test_uuid_layout() -> bool:
	var zeros: PackedByteArray = PackedByteArray()
	zeros.resize(16)
	assert_eq(ProfileScript.make_uuid(zeros), "00000000-0000-4000-8000-000000000000", "version and variant bits")
	assert_true(not ProfileScript.is_uuid("00000000-0000-1000-8000-000000000000"), "version 1 is not ours")
	return true


func test_profile_reply_sets_points_streak_and_skins() -> bool:
	var p: Node = track(ProfileScript.new())
	var fired: Array[bool] = [false]
	p.changed.connect(func() -> void: fired[0] = true)
	p.apply_profile({"points": 30, "streak": 2, "skins": ["robot", 7, "rabbit"], "best_streak": 4})
	assert_eq(p.points, 30, "points")
	assert_eq(p.streak, 2, "streak")
	assert_eq(p.skins, ["robot", "rabbit"] as Array[String], "skin ids, non-strings dropped")
	assert_true(p.loaded, "loaded")
	assert_true(fired[0], "changed emitted")
	return true


func test_missing_fields_read_as_zero() -> bool:
	var p: Node = track(ProfileScript.new())
	p.apply_profile({})
	assert_eq(p.points, 0, "points")
	assert_eq(p.skins, [] as Array[String], "no skins")
	return true


func test_skin_falls_back_to_default_when_not_unlocked() -> bool:
	var unlocked: Array[String] = ["robot"]
	assert_eq(ProfileScript.effective_skin("robot", unlocked), "robot", "unlocked")
	assert_eq(ProfileScript.effective_skin("eagle", unlocked), "", "locked -> default")
	assert_eq(ProfileScript.effective_skin("", unlocked), "", "default stays default")
	return true


func test_menu_shows_points_and_streak() -> bool:
	var Menu: GDScript = load("res://scenes/menu/menu.gd")
	var before: String = TranslationServer.get_locale()
	TranslationServer.set_locale("en")
	assert_eq(Menu.scan_stats_text(30, 2), "Points: 30   Streak: 2", "English line")
	TranslationServer.set_locale(before)
	return true
