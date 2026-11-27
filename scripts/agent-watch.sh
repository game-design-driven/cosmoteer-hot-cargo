#!/usr/bin/env bash
# Coordinator's agent watch: one compact line per agent worktree, plus the slot and main-slot locks.
# Columns: minutes since the last commit, ahead/pushed, uncommitted files, running Mac tests, held PC test slots, and how many
# of the 5 newest logs show a refused PC link (the agent's turn was not active). Running tests: PC suites that hold slots
# (the suite locks of the worktree; they run on the PC with no process here) and headless sim games (one per single-player test, two per co-op session).
# Usage: agent-watch.sh   (from any worktree; it reads the container folder that holds .bare)
# Exit code 0. It only reads state, except one action: PRs that the merge queue dropped on a known flake go back into the
# queue (scripts/requeue-flakes.sh, once per commit, with a PR comment).
# Called by: the coordinator, by hand or from a scheduled check (hot-cargo-coordinate skill).
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$(dirname "$0")/../.."
now=$(date +%s)
echo "== $(date +%H:%M)  slots held: $(ls -d /tmp/hot_cargo_slot_[0-9]*.lock 2>/dev/null | wc -l | tr -d ' ')  main-slot lock: $([ -d /tmp/hot_cargo_main_slot.lock ] && basename "$(lsof -a -d cwd -p "$(cat /tmp/hot_cargo_main_slot.lock/pid 2>/dev/null)" -Fn 2>/dev/null | grep '^n' | cut -c2-)" || echo free)"
# The worktree of a process: its working directory. Only process names and directories are read, never command lines.
worktree_of() { lsof -a -d cwd -p "$1" -Fn 2>/dev/null | grep '^n' | cut -c2- | xargs basename 2>/dev/null; }
declare -A tests=()
# A sim game runs as "timeout <s> tools/headless-sim/run.sh autotest" from the worktree root (scripts/autotest-sim.sh,
# scripts/autotest-sim-mp.sh). The game process itself runs in ../.game, so the timeout process stands for it.
sims=()
for p in $(pgrep -f 'headless-sim/run.sh autotest' 2>/dev/null); do
  [[ "$(basename "$(ps -o comm= -p "$p" 2>/dev/null)")" == timeout ]] && sims+=("$p")
done
for p in "${sims[@]}"; do
  w=$(worktree_of "$p")
  [[ -n "$w" ]] && tests[$w]=$(( ${tests[$w]:-0} + 1 ))
done
declare -A held=()
for l in /tmp/hot_cargo_slot_[0-9]*.lock; do
  [[ -d "$l" ]] || continue
  w=$(basename "$(cat "$l/owner" 2>/dev/null)" 2>/dev/null); [[ -n "$w" ]] && held[$w]=$(( ${held[$w]:-0} + 1 ))
done
for w in $(git --git-dir .bare worktree list --porcelain | awk '/^worktree /{print $2}' | xargs -n1 basename); do
  [[ "$w" == .bare || "$w" == play ]] && continue
  [[ -d "$w" ]] || continue
  last=$(git -C "$w" log -1 --format=%ct 2>/dev/null || echo "$now")
  pushed=$(git -C "$w" status -sb 2>/dev/null | head -1 | grep -o 'ahead [0-9]*' || true)
  dirty=$(git -C "$w" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
  refused=$(ls -t "$(pwd)/.state/logs/$w"/*.log 2>/dev/null | head -5 | xargs grep -l "no longer owns an active thread run\|LINK REFUSED" 2>/dev/null | wc -l | tr -d ' ')
  printf "  %-16s commit %4dm ago  %-9s dirty %-3s tests %-3s slots %-2s refused-logs %s\n" "$w" $(( (now - last) / 60 )) "${pushed:-pushed}" "$dirty" "${tests[$w]:-0}" "${held[$w]:-0}" "$refused"
done
# PRs that the queue dropped on a known flake go back in (tests/flaky.txt).
"$HERE/requeue-flakes.sh" 2>&1 | grep -v '^hint\|^  - ' || true
