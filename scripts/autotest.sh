#!/usr/bin/env bash
# Run one autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds, default 300]. A "# timeout: N" line in the script raises the timeout to N.
# It is a suite of one test (scripts/pc-suite.sh run): the PC runs it by itself, so it survives the end of the turn and a
# drop of the link. Slots, logs, the retry and the "== timing" line: see pc-suite.sh.
# Exit code: as pc-suite.sh run (0 pass or flaky, 1 fail, 3 the PC refuses the calls: run "scripts/pc-suite.sh wait" in an
# active turn). Called by: bisect-autotest.sh, or by hand.
set -euo pipefail
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
SCRIPT="$(cd "$(dirname "$SCRIPT")" && pwd)/$(basename "$SCRIPT")"
PC_SUITE_PRINT_OUT=1 exec "$(dirname "$0")/pc-suite.sh" run "${2:-300}" "$SCRIPT"
