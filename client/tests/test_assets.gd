extends "res://tests/test_case.gd"


## Missing seat art must show the seat's colour, not a colour picked from the file name (playtest bug).
func test_missing_texture_uses_the_fill_colour() -> bool:
	var red: Color = Color(1, 0, 0)
	var img: Image = Assets.texture("res://missing/seat_99.png", red).get_image()
	assert_true(img.get_pixel(0, 0).is_equal_approx(red), "placeholder is the seat colour")
	var blue: Color = Color(0, 0, 1)
	var img2: Image = Assets.texture("res://missing/seat_99.png", blue).get_image()
	assert_true(img2.get_pixel(0, 0).is_equal_approx(blue), "same path, other colour, other placeholder")
	return true
