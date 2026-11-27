#!/usr/bin/env bash
# Starts Cosmoteer for macOS for play: the normal app start, as a double click does, never the test mode.
# Before the start it deletes a test script that a test run left in the mod folder (Mods/hot_cargo/autotest.txt): with that
# file the game starts in test mode, where an error ends the game.
# Usage: play-mac.sh. Exit code 1 when the game runs already or a folder is missing.
# Limit: it opens the title screen; a new run or a save is one click there. A direct start into a run needs the test mode,
# so revisit only if the game gets a start option for a save outside the test mode.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
fail() { echo "play-mac: $*" >&2; exit 1; }
if pgrep -x MacHost > /dev/null; then fail "Cosmoteer for macOS runs already"; fi
source "$HERE/install-paths-mac.sh"
LEFT="$SAVE/Mods/hot_cargo/autotest.txt"
if [[ -f "$LEFT" ]]; then rm -f "$LEFT"; echo "play-mac: deleted a test script left by a test run ($LEFT)"; fi
open -a "Cosmoteer for macOS"
echo "play-mac: started Cosmoteer for macOS"
