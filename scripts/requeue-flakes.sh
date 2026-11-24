#!/usr/bin/env bash
# Puts open PRs back into the merge queue when the queue dropped them on a known flake: the failed queue run failed only in
# game test jobs, and each failed test is in tests/flaky.txt, or a test job failed without a failed test (infrastructure).
# At most once per PR head commit (../.state/requeued.log). Each re-queue gets a one-line PR comment, so it stays visible.
# Usage: requeue-flakes.sh [--dry-run]. Prints one line per dropped PR: re-queued, or why not.
# Called by: scripts/agent-watch.sh (the coordinator's watch, hot-cargo-coordinate).
set -euo pipefail
cd "$(dirname "$0")/.."
dry="${1:-}"
R="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
log="../.state/requeued.log"; mkdir -p "$(dirname "$log")"; touch "$log"
flaky="$( (grep -vE '^[[:space:]]*(#|$)' tests/flaky.txt || true) | awk '{ print $1 }' | sort -u)"
# Open PRs (not drafts) without a queue entry whose last queue event removed them for failed checks. The removal also turns
# auto-merge off, so it is no sign.
dropped="$(gh api graphql -f query="{ repository(owner: \"${R%/*}\", name: \"${R#*/}\") { pullRequests(states: OPEN, first: 50) { nodes {
  number headRefOid isDraft mergeQueueEntry { position }
  timelineItems(last: 1, itemTypes: [REMOVED_FROM_MERGE_QUEUE_EVENT, ADDED_TO_MERGE_QUEUE_EVENT]) { nodes { __typename ... on RemovedFromMergeQueueEvent { reason } } } } } } }" \
  --jq '.data.repository.pullRequests.nodes[] | select((.isDraft | not) and (.mergeQueueEntry | not))
    | select(.timelineItems.nodes[0].__typename == "RemovedFromMergeQueueEvent" and (.timelineItems.nodes[0].reason | ascii_downcase | test("fail")))
    | "\(.number) \(.headRefOid)"')"
while read -r n sha; do
  [[ -n "$n" ]] || continue
  if grep -qx "$n $sha" "$log"; then echo "requeue: PR $n: re-queued once on this commit already; a person decides now"; continue; fi
  run="$(gh run list -R "$R" --workflow check.yml --event merge_group -L 50 --json databaseId,headBranch,conclusion \
    -q "[.[] | select(.headBranch | startswith(\"gh-readonly-queue/main/pr-$n-\")) | select(.conclusion == \"failure\")][0].databaseId // empty")"
  [[ -n "$run" ]] || { echo "requeue: PR $n: no failed queue run found"; continue; }
  jobs="$(gh run view "$run" -R "$R" --json jobs -q '.jobs[] | select(.conclusion == "failure") | .name')"
  other="$(grep -vE '^(game-tests-shard.*|game-tests|full-suite)$' <<<"$jobs" || true)"
  [[ -z "$other" ]] || { echo "requeue: PR $n: run $run failed in $(tr '\n' ' ' <<<"$other")- not a flake"; continue; }
  tests="$(gh run view "$run" -R "$R" --log-failed | sed 's/.*Z //' | grep -E '^FAIL [a-z0-9:_-]+$' | cut -d' ' -f2 | sort -u || true)"
  unknown="$(comm -23 <(echo "$tests" | grep . || true) <(echo "$flaky" | grep . || true))"
  [[ -z "$unknown" ]] || { echo "requeue: PR $n: run $run failed $(tr '\n' ' ' <<<"$unknown")- not in tests/flaky.txt"; continue; }
  why="$(if [[ -n "$tests" ]]; then echo "known flaky tests: $(tr '\n' ' ' <<<"$tests")"; else echo "a test job failed without a failed test (infrastructure)"; fi)"
  echo "requeue: PR $n: run $run, $why-> back into the queue"
  [[ "$dry" == --dry-run ]] && continue
  scripts/queue-pr.sh "$n"
  gh pr comment "$n" -R "$R" --body "🤖 Back in the merge queue after run $run: $why(scripts/requeue-flakes.sh, once per commit)." >/dev/null
  echo "$n $sha" >> "$log"
done <<<"$dropped"
