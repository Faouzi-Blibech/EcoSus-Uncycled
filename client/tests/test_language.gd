extends "res://tests/test_case.gd"
## Languages: English by default (never the computer's language), French and Arabic selectable, and
## an Arabic-capable fallback font behind Godot's default font.


func test_languages_and_english_default() -> bool:
	assert_eq(Settings.LANGUAGES, ["en", "fr", "ar"] as Array[String], "the three languages")
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
