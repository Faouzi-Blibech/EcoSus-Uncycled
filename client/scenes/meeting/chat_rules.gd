extends RefCounted
## The rules of the chat controls that need no scene: which note a refusal gets, when a typed line or
## a ghost line may go, the "has voted" note, and how far the soft keyboard lifts the box. The meeting
## scene and the ghost panel both use them; the server decides in the end.

## Phases where an out seat may ghost chat ("at any time of the match": not in the lobby or after it).
const GHOST_PHASES: Array[String] = ["intro", "playing", "meeting", "ejection"]
## Refusal reasons that have a note of their own (the key is REJECT_<REASON>); every other
## refusal of a chat line reads as CHAT_REJECTED.
const OWN_NOTE_REASONS: Array[String] = ["too_long", "text_limit", "phrase_limit", "rate_limited"]
const GENERAL_NOTE_KEY: String = "CHAT_REJECTED"


## The translation key of the one short note for a refused chat line.
static func note_key(reason: String) -> String:
	return "REJECT_" + reason.to_upper() if OWN_NOTE_REASONS.has(reason) else GENERAL_NOTE_KEY


## Discussion stage, I am in and have typed lines left (the phrase picker has its own rule).
static func can_text(s: Object) -> bool:
	var stage: String = str((s.meeting as Dictionary).get("stage", ""))
	return s.phase == "meeting" and stage == "discussion" and not s.is_out \
			and not s.seat_out(s.seat_id) and s.text_left() > 0


## I am out: ghost chat, typed or phrases, with no per-meeting cap.
static func can_ghost(s: Object) -> bool:
	return (s.is_out or s.seat_out(s.seat_id)) and GHOST_PHASES.has(str(s.phase))


## The system line when a seat has voted: who, never for whom.
static func voted_note(name: String) -> String:
	return TranslationServer.translate(&"CHAT_HAS_VOTED") % name


## Logical pixels to lift a bottom-anchored chat box so the soft keyboard (kb_px high in window
## pixels, window_h_px high) does not cover it; 0 without a keyboard or a window height.
static func lift_px(kb_px: int, window_h_px: int, viewport_h: float) -> float:
	if kb_px <= 0 or window_h_px <= 0:
		return 0.0
	return float(kb_px) * viewport_h / float(window_h_px)
