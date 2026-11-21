extends Node
## Runs every res://tests/test_*.gd, each test_* method on a fresh instance.
## Scene (not --script) so the Net, Session, Settings and Assets autoloads exist.
## godot --headless --path client res://tests/test_runner.tscn
##
## One error rule for the whole run: any engine error, script error or push_error logged from the
## moment the runner starts until it prints its verdict fails the run (TESTS FAIL, exit 1), unless
## a test declared it with expect_error(). That covers the autoloads' setup (re-run under the
## catcher), loading and compiling each test file, errors between tests, errors in the last frames
## before the verdict, and a catcher that stops hearing errors (checked at start, after every
## test and at the end).
##
## Known limits (no more rounds planned):
## - The real autoloads' own first _ready ran before the catcher existed; the runner re-runs a fresh
##   copy of each autoload script's _ready under the catcher instead.
## - Errors logged by threads, timers or leaked nodes later than FINAL_SETTLE_FRAMES frames after
##   the last test are not seen; one leaked in a middle test may fail the next test, not its own.
## - A test that sets Engine.print_error_messages = false and back to true inside its own body
##   hides what is logged in between. Leaving it false is caught.
## - Anything the engine prints without going through the logger (a crash, a plain stdout print) is
##   not seen.

const TESTS_DIR: String = "res://tests"
const FINAL_SETTLE_FRAMES: int = 8
const SELF_CHECK_MESSAGE: String = "runner self-check: the error catcher must hear this line"


## Test method names of a script, sorted.
static func test_methods(script: GDScript) -> Array[String]:
	var methods: Array[String] = []
	for m in script.get_script_method_list():
		if String(m["name"]).begins_with("test_"):
			methods.append(m["name"])
	methods.sort()
	return methods


## Positive proof for one test file: it loaded, compiled and has at least one test method.
static func file_problems(script: GDScript, file: String) -> Array[String]:
	if script == null:
		return ["%s: cannot load" % file]
	if not script.can_instantiate():
		return ["%s: cannot compile" % file]
	if test_methods(script).is_empty():
		return ["%s: no tests" % file]
	return []


## Problems from engine errors: every error that no declaration matches, and every declaration that
## no error matched. A fragment is matched against the message only (never the file:line), and a
## declaration covers exactly one error.
static func error_problems(errors: Array[Dictionary], expected: Array[String]) -> Array[String]:
	var problems: Array[String] = []
	var used: Array[bool] = []
	used.resize(expected.size())
	used.fill(false)
	for e in errors:
		var declared: bool = false
		for i in expected.size():
			if not used[i] and str(e["message"]).contains(expected[i]):
				used[i] = true
				declared = true
				break
		if not declared:
			problems.append("unexpected engine error: %s (%s)" % [e["message"], e["where"]])
	for i in expected.size():
		if not used[i]:
			problems.append("expected engine error never raised: %s" % expected[i])
	return problems


## Errors with no declaration to cover them (the run-level rule: nothing is ever dropped).
static func undeclared(errors: Array[Dictionary], what: String) -> Array[String]:
	var problems: Array[String] = []
	for e in errors:
		problems.append("engine error %s: %s (%s)" % [what, e["message"], e["where"]])
	return problems


## True when the catcher hears an error logged right now. Logs one error (SELF_CHECK_MESSAGE), so
## a test that calls this declares it.
static func hears_errors(catcher: ErrorCatcher) -> bool:
	catcher.take()
	push_error(SELF_CHECK_MESSAGE)
	return not catcher.take().is_empty()


## Loads a script with `loader` and returns the problems of the file plus every error logged while
## it loaded and compiled (a _static_init error, a preloaded script that does not parse).
static func load_problems(catcher: ErrorCatcher, loader: Callable, file: String) -> Array[String]:
	catcher.take()
	var script: GDScript = loader.call()
	var problems: Array[String] = file_problems(script, file)
	problems.append_array(undeclared(catcher.take(), "while loading %s" % file))
	return problems


## Instantiates a fresh copy of each autoload script under the catcher (adds it to the tree so its
## _ready runs, then frees it) and returns every error that logged.
static func setup_problems(tree: SceneTree, catcher: ErrorCatcher, scripts: Array[GDScript]) -> Array[String]:
	catcher.take()
	for s in scripts:
		var n: Node = s.new() as Node
		tree.root.add_child(n)
		tree.root.remove_child(n)
		n.free()
	return undeclared(catcher.take(), "in an autoload's setup")


## The scripts of the project's autoloads.
static func autoload_scripts() -> Array[GDScript]:
	var scripts: Array[GDScript] = []
	for p in ProjectSettings.get_property_list():
		var name: String = str(p["name"])
		if not name.begins_with("autoload/"):
			continue
		var path: String = str(ProjectSettings.get_setting(name)).trim_prefix("*")
		var s: GDScript = load(path) as GDScript
		if s != null:
			scripts.append(s)
	return scripts


## Waits a few frames (timers, deferred and late errors), then returns every error logged since the
## last check and whether the catcher still hears errors. Logs one self-check error.
static func final_problems(tree: SceneTree, catcher: ErrorCatcher) -> Array[String]:
	for i in FINAL_SETTLE_FRAMES:
		await tree.process_frame
	var problems: Array[String] = undeclared(catcher.take(), "after the last test")
	if not hears_errors(catcher):
		problems.append("the error catcher stopped hearing errors (Engine.print_error_messages is %s)" % str(Engine.print_error_messages))
	return problems


## Runs one test method on a fresh instance and returns its problems (empty = pass). Positive proof:
## it returned exactly true, recorded no failure, ran an assertion and logged no undeclared engine
## error, including from deferred calls that run after it returned.
static func run_test(tree: SceneTree, catcher: ErrorCatcher, script: GDScript, method: String, label: String = "") -> Array[String]:
	var problems: Array[String] = undeclared(catcher.take(), "before %s started" % label)
	var t: RefCounted = script.new()
	var result: Variant = await t.call(method)
	# A frame's process_frame callbacks run before that frame flushes deferred calls: wait for two.
	await tree.process_frame
	await tree.process_frame
	t.cleanup()
	problems.append_array(t.failures)
	if result != true:
		problems.append("%s did not reach its end" % label)
	elif t.assertions == 0:
		problems.append("%s ran no assertions" % label)
	problems.append_array(error_problems(catcher.take(), t.expected_errors))
	if not Engine.print_error_messages:
		# Errors are not logged to the catcher while this is false, so the test proves nothing.
		Engine.print_error_messages = true
		problems.append("%s left Engine.print_error_messages false: errors were not heard" % label)
	return problems


func _ready() -> void:
	# One frame first: the root is still setting up its children inside the first _ready.
	await get_tree().process_frame
	var total: int = 0
	var failed: int = 0
	var catcher: ErrorCatcher = ErrorCatcher.new()
	OS.add_logger(catcher)
	if not hears_errors(catcher):
		print("TESTS FAIL: the runner's error catcher is not receiving errors")
		get_tree().quit(1)
		return
	for issue in setup_problems(get_tree(), catcher, autoload_scripts()):
		total += 1
		failed += 1
		print("FAIL %s" % issue)
	var files: PackedStringArray = DirAccess.get_files_at(TESTS_DIR)
	files.sort()
	for file in files:
		if not (file.begins_with("test_") and file.ends_with(".gd")) or file == "test_case.gd" or file == "test_runner.gd":
			continue
		var path: String = "%s/%s" % [TESTS_DIR, file]
		var holder: Array[GDScript] = [null]
		var loader: Callable = func() -> GDScript:
			holder[0] = load(path)
			return holder[0]
		var file_issues: Array[String] = load_problems(catcher, loader, file)
		if not file_issues.is_empty():
			total += 1
			failed += 1
			for issue in file_issues:
				print("FAIL %s" % issue)
			continue
		for method in test_methods(holder[0]):
			total += 1
			var problems: Array[String] = await run_test(get_tree(), catcher, holder[0], method, "%s::%s" % [file, method])
			if not problems.is_empty():
				failed += 1
				for f in problems:
					print("FAIL %s.%s: %s" % [file, method, f])
	for issue in await final_problems(get_tree(), catcher):
		total += 1
		failed += 1
		print("FAIL %s" % issue)
	OS.remove_logger(catcher)
	if total == 0:
		print("TESTS FAIL: no tests found")
		failed = 1
	if failed == 0:
		print("TESTS PASS %d" % total)
	else:
		print("TESTS FAIL %d/%d" % [failed, total])
	get_tree().quit(1 if failed > 0 else 0)
