extends Node
## Sound effects and music. One short sound per game event: listens to Net.event, so each server
## message plays once (snapshots never play an event sound, they only drive the clock tick, the
## infection heartbeat and the cleanliness milestones). Every button clicks; round.gd calls
## step_tick() for footsteps; the mini-game layer plays the open and close swishes.
## The files come from client/tools/gen_sounds.py (all generated, no licences). A missing file is
## silence (Assets.sound), never a crash.

const SFX_PATH: String = "res://assets/sounds/sfx/%s.wav"
const MUSIC_PATHS: Dictionary = {
	"menu": "res://assets/sounds/music/music_menu.ogg",
	"match": "res://assets/sounds/music/music_match.ogg",
}
## Players for overlapping effects; the oldest is reused when all are busy.
const POOL_SIZE: int = 8
const SFX_DB: float = -4.0
const MUSIC_DB: float = -14.0
## A sound heard from someone else (a witnessed re-pollute) is quieter than your own.
const WITNESS_DB: float = -9.0
## Footstep rhythm while walking.
const STEP_INTERVAL_S: float = 0.3
## Last seconds of the match clock that tick, and of an infection that beat.
const CLOCK_WARN_S: float = 10.0
const HEART_WARN_S: float = 10.0
## Cleanliness steps that ring a small chime when crossed upwards.
const METER_STEP: float = 25.0

## Replaceable so tests never touch the audio server: func(name: String, db: float, pitch: float).
var play_fn: Callable = func(n: String, db: float, pitch: float) -> void: _play_stream(n, db, pitch)
var enabled_fn: Callable = func() -> bool: return Settings.sound_enabled
var music_enabled_fn: Callable = func() -> bool: return Settings.music_enabled

var _pool: Array[AudioStreamPlayer] = []
var _next: int = 0
var _music: AudioStreamPlayer = null
var _music_name: String = ""
var _cache: Dictionary = {}
var _step_t: float = 0.0
var _step_alt: bool = false
var _last_clock_s: int = -1
var _last_heart_s: int = -1
var _last_meter: float = -1.0


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	for i in POOL_SIZE:
		var p: AudioStreamPlayer = AudioStreamPlayer.new()
		add_child(p)
		_pool.append(p)
	_music = AudioStreamPlayer.new()
	_music.volume_db = MUSIC_DB
	add_child(_music)
	Net.event.connect(handle_message)
	Net.match_started.connect(_on_match_started)
	Net.lobby.connect(func(_d: Dictionary) -> void: play_music("menu"))
	Net.snapshot.connect(handle_snapshot)
	get_tree().node_added.connect(_on_node_added)
	play_music("menu")


## Plays one effect by file name (without .wav). `vary` adds a small random pitch change so repeated
## sounds (steps, clicks) do not sound robotic.
func play(sfx_name: String, db: float = 0.0, vary: bool = false) -> void:
	if not enabled_fn.call():
		return
	var pitch: float = randf_range(0.93, 1.07) if vary else 1.0
	play_fn.call(sfx_name, SFX_DB + db, pitch)


## One server message (docs/protocol.md). Returns the sound it played, "" for none (tests use it).
func handle_message(msg: Dictionary) -> String:
	var s: String = sound_for(msg, Session.role)
	if s.is_empty():
		return ""
	var witnessed: bool = str(msg.get("t", "")) == "cue"
	play(s, WITNESS_DB if witnessed else 0.0)
	if str(msg.get("t", "")) == "game_over":
		stop_music()
	return s


## Which effect a message plays, "" for none. `role` is the local role ("citizen"/"polluter").
static func sound_for(msg: Dictionary, role: String) -> String:
	match str(msg.get("t", "")):
		"dash_ok":
			return "dash"
		"spot_cleaned":
			return "spot_cleaned"
		"delivered":
			return "deliver"
		"upgrade_offer":
			return "upgrade_offer"
		"upgrade_ok":
			return "upgrade_ok"
		"healed":
			return "healed"
		"mask_used":
			return "mask_used"
		"infected":
			return "infected"
		"repollute_ok":
			return "repollute"
		"cue":
			return "repollute" if str(msg.get("kind", "")) == "repollute" else ""
		"sabotage_started":
			return "smog" if str(msg.get("kind", "")) == "smog" else "sabotage_machine"
		"sabotage_ended":
			return "sabotage_fixed" if str(msg.get("result", "")) == "fixed" else ""
		"ability_unlocked", "ability_ok":
			return "ability_unlocked"
		"virus_planted":
			return "virus_planted"
		"gas_warning":
			return "gas_warning"
		"gas_done":
			return "gas_done"
		"meeting_started":
			return "meeting_started"
		"voting_started":
			return "voting_started"
		"vote_cast":
			return "vote_cast"
		"chat":
			return "chat"
		"ejection":
			return "ejection_trasher" if bool(msg.get("was_polluter", false)) else "ejection_innocent"
		"you_are_out":
			# A vote-out already played the ejection sound.
			return "" if str(msg.get("cause", "")) == "vote" else "you_are_out"
		"action_rejected":
			return "action_rejected"
		"game_over":
			var won: bool = (str(msg.get("winner", "")) == "citizens") == (role != "polluter")
			return "victory" if won else "defeat"
	return ""


## Snapshots: the last seconds of the match clock tick, an infection beats, the cleanliness
## meter chimes each time it passes a 25% step upwards.
func handle_snapshot(snap: Dictionary) -> void:
	if str(snap.get("phase", "")) != "playing":
		_last_clock_s = -1
		return
	var left: float = float(snap.get("deadline_t", 0.0)) - float(snap.get("clock_t", 0.0))
	var left_s: int = ceili(left)
	if left > 0.0 and left <= CLOCK_WARN_S and left_s != _last_clock_s:
		_last_clock_s = left_s
		play("clock_tick")
	var me: Dictionary = Net.as_dict(snap.get("me"))
	if me.has("infected_s"):
		var heart_s: int = ceili(float(me.get("infected_s", 0.0)))
		if float(me.get("infected_s", 0.0)) <= HEART_WARN_S and heart_s != _last_heart_s:
			_last_heart_s = heart_s
			play("heartbeat")
	else:
		_last_heart_s = -1
	var meter: float = float(snap.get("meter", 0.0))
	if _last_meter >= 0.0 and floorf(meter / METER_STEP) > floorf(_last_meter / METER_STEP) and meter < 100.0:
		play("meter_milestone")
	_last_meter = meter


## Footsteps: call every physics frame with whether the local player is walking.
func step_tick(delta: float, moving: bool) -> void:
	if not moving:
		_step_t = STEP_INTERVAL_S  # the first step sounds as soon as walking starts
		return
	_step_t += delta
	if _step_t >= STEP_INTERVAL_S:
		_step_t = 0.0
		_step_alt = not _step_alt
		play("step_2" if _step_alt else "step_1", -6.0, true)


func play_music(music_name: String) -> void:
	if _music == null or music_name == _music_name:
		return
	_music_name = music_name
	if not music_enabled_fn.call() or not MUSIC_PATHS.has(music_name):
		_music.stop()
		return
	var stream: AudioStream = Assets.sound(str(MUSIC_PATHS[music_name]))
	if stream is AudioStreamOggVorbis:
		(stream as AudioStreamOggVorbis).loop = true
	_music.stream = stream
	_music.play()


func stop_music() -> void:
	_music_name = ""
	if _music != null:
		_music.stop()


## Settings changed: restart or stop the music to match.
func refresh_music() -> void:
	var wanted: String = _music_name if not _music_name.is_empty() else "menu"
	_music_name = ""
	play_music(wanted)


func _on_match_started(data: Dictionary) -> void:
	_last_meter = -1.0
	play("role_trasher" if str(data.get("role", "")) == "polluter" else "role_cleaner")
	play_music("match")


## Every button in the game clicks when pressed. Set meta "no_click_sfx" on one to opt out.
func _on_node_added(node: Node) -> void:
	if node is BaseButton and not node.has_meta("no_click_sfx"):
		var b: BaseButton = node as BaseButton
		if not b.pressed.is_connected(_on_button_pressed):
			b.pressed.connect(_on_button_pressed)


func _on_button_pressed() -> void:
	play("ui_click", 0.0, true)


func _play_stream(sfx_name: String, db: float, pitch: float) -> void:
	if not _cache.has(sfx_name):
		_cache[sfx_name] = Assets.sound(SFX_PATH % sfx_name)
	var p: AudioStreamPlayer = _pool[_next]
	_next = (_next + 1) % _pool.size()
	p.stream = _cache[sfx_name]
	p.volume_db = db
	p.pitch_scale = pitch
	p.play()
