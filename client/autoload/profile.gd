extends Node
## The player's scan profile, kept by the photo-scan service (ecoverify): an anonymous id made once
## and saved on this device, plus points, streak and unlocked skins read from GET /profile/{id}.
## The menu's scan button calls start_scan(): the service returns a single-use link, opened in the
## same tab on the web (nothing to lose outside a room) and in the browser elsewhere. Coming back to
## the game refreshes the profile. No personal data: the id is a random UUID.

signal changed
## A scan could not start; `reason_key` is a strings.csv key to show.
signal scan_failed(reason_key: String)

const PROFILE_PATH: String = "user://profile.cfg"

var player_id: String = ""
var points: int = 0
var streak: int = 0
## Unlocked skin ids, as the scan service lists them (content/skins.json there). Data, not code:
## the list changes when the skins change.
var skins: Array[String] = []
## The skin picked in the lobby; "" is the default character.
var skin: String = ""
## True once the scan service answered at least once.
var loaded: bool = false

var _profile_req: HTTPRequest
var _scan_req: HTTPRequest


func _ready() -> void:
	player_id = load_or_create_id(PROFILE_PATH)
	skin = _load_skin(PROFILE_PATH)
	_profile_req = HTTPRequest.new()
	_profile_req.timeout = 8.0
	_profile_req.request_completed.connect(_on_profile_reply)
	add_child(_profile_req)
	_scan_req = HTTPRequest.new()
	_scan_req.timeout = 8.0
	_scan_req.request_completed.connect(_on_scan_reply)
	add_child(_scan_req)
	refresh()


## Back from the scan page (Android browser, desktop): fetch the new points and streak.
func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_IN:
		refresh()


## Ask the scan service for the latest profile. Does nothing in headless runs (tests, tools).
func refresh() -> void:
	if _profile_req == null or DisplayServer.get_name() == "headless":
		return
	_profile_req.cancel_request()
	_profile_req.request("%s/profile/%s" % [Settings.scan_url, player_id])


## Ask for a scan link and open it.
func start_scan() -> void:
	if _scan_req == null:
		return
	_scan_req.cancel_request()
	var body: String = JSON.stringify({"player_id": player_id})
	var err: int = _scan_req.request("%s/scan/start" % Settings.scan_url,
		["Content-Type: application/json"], HTTPClient.METHOD_POST, body)
	if err != OK:
		scan_failed.emit("MENU_SCAN_OFFLINE")


## Store the lobby choice for the next rooms too.
func choose_skin(id: String) -> void:
	skin = id
	var cfg: ConfigFile = ConfigFile.new()
	cfg.load(PROFILE_PATH)
	cfg.set_value("profile", "skin", skin)
	cfg.save(PROFILE_PATH)
	changed.emit()


## Read a /profile reply. Missing fields keep their zero values.
func apply_profile(data: Dictionary) -> void:
	points = int(data.get("points", 0))
	streak = int(data.get("streak", 0))
	skins = to_ids(data.get("skins", []))
	loaded = true
	changed.emit()


## The skin to wear: the chosen one while it is unlocked, else the default ("").
static func effective_skin(chosen: String, unlocked: Array[String]) -> String:
	return chosen if unlocked.has(chosen) else ""


static func to_ids(value: Variant) -> Array[String]:
	var out: Array[String] = []
	if typeof(value) == TYPE_ARRAY:
		for v in value:
			if typeof(v) == TYPE_STRING:
				out.append(v)
	return out


## The saved id, or a new random one saved for next time.
static func load_or_create_id(path: String) -> String:
	var cfg: ConfigFile = ConfigFile.new()
	cfg.load(path)
	var id: String = str(cfg.get_value("profile", "player_id", ""))
	if is_uuid(id):
		return id
	id = make_uuid(Crypto.new().generate_random_bytes(16))
	cfg.set_value("profile", "player_id", id)
	var err: int = cfg.save(path)
	if err != OK:
		push_warning("Profile: could not save %s (error %d)" % [path, err])
	return id


## A version-4 UUID from 16 random bytes (RFC 4122 layout).
static func make_uuid(bytes: PackedByteArray) -> String:
	var b: PackedByteArray = bytes.duplicate()
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	var h: String = b.hex_encode()
	return "%s-%s-%s-%s-%s" % [h.substr(0, 8), h.substr(8, 4), h.substr(12, 4), h.substr(16, 4), h.substr(20, 12)]


static func is_uuid(s: String) -> bool:
	var re: RegEx = RegEx.create_from_string("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
	return re.search(s) != null


static func _load_skin(path: String) -> String:
	var cfg: ConfigFile = ConfigFile.new()
	if cfg.load(path) != OK:
		return ""
	return str(cfg.get_value("profile", "skin", ""))


func _on_profile_reply(result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if result != HTTPRequest.RESULT_SUCCESS or code != 200:
		return  # offline or no service: keep what we have
	var data: Variant = JSON.parse_string(body.get_string_from_utf8())
	if typeof(data) == TYPE_DICTIONARY:
		apply_profile(data)


func _on_scan_reply(result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if result != HTTPRequest.RESULT_SUCCESS or code != 200:
		scan_failed.emit("MENU_SCAN_OFFLINE")
		return
	var data: Variant = JSON.parse_string(body.get_string_from_utf8())
	if typeof(data) != TYPE_DICTIONARY or str((data as Dictionary).get("url", "")).is_empty():
		scan_failed.emit("MENU_SCAN_OFFLINE")
		return
	open_link(str(data["url"]))


## Web: same tab (a popup would be blocked on iPhone). Android and desktop: the default browser.
func open_link(url: String) -> void:
	if OS.has_feature("web") and Engine.has_singleton("JavaScriptBridge"):
		JavaScriptBridge.eval("window.location.href = %s;" % JSON.stringify(url))
	else:
		OS.shell_open(url)
