---
name: hot-cargo-release
description: Publish a Hot Cargo GitHub release with the player zip (mod, loader, install.bat). Use when a main commit has a green full-suite check.
---

# Release

1. Check that a recent `main` commit has a green `full-suite` check: `gh run list --workflow check.yml --branch main --event workflow_dispatch --limit 5`. For a run on the newest commit: `gh workflow run check.yml --ref main`.
2. `scripts/release.sh <version>`. It picks the newest commit on `main` whose required CI checks and `full-suite` check passed (for a full suite on the newest commit: `gh workflow run check.yml --ref main`), builds `hot-cargo-<version>.zip` with `scripts/package.sh`, tags the commit, pushes the tag and publishes the release with a change list.
3. Install the release into the player's game (`hot-cargo-install`).

The zip holds the mod folder, `HotCargoLoader/`, `install.bat`, `uninstall.bat` and `INSTALL.md`. The installer finds the game through Steam and the Mods folder through Saved Games.

Dev prereleases need no action: CI publishes one for each push to `main` (see `CONTRIBUTING.md`, Releases). They are not game tested. A tested release is the one that this skill publishes.
