# Camera scan, network benchmark, builds and hosting: implementation plan (Louay)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** players scan a real recyclable item from the game (points, daily streak, skins), the game measures
network quality per operator for the mystery challenge, and the game ships as web, Android and Windows builds on
a public server.

**Architecture:** the photo service (`app.py`, FastAPI + YOLOE) gains scan links, a scan web page, QR codes and
player profiles; the token/profile/streak logic lives in a new model-free module so it is fast to test. The Godot
client gets two new autoloads, `Profile` and `NetStats`, plus menu, lobby and settings changes. The game server
gets `set_skin`, `net_report` and a `/bench` page (own commits, Hedi reviews).

**Tech Stack:** Python 3 + FastAPI + pytest; Godot 4.7.2 GDScript; .NET 10 for the two server additions;
Docker (`server/Dockerfile`).

**Spec:** `docs/design/2026-11-21-cleaners-vs-trashers.md` sections 9, 10, 12. Team plan: `docs/team/README.md`.

## Global Constraints

- Louay's client files: `client/scenes/menu/menu.gd`, `client/scenes/lobby/lobby.gd`,
  `client/ui/widgets/settings_panel.gd`, `client/autoload/settings.gd`, new `client/autoload/profile.gd`,
  `client/autoload/net_stats.gd`, new `client/ui/hud/net_stats_panel.gd`, `client/project.godot` (autoloads).
  Do not edit `round.gd`, `hud.gd`, `session.gd`, `action_rules.gd` (Faouzi's).
- `strings.csv`: add your keys right after the header line, FR + EN (Faouzi appends at the end).
- No API keys or secrets in the client. Profile ids are random UUIDs, no personal data.
- Server changes in `server/Uncycled.Server` go in their own commits; tell Hedi.
- Generated data files stay gitignored: `submitted_hashes.json`, `eco_stats.json`, new `profiles.json`,
  `scan_tokens.json`.
- Branches `scan/<short-name>`, `bench/<short-name>`, `build/<short-name>`. Agents commit only when Louay asks.

## Review Focus

1. Web build on iPhone Safari: Godot web export may fail to start or run slowly on iOS. Test the first web build
   on a real iPhone **before** anything else in Task L5.
2. A scan link reused, expired, or used by another player id: refused with a clear message on the scan page.
   Tests in Task L1.
3. Day boundary: the streak uses the Tunis date on the server, never the phone's clock. Tests in Task L1
   (injected dates across midnight).
4. A blocked popup on web: open the scan page in the same tab from the menu (nothing to lose outside a room), with
   a "Back to game" button. Check on iPhone in Task L2.
5. `net_report` flood or junk values (negative, NaN, huge): the server clamps or drops them, never crashes the
   room. Test in Task L4.

---

### Task L1: scan links, scan page, profiles (1 h 30)

**Files:**
- Create: `scan_profile.py` (no model import), `tests/test_scan_profile.py`, `content/skins.json`
- Modify: `app.py` (endpoints below; reuse the existing YOLOE + duplicate-hash code path), `requirements.txt`
  (`qrcode[pil]`, `pytest`), `.gitignore` (`profiles.json`, `scan_tokens.json`)

**Interfaces:**
- Produces in `scan_profile.py`:
  - `issue_token(player_id: str, now: float) -> str` (single use, valid `token_ttl_s` = 120 s)
  - `redeem_token(token: str, now: float) -> str | None` (player id, or None if unknown/expired/used)
  - `record_scan(player_id: str, day: datetime.date) -> dict`
    (returns `{points, streak, best_streak, skins, new_skins, scans_today}`; 10 points per scan, max 3 a day)
  - `get_profile(player_id: str) -> dict`
  - `tunis_day(ts: float) -> datetime.date` (`zoneinfo.ZoneInfo("Africa/Tunis")`)
- HTTP (photo service):
  - `POST /scan/start {player_id}` -> `{token, url, qr_url, expires_s}`.
  - `GET /scan?t=` -> HTML page with `<input type="file" accept="image/*" capture="environment">`, upload
    button, result text, "Back to game" button.
  - `POST /scan/upload` (form `token`, `file`) -> the old verify result plus the profile.
  - `GET /scan/qr?t=` -> PNG.
  - `GET /profile/{player_id}`.
- `content/skins.json`: `{"skins": [{"id": "seedling", "streak": 1}, {"id": "leaf_warrior", "streak": 3},
  {"id": "ocean_guardian", "streak": 7}, {"id": "earth_champion", "streak": 14}, {"id": "recycling_god", "streak": 30}]}`
  (ids from `mobile/services/streakManager.js`; display names go in `strings.csv` as `SKIN_<ID>`).

- [ ] **Step 1: Write the failing tests** in `tests/test_scan_profile.py`:

```python
def test_token_is_single_use():
    t = issue_token("p1", now=1000.0)
    assert redeem_token(t, now=1010.0) == "p1"
    assert redeem_token(t, now=1011.0) is None

def test_token_expires_after_ttl():
    t = issue_token("p1", now=1000.0)
    assert redeem_token(t, now=1121.0) is None
```

  Plus:
  - `test_streak_counts_consecutive_days`: 21 Nov, 22 Nov -> 2; then 24 Nov -> 1.
  - `test_same_day_scans_keep_streak_add_points_until_cap`: 4 scans in a day -> 30 points, streak 1.
  - `test_first_scan_unlocks_seedling`: `new_skins == ["seedling"]`.
  - `test_tunis_day_crosses_midnight`: 22:59 UTC on 21 Nov is 22 Nov in Tunis (UTC+1).
- [ ] **Step 2:** Run `pytest -q -x tests/test_scan_profile.py`. Expected: FAIL (module missing).
- [ ] **Step 3:** Implement `scan_profile.py` (JSON files like the existing hash store), then the endpoints in
  `app.py`. `/scan/upload` replaces the EXIF time check with `redeem_token` (keep `check_exif_timestamp` for the
  old `/verify-daily-photo` route only). Only a verified item calls `record_scan`.
- [ ] **Step 4:** Run `pytest -q tests/test_scan_profile.py`. Expected: PASS. Then by hand: `uvicorn app:app`,
  open `/scan?t=<token from /scan/start>` on a phone, photograph a bottle, see points and streak. Commit on
  `scan/service`.

### Task L2: Profile autoload, scan button in the menu (1 h 30)

**Files:**
- Create: `client/autoload/profile.gd` (autoload `Profile`), `client/ui/widgets/scan_panel.gd` (QR view for
  Windows)
- Modify: `client/project.godot` (autoload), `client/autoload/settings.gd` (`scan_url`: a constant default,
  `http://localhost:8000` until Task L5 sets the hosted photo-service URL; the photo service runs on another
  host than the game, so it cannot come from the page host), `client/scenes/menu/menu.gd` (Scan button, points
  and streak line), `strings.csv`
- Test: `client/tests/test_profile.gd` (new)

**Interfaces:**
- Produces on `Profile`: `player_id: String` (UUID v4 created once, saved in `user://profile.cfg`),
  `points: int`, `streak: int`, `skins: Array[String]`, `skin: String` (selected), signal `changed`,
  `refresh() -> void` (GET `/profile/{id}`), `start_scan() -> void`.
- Platform rule for `start_scan()` (POST `/scan/start`, then):
  - Web: `JavaScriptBridge.eval("window.location.href = '<url>'")` (same tab, Review Focus 4).
  - Android: `OS.shell_open(url)`; `refresh()` on `NOTIFICATION_APPLICATION_FOCUS_IN`.
  - Windows: open `scan_panel` with the PNG from `qr_url`; poll `refresh()` every 3 s until the streak or points
    change, then close.

- [ ] **Step 1: Write the failing tests** in `test_profile.gd`: `player_id` is created once and reloaded equal;
  parsing a `/profile` JSON sets `points`, `streak`, `skins`; `skin` falls back to `""` when not unlocked.
- [ ] **Step 2:** Run `godot --headless --path client res://tests/test_runner.tscn`. Expected: FAIL.
- [ ] **Step 3:** Implement with `HTTPRequest`. The menu shows "Scan an item" (48 px) and "Points 30, streak 2".
- [ ] **Step 4:** Run the test runner. Then by hand on Windows (QR flow) and on a phone web build (same-tab
  flow). Commit on `scan/client`.

### Task L3: lobby skins (45 min)

Seats are locked at 8 with 2 Trashers (team decision, Sat 21 Nov evening): no size picker. Skip every
`set_size` / `size` / `sizes` item below; build the skin picker only.

**Files:**
- Modify: `client/scenes/lobby/lobby.gd` (host: 6/8/10 picker sends `set_size`; everyone: skin picker sends
  `set_skin`; seats show skins), `server/Uncycled.Server/Rooms/Room.cs` + `Seat.cs` (`Skin`), `Wire.cs`
  (`seats[]` `skin` in `lobby`, `welcome`, `match_started`), `Codec.cs` (`set_skin`),
  `server/Uncycled.Server/Content/ContentLoader.cs` (load `content/skins.json`), `strings.csv`
- Test: `server/Uncycled.Server.Tests/SkinTests.cs` (new), `client/tests/test_lobby.gd`

**Interfaces:**
- Consumes: Hedi's `set_size` (Task 2) and `lobby.size`/`sizes`; `Profile.skins`, `Profile.skin`.
- Produces: `Seat.Skin : string` (default `""`); avatars read `seats[].skin` and draw
  `res://assets/skins/<id>.png` over the tinted body (tell Faouzi the avatar hook: one `set_skin(id)` call in
  `world/player/avatar.gd`).

- [ ] **Step 1: Write the failing tests:** `SkinTests.SetSkin_UnknownId_TargetInvalid`,
  `SetSkin_InLobby_ShowsInLobbySeats`, `SetSkin_DuringMatch_WrongPhase`; `test_lobby.gd`: the size picker shows
  only for the host and lists `sizes`.
- [ ] **Step 2:** Run `dotnet test server --filter SkinTests` and the Godot runner. Expected: FAIL.
- [ ] **Step 3:** Implement. The server trusts the client's skin choice (no profile check: hackathon scope).
- [ ] **Step 4:** Run both. Expected: PASS. Commit on `scan/lobby-skins`.

### Task L4: network benchmark (2 h 30)

**Files:**
- Create: `client/autoload/net_stats.gd` (autoload `NetStats`), `client/ui/hud/net_stats_panel.gd` (small
  overlay, toggled in Settings), `server/Uncycled.Server/Bench/BenchStore.cs`,
  `server/Uncycled.Server.Tests/BenchStoreTests.cs`, `client/tests/test_net_stats.gd`
- Modify: `client/autoload/settings.gd` + `client/ui/widgets/settings_panel.gd` (network label: `ooredoo`,
  `tt_5g`, `tt_5g_sa`, `wifi`, `other`; "show network stats" toggle), `client/project.godot`,
  `server/Uncycled.Server/Net/Codec.cs` (`net_report`), `server/Uncycled.Server/Program.cs`
  (`GET /bench`, `GET /bench.html`, `GET /bench.csv`), `strings.csv`

**Interfaces:**
- `NetStats` listens to `Net` signals only (no edits to `net.gd`); measures:
  - `rtt_ms` samples from `pong` (`now - c`).
  - `jitter_ms`: mean of `abs(rtt[i] - rtt[i-1])`.
  - snapshot gaps from `Net.snapshot` arrival times (late = gap > 100 ms).
  - response time: time from an action `send` to its reply (`use` -> `use_ok`/`action_rejected`, FIFO per action
    name). Faouzi's code calls `NetStats.mark_sent(action: String)` next to each `Net.send` of an action: one line
    per call site, agree on it with Faouzi.
- Produces: `NetStats.summary() -> Dictionary` with the `net_report` fields of spec section 12; sends
  `net_report` every 10 s while connected.
- `BenchStore.Add(string net, string client, BenchSample s)`, `BenchStore.Summary() -> per net: runs, samples,
  rtt_p50, rtt_p95, jitter_ms, late_pct, resp_p50_ms`; keeps at most 10 000 rows; drops a report with any
  negative, NaN or > 60 000 value (Review Focus 5); at most 1 report per 5 s per seat.

- [ ] **Step 1: Write the failing tests:**
  - `BenchStoreTests.Summary_PercentilesPerNetwork` (known samples -> known p50/p95).
  - `BenchStoreTests.Add_DropsNegativeAndNaN`.
  - `BenchStoreTests.RateLimit_OnePer5s`.
  - `test_net_stats.gd`: rtt [100, 120, 90] -> jitter 25; gaps [50, 50, 150] -> late_pct 33.3.
- [ ] **Step 2:** Run `dotnet test server --filter BenchStoreTests` and the Godot runner. Expected: FAIL.
- [ ] **Step 3:** Implement. `/bench.html` is one static HTML page (inline CSS, no external scripts) with a
  table and one horizontal bar per network for p50 round trip, p95 round trip, jitter, response time, plus the
  server location line.
- [ ] **Step 4:** Run both. Expected: PASS. By hand: two clients on different labels, open `/bench.html`, both
  rows appear. Commit on `bench/net-stats`.

### Task L5: builds and hosting (1 h 30, start the iPhone check early)

- [ ] **Step 1:** Web export (threads off, Compatibility):
  `godot --headless --path client --export-release "Web" ../build/web/index.html`; serve it and open it on a
  real iPhone (Safari) and an Android phone. Report problems to Faouzi at once (Review Focus 1).
- [ ] **Step 2:** The .NET server serves the web build from the same host (so the client's default `wss` URL
  works): `app.UseDefaultFiles(); app.UseStaticFiles();` with the web build copied to `wwwroot` in
  `server/Dockerfile`. Threads are off, so no COOP/COEP headers are needed.
- [ ] **Step 3:** Host the game server: build `server/Dockerfile` and deploy (Fly.io or any VM with HTTPS);
  check `https://<host>/health` and a match from two phones. Host the photo service: a team laptop with
  `cloudflared tunnel --url http://localhost:8000` (YOLOE is too heavy for a free VM); put both URLs in
  `Settings` defaults.
- [ ] **Step 4:** Android APK (`--export-debug "Android" ../build/android/uncycled.apk`, `INTERNET` permission)
  and Windows (`--export-release "Windows Desktop" ../build/win/Uncycled.exe`). Install and join the hosted room
  from each.
- [ ] **Step 5:** Write the URLs and how to install in `docs/team/README.md` (section "Links").

### Task L6: benchmark runs for the pitch (Sun 11:00-12:00)

- [ ] Same phone, same server, 3 runs of 3 minutes per network (`ooredoo`, `tt_5g`, `tt_5g_sa`, plus `wifi` as
  reference), each in a real match with bots. Network label set in Settings before each run.
- [ ] Screenshot `/bench.html`, export `/bench.csv`, send both to Faouzi for the deck. Write the server location
  and time of the runs on the slide. Report the numbers as measured.

---

## Done means

- `pytest -q tests/test_scan_profile.py`, `dotnet test server`, and the Godot test runner pass.
- From the hosted web build on an iPhone: scan an item -> points and streak go up -> pick the unlocked skin in
  the lobby -> others see it in the match.
- `/bench.html` shows at least 3 networks with real runs.
