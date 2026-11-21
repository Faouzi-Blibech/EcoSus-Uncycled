# Art handoff (page for the designers)

All art is optional at every moment: each image loads through `Assets` and falls back to a
coloured placeholder.

| What | Path | Size (px) | Notes |
|---|---|---|---|
| Floor and wall tiles | `client/assets/tiles/` | 32x32 | one tileset PNG; walls must read as walls on a phone |
| Player body | `client/assets/player/` | 24x32 (one frame), 4-frame walk strip later | white/greyscale, tinted by seat colour in game |
| Seat icons | `client/assets/player/icons/` | 16x16 | 12 simple shapes, so colour is never the only cue |
| Dump pile, body pile | `client/assets/piles/` | 32x32 | dump piles per waste type: plastic, glass, paper, organic, metal |
| Consoles | `client/assets/consoles/` | 32x32 | bins, compost heap, tap, meeting button, fire point, smog panel |
| HUD buttons | `client/assets/ui/` | 48x48 | use, report, hit, dump, sabotage |
| Home page | `client/assets/ui/home/` | from the 1920x1080 mockup | `background.jpg` (paper, no buttons), `logo.png`, `create.png`, `join.png`, `user.png`, `tasks.png`, `settings.png`; placed by `scenes/menu/menu.gd` at 1/3 of the mockup |
| UI kit | `client/assets/ui/kit/` | 3 texture px per screen px | buttons, round icons, paper panel, player slot, avatar card, input field from the designers' kit; drawn by `ui/widgets/paper_kit.gd` |
| Map cards | `client/assets/ui/maps/` | ~300 px | create page; only `tbs` is playable for now |
| TBS map rooms | `client/assets/maps/tbs/` | 1024 px rooms (office 720x1456, parking 848x1264), drawn at 0.5 | the Figma "Maps" close-ups; walls, obstacles and depth pieces are in `world/maps/tbs_01.layout.json` (re-export with `tools/export_map.gd --map tbs_01`) |
| Characters | `client/assets/characters/<id>/walk_<n>.png` | 300 px tall | walk cycles exported from the designers' GIFs; frame times in `ui/widgets/characters.gd`; `sprout` is white and tinted by seat colour |
| Handwritten font | `client/assets/fonts/caroni_regular.otf` | | home page captions |
| Loading screen | `client/assets/ui/loading_screen.jpg` | 16:9 (1376x768) | boot screen art, covers the window; keep the top centre clear for the status text |
| Mini-game items | `client/assets/minigames/<type>/` | 32x32 | waste items and bins for `sort_waste` |
| Audio | `client/assets/audio/` | OGG | starts after the first tap on web |

Files: PNG, lowercase `snake_case` names, no spaces. Every outside asset gets a line in
`CREDITS.md` with its licence (event rules Art. 5). The map scene's art layer can be painted in
the Godot editor over the greybox, as long as walls and markers stay where they are.
