# Mini-game piece art

The mini-games already use the paper page, the kit buttons and the spot art (`assets/spots/`) as a
faded backdrop. Their **pieces** (the things you drag or tap) still show a paper card with a name
until their picture exists here. Each file is picked up by name, no code change needed
(`Minigame.dress`): the picture fills the piece's box at its own proportions.

Generate with Gemini using `EcoSus_Asset_Skill.md` (attach 2-3 existing assets as style
references), cut the magenta background, and save as PNG with transparency under the name below.

| File | Game | Shown in a box of | Ratio to ask |
|---|---|---|---|
| `item_water_bottle.png` | Sort waste | 104 x 52 | 1:1 |
| `item_tin_can.png` | Sort waste | 104 x 52 | 1:1 |
| `item_yogurt_pot.png` | Sort waste | 104 x 52 | 1:1 |
| `item_newspaper.png` | Sort waste | 104 x 52 | 1:1 |
| `item_cardboard_box.png` | Sort waste | 104 x 52 | 1:1 |
| `item_magazine.png` | Sort waste | 104 x 52 | 1:1 |
| `item_jam_jar.png` | Sort waste | 104 x 52 | 1:1 |
| `item_wine_bottle.png` | Sort waste | 104 x 52 | 1:1 |
| `item_perfume_flask.png` | Sort waste | 104 x 52 | 1:1 |
| `item_apple_core.png` | Sort waste | 104 x 52 | 1:1 |
| `item_banana_peel.png` | Sort waste | 104 x 52 | 1:1 |
| `item_eggshell.png` | Sort waste | 104 x 52 | 1:1 |
| `bin_plastic_metal.png` | Sort waste | 144 x 64 | 1:1 |
| `bin_paper.png` | Sort waste | 144 x 64 | 1:1 |
| `bin_glass.png` | Sort waste | 144 x 64 | 1:1 |
| `bin_organic.png` | Sort waste | 144 x 64 | 1:1 |
| `can.png` | Crush cans | 88 x 96 | 1:1 |
| `can_flat.png` | Crush cans (after the tap) | 88 x 96 | 1:1, edit of `can` |
| `crate.png` | Deliver glass (step 1) | 160 x 72 | 2:1 |
| `glass_bank.png` | Deliver glass (step 2) | 200 x 80 | 5:2 |
| `bottle.png` | Deliver glass (step 2) | 72 x 64 | 1:1 |
| `compost_handle.png` | Turn compost | 64 x 64 | 1:1 |

## Prompts

Start every prompt with the template from the skill (section 7). Fill `[SUBJECT]` with:

- **Sort-waste items** (one image each, sticker style with the thin cream border, front view, 1:1,
  magenta background): a crumpled clear plastic water bottle with a mint label; a small tin can with
  a torn mustard label; an empty yogurt pot with a peeled lid; a folded newspaper; a small flattened
  cardboard box; a rolled glossy magazine; an empty glass jam jar with a gingham lid; an empty green
  wine bottle; a small cut-glass perfume flask; an apple core with two seeds; a banana peel; a
  cracked eggshell.
- **The four bins** (front view, 1:1, magenta background, same bin shape for all four, only the
  colour and the painted symbol change; generate the first, then edit it for the other three): a
  rounded wheelie recycling bin with a swing lid. Plastic and metal: mustard `#f4c361` with a bottle
  and can symbol. Paper: sky-grey blue `#bdd3db` with a newspaper symbol. Glass: leaf green
  `#8aab6c` with a jar symbol. Organic: warm brown `#836f5e` with a leaf symbol.
- **Can**: one upright drink can, scuffed metal with a mint label, front view, 1:1. Then edit:
  "Only change: the can is crushed flat from the top, half its height, crumpled sides."
- **Crate**: a wooden crate full of empty glass bottles, front 3/4 view, 2:1.
- **Glass bank**: a wide green glass-recycling bank with two round slots on top, front view, 5:2
  (same look as the glass-bank spot in `assets/spots/deliver_glass_dirty.png`).
- **Bottle**: one empty mint-glass bottle lying at a slight angle, 1:1.
- **Compost handle**: a round wooden crank knob with a short iron arm, top-down, 1:1.
