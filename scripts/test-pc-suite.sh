#!/usr/bin/env bash
# Tests the Mac side of the PC suite runner (scripts/pc-suite.sh, the suite locks of autotest-lib.sh) with a stand-in for the
# PC: no PC, no game, no build, own lock and log folders. The stand-in answers each PC call from files that the test writes.
# Usage: test-pc-suite.sh   exit code 0 when all checks hold.
# Checks: a start hands the slots to the suite; a refused link and a dropped link stop the wait loudly and keep the suite and
# its locks; a later wait collects each result once and gives the verdicts; the locks end with the suite.
# Called by: CI (check.yml, build), and by hand after a change to pc-suite.sh or the lock code.
set -uo pipefail
cd "$(dirname "$0")/.."
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
mkdir -p "$T/locks" "$T/state" "$T/stage/build"; echo testhash > "$T/stage/hash"; : > "$T/calls"

# The stand-in for "t3 machine exec <pc> -- <PowerShell>": it reads the command text, not the PC.
cat > "$T/t3" <<'STUB'
#!/usr/bin/env bash
ps="${*: -1}"
if [[ -e "$STUB/refuse" ]]; then echo "error: this thread no longer owns an active thread run" >&2; exit 1; fi
if [[ -e "$STUB/drop" ]]; then echo "error: relay was lost" >&2; exit 1; fi
case "$ps" in
  *SUITE_STARTED*) cat > /dev/null; echo start >> "$STUB/calls"; echo "SUITE_STARTED cached build testhash" ;;
  *'"<<<FILE "'*) tag="$(sed -n 's/.*StartsWith("\([^"]*\)\.").*/\1/p' <<<"$ps")"; echo "fetch $tag" >> "$STUB/calls"; cat "$STUB/fetch-$tag" ;;
  *'"job "'*) cat "$STUB/status" ;;
  *suite=ended*) cat "$STUB/lock-answer" ;;
  *Get-SlotProcess*) echo closed ;;
  *) echo "the stand-in does not know this call" >&2; exit 1 ;;
esac
STUB
chmod +x "$T/t3"
export STUB="$T" T3_CLI="$T/t3" PC_MACHINE=stand-in HOT_CARGO_LOCK_DIR="$T/locks" HOT_CARGO_STATE="$T/state" HOT_CARGO_LOG_DIR="$T/state/logs"
export HOT_CARGO_STAGE="$T/stage" HOT_CARGO_SLOTS=4 HOT_CARGO_SLOTS_PER_AGENT=0 HOT_CARGO_INTEGRATOR_RESERVE=0 AUTOTEST_POLL=1
source scripts/autotest-lib.sh

FAILED=0
check() { if eval "$2"; then echo "  ok   $1"; else echo "  FAIL $1"; FAILED=1; fi; }
locks() { ls -d "$T"/locks/hot_cargo_slot_[0-9]*.lock 2>/dev/null | wc -l | tr -d ' '; }

echo "start"
echo "suite=open" > "$T/lock-answer"
ID="$(scripts/pc-suite.sh start 100 core mp:coop-core 2> "$T/start.err")"
check "the start prints the suite id" '[[ -n "$ID" ]]'
check "the suite holds 3 slots (the session's 2, and 1 more that was free)" '[[ "$(locks)" == 3 ]]'
LOCK="$(ls -d "$T"/locks/hot_cargo_slot_[0-9]*.lock | head -1)"
check "a suite lock names the suite and has no process" '[[ "$(cat "$LOCK/suite")" == "$ID" && ! -e "$LOCK/pid" ]]'
check "a suite lock holds while the suite runs" '! lock_is_stale "$LOCK"'

echo "wait while the PC refuses the calls (the turn is not active)"
touch "$T/refuse"
OUT="$(scripts/pc-suite.sh wait "$ID" 5 2>&1)"; RC=$?
rm "$T/refuse"
check "exit code 3" '[[ $RC == 3 ]]'
check "the output says LINK REFUSED and how to collect the results" '[[ "$OUT" == *"LINK REFUSED"* && "$OUT" == *"pc-suite.sh wait $ID"* ]]'
check "the suite keeps its slots" '[[ "$(locks)" == 3 ]]'

echo "wait while the link is down"
touch "$T/drop"
OUT="$(scripts/pc-suite.sh wait "$ID" 2 2>&1)"; RC=$?
rm "$T/drop"
check "exit code 124, the suite runs on" '[[ $RC == 124 && "$OUT" == *"trying again"* ]]'
check "the suite keeps its slots" '[[ "$(locks)" == 3 ]]'

echo "wait while the suite runs: one result is ready"
printf 'suite=running\njob core\n' > "$T/status"
printf '<<<FILE core.1.host.log>>>\n12:00:00 INFO AUTOTEST line 1: telename core\n12:00:09 INFO AUTOTEST DONE\n<<<FILE core.1.timing>>>\nwait=0 deploy=1 start=2 script=3 teardown=0 total=6\n<<<FILE core.done>>>\nattempts=1 slots=t1\n' > "$T/fetch-core"
OUT="$(scripts/pc-suite.sh wait "$ID" 2 2>&1)"; RC=$?
check "the result is collected, the wait goes on (exit code 124)" '[[ $RC == 124 && "$OUT" == *"PASS core"* ]]'

echo "a later wait, after the suite ended"
printf 'suite=done\njob core\njob mp-coop-core\n' > "$T/status"
printf '<<<FILE mp-coop-core.1.deploy.log>>>\nstart of slot t2 failed: no memory\n<<<FILE mp-coop-core.done>>>\nattempts=1 slots=t2-t3\n' > "$T/fetch-mp-coop-core"
OUT="$(scripts/pc-suite.sh wait "$ID" 30 2>&1)"; RC=$?
check "exit code 1: the session failed" '[[ $RC == 1 && "$OUT" == *"FAIL mp:coop-core"* && "$OUT" == *"== failed: mp:coop-core"* ]]'
check "each result is fetched once" '[[ "$(grep -c "^fetch core$" "$T/calls")" == 1 && "$(grep -c "^fetch mp-coop-core$" "$T/calls")" == 1 ]]'
check "the summary has both tests" '[[ "$(sort "$T"/state/logs/run-*/summary.txt | tr "\n" ,)" == "FAIL mp:coop-core,PASS core," ]]'
check "the slots are free" '[[ "$(locks)" == 0 ]]'
OUT="$(scripts/pc-suite.sh wait "$ID" 30 2>&1)"; RC=$?
check "one more wait prints the same verdict with no PC call" '[[ $RC == 1 && "$(grep -c "^fetch" "$T/calls")" == 2 ]]'

echo "a suite that nobody collects"
ID="$(scripts/pc-suite.sh start 100 core 2> "$T/start.err")"
LOCK="$(ls -d "$T"/locks/hot_cargo_slot_[0-9]*.lock | head -1)"
check "its lock holds while it runs" '! lock_is_stale "$LOCK"'
echo "suite=ended" > "$T/lock-answer"; rm -f "$T"/locks/hot_cargo_suite_*.checked
check "its lock ends when the suite ended on the PC" 'lock_is_stale "$LOCK"'
echo "suite=open" > "$T/lock-answer"; rm -f "$T"/locks/hot_cargo_suite_*.checked; echo 1 > "$LOCK/until"
check "its lock ends at its time limit" 'lock_is_stale "$LOCK"'

if [[ $FAILED -eq 0 ]]; then echo "== pc suite test: all checks hold"; else echo "== pc suite test: FAILED"; cat "$T/start.err"; fi
exit $FAILED
