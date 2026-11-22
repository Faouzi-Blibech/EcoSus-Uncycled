extends RefCounted
## The skin shop's catalogue: a copy of content/skins.json (the photo service and the server read
## that file; keep the two in step). The sprout is the free starter and is not in the file.
## Every other skin is bought with scan points in the shop ("price"); the player picks which.

const STARTER: Dictionary = {"id": "sprout", "streak": 0, "rarity": "starter"}
const CATALOGUE: Array[Dictionary] = [
	{"id": "robot", "price": 100, "rarity": "common"},
	{"id": "rabbit", "price": 150, "rarity": "uncommon"},
	{"id": "eagle", "price": 250, "rarity": "rare"},
]


## Every skin in shop order: the starter first, then the catalogue (cheapest first).
static func all() -> Array[Dictionary]:
	var out: Array[Dictionary] = [STARTER]
	out.append_array(CATALOGUE)
	return out


## "wearing", "owned" or "locked" for one skin. Pure, for tests.
static func status(id: String, unlocked: Array[String], wearing: String) -> String:
	var worn: String = wearing if wearing != "" else STARTER["id"]
	if id == worn:
		return "wearing"
	if id == STARTER["id"] or unlocked.has(id):
		return "owned"
	return "locked"
