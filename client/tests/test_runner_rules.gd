extends "res://tests/test_case.gd"
## The runner's per-file rule: a test file must load, compile and contain a test method.
## Scripts are built at runtime so no broken file sits in the repo.

const RUNNER: GDScript = preload("res://tests/test_runner.gd")


func _script(source: String) -> GDScript:
	var s: GDScript = GDScript.new()
	s.source_code = source
	s.reload()
	return s


func test_null_script_cannot_load() -> bool:
	assert_eq(RUNNER.file_problems(null, "a.gd"), ["a.gd: cannot load"], "null")
	return true


func test_uncompiled_script_cannot_compile() -> bool:
	var s: GDScript = GDScript.new()
	assert_eq(RUNNER.file_problems(s, "b.gd"), ["b.gd: cannot compile"], "not reloaded")
	return true


func test_script_without_test_method_has_no_tests() -> bool:
	var s: GDScript = _script("extends RefCounted\nfunc helper() -> void:\n\tpass\n")
	assert_eq(RUNNER.file_problems(s, "c.gd"), ["c.gd: no tests"], "no test_ method")
	return true


func test_script_with_a_test_method_has_no_problems() -> bool:
	var s: GDScript = _script("extends RefCounted\nfunc test_x() -> bool:\n\treturn true\n")
	assert_eq(RUNNER.file_problems(s, "d.gd"), [] as Array[String], "one test_ method")
	return true


## Runs `test_x` of a runtime-built test script through the runner's own run_test, with its own catcher.
func _run(body: String) -> Array[String]:
	var c: ErrorCatcher = ErrorCatcher.new()
	OS.add_logger(c)
	var problems: Array[String] = await RUNNER.run_test(Engine.get_main_loop() as SceneTree, c, _script("extends \"res://tests/test_case.gd\"\n" + body), "test_x")
	OS.remove_logger(c)
	return problems


func _has_problem(problems: Array[String], fragment: String) -> bool:
	for p in problems:
		if p.contains(fragment):
			return true
	return false


func test_script_error_in_a_callee_fails_the_test() -> bool:
	expect_error("get_name")
	var problems: Array[String] = await _run("""
func _callee() -> void:
	var n = null
	n.get_name()
func test_x() -> bool:
	_callee()
	assert_true(true, "the test itself goes on")
	return true
""")
	assert_true(_has_problem(problems, "unexpected engine error"), "callee error fails the test")
	return true


func test_script_error_in_ready_of_code_under_test_fails_the_test() -> bool:
	expect_error("get_name")
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	var s: GDScript = GDScript.new()
	s.source_code = "extends Node\nfunc _ready() -> void:\n\tvar n = null\n\tn.get_name()\n"
	s.reload()
	var node: Node = track(s.new())
	node._ready()
	assert_true(true, "the test itself goes on")
	return true
""")
	assert_true(_has_problem(problems, "unexpected engine error"), "_ready error fails the test")
	return true


func test_push_error_fails_the_test() -> bool:
	expect_error("boom push")
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	push_error("boom push")
	assert_true(true, "reached")
	return true
""")
	assert_true(_has_problem(problems, "boom push"), "push_error fails the test")
	return true


func test_error_in_a_deferred_call_fails_the_test() -> bool:
	expect_error("late push")
	var problems: Array[String] = await _run("""
func _late() -> void:
	push_error("late push")
func test_x() -> bool:
	call_deferred("_late")
	assert_true(true, "reached")
	return true
""")
	assert_true(_has_problem(problems, "late push"), "deferred error fails the test")
	return true


func test_declared_error_that_never_happens_fails_the_test() -> bool:
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	expect_error("never raised")
	assert_true(true, "reached")
	return true
""")
	assert_true(_has_problem(problems, "never raised"), "missing expected error fails")
	return true


func test_declared_error_that_happens_passes() -> bool:
	expect_error("declared one")
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	expect_error("declared one")
	push_error("declared one")
	assert_true(true, "reached")
	return true
""")
	assert_eq(problems, [] as Array[String], "declared and raised")
	return true


func test_a_warning_does_not_fail_the_test() -> bool:
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	push_warning("only a warning")
	assert_true(true, "reached")
	return true
""")
	assert_eq(problems, [] as Array[String], "warning is not an error")
	return true


func _tree() -> SceneTree:
	return Engine.get_main_loop() as SceneTree


func _catcher() -> ErrorCatcher:
	var c: ErrorCatcher = ErrorCatcher.new()
	OS.add_logger(c)
	return c


func test_error_in_an_autoload_ready_fails_the_run() -> bool:
	expect_error("get_name")
	var scripts: Array[GDScript] = [_script("extends Node\nfunc _ready() -> void:\n\tvar n = null\n\tn.get_name()\n")]
	var c: ErrorCatcher = _catcher()
	var problems: Array[String] = RUNNER.setup_problems(_tree(), c, scripts)
	OS.remove_logger(c)
	assert_true(_has_problem(problems, "in an autoload's setup"), "autoload _ready error is a run problem")
	return true


func test_autoload_scripts_lists_every_project_autoload() -> bool:
	var count: int = 0
	for p in ProjectSettings.get_property_list():
		if str(p["name"]).begins_with("autoload/"):
			count += 1
	assert_true(count >= 4, "the project has autoloads")
	assert_eq(RUNNER.autoload_scripts().size(), count, "every autoload script is re-run under the catcher")
	return true


func test_static_init_error_while_a_file_loads_fails_the_run() -> bool:
	expect_error("static boom")
	var c: ErrorCatcher = _catcher()
	var loader: Callable = func() -> GDScript:
		return _script("extends RefCounted\nstatic func _static_init() -> void:\n\tpush_error(\"static boom\")\nfunc test_x() -> bool:\n\treturn true\n")
	var problems: Array[String] = RUNNER.load_problems(c, loader, "z.gd")
	OS.remove_logger(c)
	assert_true(_has_problem(problems, "while loading z.gd"), "a load-time error is a run problem")
	return true


func test_error_after_the_last_test_fails_the_run() -> bool:
	expect_error("late node")
	expect_error("runner self-check")
	var late: Node = _script("extends Node\nvar frames: int = 0\nfunc _process(_d: float) -> void:\n\tframes += 1\n\tif frames == 3:\n\t\tpush_error(\"late node\")\n").new() as Node
	_tree().root.add_child(late)
	var c: ErrorCatcher = _catcher()
	var problems: Array[String] = await RUNNER.final_problems(_tree(), c)
	OS.remove_logger(c)
	late.free()
	assert_true(_has_problem(problems, "after the last test"), "a late error is a run problem")
	assert_true(_has_problem(problems, "late node"), "with its message")
	return true


func test_a_silenced_catcher_fails_the_final_check() -> bool:
	var c: ErrorCatcher = _catcher()
	Engine.print_error_messages = false
	var problems: Array[String] = await RUNNER.final_problems(_tree(), c)
	Engine.print_error_messages = true
	OS.remove_logger(c)
	assert_true(_has_problem(problems, "stopped hearing errors"), "a deaf catcher fails the run")
	return true


func test_a_test_that_leaves_print_error_messages_off_fails() -> bool:
	var problems: Array[String] = await _run("""
func test_x() -> bool:
	Engine.print_error_messages = false
	push_error("silenced")
	assert_true(true, "reached")
	return true
""")
	assert_true(Engine.print_error_messages, "the runner switches it back on")
	assert_true(_has_problem(problems, "print_error_messages"), "the test is failed for it")
	return true


func test_error_between_tests_is_not_dropped() -> bool:
	expect_error("between tests")
	var c: ErrorCatcher = _catcher()
	push_error("between tests")
	var problems: Array[String] = await RUNNER.run_test(_tree(), c, _script("extends \"res://tests/test_case.gd\"\nfunc test_x() -> bool:\n\tassert_true(true, \"ok\")\n\treturn true\n"), "test_x", "t")
	OS.remove_logger(c)
	assert_true(_has_problem(problems, "before t started"), "an error logged before a test fails the run")
	return true


func test_a_fragment_matches_the_message_not_the_file_name() -> bool:
	var errors: Array[Dictionary] = [{"message": "Cannot call method on null", "where": "res://tests/test_zz.gd:5"}]
	var problems: Array[String] = RUNNER.error_problems(errors, ["test_zz"] as Array[String])
	assert_true(_has_problem(problems, "unexpected engine error"), "file name does not declare the error")
	assert_true(_has_problem(problems, "never raised"), "and the declaration is unused")
	return true


func test_one_declaration_covers_one_occurrence() -> bool:
	var errors: Array[Dictionary] = [{"message": "JSON failed", "where": "a:1"}, {"message": "JSON failed again", "where": "b:2"}]
	var problems: Array[String] = RUNNER.error_problems(errors, ["JSON"] as Array[String])
	assert_eq(problems.size(), 1, "the second matching error is not covered")
	assert_true(_has_problem(problems, "again"), "it is the second one")
	return true
