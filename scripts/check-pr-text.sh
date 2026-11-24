#!/usr/bin/env bash
# Checks the text of a pull request (CONTRIBUTING.md, Git; the PR template):
# - a "### Decisions" block with at least one line ("none" when the brief made every choice), before "### Game tests";
# - "### Game tests" is the last heading (scripts/ci-test-set.sh checks the lines of that block).
# Usage: check-pr-text.sh <pr number>. Needs gh with a token (GH_TOKEN) and GITHUB_REPOSITORY, or a gh login in the repo.
# Called by: .github/workflows/pr-text.yml (each edit and push of a PR) and scripts/ci-test-set.sh (each PR of a merge queue group).
# Exit code 1 with one line per problem.
set -euo pipefail
pr="${1:?usage: check-pr-text.sh <pr number>}"
repo="${GITHUB_REPOSITORY:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"
# The text without HTML comments (the template's hints) and without carriage returns.
body="$(gh api "repos/$repo/pulls/$pr" --jq '.body // ""' | tr -d '\r' | awk '/<!--/ { c = 1 } !c { print } /-->/ { c = 0 }')"
headings="$(grep -E '^#{1,6}[[:space:]]' <<<"$body" | sed 's/[[:space:]]*$//' || true)"
problems=()
grep -qx '### Decisions' <<<"$headings" || problems+=("no '### Decisions' block: list each choice that the brief did not make, one line with the reason, or 'none'")
grep -qx '### Game tests' <<<"$headings" || problems+=("no '### Game tests' block")
[[ "$(tail -1 <<<"$headings")" == '### Game tests' ]] || problems+=("'### Game tests' must be the last heading")
if grep -qx '### Decisions' <<<"$headings"; then
  decisions="$(awk '/^###[[:space:]]+Decisions[[:space:]]*$/ { on = 1; next } on && /^#/ { exit } on && NF' <<<"$body")"
  [[ -n "$decisions" ]] || problems+=("the '### Decisions' block is empty: write each decision with its reason, or 'none'")
  [[ "$(grep -nx '### Decisions' <<<"$headings" | cut -d: -f1)" -lt "$(grep -nx '### Game tests' <<<"$headings" | cut -d: -f1 || echo 0)" ]] \
    || problems+=("'### Decisions' must come before '### Game tests'")
fi
for p in "${problems[@]}"; do echo "check-pr-text: PR #$pr: $p"; done
(( ${#problems[@]} == 0 )) || exit 1
echo "check-pr-text: PR #$pr: OK"
