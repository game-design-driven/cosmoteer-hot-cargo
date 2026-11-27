---
name: hot-cargo-install
description: Install the newest pushed Hot Cargo main into the player's own Cosmoteer on the Windows PC. Use after every push to main, when the player's game is closed.
---

# Install into the player's game

The player wants the newest pushed main installed at all times. Never touch a running game.

Run `scripts/install-player.sh`. It takes the main-slot lock, checks that the game is closed, installs `origin/main` from the detached `../play` worktree, turns the mod on and records the commit in `../.state/installed_sha`. When the game runs, it prints "game running" and installs nothing; try again later.

`scripts/keep-installed.sh ../play 115` runs the same install every minute when `origin/main` moves. It only works while the calling session's turn is active, because background commands lose the link to the PC when the turn ends.

## The Mac app

The Mac app (cosmoteer-macos) runs Hot Cargo for real play. Each merge to `main` makes a dev release zip with `install-mac.sh`. Install the newest dev release into the Mac app of this Mac:

```
R=game-design-driven/cosmoteer-hot-cargo; T=$(gh release list -R $R -L1 --json tagName -q '.[0].tagName') && D=$(mktemp -d) && gh release download "$T" -R $R -D "$D" && unzip -q "$D"/*.zip -d "$D" && bash "$D"/hot-cargo-*/install-mac.sh
```

`install-mac.sh` refuses while any Mac game process runs, test games included.

To start the player's game on this Mac, run `release/play-mac.sh` (or the copy in the release zip). Never start the player's game with an autotest script: test mode turns errors into test failures that end the game, and a playtest then reports a crash. The title screen shows the version bottom left (`Hot Cargo v<version>-dev.<n>`). Co-op players need the same version.
