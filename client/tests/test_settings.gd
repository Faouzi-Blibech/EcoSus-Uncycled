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


func test_scan_service_address_follows_a_plain_http_page() -> bool:
	assert_eq(Settings.scan_url_for_page("http:", "192.168.1.20"), "http://192.168.1.20:8000", "LAN test page")
	assert_eq(Settings.scan_url_for_page("https:", "play.example.org"), Settings.DEFAULT_SCAN_URL, "https: hosted address comes from L5")
	assert_eq(Settings.scan_url_for_page("", ""), Settings.DEFAULT_SCAN_URL, "no page")
	assert_eq(Settings.default_scan_url(), "http://localhost:8000", "desktop default")
	return true
