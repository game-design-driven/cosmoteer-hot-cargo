#!/usr/bin/env bash
# Adds the next free requirement number block for a spec file to spec/blocks.txt and prints it. Free: above every number
# on origin/main, every block of the registry, and every number or block that an open PR adds.
# Usage: spec-block.sh <spec file name, for example 64-escorts.md> [size, default 10]
# Called by: the coordinator for a brief, or an agent whose brief gives no block (hot-cargo-coordinate, hot-cargo-pr-flow).
set -euo pipefail
cd "$(dirname "$0")/.."
file="${1:?usage: spec-block.sh <spec file name> [size]}" size="${2:-10}"
[[ -f "spec/$file" ]] || { echo "spec-block: no file spec/$file" >&2; exit 1; }
git fetch -q origin main
used="$( { git grep -hoE '^[0-9]+\. ' origin/main -- 'spec/*.md'
  grep -hoE '^[0-9]+-[0-9]+' spec/blocks.txt | cut -d- -f2
  for pr in $(gh pr list --state open --json number -q '.[].number'); do
    gh pr diff "$pr" 2>/dev/null | grep -oE '^\+[0-9]+(\. |-[0-9]+ )' | tr -d '+. ' | sed 's/.*-//'
  done; } | tr -d '. ' | sort -n | tail -1)"
first=$(( (used / size + 1) * size ))
echo "$first-$(( first + size - 1 )) $file" | tee -a spec/blocks.txt
