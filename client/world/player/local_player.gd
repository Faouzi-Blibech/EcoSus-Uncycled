class_name LocalPlayer
extends CharacterBody2D
## The player this client controls. Walls come from the map tileset physics layer 0, so the
## body slides along them; the server still validates every position we send.

## Works before add_child, so the avatar can be set up before it enters the tree.
var avatar: Avatar:
	get:
		return get_node("Avatar") as Avatar


func _ready() -> void:
	var shape: CircleShape2D = ($Collision as CollisionShape2D).shape as CircleShape2D
	shape.radius = Net.tiles_to_px(Avatar.RADIUS_TILES)


## Call from _physics_process. dir is the input vector (length 0..1), speed in px per second.
func move_with(dir: Vector2, speed_px_per_s: float) -> void:
	velocity = dir * speed_px_per_s
	move_and_slide()


## Puts the body at a server-given position without sliding.
func teleport_to(pos_px: Vector2) -> void:
	global_position = pos_px
	velocity = Vector2.ZERO
