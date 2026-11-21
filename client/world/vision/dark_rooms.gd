class_name DarkRooms
extends Node2D
## Dark rooms: inside each rect the ambient light is taken away (a subtracting light the size of
## the rect), so the room reads black until the player's own light reaches it. The player's light
## and its wall shadows stay what lights the room, so nothing shows through walls. Added under the
## Vision (it hides with it, so no grey room before the first position); the node is top level, so
## its lights keep the map's coordinates. Rects come from MapRoot.dark_rects() and never overlap
## (MapJson.dark_errors), because the ambient would be taken away twice.

## Side of the white light texture. Large, because a light fades over half a texel at its edge:
## with 256 px that is half a pixel for a room of 8 tiles, so the room edge is clean.
const TEX_PX: int = 256


## A node with one subtracting light per rect (tiles, map space). `ambient` is the level the
## Vision's CanvasModulate gives the world: subtracting exactly it leaves only the player's light.
static func create(rects_tiles: Array[Rect2], ambient: Color) -> DarkRooms:
	var node: DarkRooms = DarkRooms.new()
	node.top_level = true
	var img: Image = Image.create(TEX_PX, TEX_PX, false, Image.FORMAT_RGBA8)
	img.fill(Color.WHITE)
	var tex: ImageTexture = ImageTexture.create_from_image(img)
	for r in rects_tiles:
		var px: Rect2 = Rect2(r.position * MapRoot.TILE_PX, r.size * MapRoot.TILE_PX)
		var light: PointLight2D = PointLight2D.new()
		light.blend_mode = Light2D.BLEND_MODE_SUB
		light.color = ambient
		light.shadow_enabled = false
		light.texture = tex
		light.scale = px.size / Vector2(tex.get_size())  # a Light2D texture_scale is uniform: scale the node
		light.position = px.get_center()
		node.add_child(light)
	return node
