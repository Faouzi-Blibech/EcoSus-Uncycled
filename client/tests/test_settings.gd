extends "res://tests/test_case.gd"
## The default server address: localhost on desktop and Android, the page's own host on the web.


func test_http_page_uses_its_hostname_with_the_server_port() -> bool:
	assert_eq(Settings.server_url_for_page("http:", "192.168.1.20", "192.168.1.20:8060"), "ws://192.168.1.20:5000/ws", "lan page")
	assert_eq(Settings.server_url_for_page("http:", "localhost", "localhost:8060"), "ws://localhost:5000/ws", "local page")
	return true


func test_https_page_uses_its_host_and_no_port() -> bool:
	assert_eq(Settings.server_url_for_page("https:", "play.example.org", "play.example.org"), "wss://play.example.org/ws", "https page")
	assert_eq(Settings.server_url_for_page("https:", "play.example.org", "play.example.org:8443"), "wss://play.example.org:8443/ws", "https page on a port")
	return true


func test_unknown_page_falls_back_to_the_desktop_default() -> bool:
	assert_eq(Settings.server_url_for_page("", "", ""), Settings.DEFAULT_SERVER_URL, "no page")
	assert_eq(Settings.DEFAULT_SERVER_URL, "ws://localhost:5000/ws", "desktop and Android default")
	return true


func test_a_stale_localhost_address_saved_by_the_old_web_build_counts_as_unset() -> bool:
	var old: String = Settings.DEFAULT_SERVER_URL
	assert_eq(Settings.saved_url_to_use(old, true), "", "web: the old default saved on every name entry is dropped")
	assert_eq(Settings.saved_url_to_use(old, false), old, "desktop: it is a normal choice")
	assert_eq(Settings.saved_url_to_use("wss://play.example.org/ws", true), "wss://play.example.org/ws", "web: a real choice stays")
	assert_eq(Settings.saved_url_to_use("", true), "", "nothing saved")
	return true


func test_desktop_default_is_the_plain_default() -> bool:
	assert_eq(Settings.default_server_url(), Settings.DEFAULT_SERVER_URL, "not a web build")
	return true


func test_space_also_uses_and_the_hint_still_shows_e() -> bool:
	var keys: Array[int] = []
	for ev in InputMap.action_get_events("act_use"):
		if ev is InputEventKey:
			keys.append(int((ev as InputEventKey).physical_keycode))
	assert_true(keys.has(KEY_SPACE), "Space is a second key for USE (the design lists WASD/E/Space)")
	assert_eq(keys[0], int(KEY_E), "E stays the first key, so the hint says [E]")
	return true


func test_scan_address_follows_a_plain_http_page() -> bool:
	assert_eq(Settings.scan_url_for_page("http:", "192.168.1.20"), "http://192.168.1.20:8000", "LAN demo page")
	assert_eq(Settings.scan_url_for_page("https:", "play.example.org"), Settings.SCAN_URL, "https page: set it in Settings")
	assert_eq(Settings.scan_url_for_page("", ""), Settings.SCAN_URL, "no page")
	assert_eq(Settings.default_scan_url(), Settings.SCAN_URL, "desktop default")
	return true


func test_typed_scan_address_is_cleaned_up() -> bool:
	var cases: Array = [
		["192.168.1.118:8000", "http://192.168.1.118:8000"],
		["  http://192.168.1.118:8000/  ", "http://192.168.1.118:8000"],
		["https://abc.trycloudflare.com", "https://abc.trycloudflare.com"],
		["ws://localhost:5000/ws", ""],
		["not an address", ""],
		["", ""],
		["http://", ""],
	]
	for c in cases:
		assert_eq(Settings.normalize_scan_url(c[0]), c[1], "'%s'" % c[0])
	return true


func test_scan_address_follows_a_game_server_on_the_same_laptop() -> bool:
	assert_eq(Settings.scan_url_for_server("ws://192.168.1.118:5000/ws"), "http://192.168.1.118:8000", "demo laptop")
	assert_eq(Settings.scan_url_for_server("ws://localhost:5000/ws"), "", "local: keep the default")
	assert_eq(Settings.scan_url_for_server("ws://127.0.0.1:5000/ws"), "", "local: keep the default")
	assert_eq(Settings.scan_url_for_server("wss://play.example.org/ws"), "", "hosted: set by hand")
	return true


## Hosted mode (deploy/online.ps1): config.json gives the laptop's current tunnel addresses.
func test_hosted_config_is_read_and_checked() -> bool:
	var table: Array = [
		['{"server": "wss://a-b.trycloudflare.com/ws", "scan": "https://c-d.trycloudflare.com"}', "wss://a-b.trycloudflare.com/ws", "https://c-d.trycloudflare.com", "both"],
		['{"server": "wss://a.trycloudflare.com/ws"}', "wss://a.trycloudflare.com/ws", "", "no scan"],
		['{"server": "http://a.example.org/ws", "scan": "ftp://x"}', "", "", "wrong schemes are dropped"],
		['{"server": "wss://has space/ws"}', "", "", "not an address"],
		["not json", "", "", "broken file"],
		["[1, 2]", "", "", "not an object"],
		['{"scan": "https://c.trycloudflare.com/"}', "", "https://c.trycloudflare.com", "trailing slash removed"],
	]
	for row in table:
		var got: Dictionary = Settings.parse_hosted_config(row[0])
		assert_eq(got["server"], row[1], str(row[3]) + ": server")
		assert_eq(got["scan"], row[2], str(row[3]) + ": scan")
	return true


func test_hosted_addresses_become_the_defaults_and_a_saved_choice_still_wins() -> bool:
	var before: Array = [Settings.hosted_server_url, Settings.hosted_scan_url, Settings.server_url, Settings.scan_url]
	Settings.hosted_server_url = "wss://a.trycloudflare.com/ws"
	Settings.hosted_scan_url = "https://c.trycloudflare.com"
	assert_eq(Settings.default_server_url(), "wss://a.trycloudflare.com/ws", "hosted server is the default")
	assert_eq(Settings.default_scan_url(), "https://c.trycloudflare.com", "hosted scan is the default")
	Settings.hosted_server_url = ""
	Settings.hosted_scan_url = ""
	assert_eq(Settings.default_server_url(), Settings.DEFAULT_SERVER_URL, "no config: the old default")
	Settings.hosted_server_url = before[0]
	Settings.hosted_scan_url = before[1]
	Settings.server_url = before[2]
	Settings.scan_url = before[3]
	return true


func test_only_exported_builds_with_a_config_address_ask_for_it() -> bool:
	assert_true(Settings.wants_hosted_config(true, "https://x.pages.dev/config.json"), "exported build")
	assert_true(not Settings.wants_hosted_config(false, "https://x.pages.dev/config.json"), "editor run stays on localhost")
	assert_true(not Settings.wants_hosted_config(true, ""), "no config address")
	return true
