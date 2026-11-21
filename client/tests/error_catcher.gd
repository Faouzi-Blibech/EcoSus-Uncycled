class_name ErrorCatcher
extends Logger
## Registered with OS.add_logger by the test runner for the whole run. Records every engine error,
## script error and push_error as {message, where}; warnings are ignored. Never print from here:
## logging inside a logger callback hangs. The signature must use Array[ScriptBacktrace].

var _errors: Array[Dictionary] = []
var _mutex: Mutex = Mutex.new()


func _log_error(_function: String, file: String, line: int, code: String, rationale: String, _editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
	if error_type == Logger.ERROR_TYPE_WARNING:
		return
	var message: String = code if rationale.is_empty() else "%s %s" % [code, rationale]
	_mutex.lock()
	_errors.append({"message": message, "where": "%s:%d" % [file, line]})
	_mutex.unlock()


## The errors seen since the last take(); empties the list.
func take() -> Array[Dictionary]:
	_mutex.lock()
	var out: Array[Dictionary] = _errors.duplicate()
	_errors.clear()
	_mutex.unlock()
	return out
