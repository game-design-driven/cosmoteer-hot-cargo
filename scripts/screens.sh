#!/usr/bin/env bash
# Run a screenshot script (tests/screens/<name>.txt) in a test slot with graphics, and copy the PNGs to this Mac.
# Usage: [RUNNER=pc|mac] [SLOT=tN] screens.sh <script> <out-dir> [timeout-seconds, default 300]
# RUNNER=mac: run the script on this Mac instead, with the cosmoteer-macos host (the real game with a Metal renderer, in a
# hidden window of 2560x1440 pixels). No PC and no player hold check. The game takes one Mac sim slot (take_sim_slots in
# autotest-lib.sh), so the Mac sim cap HOT_CARGO_SIM_JOBS holds for it too. It needs the cosmoteer-macos checkout next to
# this repo: COSMOTEER_MACOS, default <this container>/../cosmoteer-macos/main, with tools/hotcargo-screens.sh.
# RUNNER=pc (default):
# The slot: SLOT, or a free test slot (the test slot locks). Never main: the player's game stays untouched.
# The game starts with a window, minimized, with the slot's own save folder (--rootsavepath, scripts/pc.sh SLOT_WINDOWED).
# While <container>/.state/player-hold exists it refuses (exit 3): a window could take focus or GPU time from the player.
# It deploys this worktree, runs the script until it quits the game, prints the log lines and copies the screenshots.
# Exit code 0 when the script passed; 1 when it failed; 2 for SLOT=main; 3 while the player hold exists.
# Called by: agents, for PR screenshots and visual checks (hot-cargo-screenshots skill).
set -euo pipefail
SCRIPT="${1:?usage: screens.sh <script> <out-dir> [timeout]}"
OUT="${2:?usage: screens.sh <script> <out-dir> [timeout]}"
TIMEOUT="${3:-300}"
case "${RUNNER:-pc}" in
  pc) ;;
  mac)
    HERE="$(cd "$(dirname "$0")/.." && pwd)"
    MACOS="${COSMOTEER_MACOS:-$HERE/../../cosmoteer-macos/main}"
    [[ -x "$MACOS/tools/hotcargo-screens.sh" ]] || {
      echo "screens: RUNNER=mac needs the cosmoteer-macos checkout with tools/hotcargo-screens.sh at $MACOS (or set COSMOTEER_MACOS)" >&2
      exit 2
    }
    source "$(dirname "$0")/autotest-lib.sh"
    echo "== screens: takes a Mac sim slot (cap $(sim_jobs)); waits while all are busy" >&2
    SIM_LOCK="$(take_sim_slots $$ 1)"
    trap 'rm -rf "$SIM_LOCK"' EXIT
    rc=0
    "$MACOS/tools/hotcargo-screens.sh" "$HERE" "$SCRIPT" "$OUT" "$TIMEOUT" || rc=$?
    exit $rc
    ;;
  *) echo "screens: RUNNER must be pc or mac, not '$RUNNER'" >&2; exit 2 ;;
esac
source "$(dirname "$0")/autotest-lib.sh"
[[ "${SLOT:-}" != main ]] || { echo "screens: never slot main (the player's game); use a test slot" >&2; exit 2; }
export SLOT_WINDOWED=1
[[ -e "$STATE_DIR/player-hold" ]] && { echo "screens: the player plays (.state/player-hold); no windowed test slot now" >&2; exit 3; }
if [[ -n "${SLOT:-}" ]]; then
  until try_lock "$(slot_lock "$SLOT")"; do clean_stale_locks "$(slot_lock "$SLOT")"; echo "screens: slot $SLOT busy; waiting" >&2; sleep 15; done
else
  SLOT="$(take_slot)"
fi
export SLOT
trap 'release_slot "$SLOT"' EXIT
echo "== screens in slot $SLOT (windowed, minimized)"

source "$LIB_DIR/pc.sh"
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }' </dev/null >/dev/null 2>&1 || true
# Deploy and start (deploy.sh autotest mode). A test slot turns the mod on in its own settings.rules.
{ echo "telename $(basename "$SCRIPT" .txt)"; cat "$SCRIPT"; } | start_slot "$SLOT"
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(slot_log "$SLOT")"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
grep -E 'AUTOTEST (FAIL|DONE|PASS)|ux: screenshot' <<<"$LOG" || true
grep -A3 ' ERROR ' <<<"$LOG" || true
mkdir -p "$OUT"
echo "$LOG" > "$OUT/$(basename "$SCRIPT" .txt).log"
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { tar.exe -cf - -C $d . }' </dev/null | tar -xf - -C "$OUT"
ls "$OUT"
log_passed <<<"$LOG"
