#!/usr/bin/env bash
# Run one co-op session on the PC: one headless instance hosts a multiplayer career, the other instances join it over
# 127.0.0.1 and follow.
# Usage: [PLAYERS=3] autotest-mp.sh tests/autotest-mp/<session>.txt [timeout-seconds, default 600]
# PLAYERS (default 2): the host and PLAYERS-1 clients. Every client runs the same client script; "on 1" and "as 1" address the
# first client. The session file can hold lines for one player count only ("@3p <command>", see coop_lines in autotest-lib.sh).
# The session file is the host's script. Its "client: <command>" lines are the client's commands before the join (local
# settings). The client then runs tests/autotest-mp/client.txt: join, then follow the host.
# "{PORT}" becomes the host's UDP port (35870 + host slot number), so parallel sessions do not collide.
# It is a suite of one session (scripts/pc-suite.sh run): the PC runs it by itself. The host starts first; the clients start
# when the host log shows "AUTOTEST hosting". scripts/coop-report.sh gives the verdict from the host log and each client log.
# Exit code: as pc-suite.sh run; 2 for a bad PLAYERS value or a missing session. Called by hand.
set -euo pipefail
SESSION="${1:?usage: autotest-mp.sh <session file> [timeout]}"
PLAYERS="${PLAYERS:-2}"
case "$PLAYERS" in [2-8]) ;; *) echo "PLAYERS must be 2 to 8, not '$PLAYERS'"; exit 2 ;; esac
[[ -f "$SESSION" ]] || { echo "no session $SESSION"; exit 2; }
SESSION="$(cd "$(dirname "$SESSION")" && pwd)/$(basename "$SESSION")"
PC_SUITE_COOP_TIMEOUT="${2:-600}" PC_SUITE_PRINT_OUT=1 exec "$(dirname "$0")/pc-suite.sh" run 300 "mp$([[ "$PLAYERS" -ne 2 ]] && echo "$PLAYERS" || true):$SESSION"
