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
| Loading screen | `client/assets/ui/loading_screen.jpg` | 16:9 (1376x768) | boot screen art, covers the window; keep the top centre clear for the status text |
| Mini-game items | `client/assets/minigames/<type>/` | 32x32 | waste items and bins for `sort_waste` |
| Audio | `client/assets/audio/` | OGG | starts after the first tap on web |

Files: PNG, lowercase `snake_case` names, no spaces. Every outside asset gets a line in
`CREDITS.md` with its licence (event rules Art. 5). The map scene's art layer can be painted in
the Godot editor over the greybox, as long as walls and markers stay where they are.
