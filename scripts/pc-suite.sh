#!/usr/bin/env bash
# Run game tests on the PC as a suite that the PC runs by itself (scripts/pc-suite.ps1). The Mac only starts the suite and
# collects the results, so a suite survives the end of the agent's turn and a drop of the T3 link. Every PC test goes
# through it: autotest.sh, autotest-mp.sh, autotest-all.sh and autotest-affected.sh.
# Usage:
#   pc-suite.sh start [--telemetry] <timeout> <test> [...]   builds, uploads and starts the suite. Prints the suite id.
#   pc-suite.sh wait [suite id] [timeout-seconds, default 14400]   collects results until the suite ends; prints the summary.
#   pc-suite.sh run [--telemetry] <timeout> <test> [...]     start, then wait.
#   pc-suite.sh stop [suite id]                              stops the suite's games on the PC; run wait after it for the results.
#   pc-suite.sh list                                         the suites of this worktree, newest first, with their state.
# Without a suite id, wait and stop take the newest suite of this worktree.
# A test is a name ("tech-start"), a co-op session ("mp:coop-core"; "mp3:coop-core" with 3 players, mp<N>: with N), or the
# path of a script file with the same prefixes ("mp:some/dir/session.txt"). <timeout> is the seconds for one test: a
# single-player test also reads its "# timeout: N" line, and a co-op session gets twice the seconds plus 200 (its waits count
# game seconds, and a loaded PC runs the sim slower); PC_SUITE_COOP_TIMEOUT sets the seconds of a session.
# - start takes the suite's test slots on this Mac (the slot locks of autotest-lib.sh): the slots that are free now, at least
#   as many as its largest test needs. The suite keeps them until it ends. The locks then name the suite, not a process
#   (suite_lock_ended in autotest-lib.sh), so they hold while no process of the suite runs on this Mac.
# - wait can stop and start again at any time, also from a later turn or after a restart of this Mac: the results stay on
#   the PC, and what this Mac already collected is in the run folder. A failed PC call is tried again until the timeout.
# - Results: the same files as before in <container>/.state/logs/<worktree>/ (one log per run, run-<time>/summary.txt with
#   one PASS, FLAKY or FAIL line per test, run-<time>/<test>.out with the lines that explain a failure).
# - A failed single-player test runs once more (AUTOTEST_RETRY, default 1); a pass on the retry is FLAKY and counts as a pass.
#   A co-op session runs again only when nothing ran in its host.
# Exit code of wait and run: 0 when every test passed; 1 when a test failed; 3 when the PC refuses the calls because this
# agent's turn is not active (the suite keeps running: run wait again in an active turn); 124 on timeout (same).
# Exit code of start: 1 when the build or the start failed; 2 for a bad test name; 3 as above (nothing started).
# Called by: autotest.sh, autotest-mp.sh, autotest-all.sh, autotest-affected.sh, and by hand.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
source scripts/pc.sh
set +e

usage() { sed -n '5,10p' "$0" | cut -c3- >&2; exit 2; }
SUITES_ON_PC='(Join-Path $env:USERPROFILE "HotCargoSlots\_suites")'

# A PC call for a suite. Output and errors go to stdout. Exit code 3 when the PC refuses the calls (the turn is not active).
suite_call() {
  local out rc=0
  out="$(pc "$1" 2>&1)" || rc=$?
  echo "$out" | tr -d '\r'
  if [[ "$out" == *"$LINK_REFUSED_TEXT"* ]]; then return 3; fi
  return $rc
}
refused() {
  echo "== PC SUITE LINK REFUSED: the PC refused the calls, because this agent's turn is not active."
  echo "== $1"
  exit 3
}

# The run folder of a suite id on this Mac, or of the newest suite of this worktree.
run_dir_of() {
  local id="${1:-}" d
  for d in $(ls -td "$LOG_DIR"/run-*/ 2>/dev/null); do
    [[ -f "$d/suite" ]] || continue
    if [[ -z "$id" || "$(cat "$d/suite")" == "$id" ]]; then echo "${d%/}"; return; fi
  done
  echo "pc-suite: no suite${id:+ $id} in $LOG_DIR" >&2; return 1
}

# job_of <test>: prints "<tag> <players> <name> <script file> <test as the summary names it>". The tag is the job's name on
# the PC and in file names. A test given as a path shows with its file name.
job_of() {
  local item="$1" prefix="" players=1 rest file name
  if [[ "$item" =~ ^mp([2-8]?):(.*)$ ]]; then prefix="mp:"; players="${BASH_REMATCH[1]:-2}"; rest="${BASH_REMATCH[2]}"; else rest="$item"; fi
  if [[ "$rest" == */* ]]; then file="$rest"
  elif [[ -n "$prefix" ]]; then file="tests/autotest-mp/$rest.txt"
  else file="tests/autotest/$rest.txt"; fi
  [[ -f "$file" ]] || { echo "pc-suite: no test $item ($file)" >&2; return 1; }
  name="$(basename "$file" .txt)"
  if [[ -z "$prefix" ]]; then echo "$name 1 $name $file $name"; return; fi
  [[ -f "$(dirname "$file")/client.txt" ]] || { echo "pc-suite: no $(dirname "$file")/client.txt for $item" >&2; return 1; }
  echo "mp-$name$([[ "$players" -ne 2 ]] && echo "-${players}p") $players $name $file ${item%%:*}:$name"
}

start() {
  local telemetry=no
  if [[ "${1:-}" == --telemetry ]]; then telemetry=yes; shift; fi
  local timeout="${1:-}"; shift || true
  [[ -n "$timeout" && "$timeout" != *[!0-9]* && $# -gt 0 ]] || usage
  local stamp id run_dir send
  stamp="$(date +%Y%m%d-%H%M%S)"
  id="$(basename "$REPO_ROOT" | tr -c 'A-Za-z0-9._\n-' '_')-$stamp-$$"
  run_dir="$LOG_DIR/run-$stamp-$$"
  send="$run_dir/send"
  mkdir -p "$send/jobs"

  # The jobs: co-op sessions first. Two slots free up together when a session ends, so the next session fits at once, and
  # single-player tests that take every freed slot cannot starve the sessions.
  local item tag players name file shown t need=1 total=0 jobs=()
  for item in "$@"; do
    read -r tag players name file shown <<<"$(job_of "$item")" || true
    [[ -n "${file:-}" ]] || { rm -rf "$run_dir"; exit 2; }
    [[ " ${jobs[*]:-} " != *" $tag "* ]] || { echo "pc-suite: test $item is in the list twice" >&2; rm -rf "$run_dir"; exit 2; }
    if [[ "$players" -eq 1 ]]; then
      t="$(test_timeout "$file" "$timeout")" || exit 2
      { echo "telename $name"; speed_line "$file"; cat "$file"; } > "$send/jobs/$tag.host.txt"
    else
      t="${PC_SUITE_COOP_TIMEOUT:-$((timeout * 2 + 200))}"
      coop_host_script "$file" "{PORT}" "$players" > "$send/jobs/$tag.host.txt"
      coop_client_script "$file" "{PORT}" "$players" > "$send/jobs/$tag.client.txt"
    fi
    jobs+=("$tag")
    echo "$tag $players $t $shown $name" >> "$run_dir/jobs.txt"
    (( players > need )) && need=$players
    total=$((total + players))
  done
  sort -s -k2,2nr "$run_dir/jobs.txt" -o "$run_dir/jobs.txt"

  export HOT_CARGO_STAGE="${HOT_CARGO_STAGE:-}"
  if [[ -z "$HOT_CARGO_STAGE" ]]; then
    HOT_CARGO_STAGE="$run_dir/stage"
    scripts/stage.sh "$HOT_CARGO_STAGE" > "$run_dir/build.log" 2>&1 || { cat "$run_dir/build.log"; echo "== build failed"; exit 1; }
  else
    echo "stage $HOT_CARGO_STAGE" > "$run_dir/build.log"
  fi
  local hash; hash="$(cat "$HOT_CARGO_STAGE/hash")"
  [[ -n "$hash" ]] || { echo "pc-suite: empty build hash in $HOT_CARGO_STAGE" >&2; exit 1; }

  # The slots: wait for as many as the largest job needs, then take the other slots that are free now.
  local pool=() s
  trap 'for s in "${pool[@]:-}"; do [[ -n "$s" ]] && rm -rf "$(slot_lock "$s")"; done' EXIT
  read -r -a pool <<<"$(take_slots "$need")"
  while (( ${#pool[@]} < total )) && s="$(take_one_slot "$(( $(date +%s) + 2 ))")"; do pool+=("$s"); done

  # The longest time that the suite can take: every job at its timeout, with its retries, plus 150 s for each start and end.
  local retry="${AUTOTEST_RETRY:-1}" work=0 deadline min_free="${HOT_CARGO_MIN_FREE_GB:-3}"
  while read -r tag players t _; do work=$((work + (t + 150) * (retry + 1) * players)); done < "$run_dir/jobs.txt"
  deadline=$((work / ${#pool[@]} + 600))
  {
    echo "set hash $hash"; echo "set slots $(IFS=,; echo "${pool[*]}")"; echo "set retry $retry"; echo "set deadline $deadline"
    echo "set whilePlaying ${HOT_CARGO_SLOTS_WHILE_PLAYING:-4}"; echo "set minFreeGB $min_free"
    while read -r tag players t _; do echo "job $tag $players $t"; done < "$run_dir/jobs.txt"
  } > "$send/suite.txt"
  { echo "$PS_PATHS_BODY"; echo '$startGame = {'"$PS_START"'}'; } > "$send/paths.ps1"
  cp scripts/pc-suite.ps1 "$send/"

  # One PC call: unpack the suite folder, put the build into the cache, start the runner. A second call with the same id
  # only confirms the start, so a call whose answer got lost is safe to repeat.
  local ps out rc try
  ps='$ErrorActionPreference = "Stop"; $hash = "'"$hash"'"; $suite = Join-Path '"$SUITES_ON_PC"' "'"$id"'"
if (Test-Path (Join-Path $suite "started")) { "SUITE_STARTED"; return }
$unpackDir = $suite
'"$PS_RECEIVE_BUILD"'
Get-ChildItem (Split-Path $suite) -Directory | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-2) } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Start-Process powershell -WindowStyle Hidden -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ([char]34 + (Join-Path $suite "pc-suite.ps1") + [char]34), "-Suite", ([char]34 + $suite + [char]34))
for ($i = 0; $i -lt 30 -and -not (Test-Path (Join-Path $suite "started")); $i++) { Start-Sleep -Milliseconds 500 }
if (Test-Path (Join-Path $suite "started")) { "SUITE_STARTED " + $cacheState } else { throw "the suite runner did not start" }'
  send_tar() { COPYFILE_DISABLE=1 tar -cf - -C "$send" . "$@"; }
  for try in 1 2 3; do
    rc=0
    out="$(send_tar | suite_call "$ps")" || rc=$?
    if [[ "$out" == *NEED_UPLOAD* ]]; then rc=0; out="$(send_tar -C "$HOT_CARGO_STAGE" build | suite_call "$ps")" || rc=$?; fi
    [[ "$out" == *SUITE_STARTED* || $rc -eq 3 ]] && break
    echo "== the start call failed (try $try of 3): $(tail -2 <<<"$out")" >&2
    sleep 10
  done
  if [[ "$out" != *SUITE_STARTED* ]]; then
    echo "$out" | tail -5 >&2
    [[ $rc -eq 3 ]] && refused "Nothing started. Start the suite again in an active turn." >&2
    echo "== pc-suite: the PC did not start the suite" >&2; exit 1
  fi

  # From here the suite owns its slots: the locks name the suite and end with it (suite_lock_ended in autotest-lib.sh).
  # A worktree with the scripts from before the suite locks reads only "pid" and takes a lock whose process is gone. So the
  # pid file names a holder: a process in its own session (as scripts/detach.sh) that only sleeps until the suite's time limit.
  # Revisit (drop the holder) when no worktree with the old lock code is left.
  local holder
  nohup perl -MPOSIX -e 'POSIX::setsid() or die "setsid: $!"; sleep $ARGV[0]' "$((deadline + 120))" > /dev/null 2>&1 < /dev/null &
  holder=$!
  for s in "${pool[@]}"; do
    echo "$(( $(date +%s) + deadline + 120 ))" > "$(slot_lock "$s")/until"; echo "$id" > "$(slot_lock "$s")/suite"; echo "$holder" > "$(slot_lock "$s")/pid"
  done
  trap - EXIT
  echo "$id" > "$run_dir/suite"; echo "${pool[*]}" > "$run_dir/slots"; date -u +%Y-%m-%dT%H:%M:%S > "$run_dir/started-utc"
  [[ "$telemetry" == yes ]] && touch "$run_dir/telemetry"
  rm -rf "$send" "$run_dir/stage"
  {
    echo "== suite $id runs on the PC: $(grep -c . "$run_dir/jobs.txt") tests in slots ${pool[*]} ($(grep -o 'SUITE_STARTED.*' <<<"$out" | cut -d' ' -f2-))"
    echo "== it keeps running without this Mac. Results: scripts/pc-suite.sh wait $id"
  } >&2
  echo "$id"
}

# The lines of a single-player job for <run dir>/<name>.out, and its verdict in VERDICT. Ported from the old autotest.sh.
collect_single() {
  local tag="$1" name="$2" attempts="$3" slots="$4" raw="$5" a log pass first=""
  VERDICT=FAIL
  for ((a = 1; a <= attempts; a++)); do
    log="$LOG_DIR/$name.$(date +%Y%m%d-%H%M%S).$slots$([[ $a -gt 1 ]] && echo .retry).log"
    cat "$raw/$tag.$a.deploy.log" "$raw/$tag.$a.host.log" > "$log" 2>/dev/null
    echo "== $name: slot $slots, log $log"
    # A pass needs this run's own log: its first script line is "telename <this test>".
    pass=no
    if log_passed < "$log" && grep -qF "telename $name" "$log"; then pass=yes; fi
    if [[ "$pass" == no && -f "$raw/$tag.$a.host.vanilla.log" ]]; then { echo; echo "=== vanilla game log"; cat "$raw/$tag.$a.host.vanilla.log"; } >> "$log"; fi
    echo "== timing $name $(cat "$raw/$tag.$a.timing" 2>/dev/null)" | tee -a "$log"
    if [[ "$pass" == yes && $a -eq 1 ]]; then echo "== AUTOTEST OK $name (slot $slots)"; VERDICT=PASS; return; fi
    if [[ "$pass" == yes ]]; then
      echo "== AUTOTEST FLAKY $name (failed once, passed on retry; first log $first)"
      echo "$(date +%F\ %T) $name $first" >> "$STATE_DIR/flaky.log"
      VERDICT=FLAKY; return
    fi
    if [[ $a -eq 1 ]]; then first="$log"; echo "== first run failed:"; else echo "== retry failed:"; fi
    failure_lines "$log"
  done
  echo "== AUTOTEST FAILED $name (log ${log:-none})"
}

# The same for a co-op session: the verdict comes from scripts/coop-report.sh. Ported from the old autotest-mp.sh.
collect_coop() {
  local tag="$1" name="$2" attempts="$3" slots="$4" raw="$5" a="$3" i log host client report_rc=0 roles=()
  VERDICT=FAIL
  IFS=- read -r -a roles <<<"$slots"
  host="$raw/$tag.$a.host.log"
  log="$LOG_DIR/$tag.$(date +%Y%m%d-%H%M%S).$slots.log"
  { cat "$raw/$tag".*.deploy.log 2>/dev/null; echo "=== host (${roles[0]})"; cat "$host" 2>/dev/null
    for ((i = 1; i < ${#roles[@]}; i++)); do echo "=== client (${roles[i]})"; cat "$raw/$tag.$a.client$i.log" 2>/dev/null; done; } > "$log"
  [[ $a -gt 1 ]] && echo "== nothing ran in the host at the first try; this is the second try"
  echo "== timing $tag $(cat "$raw/$tag.$a.timing" 2>/dev/null)" | tee -a "$log"
  echo "== $tag: log $log"
  # Each log must be this run's own (first script line "telename <session>.<side>").
  if ! grep -qF "telename $name.host" "$host" 2>/dev/null; then
    echo "== result: FAIL: no host log of this run (the start failed)"; report_rc=1
    grep -v '^$' "$raw/$tag.$a.deploy.log" 2>/dev/null | tail -5 | sed 's/^/   /'
  else
    # One report for each client: the host's steps, and the client's desyncs and state hashes against the host's.
    for ((i = 1; i < ${#roles[@]}; i++)); do
      client="$raw/$tag.$a.client$i.log"
      grep -qF "telename $name.client" "$client" 2>/dev/null || client=/dev/null
      [[ ${#roles[@]} -ne 2 ]] && echo "== client ${roles[i]}"
      scripts/coop-report.sh "$host" "$client" | tee -a "$log"
      [[ ${PIPESTATUS[0]} -eq 0 ]] || report_rc=1
    done
  fi
  if [[ $report_rc -eq 0 ]]; then echo "== MP AUTOTEST OK $tag (slots $slots)"; VERDICT=PASS; return; fi
  # A game crash shows only in the vanilla game log (the game catches it and exits): for each machine that did not finish,
  # its exception lines.
  for ((i = 0; i < ${#roles[@]}; i++)); do
    local side; side="$([[ $i -eq 0 ]] && echo host || echo "client$i")"
    [[ -f "$raw/$tag.$a.$side.vanilla.log" ]] || continue
    { echo; echo "=== vanilla game log, $side"; cat "$raw/$tag.$a.$side.vanilla.log"; } >> "$log"
    grep -q 'AUTOTEST DONE' "$raw/$tag.$a.$side.log" 2>/dev/null && continue
    grep -m3 'Exception' "$raw/$tag.$a.$side.vanilla.log" | cut -c1-300 | sed "s/^/   $side vanilla: /" | tee -a "$log"
  done
  echo "== MP AUTOTEST FAILED $tag (log $log)"
}

# Fetches the files of a finished job from the PC and writes its logs, its .out file and its summary line.
collect() {
  local run_dir="$1" id="$2" tag="$3" raw out rc=0 players t item name attempts slots reason
  read -r _ players t item name <<<"$(grep "^$tag " "$run_dir/jobs.txt")"
  raw="$run_dir/raw-$tag"; rm -rf "$raw"; mkdir -p "$raw"
  out="$(suite_call '$r = Join-Path (Join-Path '"$SUITES_ON_PC"' "'"$id"'") "results"
Get-ChildItem $r | Where-Object { $_.Name.StartsWith("'"$tag"'.") } | Sort-Object Name | ForEach-Object { "<<<FILE " + $_.Name + ">>>"; Get-Content $_.FullName -Raw }')" || rc=$?
  [[ $rc -eq 0 && "$out" == *"<<<FILE $tag.done>>>"* ]] || { rm -rf "$raw"; return "$([[ $rc -eq 3 ]] && echo 3 || echo 1)"; }
  awk -v dir="$raw" '/^<<<FILE .*>>>$/ { file = dir "/" substr($0, 9, length($0) - 11); next } file != "" { print > file }' <<<"$out"
  attempts="$(sed -n 's/.*attempts=\([0-9]*\).*/\1/p' "$raw/$tag.done")"; slots="$(sed -n 's/.*slots=\([^ ]*\).*/\1/p' "$raw/$tag.done")"
  reason="$(sed -n 's/.*reason=\([^ ]*\).*/\1/p' "$raw/$tag.done")"
  {
    if [[ "${attempts:-0}" -eq 0 ]]; then
      VERDICT=FAIL
      echo "== AUTOTEST FAILED $item: it did not run (${reason:-no reason}; slots $slots)"
      cat "$raw/$tag.worker.err.log" "$raw/$tag.worker.out.log" 2>/dev/null | grep -v '^$' | tail -15 | sed 's/^/   /'
    elif [[ "$players" -eq 1 ]]; then collect_single "$tag" "$name" "$attempts" "$slots" "$raw"
    else collect_coop "$tag" "$name" "$attempts" "$slots" "$raw"; fi
    echo "$VERDICT" > "$run_dir/verdict-$tag"
  } > "$run_dir/$item.out" 2>&1
  rm -rf "$raw"
  echo "$(cat "$run_dir/verdict-$tag") $item" | tee -a "$run_dir/summary.txt"
  [[ -n "${PC_SUITE_PRINT_OUT:-}" ]] && cat "$run_dir/$item.out"
  return 0
}

# Frees the suite's slot locks on this Mac. $2 = stop: also stops the games of its slots (a suite whose runner died).
release_locks() {
  local run_dir="$1" id s lock; id="$(cat "$run_dir/suite")"
  for s in $(cat "$run_dir/slots" 2>/dev/null); do
    lock="$(slot_lock "$s")"
    [[ "$(cat "$lock/suite" 2>/dev/null)" == "$id" ]] || continue
    kill "$(cat "$lock/pid" 2>/dev/null)" 2>/dev/null
    if [[ "${2:-}" == stop ]]; then release_slot "$s"; else rm -rf "$lock"; fi
  done
}

# Seconds per phase over every run of the suite (a retry is a run of its own).
phase_times() {
  local run_dir="$1" lines vals n p
  lines="$(cat "$run_dir"/*.out 2>/dev/null | grep '^== timing ' || true)"
  [[ -n "$lines" ]] || return 0
  echo "== phase seconds over $(grep -c . <<<"$lines") runs:"
  for p in wait deploy start script teardown total; do
    vals="$(tr ' ' '\n' <<<"$lines" | grep "^$p=" | cut -d= -f2 | sort -n)"
    [[ -n "$vals" ]] || continue
    n="$(grep -c . <<<"$vals")"
    printf '   %-9s sum %6d   median %4d   max %4d\n' "$p" "$(paste -sd+ - <<<"$vals" | bc)" "$(sed -n "$(((n + 1) / 2))p" <<<"$vals")" "$(tail -1 <<<"$vals")"
  done
}

wait_suite() {
  local run_dir id timeout="${2:-14400}" out rc state tag fails=0 resume
  run_dir="$(run_dir_of "${1:-}")" || exit 2
  id="$(cat "$run_dir/suite")"
  resume="The suite keeps running on the PC. Collect the results in an active turn: scripts/pc-suite.sh wait $id"
  touch "$run_dir/summary.txt"
  if [[ ! -f "$run_dir/ended" ]]; then
    while :; do
      rc=0
      out="$(suite_call '$s = Join-Path '"$SUITES_ON_PC"' "'"$id"'"; $r = Join-Path $s "started"
if (-not (Test-Path $s)) { "suite=missing" } elseif (Test-Path (Join-Path $s "done")) { "suite=" + (Get-Content (Join-Path $s "done")) }
elseif ((Test-Path $r) -and (Get-Process -Id ([int](Get-Content $r)) -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq "powershell" })) { "suite=running" } else { "suite=dead" }
Get-ChildItem (Join-Path $s "results") -Filter "*.done" -ErrorAction SilentlyContinue | ForEach-Object { "job " + $_.BaseName }')" || rc=$?
      [[ $rc -eq 3 ]] && refused "$resume"
      state="$(sed -n 's/^suite=//p' <<<"$out" | head -1)"
      if [[ -z "$state" ]]; then
        # The link dropped ("relay was lost" and the like): the suite does not need it. Try again.
        fails=$((fails + 1))
        (( fails == 1 || fails % 20 == 0 )) && echo "== no answer from the PC ($(tail -1 <<<"$out" | cut -c1-120)); the suite runs on, trying again"
      else
        fails=0
        for tag in $(sed -n 's/^job //p' <<<"$out"); do
          [[ -f "$run_dir/verdict-$tag" ]] && continue
          grep -q "^$tag " "$run_dir/jobs.txt" || continue
          collect "$run_dir" "$id" "$tag"; rc=$?
          [[ $rc -eq 3 ]] && refused "$resume"
          [[ $rc -eq 0 ]] || echo "== could not read the results of $tag from the PC; trying again"
        done
        if [[ "$state" != running && $(ls "$run_dir"/verdict-* 2>/dev/null | wc -l) -ge $(sed -n 's/^job //p' <<<"$out" | grep -c .) ]]; then break; fi
      fi
      if (( SECONDS > timeout )); then echo "== pc-suite: still running after ${timeout}s. $resume"; exit 124; fi
      sleep "${AUTOTEST_POLL:-10}"
    done
    # Jobs with no result on the PC: the suite folder is gone or its runner died.
    while read -r tag _ _ item _; do
      [[ -f "$run_dir/verdict-$tag" ]] && continue
      echo FAIL > "$run_dir/verdict-$tag"
      echo "== AUTOTEST FAILED $item: no result on the PC (suite $state)" > "$run_dir/$item.out"
      echo "FAIL $item" | tee -a "$run_dir/summary.txt"
    done < "$run_dir/jobs.txt"
    case "$state" in
      done) release_locks "$run_dir" ;;
      *) echo "== the suite ended as '$state' on the PC (stopped: pc-suite.sh stop; deadline: it ran too long; dead or missing: its runner or folder is gone)"
         release_locks "$run_dir" stop ;;
    esac
    echo "$state" > "$run_dir/ended"
    phase_times "$run_dir" | tee "$run_dir/phases.txt"
    if [[ -f "$run_dir/telemetry" ]]; then
      scripts/pull-telemetry.sh >/dev/null && (cd tools/telemetry && uv run hc-telemetry --since "$(cat "$run_dir/started-utc")") || echo "== telemetry report failed"
    fi
  fi

  local failed flaky t
  failed="$(grep '^FAIL ' "$run_dir/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
  flaky="$(grep '^FLAKY ' "$run_dir/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
  [[ -n "$flaky" ]] && echo "== flaky (passed on retry): $flaky"
  if [[ -z "$failed" ]]; then
    echo "== all $(grep -c . "$run_dir/summary.txt") autotests passed ($run_dir)"
    if [[ -f "$run_dir/pass-sha" ]]; then touch "$STATE_DIR/autotest_pass_$(cat "$run_dir/pass-sha")"; echo "== pass marker written for $(cat "$run_dir/pass-sha")"; fi
    exit 0
  fi
  # A co-op session prints its step lines and one result line with the reason (join failed, steps failed, desync).
  for t in $failed; do
    echo "--- $t"
    if [[ "$t" == mp*:* ]]; then coop_failure_lines "$run_dir/$t.out"
    else grep -A60 "first run failed\|retry failed\|did not run\|no result" "$run_dir/$t.out" | head -40; fi
  done
  echo "== failed: $failed($run_dir)"; exit 1
}

stop_suite() {
  local run_dir id out rc=0
  run_dir="$(run_dir_of "${1:-}")" || exit 2
  id="$(cat "$run_dir/suite")"
  out="$(suite_call '$s = Join-Path '"$SUITES_ON_PC"' "'"$id"'"; if (Test-Path $s) { Set-Content (Join-Path $s "stop") "stop"; "stop asked" } else { "suite=missing" }')" || rc=$?
  [[ $rc -eq 3 ]] && refused "The suite still runs. Stop it in an active turn: scripts/pc-suite.sh stop $id"
  echo "$out"
  echo "== the suite stops its games within some seconds. Results of the tests that ran: scripts/pc-suite.sh wait $id"
}

list_suites() {
  local d
  for d in $(ls -td "$LOG_DIR"/run-*/ 2>/dev/null); do
    [[ -f "$d/suite" ]] || continue
    echo "$(cat "$d/suite")  $(cat "$d/ended" 2>/dev/null || echo "not collected")  $(grep -c . "$d/summary.txt" 2>/dev/null || echo 0) of $(grep -c . "$d/jobs.txt") results  ${d%/}"
  done
}

case "${1:-}" in
  start) shift; start "$@" ;;
  wait) shift; wait_suite "$@" ;;
  run) shift; id="$(start "$@")" || exit $?; wait_suite "$id" ;;
  stop) shift; stop_suite "$@" ;;
  list) list_suites ;;
  *) usage ;;
esac
