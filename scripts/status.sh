#!/usr/bin/env bash
# The whole picture in one run: main and its newest full suite, the merge queue and PRs that left it, every open PR, the
# releases, the Hot Cargo version installed on this Mac and on the PC, the player hold, the Mac sim cap and free disk space.
# Usage: status.sh   (from any worktree). It only reads; it changes nothing. A part that cannot answer prints why.
# Called by: the coordinator and agents (skill hot-cargo-status).
set -uo pipefail
cd "$(dirname "$0")/.."
R="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
now=$(date +%s)
ago() { local t; t=$(date -j -u -f '%Y-%m-%dT%H:%M:%SZ' "$1" +%s 2>/dev/null || date -u -d "$1" +%s); echo "$(( (now - t) / 60 )) min"; }

echo "== main"
git fetch -q origin main 2>/dev/null || echo "(fetch failed; showing the last known main)"
git log -1 --format='%h %s (%cr)' origin/main
# The newest main commit with a full-suite check: the scheduled run skips a commit that already has a green one.
for sha in $(git rev-list --first-parent -20 origin/main); do
  fs="$(gh api "repos/$R/commits/$sha/check-runs?check_name=full-suite" --jq '[.check_runs[] | select(.conclusion != "skipped")][0] | select(.) | "\(.status) \(.conclusion // "-") \(.completed_at // .started_at)"' 2>/dev/null)"
  [[ -n "$fs" ]] && { echo "full suite: ${sha:0:9} $fs"; break; }
done
[[ -n "${fs:-}" ]] || echo "full suite: none on the last 20 main commits"

echo "== merge queue"
gh api graphql -f query="{ repository(owner: \"${R%/*}\", name: \"${R#*/}\") { mergeQueue(branch: \"main\") { entries(first: 30) { nodes { position state pullRequest { number title } } } } } }" \
  --jq '.data.repository.mergeQueue.entries.nodes[] | "\(.position). #\(.pullRequest.number) \(.state) \(.pullRequest.title)"' | grep . || echo "(empty)"

echo "== open PRs (number, branch, age, last commit, checks, queue; a last removal from the queue)"
gh api graphql -f query="{ repository(owner: \"${R%/*}\", name: \"${R#*/}\") { pullRequests(states: OPEN, first: 50, orderBy: {field: CREATED_AT, direction: ASC}) { nodes {
  number title headRefName createdAt isDraft autoMergeRequest { enabledAt } mergeQueueEntry { position }
  commits(last: 1) { nodes { commit { committedDate statusCheckRollup { state } } } }
  timelineItems(last: 1, itemTypes: [REMOVED_FROM_MERGE_QUEUE_EVENT, ADDED_TO_MERGE_QUEUE_EVENT]) { nodes { __typename ... on RemovedFromMergeQueueEvent { createdAt reason } } } } } } }" \
  --jq '.data.repository.pullRequests.nodes[] | [.number, .headRefName, .createdAt, .commits.nodes[0].commit.committedDate,
    (.commits.nodes[0].commit.statusCheckRollup.state // "NONE"), (if .isDraft then "draft" elif .mergeQueueEntry then "queued \(.mergeQueueEntry.position)" elif .autoMergeRequest then "auto-merge" else "-" end),
    (.timelineItems.nodes[0] | if .__typename == "RemovedFromMergeQueueEvent" then "LEFT the queue \(.createdAt): \(.reason)" else "" end), .title] | map(tostring) | join("\u001f")' |
  # A separator that is no white space, so an empty column keeps its place.
  while IFS=$'\x1f' read -r n branch created pushed checks queue left title; do
    printf '#%s %s | age %s | last commit %s ago | %s | %s %s\n   %s\n' "$n" "$branch" "$(ago "$created")" "$(ago "$pushed")" "$checks" "$queue" "$left" "$title"
  done

echo "== releases"
gh release list -R "$R" -L 1 --json tagName,publishedAt -q '.[] | "newest dev release: \(.tagName) \(.publishedAt)"'
gh release list -R "$R" -L 1 --exclude-pre-releases --json tagName,publishedAt -q '.[] | "newest release: \(.tagName) \(.publishedAt)"' | grep . || echo "newest release: none"

echo "== installed"
mac="$(grep -h -m1 '^Version' "$HOME/Library/Application Support/Cosmoteer/"*/Mods/hot_cargo/mod.rules 2>/dev/null | head -1 | cut -d= -f2 | tr -d ' ')"
echo "this Mac: ${mac:-not installed}"
# The PC through the T3 link; an offline PC or a refused link prints "PC offline".
pcv="$(timeout 30 bash -c 'source scripts/pc.sh; pc "$PS_PATHS"'"'"'$f = Join-Path $mainMods "hot_cargo\mod.rules"; if (Test-Path $f) { (Select-String -Path $f -Pattern "^Version" | Select-Object -First 1).Line } else { "not installed" }'"'"'' 2>/dev/null | tr -d '\r' | grep -m1 -E 'Version|not installed')"
echo "PC: $(if [[ -n "$pcv" ]]; then echo "$pcv" | sed 's/^Version *= *//'; else echo "PC offline"; fi)"

echo "== this Mac"
scripts/player-hold.sh status 2>/dev/null || echo "player hold: unknown (no scripts/pc.local.sh)"
source scripts/autotest-lib.sh >/dev/null 2>&1 && echo "HOT_CARGO_SIM_JOBS: $(sim_jobs)" || echo "HOT_CARGO_SIM_JOBS: unknown"
df -h /System/Volumes/Data 2>/dev/null | awk 'NR == 2 { print "free disk: " $4 " of " $2 }' || df -h . | awk 'NR == 2 { print "free disk: " $4 }'
