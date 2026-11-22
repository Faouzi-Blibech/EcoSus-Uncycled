extends Node
## Player settings persisted to user://settings.cfg.
## Also, at startup: loads the translations and registers the keyboard input actions.

const I18nLoader := preload("res://i18n/i18n_loader.gd")
const RotateOverlay := preload("res://ui/widgets/rotate_overlay.gd")

## The one place the server port and path live; every default address is built from them.
const SERVER_PORT: int = 5000
const SERVER_PATH: String = "/ws"
const DEFAULT_SERVER_URL: String = "ws://localhost:%d%s" % [SERVER_PORT, SERVER_PATH]
## The photo service (ecoverify/, scan links and profiles). It runs on its own host, so it is not
## derived from the page; set the hosted address here when it is online. 127.0.0.1, not localhost:
## Godot tries IPv6 (::1) for localhost, and a service listening on IPv4 only refuses it.
const SCAN_URL: String = "http://127.0.0.1:8000"
const SCAN_PORT: int = 8000
## Hosted mode: exported builds read the live game server and photo service addresses from this
## file at startup (deploy/online.ps1 publishes it each time the laptop's tunnels start, since their
## addresses change). The web build reads the config.json next to its own page instead.
## "" turns hosted mode off.
const HOSTED_CONFIG_URL: String = "https://uncycled.pages.dev/config.json"
const SETTINGS_PATH: String = "user://settings.cfg"
## The players' community (Settings has a button to it).
const COMMUNITY_URL: String = "https://www.reddit.com/r/EcoSus/"
const LANGUAGES: Array[String] = ["en", "fr", "ar", "sv"]
## Godot's default font has no Arabic letters; this one is added behind it (see _add_font_fallbacks).
const ARABIC_FONT_PATH: String = "res://assets/fonts/NotoSansArabic.ttf"

var player_name: String = ""
var language: String = "en"
var server_url: String = DEFAULT_SERVER_URL
var scan_url: String = SCAN_URL
## From the hosted config.json (see HOSTED_CONFIG_URL); "" until it arrives or when there is none.
## They replace the built-in defaults; an address the player saved in Settings still wins.
var hosted_server_url: String = ""
var hosted_scan_url: String = ""
## Phone buzz on meetings, infection and gas (see haptics.gd); no settings screen toggle yet.
var haptics_enabled: bool = true
## Sound effects and music (see sfx.gd); toggled in the Settings panel.
var sound_enabled: bool = true
var music_enabled: bool = true


func _ready() -> void:
	I18nLoader.load_csv()
	_register_input_actions()
	_add_font_fallbacks()
	# English until the player picks a language in Settings (not the computer's language).
	load_settings()
	apply_language()
	if OS.has_feature("web") or OS.has_feature("mobile"):
		get_tree().root.add_child.call_deferred(RotateOverlay.new())


## The address used when the player has not saved one: the page's own host on the web (a phone
## then needs only a name and a code), localhost on desktop and Android.
func default_server_url() -> String:
	if not hosted_server_url.is_empty():
		return hosted_server_url
	if OS.has_feature("web") and Engine.has_singleton("JavaScriptBridge"):
		return server_url_for_page(
			str(JavaScriptBridge.eval("window.location.protocol")),
			str(JavaScriptBridge.eval("window.location.hostname")),
			str(JavaScriptBridge.eval("window.location.host")))
	return DEFAULT_SERVER_URL


## The server address for a web page: wss on the page's own host for https, ws on its hostname and
## the server port for http. `host` is hostname plus port, as in window.location.
static func server_url_for_page(protocol: String, hostname: String, host: String) -> String:
	if hostname.is_empty():
		return DEFAULT_SERVER_URL
	if protocol == "https:":
		return "wss://%s%s" % [host, SERVER_PATH]
	return "ws://%s:%d%s" % [hostname, SERVER_PORT, SERVER_PATH]


## The scan service address when nothing is saved: on a plain-http web page (a LAN demo) the page's
## own hostname on the scan port, so phones that open the game from a PC also reach its scan service;
## otherwise SCAN_URL. On https pages and native builds, set it in Settings (or here when hosted).
static func scan_url_for_page(protocol: String, hostname: String) -> String:
	if protocol == "http:" and not hostname.is_empty():
		return "http://%s:%d" % [hostname, SCAN_PORT]
	return SCAN_URL


## The scan service on the same machine as a game server typed in Settings: the phone then needs one
## address (the demo laptop runs both). "" for localhost or a hosted wss server (set that one by hand).
static func scan_url_for_server(server: String) -> String:
	if not server.begins_with("ws://"):
		return ""
	var host: String = server.trim_prefix("ws://").get_slice("/", 0).get_slice(":", 0)
	if host.is_empty() or host == "localhost" or host == "127.0.0.1":
		return ""
	return "http://%s:%d" % [host, SCAN_PORT]


func default_scan_url() -> String:
	if not hosted_scan_url.is_empty():
		return hosted_scan_url
	if OS.has_feature("web") and Engine.has_singleton("JavaScriptBridge"):
		return scan_url_for_page(
			str(JavaScriptBridge.eval("window.location.protocol")),
			str(JavaScriptBridge.eval("window.location.hostname")))
	var same_machine: String = scan_url_for_server(server_url)
	return same_machine if not same_machine.is_empty() else SCAN_URL


## True when this build should ask for the hosted config: exported builds only (a run from the
## editor keeps its local server), and only when there is an address to ask.
static func wants_hosted_config(is_exported: bool, url: String) -> bool:
	return is_exported and not url.is_empty()


## Where to read config.json: next to the page on the web (same origin, any host), else HOSTED_CONFIG_URL.
## A time stamp in the query skips any cached copy (the addresses change on every tunnel restart).
func hosted_config_url() -> String:
	var url: String = HOSTED_CONFIG_URL
	if OS.has_feature("web") and Engine.has_singleton("JavaScriptBridge"):
		url = str(JavaScriptBridge.eval("new URL('config.json', window.location.href).href"))
	if url.is_empty():
		return ""
	return "%s?t=%d" % [url, int(Time.get_unix_time_from_system())]


## {server, scan} from config.json; each "" when missing or not a usable address (server: ws/wss,
## scan: http/https, no spaces).
static func parse_hosted_config(text: String) -> Dictionary:
	var out: Dictionary = {"server": "", "scan": ""}
	var json: JSON = JSON.new()
	if json.parse(text) != OK or not json.data is Dictionary:
		return out
	var parsed: Variant = json.data
	var server: String = str((parsed as Dictionary).get("server", "")).strip_edges()
	if (server.begins_with("wss://") or server.begins_with("ws://")) and not server.contains(" "):
		out["server"] = server
	var scan_raw: String = str((parsed as Dictionary).get("scan", "")).strip_edges()
	if scan_raw.begins_with("https://") or scan_raw.begins_with("http://"):
		out["scan"] = normalize_scan_url(scan_raw)
	return out


## The hosted addresses arrived: they become the defaults, then the saved settings are read again
## (a saved address still wins).
func apply_hosted(server: String, scan: String) -> void:
	hosted_server_url = server
	hosted_scan_url = scan
	load_settings()


## What the player typed as the scan address, cleaned up: "http://" added when no scheme is given,
## no trailing slash. "" when it cannot be an address (spaces, other schemes).
static func normalize_scan_url(text: String) -> String:
	var url: String = text.strip_edges()
	if url.contains(" "):
		return ""
	var scheme: String = "http"
	if url.contains("://"):
		scheme = url.get_slice("://", 0)
		url = url.get_slice("://", 1)
	url = url.rstrip("/")
	if url.is_empty() or not (scheme == "http" or scheme == "https"):
		return ""
	return "%s://%s" % [scheme, url]


## The saved address worth using, "" when there is none. On the web the old default
## (ws://localhost:5000/ws) counts as unset: an earlier build saved it on every name entry, and it
## would keep a phone dialling its own localhost instead of the page host.
static func saved_url_to_use(saved: String, is_web: bool) -> String:
	if is_web and saved == DEFAULT_SERVER_URL:
		return ""
	return saved


func load_settings() -> void:
	server_url = default_server_url()
	scan_url = default_scan_url()
	var cfg: ConfigFile = ConfigFile.new()
	if cfg.load(SETTINGS_PATH) != OK:
		return
	player_name = str(cfg.get_value("settings", "player_name", player_name))
	var lang: String = str(cfg.get_value("settings", "language", language))
	if LANGUAGES.has(lang):
		language = lang
	var url: String = saved_url_to_use(str(cfg.get_value("settings", "server_url", "")), OS.has_feature("web"))
	server_url = url if not url.is_empty() else default_server_url()
	var scan: String = normalize_scan_url(str(cfg.get_value("settings", "scan_url", "")))
	scan_url = scan if not scan.is_empty() else default_scan_url()
	haptics_enabled = bool(cfg.get_value("settings", "haptics_enabled", haptics_enabled))
	sound_enabled = bool(cfg.get_value("settings", "sound_enabled", sound_enabled))
	music_enabled = bool(cfg.get_value("settings", "music_enabled", music_enabled))


func save() -> void:
	var cfg: ConfigFile = ConfigFile.new()
	cfg.set_value("settings", "player_name", player_name)
	cfg.set_value("settings", "language", language)
	cfg.set_value("settings", "haptics_enabled", haptics_enabled)
	cfg.set_value("settings", "sound_enabled", sound_enabled)
	cfg.set_value("settings", "music_enabled", music_enabled)
	# The default is not a saved choice: on the web it follows the page the next time too.
	if server_url != default_server_url():
		cfg.set_value("settings", "server_url", server_url)
	if scan_url != default_scan_url():
		cfg.set_value("settings", "scan_url", scan_url)
	var err: int = cfg.save(SETTINGS_PATH)
	if err != OK:
		push_warning("Settings: could not save %s (error %d)" % [SETTINGS_PATH, err])


## Adds the Arabic font as a fallback of the default font, so every Label can show Arabic (the text
## server shapes it right to left). Missing font file: nothing happens.
func _add_font_fallbacks() -> void:
	if not ResourceLoader.exists(ARABIC_FONT_PATH):
		return
	var arabic: Font = load(ARABIC_FONT_PATH) as Font
	var base: Font = ThemeDB.fallback_font
	if arabic == null or base == null or base.fallbacks.has(arabic):
		return
	var list: Array[Font] = base.fallbacks.duplicate()
	list.append(arabic)
	base.fallbacks = list


func apply_language() -> void:
	TranslationServer.set_locale(language)


func _register_input_actions() -> void:
	_add_action("move_left", [KEY_A, KEY_LEFT])
	_add_action("move_right", [KEY_D, KEY_RIGHT])
	_add_action("move_up", [KEY_W, KEY_UP])
	_add_action("move_down", [KEY_S, KEY_DOWN])
	# The design lists Space next to E; E stays first so the HUD hint reads [E].
	_add_action("act_use", [KEY_SPACE])
	_add_action("act_dash", [KEY_SHIFT])


## Physical keys, so WASD keeps the same shape on AZERTY keyboards (ZQSD).
func _add_action(action: StringName, keys: Array) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	for k in keys:
		var ev: InputEventKey = InputEventKey.new()
		ev.physical_keycode = int(k) as Key
		InputMap.action_add_event(action, ev)
