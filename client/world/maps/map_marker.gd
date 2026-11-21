@tool
class_name MapMarker
extends Marker2D
## A point of interest painted on a map scene. Exported to the map JSON by MapJson.
## For kinds "zone", "dark" and "gas_room" the marker position is the rect's top-left corner in pixels.
## "dark" is client-only: MapJson writes it to the .meta.json, never to the server JSON.

@export var marker_id: String = ""
@export_enum("spawn", "task", "emergency", "fire_fix", "smog_fix", "zone", "dark", "machine", "gas_room") var kind: String = "spawn"
## Interaction range in tiles (consoles and specials).
@export var range_tiles: float = 1.2
## Rect size in tiles (kinds "zone", "dark" and "gas_room").
@export var zone_size_tiles: Vector2i = Vector2i.ZERO
## i18n key of the zone's display name (kind "zone" only).
@export var name_key: String = ""
