#!/usr/bin/env bash
# The pull requests of a merge queue group, one number per line: the merge commits "<title> (#N)" (the repo titles merges
# with the PR title) or "Merge pull request #N ..." (older) between the group's base and head. Only merge commits (two parents)
# count: a branch commit that ends in "(#N)" names an issue, not a PR of the group (queue runs of PRs 374 and 375 failed on it).
# Usage: queue-group-prs.sh <base sha> <head sha>. Needs gh and GITHUB_REPOSITORY. Exit code 1 when the group has none.
# Called by: scripts/ci-test-set.sh --group and scripts/check-rules.sh --group.
set -euo pipefail
base="${1:?usage: queue-group-prs.sh <base sha> <head sha>}" head="${2:?usage: queue-group-prs.sh <base sha> <head sha>}"
prs="$(gh api "repos/${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must be set}/compare/$base...$head" --jq '.commits[] | select(.parents | length > 1) | .commit.message | split("\n")[0]' \
  | sed -n -e 's/^Merge pull request #\([0-9]*\) .*/\1/p' -e 's/.* (#\([0-9]*\))$/\1/p' | sort -un)"
[[ -n "$prs" ]] || { echo "queue-group-prs: no '<title> (#N)' or 'Merge pull request #N' commit between $base and $head" >&2; exit 1; }
echo "$prs"
