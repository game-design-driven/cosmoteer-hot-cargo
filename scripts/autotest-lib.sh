#!/usr/bin/env bash
# Shared parts of autotest.sh and autotest-mp.sh. Source it; it does not run anything.
# Slots t1..tN (N = HOT_CARGO_SLOTS, default 6) are headless test instances on the PC (see pc.sh).
# Sourced by every script that takes PC or sim slots or reads test lists (grep -l autotest-lib.sh scripts/).
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"
# Shared slot settings for every worktree: <container>/.state/slots.env (for example HOT_CARGO_SLOTS=6), read before each
# slot is taken, so a change applies to all agents at once without a merge. Environment variables set before the run win.
SLOTS_ENV="$(cd "$REPO_ROOT/.." && pwd)/.state/slots.env"
load_slot_settings() { if [[ -f "$SLOTS_ENV" ]]; then set -a; source "$SLOTS_ENV"; set +a; fi; SLOTS="${HOT_CARGO_SLOTS:-6}"; }
load_slot_settings
# Full logs of every run stay here, one folder per worktree, so a later run in the same slot cannot overwrite them. They stay
# HOT_CARGO_TEST_OUTPUT_HOURS (default 24); the newest run of each worktree stays longer (prune_all_test_output).
# Logs, flaky list and pass markers live in <container>/.state (next to .bare and .game), not in /tmp, so they survive a restart.
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$REPO_ROOT/.." && pwd)/.state}"
LOG_DIR="${HOT_CARGO_LOG_DIR:-$STATE_DIR/logs/$(basename "$REPO_ROOT")}"
mkdir -p "$LOG_DIR"

# Test output age limit: HOT_CARGO_TEST_OUTPUT_HOURS (.state/slots.env), default 24. The Mac disk filled up twice with logs,
# run folders and stage copies (2026-11-15).
test_output_hours() {
  local h="${HOT_CARGO_TEST_OUTPUT_HOURS:-24}"
  [[ -n "$h" && "$h" != *[!0-9]* ]] || { echo "HOT_CARGO_TEST_OUTPUT_HOURS must be a number of hours, not '$h'" >&2; return 1; }
  echo "$h"
}
# prune_test_output <folder> [run folder pattern, default run-*]: deletes the files and folders directly in <folder> that are
# older than the limit, except the newest run: the newest folder that matches the pattern and everything at least as new as
# its build.log (the logs that this run wrote). Without such a folder it keeps the newest entry. It refuses any folder that
# is not test output (.state/logs/<worktree>, or the .runs folder of the cosmoteer-macos runners).
prune_test_output() {
  local dir="$1" pattern="${2:-run-*}" hours run ref keep=()
  hours="$(test_output_hours)" || return 1
  case "$dir" in */.state/logs/*|*/.runs) ;; *) echo "prune_test_output: $dir is not a test output folder" >&2; return 1 ;; esac
  [[ -d "$dir" ]] || return 0
  run="$(ls -td "$dir"/$pattern/ 2>/dev/null | head -1)"; run="${run%/}"
  if [[ -n "$run" && -f "$run/build.log" ]]; then ref="$run/build.log" keep=(! -path "$run")
  else
    ref="$(ls -t "$dir" | head -1)"
    [[ -n "$ref" ]] || return 0
    ref="$dir/$ref"
  fi
  find "$dir" -mindepth 1 -maxdepth 1 -mmin +$((hours * 60)) ! -newer "$ref" ! -path "$ref" "${keep[@]}" -exec rm -rf {} +
}
# Prunes all test output on this machine: each worktree folder in .state/logs (prune_test_output), loose files in .state/logs,
# and the sim save and stage folders that a stopped run left in the temp folder (tmp.* with Mods/hot_cargo or build/hot_cargo).
# Called at the start of every sim run (autotest-sim.sh).
prune_all_test_output() {
  local hours d
  hours="$(test_output_hours)" || return 1
  for d in "$STATE_DIR"/logs/*/; do prune_test_output "${d%/}"; done
  find "$STATE_DIR/logs" -mindepth 1 -maxdepth 1 -type f -mmin +$((hours * 60)) -delete
  for d in "${TMPDIR:-/tmp}"/tmp.*; do
    if [[ -d "$d/Mods/hot_cargo" || -d "$d/build/hot_cargo" ]] && [[ -n "$(find "$d" -maxdepth 0 -mmin +$((hours * 60)))" ]]; then rm -rf "$d"
    elif [[ "$d" == *.build.log && -n "$(find "$d" -maxdepth 0 -mmin +$((hours * 60)))" ]]; then rm -f "$d"; fi
  done
}

# Modification time of a file in seconds: GNU stat (Linux, CI) first, then BSD stat (macOS).
mtime() { stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null; }

# True when a lock folder's owner process is gone. A lock with no pid yet is being created, so it is stale only when it
# is older than 60 s (its owner died between mkdir and the pid write; otherwise it would block a slot forever).
lock_is_stale() {
  local pid; pid="$(cat "$1/pid" 2>/dev/null)"
  if [[ -n "$pid" ]]; then ! kill -0 "$pid" 2>/dev/null; return; fi
  [[ -d "$1" ]] && (( $(date +%s) - $(mtime "$1" || echo 0) > 60 ))
}
# Writes this shell's pid into a lock folder in one rename, so a reader never sees an empty pid file.
lock_write_pid() { echo $$ > "$1/pid.tmp" && mv -f "$1/pid.tmp" "$1/pid"; }

# Folder of the PC slot locks. Only scripts/test-slot-locks.sh changes it, so its dummy takers never touch live locks.
LOCK_DIR="${HOT_CARGO_LOCK_DIR:-/tmp}"
slot_lock() { echo "$LOCK_DIR/hot_cargo_slot_${1#t}.lock"; }

# Removes the lock folders in "$@" whose owner is gone. One cleaner at a time (a mutex folder): two cleaners that judge the
# same old lock stale could otherwise remove the new lock that a third taker made in between. A cleaner holds the mutex for
# a moment only, so a mutex older than 30 s is left by a killed cleaner and is removed.
# Limit: two takers that both find such a dead mutex can still race on its removal; it needs a killed cleaner first.
clean_stale_locks() {
  local mutex="$LOCK_DIR/hot_cargo_lock_cleanup.lock" l
  if ! mkdir "$mutex" 2>/dev/null; then
    if (( $(date +%s) - $(mtime "$mutex" || date +%s) > 30 )); then rm -rf "$mutex"; fi
    return 0
  fi
  for l in "$@"; do if [[ -d "$l" ]] && lock_is_stale "$l"; then rm -rf "$l"; fi; done
  rm -rf "$mutex"
}

# Takes a lock folder for this shell: mkdir, the owner, then the pid. It counts only when the pid landed and reads back as
# this shell's pid; otherwise another taker's cleanup removed the folder in between, and the lock is not ours.
try_lock() {
  mkdir "$1" 2>/dev/null || return 1
  { echo "$REPO_ROOT" > "$1/owner" && lock_write_pid "$1" && [[ "$(cat "$1/pid" 2>/dev/null)" == "$$" ]]; } 2>/dev/null
}

# The Mac sim cap: game processes at once on this machine, for every worktree. HOT_CARGO_SIM_JOBS (.state/slots.env), else
# from the cores and the memory: one run takes about one core and 2 GB at its peak, and two cores stay free for the agents
# and the build. Every Mac game takes a slot under it: scripts/autotest-sim.sh and scripts/screens.sh RUNNER=mac.
sim_jobs() {
  local cores mem_gb jobs
  if [[ "$(uname)" == Darwin ]]; then cores="$(sysctl -n hw.ncpu)" mem_gb=$(( $(sysctl -n hw.memsize) / 1024 / 1024 / 1024 ))
  else cores="$(nproc)" mem_gb=$(( $(awk '/^MemTotal:/ { print $2 }' /proc/meminfo) / 1024 / 1024 )); fi
  jobs="${HOT_CARGO_SIM_JOBS:-$(( cores - 2 < mem_gb / 2 ? cores - 2 : mem_gb / 2 ))}"
  (( jobs >= 1 )) || jobs=1
  echo "$jobs"
}

# Takes $2 free sim slots at once (all or none, so two co-op runs cannot deadlock), writes the owner pid $1 into each and
# prints their lock folders. A co-op session runs two game processes.
take_sim_slots() {
  local i lock got=() jobs; jobs="$(sim_jobs)"
  while :; do
    got=()
    for ((i = 1; i <= jobs && ${#got[@]} < $2; i++)); do
      lock="/tmp/hot_cargo_sim_slot_$i.lock"
      if lock_is_stale "$lock"; then rm -rf "$lock"; fi
      # A slot counts only when its pid landed: another taker's stale cleanup can remove a lock between mkdir and the write.
      if mkdir "$lock" 2>/dev/null && echo "$1" > "$lock/pid.tmp" 2>/dev/null && mv -f "$lock/pid.tmp" "$lock/pid" 2>/dev/null; then got+=("$lock"); fi
    done
    if [[ ${#got[@]} -eq $2 ]]; then echo "${got[*]}"; return; fi
    rm -rf "${got[@]}"
    sleep 2
  done
}

# While the player's own game runs (slot main), tests use at most HOT_CARGO_SLOTS_WHILE_PLAYING slots (default 4), so
# the player's game keeps its frame rate. The check is cached for 60 s in a shared file, so it costs one PC call a minute.
PLAYING_CACHE=/tmp/hot_cargo_player_playing
usable_slots() {
  local now age
  now="$(date +%s)"
  age=$(( now - $(mtime "$PLAYING_CACHE" || echo 0) ))
  if [[ $age -gt 60 ]]; then
    # A failed PC call counts as playing, so a broken link never lets tests load the player's game.
    local out
    if out="$(SLOT=main bash -c 'source "$0/pc.sh"; pc "$PS_PATHS"'"'"'if (Get-SlotProcess) { "running" } else { "closed" }'"'"'' "$LIB_DIR" 2>/dev/null)" && [[ "$out" == *closed* ]]; then echo no > "$PLAYING_CACHE"; else echo yes > "$PLAYING_CACHE"; fi
  fi
  load_slot_settings
  if [[ "$(cat "$PLAYING_CACHE" 2>/dev/null)" == yes ]]; then echo "${HOT_CARGO_SLOTS_WHILE_PLAYING:-4}"; else echo "$SLOTS"; fi
}

# Waits for one free test slot, locks it for this shell process and prints its name.
# A lock whose owner process is gone is stale and is taken over.
take_one_slot() {
  local i lock limit busy deadline="${1:-}"
  while :; do
    if [[ -n "$deadline" && $(date +%s) -ge $deadline ]]; then return 1; fi
    limit="$(usable_slots)"
    # Off by default: HOT_CARGO_INTEGRATOR_RESERVE slots stay free for the main worktree, if contention returns.
    if [[ "$(basename "$REPO_ROOT")" != main && ${HOT_CARGO_INTEGRATOR_RESERVE:-0} -gt 0 && $limit -gt ${HOT_CARGO_INTEGRATOR_RESERVE:-0} ]]; then limit=$((limit - HOT_CARGO_INTEGRATOR_RESERVE)); fi
    # Off by default: with HOT_CARGO_SLOTS_PER_AGENT > 0, an agent worktree holds at most that many slots, so one agent's big batch
    # cannot starve the others. The main worktree (the integrator) has no cap.
    if [[ "$(basename "$REPO_ROOT")" != main && ${HOT_CARGO_SLOTS_PER_AGENT:-0} -gt 0 ]]; then
      local mine=0 l
      for l in "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock; do [[ "$(cat "$l/owner" 2>/dev/null)" == "$REPO_ROOT" ]] && mine=$((mine + 1)); done
      if [[ $mine -ge ${HOT_CARGO_SLOTS_PER_AGENT:-0} ]]; then sleep 5; continue; fi
    fi
    clean_stale_locks "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock
    # Only the numbered slot locks count; the co-op pair lock is a queue, not a slot.
    busy="$(ls -d "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock 2>/dev/null | wc -l | tr -d ' ')"
    if [[ $busy -ge $limit ]]; then sleep 2; continue; fi
    # Only slot numbers up to the limit, so mkdir enforces the limit: two takers that both counted one free slot cannot both
    # take a slot. The busy count above covers higher slots that are still held after the limit went down.
    for ((i = 1; i <= limit && i <= SLOTS; i++)); do
      lock="$(slot_lock "t$i")"
      if try_lock "$lock"; then echo "t$i"; return; fi
    done
    sleep 2
  done
}

# Takes N slots and prints their names: all N or none. Two-slot takers (co-op tests) queue on one global lock and then
# hold each slot as it frees up, so single-slot tests that grab every freed slot cannot starve a co-op test. Only one taker
# at a time holds some slots while it waits for more, so they cannot deadlock.
# The queue lock and the slots it collects are held at most HOT_CARGO_PAIR_WAIT seconds (default 600) in total, counted
# from taking the queue lock. Then the taker gives everything back and lines up again, so it never blocks every co-op test.
# HOT_CARGO_PAIR_POLL and HOT_CARGO_PAIR_RETRY (seconds, default 5 and 30) only shorten scripts/test-slot-locks.sh.
take_slots() {
  local want="${1:-1}" got=() pair="$LOCK_DIR/hot_cargo_slot_pair.lock" s deadline
  if [[ "$want" -eq 1 ]]; then take_one_slot; return; fi
  while :; do
    until try_lock "$pair"; do
      clean_stale_locks "$pair"
      sleep "${HOT_CARGO_PAIR_POLL:-5}"
    done
    deadline=$(( $(date +%s) + ${HOT_CARGO_PAIR_WAIT:-600} ))
    got=()
    while [[ ${#got[@]} -lt "$want" ]] && s="$(take_one_slot "$deadline")"; do got+=("$s"); done
    if [[ ${#got[@]} -eq "$want" ]]; then rm -rf "$pair"; echo "${got[*]}"; return; fi
    for s in "${got[@]}"; do rm -rf "$(slot_lock "$s")"; done
    rm -rf "$pair"
    echo "co-op test: no $want slots within ${HOT_CARGO_PAIR_WAIT:-600} s; lining up again" >&2
    sleep "${HOT_CARGO_PAIR_RETRY:-30}"
  done
}
take_slot() { take_slots 1; }

# Stops the slot's game and frees the slot.
release_slot() {
  SLOT="$1" "$LIB_DIR/stop.sh" >/dev/null 2>&1 || true
  rm -rf "$(slot_lock "$1")"
}

# Deploys this worktree to the slot, installs the autotest script from stdin and starts the game, in one PC call
# (deploy.sh autotest mode). A test slot's running game is stopped first; the main slot fails if its game runs.
start_slot() {
  local slot="$1" script rc=0
  script="$(mktemp)"
  cat > "$script"
  AUTOTEST_SCRIPT="$script" SLOT="$slot" "$LIB_DIR/deploy.sh" </dev/null || rc=$?
  rm -f "$script"
  return $rc
}

# T3 refuses PC calls from an agent thread that has no active turn (the agent ended its turn while its tests run).
# Every later call fails the same way, so a run that sees this text stops at once (exit code 3, no retry).
LINK_REFUSED_TEXT="no longer owns an active thread run"

# Prints one line about the slot's game: "running=0|1 started=0|1 done=0|1 match=0|1".
# started: the log has a script line. done: the log has AUTOTEST DONE. match: the log has the text in $2 (optional).
# A small PC call, so polls can be frequent. When the PC call fails, it prints the error text in place of the line.
slot_status() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; $t = if (Test-Path $l) { [string](Get-Content $l -Raw) } else { "" }
$m = "'"${2:-}"'"
"running=" + [int][bool](Get-SlotProcess) + " started=" + [int]$t.Contains("AUTOTEST line") + " done=" + [int]$t.Contains("AUTOTEST DONE") + " match=" + [int]($m -ne "" -and $t.Contains($m))'
  ) 2>&1 || true
}

# Waits until the slot's game exits or its log has AUTOTEST DONE for two polls in a row (the game then had time to
# exit by itself), or until $2 seconds pass. Polls every AUTOTEST_POLL seconds (default 3).
# Stops early when the PC refuses calls (sets LINK_REFUSED=yes) or after 5 failed status calls in a row.
# Sets STARTED_AT (SECONDS when the first script line showed, empty if never) and ENDED_AT.
wait_slot_end() {
  local slot="$1" timeout="$2" from=$SECONDS status dones=0 fails=0
  STARTED_AT="" LINK_REFUSED=no
  while (( SECONDS - from < timeout )); do
    sleep "${AUTOTEST_POLL:-3}"
    status="$(slot_status "$slot")"
    if [[ "$status" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
    if [[ "$status" != *running=* ]]; then fails=$((fails + 1)); [[ $fails -ge 5 ]] && break; continue; fi
    fails=0
    if [[ -z "$STARTED_AT" && "$status" == *started=1* ]]; then STARTED_AT=$SECONDS; fi
    if [[ "$status" == *running=0* ]]; then break; fi
    if [[ "$status" == *done=1* ]]; then dones=$((dones + 1)); [[ $dones -ge 2 ]] && break; fi
  done
  ENDED_AT=$SECONDS
}

# Test slots only: stops the slot's game and prints its Hot Cargo log, then the line <<<VANILLA LOG>>> and the tail of
# its newest vanilla log, in one PC call. Fails when the PC call fails (then the game may still run).
slot_finish() {
  [[ "$1" != main ]] || { echo "slot_finish: never for the main slot" >&2; return 1; }
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$p = Get-SlotProcess; if ($p) { $p | Stop-Process -Force; $p | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }; if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }
"<<<VANILLA LOG>>>"
$cl = Get-ChildItem (Join-Path $root "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($cl) { "=== " + $cl.Name; Get-Content $cl.FullName -Tail 150 }'
  )
}

# Prints the slot's Hot Cargo log, and the line <<GAME EXITED>> when the slot's game no longer runs.
slot_log() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }; if (-not (Get-SlotProcess)) { "<<GAME EXITED>>" }'
  )
}

# The two scripts of a co-op session (any runner: PC slots or the headless sim). Args: <session file> <port> [players, default 2].
# Host: the session file without its "client:" lines. Client: those lines (settings before the join), then client.txt.
# "{PORT}" becomes the port and "{PLAYERS}" the player count. A line that starts with "@<n>p " runs only with n players.
# The timeout of a test run: the runner's timeout ($2), or the script's "# timeout: <seconds>" line ($1) when that is
# larger (long playthroughs). Every runner uses it: scripts/autotest.sh (PC), scripts/autotest-sim.sh (Mac, CI shards).
test_timeout() {
  local x; x="$(grep -m1 '^# timeout:' "$1" | cut -d: -f2 | tr -d ' ' || true)"
  case "$x" in "") echo "$2"; return ;; *[!0-9]*) echo "test_timeout: '# timeout: $x' in $1 is not a number of seconds" >&2; return 1 ;; esac
  if (( x > $2 )); then echo "$x"; else echo "$2"; fi
}
# The game speed line of a test script (the game's own clock scale; waits count game seconds, so a script means the same
# at every speed): the test's "# speed: <x>" line, else HOT_CARGO_TEST_SPEED, else HOT_CARGO_DEFAULT_TEST_SPEED. Prints
# nothing at speed 1. A value must be a step of the game's speed bar (0.125 to 8, cosmoteer.rules GameSpeeds).
# The default (spec 1970): measured in CI on 2026-11-23 (CONTRIBUTING.md, Test speed). Revisit if tests turn flaky at it.
HOT_CARGO_DEFAULT_TEST_SPEED=4
speed_line() {
  local x; x="$(sed -n 's/^# speed: *\([0-9.]*\)$/\1/p' "$1" | head -1)"; x="${x:-${HOT_CARGO_TEST_SPEED:-$HOT_CARGO_DEFAULT_TEST_SPEED}}"
  if [[ "$x" != 1 ]]; then echo "defaultspeed $x"; fi
}
# Every client runs the same client script. The first line names the run (telename <session>.host / .client); a pass needs it in the log.
coop_lines() { awk -v p="${2:-2}" '/^@[0-9]+p / { if (substr($1, 2) + 0 != p) next; sub(/^@[0-9]+p /, "") } { print }' "$1"; }
coop_host_script() { echo "telename $(basename "$1" .txt).host"; speed_line "$1"; coop_lines "$1" "${3:-2}" | grep -v '^client:' | sed "s/{PORT}/$2/g; s/{PLAYERS}/${3:-2}/g"; }
coop_client_script() {
  echo "telename $(basename "$1" .txt).client"; coop_lines "$1" "${3:-2}" | sed -n 's/^client: *//p'; sed "s/{PORT}/$2/g" "$(dirname "$1")/client.txt"
}

# Prints the names of the co-op sessions: every tests/autotest-mp/*.txt except client.txt (the generic client).
coop_sessions() { local f; for f in "$REPO_ROOT"/tests/autotest-mp/*.txt; do [[ "$(basename "$f")" == client.txt ]] || basename "$f" .txt; done; }

# True when a log (stdin) passed: it has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
log_passed() {
  local log
  log="$(cat)"
  grep -q 'AUTOTEST DONE' <<<"$log" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$log"
}

# Prints the lines of a log file that explain a failure, with a little context.
failure_lines() {
  grep -n -B2 -A4 -E 'AUTOTEST FAIL| ERROR |Exception|MP DESYNC|MPSTATE MISMATCH' "$1" | head -60 || true
  grep -q 'AUTOTEST DONE' "$1" || { echo "(no AUTOTEST DONE: the game exited early, timed out, or the PC link failed; first log lines:)"; grep -v "^$" "$1" | head -6; }
}
