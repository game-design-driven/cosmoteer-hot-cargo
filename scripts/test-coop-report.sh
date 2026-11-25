#!/usr/bin/env bash
# Checks the verdicts of scripts/coop-report.sh on small fake logs: a host that stops in a step (a crash), and a session that passes.
# Usage: scripts/test-coop-report.sh. Exit code 1 when a check fails. Called by: check.yml (build job) and by hand.
set -euo pipefail
cd "$(dirname "$0")/.."
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail() { echo "test-coop-report: $*" >&2; exit 1; }
has() { grep -qF -- "$2" <<<"$1" || fail "$3: no line '$2' in:"$'\n'"$1"; }

cat > "$T/host-start" <<'EOF'
10:00:00.000 INFO AUTOTEST MP game 1: start
10:00:00.100 INFO AUTOTEST STEP START s1 tick=10
10:00:01.000 INFO AUTOTEST STEP RESULT s1 PASS
10:00:01.000 INFO AUTOTEST STEP START s2 tick=40
10:00:01.100 INFO AUTOTEST line 7: waitfor x == 1 60
EOF
cat > "$T/client-start" <<'EOF'
10:00:00.000 INFO AUTOTEST MP game 1: start
10:00:01.100 INFO AUTOTEST line 0: follow
EOF

# A crash in step s2: neither machine logs AUTOTEST DONE.
{ cat "$T/host-start"; echo "10:00:02.000 INFO System.ArgumentOutOfRangeException: quantity"; } > "$T/host"
out="$(scripts/coop-report.sh "$T/host" "$T/client-start")" && fail "crash: the report passed"
has "$out" "PASS s1" crash
has "$out" "STOPPED s2: the host stopped in this step" crash
has "$out" "== result: FAIL: host did not finish; client did not finish" crash
has "$out" "   host: did not finish; last script line: line 7: waitfor x == 1 60" crash
has "$out" "   host exception: 10:00:02.000 INFO System.ArgumentOutOfRangeException: quantity" crash
has "$out" "   client: did not finish; last script line: line 0: follow" crash

# Both machines finish.
{ cat "$T/host-start"; printf '%s\n' "10:00:03.000 INFO AUTOTEST STEP RESULT s2 PASS" "10:00:03.100 INFO AUTOTEST STEPS passed=2 failed=0" "10:00:03.200 INFO AUTOTEST DONE"; } > "$T/host"
{ cat "$T/client-start"; echo "10:00:03.300 INFO AUTOTEST DONE"; } > "$T/client"
out="$(scripts/coop-report.sh "$T/host" "$T/client")" || fail "pass: the report failed:"$'\n'"$out"
has "$out" "PASS s2" pass
grep -q "did not finish" <<<"$out" && fail "pass: a finished machine has a 'did not finish' line"
echo "test-coop-report: ok"
