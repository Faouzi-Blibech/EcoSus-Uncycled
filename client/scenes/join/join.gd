extends "res://scenes/entry_page.gd"
## Join a room with its 4-letter code (Figma "jOIN gAME": paper sheet, code field, Join).

const CODE_LENGTH: int = 4

## Rects are the Figma frame at 1/3 scale.
const PAPER_RECT: Rect2 = Rect2(70.0, 69.0, 515.0, 282.0)
const CODE_RECT: Rect2 = Rect2(188.0, 140.0, 260.0, 80.0)
const CODE_FONT_SIZE: int = 40
const JOIN_RECT: Rect2 = Rect2(254.0, 236.0, 132.0, 44.0)
const STATUS_RECT: Rect2 = Rect2(110.0, 284.0, 420.0, 40.0)

var _code_edit: LineEdit
var _join_btn: Button


func _build(stage: Control) -> void:
	_add_paper(stage, PAPER_RECT)

	_code_edit = PaperKit.line_edit(tr("MENU_CODE_PLACEHOLDER"), CODE_FONT_SIZE, CODE_LENGTH)
	PaperKit.place_fit(_code_edit, CODE_RECT, PaperKit.aspect_of(PaperKit.tex("field_input.png")))
	_code_edit.text_changed.connect(_on_code_changed)
	_code_edit.text_submitted.connect(func(_t: String) -> void: _on_join())
	stage.add_child(_code_edit)

	_join_btn = PaperKit.button(tr("MENU_JOIN"), "green")
	PaperKit.place_button(_join_btn, JOIN_RECT)
	_join_btn.pressed.connect(_on_join)
	stage.add_child(_join_btn)

	PaperKit.place(_status, STATUS_RECT)
	stage.add_child(_status)
	_code_edit.grab_focus.call_deferred()


## Codes are upper case; typing lower case is fine.
func _on_code_changed(text: String) -> void:
	var upper: String = text.to_upper()
	if upper != text:
		var caret: int = _code_edit.caret_column
		_code_edit.text = upper
		_code_edit.caret_column = caret


func _on_join() -> void:
	var code: String = _code_edit.text.strip_edges().to_upper()
	if code.length() != CODE_LENGTH:
		_status.text = tr("MENU_BAD_CODE")
		return
	_send("join", {"code": code})


func _apply_busy(busy_now: bool) -> void:
	_join_btn.disabled = busy_now
