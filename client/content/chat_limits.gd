extends RefCounted
## Fallbacks for the chat limits of the welcome tuning ("meeting": text_max_chars, text_per_meeting,
## phrases_per_meeting, chat_min_gap_s). The server enforces the same numbers; the client reads the
## tuning first (Session) and uses these only when the server sends none. One place, no autoload, so
## the probe tools can read it too.

const Phrases := preload("res://content/phrases.gd")

## Characters of one typed line (the server counts code points, like a Godot LineEdit).
const TEXT_MAX_CHARS: int = 120
const TEXT_PER_MEETING: int = 5
const PHRASES_PER_MEETING: int = Phrases.LIMIT
## Seconds between two chat lines of one seat, typed or phrase.
const MIN_GAP_S: float = 2.0
