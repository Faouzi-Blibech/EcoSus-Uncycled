# Art list for v2 (Yasmin + Khammar)

What the game needs for Cleaners vs Trashers, in priority order. Design rules: `docs/design/2026-11-21-cleaners-vs-trashers.md`.

## How to deliver

- PNG with a transparent background. Pixel art at the game's scale: 1 map tile = **32 x 32 px**; the screen is
  640 x 360 (so a tile is 1/20 of the screen width).
- Use the **exact file names and folders** below, under `client/assets/`. The game already loads these paths and
  shows a coloured square until the file exists, so art can arrive in any order without breaking the build.
- Khammar adds the files, opens the project once in Godot 4.7.2 (it creates the `.import` files), checks them in
  the game, then commits the PNG **and** its `.import` file on a branch `art/<short-name>`.
- Character art is drawn in white and light grey: the game tints it with each player's colour (12 colours).
- Original work only: nothing traced or copied from other games.

## P1: needed for the demo (by Sun 09:00)

| What | File(s) | Size |
|---|---|---|
| Character walk sheet (tintable) | `player/cleaner_sheet.png`: rows down, left, right, up; 4 frames each | 128 x 128 (32 x 32 frames) |
| Body left behind (for reports) | `piles/pile_body.png` | 32 x 32 |
| 6 spot types, dirty and clean | `spots/<type>_dirty.png`, `spots/<type>_clean.png` with `<type>` = `sort_waste`, `turn_compost`, `check_meter`, `crush_cans`, `rinse_bottles`, `deliver_glass` | 32 x 32 or 64 x 64 |
| Recycling Machine, 3 states | `machine/machine_idle.png`, `machine/machine_working.png`, `machine/machine_jammed.png` | 64 x 64 |
| Upgrade icons | `icons/upgrade_backpack.png`, `icons/upgrade_tool.png`, `icons/upgrade_heal.png`, `icons/upgrade_mask.png` | 48 x 48 |
| Toxic gas cloud (tileable, semi-transparent) | `fx/gas_cloud.png` (4 frames side by side) | 128 x 32 |
| Cleanliness meter frame + fill | `ui/meter_frame.png`, `ui/meter_fill.png` | 160 x 16 |
| Logo / title | `ui/logo.png` | 320 x 96 |

## P2: by Sun 11:00

| What | File(s) | Size |
|---|---|---|
| 5 skins (accessory drawn over the character, not tinted) | `skins/seedling.png`, `skins/leaf_warrior.png`, `skins/ocean_guardian.png`, `skins/earth_champion.png`, `skins/recycling_god.png` | 32 x 32 |
| Skin preview cards for the lobby | `skins/<id>_card.png` | 64 x 64 |
| Trasher ability icons | `icons/ability_virus.png`, `icons/ability_gas.png` | 48 x 48 |
| Action button icons | `icons/action_clean.png`, `action_deliver.png`, `action_repollute.png`, `action_sabotage.png`, `action_heal.png`, `action_fix.png`, `action_report.png`, `action_meeting.png` | 48 x 48 |
| Virus countdown icon (HUD) | `icons/virus.png` | 32 x 32 |
| "Someone re-polluted here" cue | `fx/cue_repollute.png` | 32 x 32 |
| Menu background | `ui/menu_bg.png` | 640 x 360 |
| Bag carried (HUD counter) | `icons/bag.png` | 32 x 32 |

## P3: if time allows

| What | File(s) | Size |
|---|---|---|
| Map tiles for market, garden, depot (floor, wall, props) | `tiles/<zone>_tiles.png` | 32 x 32 tiles on one sheet |
| Pitch deck key art: Cleaners vs Trashers line-up, machine, a dirty vs clean street | for the deck, not in the game (send to Faouzi by Sun 12:00) | 1920 x 1080 |
| Results screen art: Cleaners win / Trashers win | `ui/result_cleaners.png`, `ui/result_trashers.png` | 320 x 180 |

## Who does what (suggested)

- **Yasmin (digital artist):** character sheet, skins, machine, spot illustrations, deck key art.
- **Khammar (graphic designer, integration):** icons, meter, logo, menu background, gas and cue effects, putting
  every file into `client/assets/` and checking it in game. Tell Faouzi when a P1 group is in.
