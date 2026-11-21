@tool
class_name MapRoot
extends Node2D
## Root of a map scene: child `Floor` (TileMapLayer) and child `Markers` (Node2D holding MapMarkers).

const TILE_PX: int = 32

@export var map_id: String = ""


func floor_layer() -> TileMapLayer:
	return get_node("Floor") as TileMapLayer


## Pieces of the art drawn in front of players standing above them (y-sorted), or null.
func occluders() -> Node2D:
	return get_node_or_null("Occluders") as Node2D


func markers() -> Array[MapMarker]:
	var out: Array[MapMarker] = []
	var holder: Node = get_node_or_null("Markers")
	if holder != null:
		_collect(holder, out)
	return out


func marker(id: String) -> MapMarker:
	for m in markers():
		if m.marker_id == id:
			return m
	return null


## The zone marker whose rect contains the point (pixels, map space), else null.
func zone_at(pos_px: Vector2) -> MapMarker:
	for m in markers():
		if m.kind != "zone":
			continue
		var rect := Rect2(m.position, Vector2(m.zone_size_tiles * TILE_PX))
		if rect.has_point(pos_px):
			return m
	return null


## The dark-room rects in tiles (map space), in marker order. Client-only: see MapJson.dark_of.
func dark_rects() -> Array[Rect2]:
	var out: Array[Rect2] = []
	for m in markers():
		if m.kind == "dark":
			out.append(Rect2(m.position / TILE_PX, Vector2(m.zone_size_tiles)))
	return out


func _collect(node: Node, out: Array[MapMarker]) -> void:
	for child in node.get_children():
		if child is MapMarker:
			out.append(child)
		_collect(child, out)


## True when the point (tiles, map space) lies in a dark room; the same half-open rule as the server.
func is_dark(pos_tiles: Vector2) -> bool:
	for r in dark_rects():
		if r.has_point(pos_tiles):
			return true
	return false
