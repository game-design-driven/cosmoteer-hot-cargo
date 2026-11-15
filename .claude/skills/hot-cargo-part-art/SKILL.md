---
name: hot-cargo-part-art
description: Draw or change the art of a Hot Cargo ship part so it fits the vanilla terran look. Vanilla deck and walls by reference from the rules, own layers from tools/sprites/make_sprites.py, a quick preview and in-game screenshots. Use for a new part, an art change, or a part that looks out of place.
---

# Part art

The rules and the reasons are in `docs/part-art.md`. This skill gives the order.

1. Never commit a vanilla image or an edit of one. Reference vanilla layers by path from the rules (`mod/autonomy/part_art.rules`).
2. Look at 2 or 3 vanilla parts of the same size and job in `<container>/../cosmoteer-macos/.game/Data/ships/terran/`.
3. In the part's rules: one `DeckTile` per tile, a `Walls` frame from `part_art.rules`, and the own layers from `sprites/<part>/`.
4. In `tools/sprites/make_sprites.py`: one function per part, built from `kit.Sprite` shapes. Run `uv run tools/sprites/make_sprites.py`.
5. Preview without the game: `uv run tools/sprites/preview.py <scratch>/preview.png`. Compare with the vanilla columns. Repeat step 4 until the part fits.
6. In-game: `RUNNER=mac scripts/screens.sh tests/screens/part-art.txt <scratch folder>` (`hot-cargo-screenshots`). Look at the roofs, build mode, the part list and both damage levels.
7. After a rules change, run the smoke test of the part's area (`scripts/autotest-affected.sh`): a broken path stops the game while it loads data.
8. In the PR: before and after images (`gh-pr-images`), and one line on how each committed PNG was made.
