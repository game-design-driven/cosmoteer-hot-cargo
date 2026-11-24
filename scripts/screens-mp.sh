#!/usr/bin/env bash
# Co-op screenshots: the host runs in a windowed test slot (graphics, minimized), the client in a headless test slot.
# Usage: [RUNNER=pc|mac] screens-mp.sh <host script> <client script> <local output folder> [timeout-seconds]
# RUNNER=mac: two hidden Mac games through scripts/screens.sh (the Mac runner and its sim slots), the client's logs in <out>/client.
# Never slot main. While <container>/.state/player-hold exists it refuses (exit 3), like scripts/screens.sh.
# "{PORT}" in both scripts becomes the host port (35870 + host slot number, as scripts/autotest-mp.sh).
# Exit code 0 when both logs pass. Called by: agents, for co-op PR screenshots (hot-cargo-screenshots).
set -euo pipefail
HOST_SCRIPT="${1:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
CLIENT_SCRIPT="${2:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
OUT="${3:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
if [[ "${RUNNER:-pc}" == mac ]]; then
  # Both games use the Mac game data: a headless sim client cannot join a Mac host ("game data is incompatible").
  # Co-op needs the real-time clock (MAC_SIM_REALTIME). Both start together: each loads for minutes, the client retries its join
  # meanwhile, and the host waits for it. A random port, as scripts/autotest-sim-mp.sh, so parallel sessions rarely meet.
  PORT=$((40000 + RANDOM % 20000)) TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  mkdir -p "$TMP/host" "$TMP/client" "$OUT/client"
  sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT" > "$TMP/host/$(basename "$HOST_SCRIPT")"
  sed "s/{PORT}/$PORT/g" "$CLIENT_SCRIPT" > "$TMP/client/$(basename "$CLIENT_SCRIPT")"
  export MAC_SIM_REALTIME=1
  "$(dirname "$0")/screens.sh" "$TMP/host/$(basename "$HOST_SCRIPT")" "$OUT" "${4:-900}" & HOST_PID=$!
  "$(dirname "$0")/screens.sh" "$TMP/client/$(basename "$CLIENT_SCRIPT")" "$OUT/client" "${4:-900}" & CLIENT_PID=$!
  rc=0
  wait "$HOST_PID" || rc=$?
  wait "$CLIENT_PID" || rc=$?
  exit "$rc"
fi
TIMEOUT="${4:-400}"
source "$(dirname "$0")/autotest-lib.sh"
[[ -e "$STATE_DIR/player-hold" ]] && { echo "screens-mp: the player plays (.state/player-hold); no windowed test slot now" >&2; exit 3; }
read -r HOST CLIENT <<<"$(take_slots 2)"
trap 'release_slot "$HOST"; release_slot "$CLIENT"' EXIT
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST (windowed), client slot $CLIENT (headless), port $PORT"
# Only the host start gets the window (scripts/pc.sh SLOT_WINDOWED).
sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT" | SLOT_WINDOWED=1 start_slot "$HOST"
HOST_LOG="" CLIENT_LOG="" CLIENT_STARTED=no
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  HOST_LOG="$(slot_log "$HOST")"
  if [[ "$CLIENT_STARTED" == no ]]; then
    if grep -q 'AUTOTEST hosting' <<<"$HOST_LOG"; then
      sed "s/{PORT}/$PORT/g" "$CLIENT_SCRIPT" | start_slot "$CLIENT"
      CLIENT_STARTED=yes
    elif grep -q '<<GAME EXITED>>' <<<"$HOST_LOG"; then
      break
    fi
    continue
  fi
  CLIENT_LOG="$(slot_log "$CLIENT")"
  if grep -q '<<GAME EXITED>>' <<<"$HOST_LOG" && grep -q '<<GAME EXITED>>' <<<"$CLIENT_LOG"; then break; fi
done

echo "=== host ($HOST)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR|screenshot' <<<"$HOST_LOG" || true
echo "=== client ($CLIENT)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR' <<<"$CLIENT_LOG" || true
mkdir -p "$OUT"
echo "$HOST_LOG" > "$OUT/$(basename "$HOST_SCRIPT" .txt).log"
echo "$CLIENT_LOG" > "$OUT/$(basename "$CLIENT_SCRIPT" .txt).log"
(
  export SLOT="$HOST"
  source "$LIB_DIR/pc.sh"
  pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { tar.exe -cf - -C $d . }' </dev/null | tar -xf - -C "$OUT"
)
echo "== screenshots in $OUT"
log_passed <<<"$HOST_LOG" && log_passed <<<"$CLIENT_LOG"
