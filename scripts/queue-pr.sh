#!/usr/bin/env bash
# Put PR <n> into the merge queue of main. Turns on auto-merge. When the required checks passed and no queue entry appears
# within 30 s, it adds the entry itself (GraphQL enqueuePullRequest): `gh pr merge --auto` sometimes turns on auto-merge and
# queues nothing, also after all checks passed.
# Prints the queue state and position. Exit 0 when queued, or when the checks still run (auto-merge then queues the PR; run
# this script again after the checks if no entry appears). Exit 1 on a closed or draft PR, failed checks, or no entry.
# Called by: agents and the coordinator (skill hot-cargo-pr-flow, step 7 and a requeue after a flaky queue run).
set -euo pipefail
n="${1:?usage: scripts/queue-pr.sh <pr number>}"
fail() { echo "queue-pr: PR $n: $*" >&2; exit 1; }
entry() { gh api graphql -f query="query { node(id: \"$id\") { ... on PullRequest { mergeQueueEntry { state position } } } }" \
  -q '.data.node.mergeQueueEntry | select(.) | "\(.state) position \(.position)"'; }

read -r id state draft < <(gh pr view "$n" --json id,state,isDraft -q '"\(.id) \(.state) \(.isDraft)"')
[[ "$state" == OPEN ]] || fail "state $state"
[[ "$draft" == false ]] || fail "is a draft; run gh pr ready $n first"
if e="$(entry)" && [[ -n "$e" ]]; then echo "queue-pr: PR $n queued: $e"; exit 0; fi

gh pr merge "$n" --auto >/dev/null
# gh pr checks exits 8 while checks run; the buckets say it all.
buckets="$(gh pr checks "$n" --required --json bucket -q '[.[].bucket] | unique | join(",")' || true)"
case "$buckets" in
  # Right after a push or a new PR, GitHub lists no checks for some seconds: the same as running checks.
  "") echo "queue-pr: PR $n: auto-merge on, no required checks reported yet; it queues when they pass"; exit 0 ;;
  *fail*|*cancel*) fail "required checks failed ($buckets): gh pr checks $n" ;;
  *pending*) echo "queue-pr: PR $n: auto-merge on, required checks still run; it queues when they pass"; exit 0 ;;
esac

for _ in 1 2 3 4 5 6; do
  e="$(entry)"; [[ -n "$e" ]] && { echo "queue-pr: PR $n queued: $e"; exit 0; }
  sleep 5
done
echo "queue-pr: PR $n: checks passed but no queue entry after 30 s; adding it with enqueuePullRequest"
gh api graphql -f query="mutation { enqueuePullRequest(input: {pullRequestId: \"$id\"}) { clientMutationId } }" >/dev/null
e="$(entry)"; [[ -n "$e" ]] || fail "still no queue entry after enqueuePullRequest"
echo "queue-pr: PR $n queued: $e"
