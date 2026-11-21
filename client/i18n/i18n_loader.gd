extends RefCounted
## Parses i18n/strings.csv (columns: keys,en,fr,...) and registers one Translation per language
## column with the TranslationServer. Works without the editor's CSV import step.

const CSV_PATH: String = "res://i18n/strings.csv"


static func load_csv(path: String = CSV_PATH) -> void:
	var file: FileAccess = FileAccess.open(path, FileAccess.READ)
	if file == null:
		push_error("I18n: cannot open %s (error %d)" % [path, FileAccess.get_open_error()])
		return
	var text: String = file.get_as_text().trim_prefix("﻿")
	file.close()

	var rows: Array = parse_csv(text)
	if rows.is_empty():
		push_error("I18n: %s is empty" % path)
		return

	var header: PackedStringArray = rows[0]
	var translations: Array[Translation] = []
	for col in range(1, header.size()):
		var tr_obj: Translation = Translation.new()
		tr_obj.locale = header[col].strip_edges()
		translations.append(tr_obj)

	for r in range(1, rows.size()):
		var row: PackedStringArray = rows[r]
		if row.size() == 0:
			continue
		var key: String = row[0].strip_edges()
		if key.is_empty() or key.begins_with("#"):
			continue
		for col in range(1, header.size()):
			if col >= row.size():
				continue
			var msg: String = row[col].replace("\\n", "\n")
			if msg.is_empty():
				continue
			translations[col - 1].add_message(StringName(key), StringName(msg))

	for t in translations:
		TranslationServer.add_translation(t)


## Minimal RFC 4180 style parser: commas, double quotes ("" escapes), CRLF or LF line ends.
static func parse_csv(text: String) -> Array:
	var rows: Array = []
	var row: PackedStringArray = PackedStringArray()
	var field: String = ""
	var in_quotes: bool = false
	var i: int = 0
	var n: int = text.length()
	while i < n:
		var c: String = text[i]
		if in_quotes:
			if c == "\"":
				if i + 1 < n and text[i + 1] == "\"":
					field += "\""
					i += 1
				else:
					in_quotes = false
			else:
				field += c
		else:
			if c == "\"":
				in_quotes = true
			elif c == ",":
				row.append(field)
				field = ""
			elif c == "\n":
				row.append(field)
				field = ""
				rows.append(row)
				row = PackedStringArray()
			elif c == "\r":
				pass
			else:
				field += c
		i += 1
	if field != "" or row.size() > 0:
		row.append(field)
		rows.append(row)
	return rows
