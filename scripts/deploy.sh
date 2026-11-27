#!/usr/bin/env bash
# Build the mod on this Mac and install it on the PC, in the slot that SLOT selects (see pc.sh).
# - Stages the build with stage.sh, or uses the stage in HOT_CARGO_STAGE (autotest-all.sh stages once per suite).
# - The PC keeps one unpacked copy of each build in %USERPROFILE%\HotCargoSlots\_builds\<hash> (the 30 newest).
#   A build that the PC has already is not uploaded again; the slot gets a copy of the cached build.
# - scripts/slot-install.ps1 (shipped in the build) does the install: in slot main it copies the build over the mod folder and
#   deletes nothing (spec 2260, the player installers' rule); a test slot's mod folder is replaced but for telemetry. It keeps
#   the player's tunables file in slot main, installs the startup hook loader and the test slot settings.
# Autotest mode (AUTOTEST_SCRIPT=<file>, used by autotest-lib.sh start_slot): the same PC call also stops the test slot's
# game first, installs the file as autotest.txt and starts the game. It never stops the main slot: it fails if that game runs.
# Before a test slot's game starts, the PC waits up to 120 s for HOT_CARGO_MIN_FREE_GB (default 3) of free memory.
# Exit code 1 when the build or the PC deploy fails; 3 when the player hold refuses an autotest start (pc.sh).
# Called by: autotest-lib.sh (start_slot), install-player.sh, keep-installed.sh, or by hand through with-slot.sh.
source "$(dirname "$0")/pc.sh"
# Autotest mode starts a game; a plain install (install-player.sh) only copies files and checks that the game is closed.
if [[ -n "${AUTOTEST_SCRIPT:-}" ]]; then refuse_if_player_hold start; fi

STAGE="${HOT_CARGO_STAGE:-}"
OWN_STAGE=""
if [[ -z "$STAGE" ]]; then
  STAGE="$(mktemp -d)"; OWN_STAGE="$STAGE"
  "$REPO_ROOT/scripts/stage.sh" "$STAGE" || { rm -rf "$STAGE"; exit 1; }
fi
HASH="$(cat "$STAGE/hash")"
[[ -n "$HASH" ]] || { echo "deploy: empty build hash in $STAGE" >&2; exit 1; }
SEND_DIR="$(mktemp -d)"
trap 'rm -rf "$SEND_DIR"; if [[ -n "$OWN_STAGE" ]]; then rm -rf "$OWN_STAGE"; fi' EXIT
# stdin of the PC call: a tar with autotest.txt (autotest mode only) and, on the second call, the build.
if [[ -n "${AUTOTEST_SCRIPT:-}" ]]; then cp "$AUTOTEST_SCRIPT" "$SEND_DIR/autotest.txt"; else touch "$SEND_DIR/none"; fi
send() { COPYFILE_DISABLE=1 tar -cf - -C "$SEND_DIR" . "$@"; }

# The PC side: unpack stdin and put a new build into the cache (PS_RECEIVE_BUILD in pc.sh), then run the build's
# slot-install.ps1. Kept short: the T3 link allows 8192 characters.
PS_BOOT='$ErrorActionPreference = "Stop"
$tmpDir = Join-Path $env:USERPROFILE ("HotCargoSlots\_builds\tmp-" + $slot + "-" + $PID); $unpackDir = $tmpDir
'"$PS_RECEIVE_BUILD"'
$autotestFile = Join-Path $tmpDir "autotest.txt"; if (-not (Test-Path $autotestFile)) { $autotestFile = $null }
$startGame = {'"$PS_START"'}
try { . ([scriptblock]::Create((Get-Content (Join-Path $cache "slot-install.ps1") -Raw))) } finally { Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
'

# First call without the build. The PC answers NEED_UPLOAD when it does not have this build yet; then upload it.
MIN_FREE_GB="${HOT_CARGO_MIN_FREE_GB:-3}"
[[ -n "$MIN_FREE_GB" && "$MIN_FREE_GB" != *[!0-9]* ]] || { echo "deploy: HOT_CARGO_MIN_FREE_GB must be a whole number of GB" >&2; exit 1; }
deploy_call() { pc "$PS_PATHS"'$hash = "'"$HASH"'"; $minFreeGB = '"$MIN_FREE_GB"';'"$PS_BOOT"; }
OUT="$(send | deploy_call)"
if [[ "$OUT" == *NEED_UPLOAD* ]]; then OUT="$(send -C "$STAGE" build | deploy_call)"; fi
[[ "$OUT" == *"deployed to"* ]] || { echo "$OUT"; echo "deploy: the PC did not confirm the deploy" >&2; exit 1; }
echo "$OUT"
