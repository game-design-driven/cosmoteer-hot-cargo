#!/usr/bin/env bash
# Fail when:
# - two spec files share a section number (the file name prefix, for example "42-");
# - two requirement lines in spec/*.md share a number ("1004. ");
# - two blocks of spec/blocks.txt overlap, or a block names a file that does not exist;
# - a requirement number that is new against the base (SPEC_BASE, default origin/main) lies outside the blocks of its file.
# Parallel branches often pick the same next numbers; in the merge queue this check catches a clash between two pull requests.
# Runs in CI (.github/workflows/check.yml, job spec-numbers, with SPEC_BASE the base of the PR or the queue group).
# Exit code 1 when a rule breaks.
set -euo pipefail
cd "$(dirname "$0")/.."
rc=0
dup_sections="$(ls spec | grep -E '^[0-9]+-' | cut -d- -f1 | sort | uniq -d | tr '\n' ' ')"
if [[ -n "$dup_sections" ]]; then echo "check-spec-numbers: two spec files share the section number: $dup_sections"; rc=1; fi
dup_reqs="$(cat spec/*.md | grep -oE '^[0-9]+\. ' | sort | uniq -d | tr -d '. ' | tr '\n' ' ' || true)"
if [[ -n "$dup_reqs" ]]; then
  echo "check-spec-numbers: requirement numbers used twice: $dup_reqs"
  for n in $dup_reqs; do grep -n "^$n\. " spec/*.md | cut -c1-120; done
  rc=1
fi

# Blocks: "<first>-<last> <file>", sorted by first number.
blocks="$(grep -vE '^[[:space:]]*(#|$)' spec/blocks.txt | awk '{ split($1, r, "-"); print r[1], r[2], $2 }' | sort -n)"
while read -r first last file; do
  [[ -f "spec/$file" ]] || { echo "check-spec-numbers: spec/blocks.txt: no file spec/$file"; rc=1; }
  (( first <= last )) || { echo "check-spec-numbers: spec/blocks.txt: block $first-$last is empty"; rc=1; }
done <<<"$blocks"
overlap="$(awk 'NR > 1 && $1 <= prev { print prevline " and " $1 "-" $2 " " $3 } { if ($2 > prev) prev = $2; prevline = $1 "-" $2 " " $3 }' <<<"$blocks")"
if [[ -n "$overlap" ]]; then echo "check-spec-numbers: spec/blocks.txt: blocks overlap: $overlap"; rc=1; fi

# New numbers against the base: each must lie in a block of its own file.
base="${SPEC_BASE:-origin/main}"
git rev-parse --verify -q "$base^{commit}" >/dev/null || { echo "check-spec-numbers: no base commit '$base' (fetch it, or set SPEC_BASE)"; exit 1; }
old="$(git grep -hoE '^[0-9]+\. ' "$base" -- 'spec/*.md' | tr -d '. ' | sort -u)"
outside="$(grep -oHE '^[0-9]+\. ' spec/*.md | sed 's#^spec/##; s/:/ /; s/\. *$//' \
  | awk 'FILENAME == ARGV[1] { seen[$1] = 1; next }
         FILENAME == ARGV[2] { m++; lo[m] = $1; hi[m] = $2; file[m] = $3; next }
         !($2 in seen) { ok = 0; for (i = 1; i <= m; i++) if ($1 == file[i] && $2 >= lo[i] && $2 <= hi[i]) ok = 1; if (!ok) print $2 " in " $1 }' \
    <(echo "$old") <(echo "$blocks") -)"
if [[ -n "$outside" ]]; then
  echo "check-spec-numbers: new requirement numbers outside the blocks of their file in spec/blocks.txt (scripts/spec-block.sh gives a free block):"
  echo "$outside" | sed 's/^/  /'
  rc=1
fi
(( rc == 0 )) && echo "check-spec-numbers: OK"
exit "$rc"
