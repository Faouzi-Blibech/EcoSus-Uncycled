extends "res://tests/test_case.gd"
## The create and join pages compile (a parse error there only shows when the page opens).

const PAGES: Array[String] = [
	"res://scenes/create/create.gd",
	"res://scenes/join/join.gd",
	"res://scenes/entry_page.gd",
]


func test_pages_compile() -> bool:
	for path in PAGES:
		var script: GDScript = load(path) as GDScript
		assert_true(script != null and script.can_instantiate(), "%s compiles" % path)
	return true

