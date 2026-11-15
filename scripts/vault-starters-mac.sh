#!/usr/bin/env bash
# Build the starter presets (mod/tech/starters, spec 1017) on this Mac: the autotest command vaultdesigns builds Tech Vaults into
# the vanilla starters of the cosmoteer-macos game copy, with the real game and graphics (the cosmoteer-macos host, hidden window).
# It needs no PC, no slot and no player hold check, so an agent can run it (vault-saved-ships.sh --starters needs the player's slot).
# Usage: vault-starters-mac.sh [timeout seconds, default 900]
# It first puts the vanilla starters in mod/tech/starters, so the test career starts on a ship that loads with the current
# vault part (an old preset can hold vaults of another size). Then the new designs replace them. git shows the change.
# Needs the cosmoteer-macos checkout with tools/hotcargo-screens.sh: COSMOTEER_MACOS, default <this container>/../cosmoteer-macos/main.
# Exit code 0 when every starter got its vaults; 1 when the run failed; 2 for a missing checkout.
# Called by: agents that change the vault part or the vault placement (hot-cargo-player-ships skill).
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$HERE/mod/tech/starters"
TIMEOUT="${1:-900}"
MACOS="${COSMOTEER_MACOS:-$HERE/../../cosmoteer-macos/main}"
[[ -x "$MACOS/tools/hotcargo-screens.sh" ]] || { echo "vault-starters-mac: needs tools/hotcargo-screens.sh in $MACOS (or set COSMOTEER_MACOS)" >&2; exit 2; }
SRC="$(cd "$MACOS/../.game/Data/builtin_ships/Starter" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/designs" "$WORK/out"
cp "$SRC"/*.ship.png "$OUT/"
# The vanilla starters have no vault, so the test game starts without the Fit Tech Vaults offer.
printf 'render on\nnewgamecheck off\nnewgame run\nwait 8\nvaultdesigns %s|%s\nquit\n' "$SRC" "$WORK/designs" > "$WORK/vault-starters.txt"
LOG_DIR="$HERE/../.state/logs/$(basename "$HERE")"
mkdir -p "$LOG_DIR"
RC=0
"$MACOS/tools/hotcargo-screens.sh" "$HERE" "$WORK/vault-starters.txt" "$WORK/out" "$TIMEOUT" || RC=$?
cp "$WORK/out/vault-starters.log" "$LOG_DIR/vault-starters.log" 2>/dev/null || true
[[ $RC -eq 0 ]] || { echo "the run failed: see $LOG_DIR/vault-starters.log; mod/tech/starters holds the vanilla starters now (git restore it)"; exit 1; }
grep -E 'vaultdesigns|vaults:' "$WORK/out/vault-starters.log" | grep -v 'System\.' | tail -40 || true
# A design that fails keeps its source version (VaultDesigns.Failed), so no file may go to the presets then.
if grep -q ': UNCHANGED' "$WORK/out/vault-starters.log"; then echo "a starter got no vaults (UNCHANGED above); the presets did not change"; exit 1; fi
shopt -s nullglob
designs=("$WORK/designs"/*.ship.png)
[[ ${#designs[@]} -gt 0 ]] || { echo "no designs came out"; exit 1; }
cp "${designs[@]}" "$OUT/"
ls -l "$OUT"
