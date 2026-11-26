#!/usr/bin/env bash
# Every rule check that a PR must pass, in one run, with one PR comment that says what is wrong and how to fix it:
# - the PR text (scripts/check-pr-text.sh: Decisions block, Game tests last, a test for "Fixes #N", no PLAN.md when ready);
# - the lines of the Game tests block (scripts/ci-test-set.sh);
# - spec numbers, their blocks and the references to them (scripts/check-spec-numbers.sh);
# - the co-op determinism lint (tools/determinism-lint.py);
# - the area dependency ratchet (tools/area-deps.py).
# Usage: check-rules.sh <pr number> [--no-comment]   a PR: then it updates its one rule comment, or deletes it when all pass
#        check-rules.sh --group <base sha> <head sha>  a merge queue group: the texts of its PRs, and the merged tree
# Needs gh with a token (GH_TOKEN) and GITHUB_REPOSITORY (or a gh login), and the git history of the base.
# Called by: .github/workflows/pr-text.yml (check pr-text, required on main). Exit code 1 when a rule breaks.
set -uo pipefail
cd "$(dirname "$0")/.."
export GITHUB_REPOSITORY="${GITHUB_REPOSITORY:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
MARK="<!-- hot-cargo-rule-check -->"
report="$(mktemp)"; trap 'rm -f "$report"' EXIT
failed=0
# One section per broken rule: its name, then the check's own lines, which name the problem and the fix.
rule() {
  local name="$1" out; shift
  out="$("$@" 2>&1)" && return 0
  failed=1
  printf '#### %s\n\n```\n%s\n```\n\n' "$name" "$(grep -v '^hint:\|^  - \|gh event listen' <<<"$out")" >> "$report"
}

if [[ "${1:-}" == --group ]]; then
  base="${2:?--group needs a base and a head sha}" head="${3:?--group needs a base and a head sha}"
  for pr in $(scripts/queue-group-prs.sh "$base" "$head"); do rule "PR #$pr: PR text" scripts/check-pr-text.sh "$pr"; done
  rule "Spec numbers" scripts/check-spec-numbers.sh
  rule "Co-op determinism" python3 tools/determinism-lint.py
  rule "Area dependencies" python3 tools/area-deps.py
  cat "$report"
  exit "$failed"
fi

pr="${1:?usage: check-rules.sh <pr number> [--no-comment] | --group <base> <head>}"
rule "PR text" scripts/check-pr-text.sh "$pr"
rule "Game tests block" scripts/ci-test-set.sh "$pr"
rule "Spec numbers" scripts/check-spec-numbers.sh
rule "Co-op determinism" python3 tools/determinism-lint.py
rule "Area dependencies" python3 tools/area-deps.py
cat "$report"
[[ "${2:-}" == --no-comment ]] && exit "$failed"

# The one rule comment of the PR: updated in place, deleted when every rule passes.
id="$(gh api "repos/$GITHUB_REPOSITORY/issues/$pr/comments" --paginate --jq ".[] | select(.body | startswith(\"$MARK\")) | .id" | head -1)"
if (( failed == 0 )); then
  [[ -z "$id" ]] || gh api -X DELETE "repos/$GITHUB_REPOSITORY/issues/comments/$id" > /dev/null
  echo "check-rules: PR #$pr: every rule passes"
  exit 0
fi
body="$(printf '%s\n🤖 **Rule checks failed** on %s. Fix each item below, then push or edit the PR text. This comment changes with each run and goes away when every rule passes (`scripts/check-rules.sh %s --no-comment` runs the same checks locally).\n\n%s' \
  "$MARK" "$(gh api "repos/$GITHUB_REPOSITORY/pulls/$pr" --jq '.head.sha[0:9]')" "$pr" "$(cat "$report")")"
if [[ -n "$id" ]]; then gh api -X PATCH "repos/$GITHUB_REPOSITORY/issues/comments/$id" -f body="$body" > /dev/null
else gh api "repos/$GITHUB_REPOSITORY/issues/$pr/comments" -f body="$body" > /dev/null; fi
exit 1
