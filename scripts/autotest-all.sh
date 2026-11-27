#!/usr/bin/env bash
# Run autotests on the PC as one suite and print a summary. The PC runs the suite by itself (scripts/pc-suite.sh), so it
# survives the end of the agent's turn and a drop of the link.
# Usage: autotest-all.sh [--detach] [--telemetry] [timeout-per-test] [test ...]
# --detach: only start the suite and print its id. Collect the results later, in any turn: scripts/pc-suite.sh wait <id>.
# --telemetry: after the tests, copy the telemetry of every slot to ../.telemetry (scripts/pull-telemetry.sh) and write a report
#   of the runs that these tests made (tools/telemetry). The report path is printed at the end.
# - No test names: every tests/autotest/*.txt and every co-op session (mp:<session>, see coop_sessions in autotest-lib.sh).
# - Test names: only those, for example "tech-start mp:coop-economy" (autotest-affected.sh passes its list this way).
#   "mp3:<session>" runs a session with 3 players (the host and 2 clients, 3 slots).
# Each test retries once; a pass on the retry counts as FLAKY, not as a failure.
# The suite builds and stages the mod once (scripts/stage.sh), so all tests run the same build. Changes made in the worktree
# after the start are not in it.
# At the end it prints the seconds per phase over all runs, so the slowest phase is visible.
# Exit code: as pc-suite.sh run (0 only if every test passed; 3 when the PC refuses the calls: the suite runs on, collect
# it with "scripts/pc-suite.sh wait" in an active turn). Full logs: <container>/.state/logs/<worktree>/.
# Called by: agents, autotest-affected.sh and autotest-mp-all.sh.
set -uo pipefail
cd "$(dirname "$0")/.."
DETACH=no TELEMETRY=()
if [[ "${1:-}" == "--detach" ]]; then DETACH=yes; shift; fi
if [[ "${1:-}" == "--telemetry" ]]; then TELEMETRY=(--telemetry); shift; fi
TIMEOUT="${1:-400}"
shift || true
source scripts/autotest-lib.sh
# A clean tree gets a pass marker when its full run passes. It is a local record only: release.sh reads the CI full-suite check.
START_SHA="$(git rev-parse HEAD)"
CLEAN_AT_START="$([[ -z "$(git status --porcelain)" && $# -eq 0 ]] && echo yes || echo no)"

TESTS=()
if [[ $# -gt 0 ]]; then TESTS=("$@"); else
  for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done
  for n in $(coop_sessions); do TESTS+=("mp:$n"); done
fi

ID="$(scripts/pc-suite.sh start "${TELEMETRY[@]+"${TELEMETRY[@]}"}" "$TIMEOUT" "${TESTS[@]}")" || exit $?
if [[ "$CLEAN_AT_START" == yes ]]; then echo "$START_SHA" > "$(dirname "$(grep -lx "$ID" "$LOG_DIR"/run-*/suite)")/pass-sha"; fi
[[ "$DETACH" == yes ]] && exit 0
exec scripts/pc-suite.sh wait "$ID"
