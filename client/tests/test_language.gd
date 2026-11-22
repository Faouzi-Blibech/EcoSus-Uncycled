extends "res://tests/test_case.gd"
## Languages: English by default (never the computer's language), French, Arabic and Swedish
## selectable, each with its flag, and an Arabic-capable fallback font behind Godot's default font.

const PaperKit := preload("res://ui/widgets/paper_kit.gd")


func test_languages_and_english_default() -> bool:
	assert_eq(Settings.LANGUAGES, ["en", "fr", "ar", "sv"] as Array[String], "the four languages")
	var fresh: Object = track(load("res://autoload/settings.gd").new())
	assert_eq(fresh.language, "en", "English until the player picks another")
	return true


func test_arabic_letters_have_a_font() -> bool:
	var found: bool = false
	for f in ThemeDB.fallback_font.fallbacks:
		if (f as Font).has_char(0x0639):
			found = true
	assert_true(found, "a fallback font draws Arabic (ain)")
	return true


func test_every_language_is_loaded() -> bool:
	var loaded: PackedStringArray = TranslationServer.get_loaded_locales()
	for code in Settings.LANGUAGES:
		assert_true(loaded.has(code), "translation loaded: " + code)
	return true


## Swedish is complete: every English key has a Swedish line.
func test_swedish_has_every_key() -> bool:
	var en: Translation = TranslationServer.get_translation_object("en")
	var sv: Translation = TranslationServer.get_translation_object("sv")
	assert_true(en != null and sv != null, "en and sv loaded")
	var missing: Array[String] = []
	for key in en.get_message_list():
		if sv.get_message(key) == StringName():
			missing.append(String(key))
	assert_eq(missing, [] as Array[String], "keys without Swedish")
	return true


## Flag colours at points that tell each flag apart: (language, u, v, colour).
func test_flags_show_the_right_colours() -> bool:
	var cases: Array = [
		["fr", 0.1, 0.5, FlagArt.FR_BLUE],
		["fr", 0.5, 0.5, Color.WHITE],
		["fr", 0.9, 0.5, FlagArt.FR_RED],
		["sv", 6.0 / 16.0, 0.1, FlagArt.SE_YELLOW],
		["sv", 0.9, 0.5, FlagArt.SE_YELLOW],
		["sv", 0.1, 0.1, FlagArt.SE_BLUE],
		["ar", 0.1, 0.1, FlagArt.TN_RED],
		["ar", 0.5, 0.5 - 0.22, Color.WHITE],
		["ar", 0.5 + 0.25 * 0.25 / 1.5, 0.5, FlagArt.TN_RED],
		["en", 0.05, 0.05, FlagArt.US_BLUE],
		["en", 0.45, 0.5, FlagArt.US_RED],
		["en", 0.95, 0.5, FlagArt.UK_RED],
		["en", 0.75, 0.9, FlagArt.UK_BLUE],
	]
	for c in cases:
		assert_eq(FlagArt.color_at(c[0], c[1], c[2]), c[3], "%s at (%.2f, %.2f)" % [c[0], c[1], c[2]])
	return true


func test_every_language_has_a_flag() -> bool:
	for code in Settings.LANGUAGES:
		var tex: Texture2D = FlagArt.texture(code)
		assert_true(tex != null and tex.get_width() == FlagArt.WIDTH_PX, "flag for " + code)
	return true


## Arabic mirrors layouts; the drawn art stage must not (regression: the home logo and the two big
## buttons were off screen in Arabic).
func test_the_art_stage_stays_in_place_in_arabic() -> bool:
	var before: String = TranslationServer.get_locale()
	TranslationServer.set_locale("ar")
	var host: Control = track(Control.new()) as Control
	host.layout_direction = Control.LAYOUT_DIRECTION_RTL
	host.size = Vector2(640.0, 360.0)
	(Engine.get_main_loop() as SceneTree).current_scene.add_child(host)
	var stage: Control = PaperKit.stage(host)
	var pic: TextureRect = PaperKit.picture(null, Rect2(250.0, 10.0, 140.0, 53.0))
	stage.add_child(pic)
	TranslationServer.set_locale(before)
	assert_eq(pic.get_rect(), Rect2(250.0, 10.0, 140.0, 53.0), "logo where the layout puts it")
	return true
