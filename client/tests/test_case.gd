extends RefCounted
## Base class for headless tests. Failures are collected, never asserted, so the runner can report them.
##
## Convention: every test is `func test_<name>() -> bool:` and ends with `return true`.
## The runner passes a test only on positive proof: the call returned exactly true (an aborted
## call, e.g. a script runtime error, returns null), no failure was recorded, and at least one
## assert_* ran. A run that finds zero tests also fails.
##
## Engine errors: any script error, engine error or push_error logged while a test runs (including
## deferred calls that run right after it) fails the test. A test that expects one calls
## expect_error("part of the message") first; an expected error that never happens fails too.
## Warnings never fail a test.

var failures: Array[String] = []
var assertions: int = 0
var expected_errors: Array[String] = []
var _owned: Array[Object] = []


## Keep an object (e.g. a Node) alive for the test; the runner frees it afterwards.
func track(o: Object) -> Object:
	_owned.append(o)
	return o


## Declare an engine error this test provokes on purpose; `fragment` must be part of its message.
func expect_error(fragment: String) -> void:
	expected_errors.append(fragment)


func cleanup() -> void:
	for o in _owned:
		o.free()
	_owned.clear()


func assert_eq(a: Variant, b: Variant, msg: String = "") -> void:
	assertions += 1
	if typeof(a) != typeof(b) or a != b:
		failures.append("%s: expected %s, got %s" % [msg, str(b), str(a)])


func assert_true(c: bool, msg: String = "") -> void:
	assertions += 1
	if not c:
		failures.append("%s: expected true" % msg)


func assert_near(a: float, b: float, eps: float, msg: String = "") -> void:
	assertions += 1
	if absf(a - b) > eps:
		failures.append("%s: expected %s +/- %s, got %s" % [msg, str(b), str(eps), str(a)])
