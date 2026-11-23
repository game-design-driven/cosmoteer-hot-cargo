# Part art

How Hot Cargo parts get art that fits the vanilla terran look. Spec: 1057. Skill: `hot-cargo-part-art`.

## Rule: no vanilla pixels in the repo

The repo is public. No committed image is a copy or an edit of a game image.

- To show vanilla art, reference the game file by path from the rules: `File = "./Data/ships/terran/<part>/<layer>.png"`. The game loads it from the player's own install.
- Every committed PNG is the output of `tools/sprites/make_sprites.py`. The script is the source. Change the script, then run it again.
- The colours in `tools/sprites/kit.py` are picked by eye from the vanilla parts. They are numbers, not art.
- `tools/sprites/preview.py` writes images with vanilla pixels. Write them outside the repo only. The script refuses a path inside the repo.

## The workflow

A part has two kinds of layers.

| Kind | Layers | Source |
|---|---|---|
| Generic vanilla layers | deck floor, wall frame, roof light | referenced from the rules, shared in `mod/autonomy/part_art.rules` |
| Own layers | machine, doodads, lights, roof, roof normals, roof glows, blueprint, icon | `tools/sprites/make_sprites.py` |

Steps for a new part or a change:

1. Look at 2 or 3 vanilla parts of the same size and job. They are in the game folder: `<container>/../cosmoteer-macos/.game/Data/ships/terran/` on the Mac.
2. In the part's rules, add one `DeckTile` per tile and a `Walls` frame from `part_art.rules`. If no frame fits, add one there that references a vanilla wall image.
3. In `make_sprites.py`, add a function for the part. Build each layer from `kit.Sprite` shapes. Copy the structure of an existing part.
4. Run `uv run tools/sprites/make_sprites.py`.
5. Run `uv run tools/sprites/preview.py <scratch>/preview.png`. Compare the part with the vanilla parts in the same image. Repeat from step 3.
6. Take in-game screenshots: `RUNNER=mac scripts/screens.sh tests/screens/part-art.txt <scratch folder>`. Add the part to that script if it is new.

## How the kit draws

`kit.Sprite` is a height map with a material per pixel. Shapes write into it: `put` (plates, with a bevel), `dome`, `cylinder`, `pipe`, `cut` (grooves), `slots` (grilles), `rivets`, `greebles` (small panels and vents in a free area), `hole` (roof openings).

The kit then reads the height map in one of three ways:

- `color()`: metal, lit from the top left, with dark crevices and small specular highlights. Use it for the machine and the doodads.
- `roof()`: flat paint (green `0,230,0` is the paint mask) with faint panel lines. The game lights the roof through its normal map.
- `normals()`: the normal map. Green points up, as in vanilla.

Other helpers: `glow` (roof glows around the roof holes), `lights` (dim light on black for `lights_add`), `damaged` (the `_33` and `_66` levels), `blueprint`, `deck` and `frame` (for the editor icon only).

## Vanilla conventions

- 64 px per tile.
- Light from the top left. Bright rims on top-left edges, near-black gaps.
- Layer order: `floors`, `turrets`, `doodads_low`, `walls`, crew, `weapons`, `doodads_high`, `roofs`, `roof_doodads`. The machine of a part goes on `turrets`, so it draws over the referenced deck and under the doodads.
- Roofs are paint with holes. Most roof detail is in the normal map. Most colour on a roof is in the roof glows around the holes.
- Lights (`lights_add`) are dim. A bright light washes out the part.
- Damage cuts jagged holes with a soft dark scorch. In a machine, the holes are dark, because a clear hole shows the light deck below.

## Items

Items (resources) follow the same rule: the script is the source. Spec: 2000 to 2004.

1. Add a design function to `tools/sprites/make_items.py` and add it to `ITEMS`. The function name is the item ID without `yarden_zamir.`. The script finds the rules file by its `ID` line.
2. In the item rules, reference `Icon`, `Overlay`, `NuggetSprite` and `StackSprites` from `sprites/<item>/sprites.rules`. Copy the four lines from an existing item.
3. Run `uv run tools/sprites/make_items.py`. It writes the PNG files and `sprites.rules` next to the item rules.
4. Run `uv run tools/sprites/item_sheet.py <scratch>/items.png`. Compare each item with the vanilla rows at the top and at game size in the last row.
5. For a blinking light, add the design to `ANIM` and draw the light off when the frame is not lit.

## Limits

- The kit draws geometric machines: plates, rings, pipes, grilles. Organic or hand-painted detail (cables in loops, worn paint) needs hand work. If a part needs that, draw it by hand as a PNG and say so in the PR.
- A sprite cannot use part of a vanilla image: the part sprites support tint, a quarter rotation and a mirror, but no sub-rectangle. A vanilla layer is usable only as a whole image.
- Vanilla wall frames are made for their own part. A frame for a new size needs a vanilla part of that size, or an own frame from the kit.
- `preview.py` approximates the game lighting. The in-game screenshot is the final check.
