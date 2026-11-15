#!/usr/bin/env bash
# Install origin/main into the player's own game (slot main) from the detached ../play worktree.
# Never touches a running game: it then prints "game running" and exits 0, so callers can try again later. An offline PC
# prints "PC unreachable" and exits 0 too.
# Takes the main-slot lock, so it does not collide with screenshot runs.
# Exit code 0 when installed, when the game runs or when the PC is offline; 1 without ../play or for an unexpected PC answer.
# Called by: vault-saved-ships.sh, and agents or the coordinator after a merge (hot-cargo-install skill).
set -euo pipefail
cd "$(dirname "$0")/.."
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$(dirname "$0")/../.." && pwd)/.state}"; mkdir -p "$STATE_DIR"
PLAY="$(cd .. && pwd)/play"
[[ -d "$PLAY" ]] || { echo "install: no ../play worktree (git worktree add --detach ../play origin/main)"; exit 1; }
LOCK=/tmp/hot_cargo_main_slot.lock
until mkdir "$LOCK" 2>/dev/null; do
  if [[ -f "$LOCK/pid" ]] && ! kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then rm -rf "$LOCK"; continue; fi
  sleep 15
done
echo $$ > "$LOCK/pid"
trap 'rm -rf "$LOCK"' EXIT
cd "$PLAY"
source scripts/pc.sh
SLOT=main
# An unreachable PC is not a running game: say which one, so callers know why nothing was installed.
state="$(pc "$PS_PATHS"'if (Get-SlotProcess) { "running" } else { "closed" }' 2>&1)" || { echo "install: PC unreachable, not installed ($state)"; exit 0; }
case "$state" in
  *closed*) ;;
  *running*) echo "install: game running, not installed"; exit 0 ;;
  *) echo "install: unexpected answer from the PC, not installed: $state"; exit 1 ;;
esac
git fetch -q origin
git checkout -q --detach origin/main
SLOT=main scripts/deploy.sh | tail -1
SLOT=main scripts/mod-toggle.sh on > /dev/null
git rev-parse HEAD > "$STATE_DIR/installed_sha"
echo "install: $(git log --oneline -1)"
