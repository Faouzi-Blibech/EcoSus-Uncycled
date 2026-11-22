extends Node
## The player's scan profile, kept by the photo service (ecoverify/): points, daily streak, skins.
## The player id is a random UUID created once on this device (user://profile.cfg); the service knows
## the player only by it. start_scan() asks the service for a single-use link and opens the camera
## page on this device (open_mode): the game is a phone game, so the photo is taken right here, no QR.
## The menu listens to the signals.

signal changed()
## The scan page (camera) was opened; the game watches for the photo to be counted.
signal scan_opened()
signal scan_failed()
## The service counted a new scan (points, streak or today's scans went up).
signal scan_done()

const PROFILE_PATH: String = "user://profile.cfg"
const TIMEOUT_S: float = 8.0
## While the scan page is open: how often to ask whether the photo was counted, and for how long.
const POLL_S: float = 3.0
const POLL_MAX_S: float = 180.0

var player_id: String = ""
var points: int = 0
var streak: int = 0
var skins: Array[String] = []
## Selected skin id, "" when none (or when the saved one is not unlocked).
var skin: String = ""
var scans_today: int = 0
## True once /profile answered (the menu hides the numbers until then).
var loaded: bool = false

var _get: HTTPRequest
var _post: HTTPRequest
var _poll: Timer
var _poll_left: float = 0.0
var _before: Dictionary = {}


func _ready() -> void:
	load_or_create_id(PROFILE_PATH)
	_get = _make_request(_on_profile)
	_post = _make_request(_on_scan_start)
	_poll = Timer.new()
	_poll.wait_time = POLL_S
	_poll.timeout.connect(_on_poll)
	add_child(_poll)
	refresh()


## Android: back from the browser, the photo may have been counted.
func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_IN and _get != null:
		refresh()


func _make_request(handler: Callable) -> HTTPRequest:
	var r: HTTPRequest = HTTPRequest.new()
	r.timeout = TIMEOUT_S
	r.request_completed.connect(handler)
	add_child(r)
	return r


## Reads the id saved in `path`, or makes and saves one. Returns it.
func load_or_create_id(path: String) -> String:
	var cfg: ConfigFile = ConfigFile.new()
	if cfg.load(path) == OK:
		player_id = str(cfg.get_value("profile", "player_id", ""))
		skin = str(cfg.get_value("profile", "skin", ""))
	if player_id.is_empty():
		player_id = new_uuid()
		cfg.set_value("profile", "player_id", player_id)
		cfg.save(path)
	return player_id


## A random UUID version 4 (the format the photo service checks).
static func new_uuid() -> String:
	var b: PackedByteArray = Crypto.new().generate_random_bytes(16)
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	var h: String = b.hex_encode()
	return "%s-%s-%s-%s-%s" % [h.substr(0, 8), h.substr(8, 4), h.substr(12, 4), h.substr(16, 4), h.substr(20, 12)]


## How a scan link is opened: "same_tab" on the web (the scan page's Back returns to the game),
## "browser" in the Android app (Back returns to the game). Desktop builds are for testing only and
## behave like the app: the page opens in the PC's browser.
static func open_mode(is_web: bool, _is_mobile: bool) -> String:
	return "same_tab" if is_web else "browser"


## True while a request is on its way. A finished request can leave its connection open
## (STATUS_CONNECTED, keep-alive): that is not busy, or every later refresh would be skipped and the
## menu would never see the new points after a photo.
static func is_busy(status: int) -> bool:
	return status in [HTTPClient.STATUS_RESOLVING, HTTPClient.STATUS_CONNECTING,
		HTTPClient.STATUS_REQUESTING, HTTPClient.STATUS_BODY]


## True when the newer /profile shows a scan the older one did not.
static func scan_counted(before: Dictionary, after: Dictionary) -> bool:
	for key in ["points", "streak", "scans_today"]:
		if int(after.get(key, 0)) > int(before.get(key, 0)):
			return true
	return false


func apply_profile(data: Dictionary) -> void:
	points = int(data.get("points", 0))
	streak = int(data.get("streak", 0))
	scans_today = int(data.get("scans_today", 0))
	skins.clear()
	for s in Net.as_array(data.get("skins")):
		skins.append(str(s))
	if not skins.has(skin):
		skin = ""
	loaded = true


## The lobby's pick (plan L3), saved for the next rooms; "" is the default character.
func choose_skin(id: String) -> void:
	skin = id
	var cfg: ConfigFile = ConfigFile.new()
	cfg.load(PROFILE_PATH)
	cfg.set_value("profile", "skin", skin)
	cfg.save(PROFILE_PATH)
	changed.emit()


func refresh() -> void:
	if is_busy(_get.get_http_client_status()):
		return
	_get.request("%s/profile/%s" % [Settings.scan_url, player_id])


## Asks the service for a single-use scan link, then opens it (see open_mode).
func start_scan() -> void:
	if is_busy(_post.get_http_client_status()):
		return
	_before = _snapshot()
	var body: String = JSON.stringify({"player_id": player_id})
	var err: Error = _post.request("%s/scan/start" % Settings.scan_url, ["Content-Type: application/json"], HTTPClient.METHOD_POST, body)
	if err != OK:
		scan_failed.emit()


func _snapshot() -> Dictionary:
	return {"points": points, "streak": streak, "scans_today": scans_today}


static func _json(code: int, body: PackedByteArray) -> Dictionary:
	if code < 200 or code >= 300:
		return {}
	var parsed: Variant = JSON.parse_string(body.get_string_from_utf8())
	return parsed if parsed is Dictionary else {}


func _on_profile(_result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	var data: Dictionary = _json(code, body)
	if data.is_empty():
		return
	apply_profile(data)
	changed.emit()
	if _poll_left > 0.0 and scan_counted(_before, _snapshot()):
		_poll_left = 0.0
		_poll.stop()
		scan_done.emit()


func _on_scan_start(_result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	var data: Dictionary = _json(code, body)
	var url: String = str(data.get("url", ""))
	if url.is_empty():
		scan_failed.emit()
		return
	match open_mode(OS.has_feature("web"), OS.has_feature("mobile")):
		"same_tab":
			JavaScriptBridge.eval("window.location.href = %s" % JSON.stringify(url))
		_:
			OS.shell_open(url)
			# Watch for the photo while the camera page is open; coming back also refreshes.
			_poll_left = POLL_MAX_S
			_poll.start()
			scan_opened.emit()


func _on_poll() -> void:
	_poll_left -= POLL_S
	if _poll_left <= 0.0:
		_poll.stop()
		return
	refresh()
