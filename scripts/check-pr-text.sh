#!/usr/bin/env bash
# Checks the text of a pull request (CONTRIBUTING.md, Git; the PR template):
# - a "### Decisions" block with at least one line ("none" when the brief made every choice), before "### Game tests";
# - "### Game tests" is the last heading (scripts/ci-test-set.sh checks the lines of that block);
# - a ready PR (not a draft) holds no PLAN.md (CLAUDE.md, Agents: the plan goes before the final commit);
# - a PR that fixes an issue ("Fixes #N", also "Closes", "Resolves") changes or adds a test, which catches the bug again.
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
grep -qx '### Decisions' <<<"$headings" || problems+=("No '### Decisions' block. Fix: add '### Decisions' before '### Game tests', with one line per choice that the brief did not make (what and why), or 'none'.")
grep -qx '### Game tests' <<<"$headings" || problems+=("No '### Game tests' block. Fix: end the PR text with '### Game tests' and its lines ('+ <test>', '- <test>', 'all' or 'none: <reason>'; PR template).")
[[ "$(tail -1 <<<"$headings")" == '### Game tests' ]] || problems+=("'### Game tests' is not the last heading. Fix: move every block that follows it (images, checkboxes, notes) above it.")
if grep -qx '### Decisions' <<<"$headings"; then
  decisions="$(awk '/^###[[:space:]]+Decisions[[:space:]]*$/ { on = 1; next } on && /^#/ { exit } on && NF' <<<"$body")"
  [[ -n "$decisions" ]] || problems+=("The '### Decisions' block is empty. Fix: write one line per decision with its reason, or 'none'.")
  [[ "$(grep -nx '### Decisions' <<<"$headings" | cut -d: -f1)" -lt "$(grep -nx '### Game tests' <<<"$headings" | cut -d: -f1 || echo 0)" ]] \
    || problems+=("'### Decisions' comes after '### Game tests'. Fix: move the Decisions block above '### Game tests'.")
fi
if grep -qiE '(^|[^[:alnum:]])(fix(es|ed)?|close[sd]?|resolve[sd]?) #[0-9]+' <<<"$body"; then
  gh api "repos/$repo/pulls/$pr/files" --paginate --jq '.[].filename' | grep -qE '^tests/|^tools/[^/]+/tests/|Tests\.cs$' \
    || problems+=("The PR fixes an issue but changes no test. Fix: add a unit test or a game test that fails without the fix (tests/, tools/<tool>/tests/, *Tests.cs).")
fi
if [[ "$(gh api "repos/$repo/pulls/$pr" --jq .draft)" == false ]] && gh api "repos/$repo/pulls/$pr/files" --paginate --jq '.[] | select(.status != "removed") | .filename' | grep -qx PLAN.md; then
  problems+=("PLAN.md is still in the PR. Fix: delete PLAN.md in a commit (git rm PLAN.md), or turn the PR back to draft while you work.")
fi
for p in "${problems[@]}"; do echo "check-pr-text: PR #$pr: $p"; done
(( ${#problems[@]} == 0 )) || exit 1
echo "check-pr-text: PR #$pr: OK"
