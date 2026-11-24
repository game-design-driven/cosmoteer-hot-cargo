---
name: hot-cargo-screenshots
description: Take in-game screenshots of Hot Cargo, on the Mac (RUNNER=mac, the cosmoteer-macos host) or in a windowed test slot on the PC (never the player's game slot). Use for any visual check, PR screenshots, README images or UI review.
---

# Screenshots

A PR agent takes its own screenshots. Never use slot main on the PC: it is the player's game.

1. Write a script `tests/screens/<name>.txt`. It starts with `render on` (the game draws only with it) and uses `screenshot NAME`.
2. Run: `scripts/screens.sh tests/screens/<name>.txt <local folder> [timeout]`. It takes a free test slot (or `SLOT=tN`), deploys this worktree, starts the game with graphics, minimized, with the slot's own save folder, runs the script, and copies the PNGs and the log to the folder. PC calls need an active turn: a background Bash call and `t3_background_wait` for long scripts.
3. Look at the PNGs with the Read tool. For a PR, use the `gh-pr-images` skill.

## Which runner

- **Mac (`RUNNER=mac scripts/screens.sh tests/screens/<name>.txt <local folder> [timeout]`):** use it first, for single-player and co-op screen scripts. It needs no PC, no PC test slot and no active turn, and it works while the player holds the PC. The game takes one Mac sim slot, under the same cap as the sim tests (`HOT_CARGO_SIM_JOBS`), so it waits while the slots are busy.
  - It runs the real game with the cosmoteer-macos host: a Metal renderer, a hidden window of 2560x1440 pixels (the PC screenshot size), no sound.
  - It needs the cosmoteer-macos checkout next to this repo (`<container>/../cosmoteer-macos/main`, or `COSMOTEER_MACOS=<worktree>`) with `tools/hotcargo-screens.sh`. Without it, `screens.sh` stops with a message.
  - Differences from the PC: the text rasterizer differs a little. On 2026-11-15 the same scripts gave the same UI; `systems` differed in 1% of pixels, `map-consequences` in about 9% (animated map background).
  - The default timeout is 900 s: loading game data takes minutes on a busy Mac.
  - Use the up-to-date `cosmoteer-macos/main` (the default), not an older worktree. Reason: an old runner drew the UI at half scale and put the camera off-centre, which looked like a script bug.
  - The first `auto_frame` after `auto_interior on`, or after damage that shows a hint, does not hold: call it twice, with a wait between.
  - `auto_frame` sizes under about 13 do not zoom closer on the Mac: crop the 2560x1440 shot instead.
  - `auto_cargo` fills only flexible storage (`FlexResourceGrid`). Fill vaults with `tech add <n>`.
- **PC (default, `RUNNER=pc`):** use it for the final look of a Windows-only detail, or when the Mac runner is missing.

While `.state/player-hold` exists, `screens.sh` refuses (exit 3): a window in a test slot could take focus or GPU time from the player. Headless tests stay allowed. Try again when the hold ends, or ask the coordinator.

Co-op screenshots: `scripts/screens-mp.sh tests/screens/<name>.host.txt tests/screens/<name>.client.txt <local folder>`. The host runs in a windowed test slot, the client in a headless test slot.

Co-op screenshots on the Mac: `RUNNER=mac scripts/screens-mp.sh <host script> <client script> <local folder>`. Two hidden Mac games run, through `screens.sh`. The client logs go to `<local folder>/client`.

Rules:
- Keep sessions short.
- A scene outside the player's sight shows only radar blips. Add `reveal on` (fog of war off) to the screenshot script, never to a game test.
- Images for the repo go to `docs/screenshots/` as JPEG. Crop out anything personal: user names, paths, Steam names.
