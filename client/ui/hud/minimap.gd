class_name Minimap
extends Control
## Round mini map of the HUD: the map's own room pictures and corridors, shrunk, in a paper ring
## centred on the local player (red dot), with the Recycling Machine marked. It reads the live
## map nodes, so it always matches the layout. Call `bind_map` once the round has a map.

const DIAMETER_PX: float = 72.0
## Tiles from the centre to the rim.
const VIEW_RADIUS_TILES: float = 14.0
const TILE_PX: float = 32.0
const PAPER: Color = Color("faf2dd")
const INK: Color = Color("3e2b25")
const BACKDROP: Color = Color("b0614a")
const CORRIDOR: Color = Color("efe6d6")
const ME: Color = Color("d26b56")
const MACHINE: Color = Color("6f9a52")
const RIM_PX: float = 2.5
const DOT_PX: float = 3.0

## Returns the local player's position in map pixels.
var player_position: Callable = Callable()
var _view: Control
var _art: Node2D
var _corridors: Dictionary = {}
var _machine_px: Vector2 = Vector2.INF


func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(DIAMETER_PX, DIAMETER_PX)
	size = custom_minimum_size
	# The round paper window clips the shrunk map to a circle.
	var window: Panel = Panel.new()
	var box: StyleBoxFlat = StyleBoxFlat.new()
	box.bg_color = BACKDROP
	box.set_corner_radius_all(int(ceilf(DIAMETER_PX * 0.5)))
	box.anti_aliasing = true
	window.add_theme_stylebox_override("panel", box)
	window.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
	window.mouse_filter = Control.MOUSE_FILTER_IGNORE
	window.size = custom_minimum_size
	add_child(window)
	_view = Control.new()
	_view.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_view.size = custom_minimum_size
	_view.draw.connect(_draw_view)
	window.add_child(_view)
	var rim: Control = Control.new()
	rim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rim.size = custom_minimum_size
	rim.draw.connect(_draw_rim.bind(rim))
	add_child(rim)


func bind_map(map: MapRoot) -> void:
	_art = map.get_node_or_null("Art") as Node2D if map != null else null
	_corridors = {}
	if _art != null:
		for c in _art.get_children():
			if "corridor_cells" in c:
				_corridors = c.get("corridor_cells")
	var m: MapMarker = map.marker("machine") if map != null else null
	_machine_px = m.global_position if m != null else Vector2.INF


func _process(_delta: float) -> void:
	if visible:
		_view.queue_redraw()


func _draw_view() -> void:
	if _art == null or not player_position.is_valid():
		return
	var k: float = (DIAMETER_PX * 0.5) / (VIEW_RADIUS_TILES * TILE_PX)
	var centre: Vector2 = _view.size * 0.5
	var me: Vector2 = player_position.call()
	var origin: Vector2 = centre - me * k
	var cell: float = TILE_PX * k
	for c in _corridors:
		var p: Vector2 = origin + Vector2(c as Vector2i) * cell
		if p.x > -cell and p.y > -cell and p.x < DIAMETER_PX and p.y < DIAMETER_PX:
			_view.draw_rect(Rect2(p, Vector2(cell, cell)).grow(0.15), CORRIDOR)
	for s in _art.get_children():
		var sprite: Sprite2D = s as Sprite2D
		if sprite == null or sprite.texture == null:
			continue
		var rect: Rect2 = Rect2(origin + sprite.position * k, sprite.texture.get_size() * sprite.scale * k)
		if rect.intersects(Rect2(Vector2.ZERO, _view.size)):
			_view.draw_texture_rect(sprite.texture, rect, false)
	if _machine_px != Vector2.INF:
		_view.draw_circle(origin + _machine_px * k, DOT_PX, MACHINE, true, -1.0, true)
		_view.draw_arc(origin + _machine_px * k, DOT_PX, 0.0, TAU, 16, INK, 1.0, true)
	_view.draw_circle(centre, DOT_PX, ME, true, -1.0, true)
	_view.draw_arc(centre, DOT_PX, 0.0, TAU, 16, INK, 1.2, true)


## The paper rim over the window.
func _draw_rim(rim: Control) -> void:
	var c: Vector2 = Vector2(DIAMETER_PX, DIAMETER_PX) * 0.5
	rim.draw_arc(c, DIAMETER_PX * 0.5 - RIM_PX * 0.5, 0.0, TAU, 48, PAPER, RIM_PX + 1.0, true)
	rim.draw_arc(c, DIAMETER_PX * 0.5, 0.0, TAU, 48, INK, 1.5, true)
	rim.draw_arc(c, DIAMETER_PX * 0.5 - RIM_PX - 0.5, 0.0, TAU, 48, INK, 1.0, true)
