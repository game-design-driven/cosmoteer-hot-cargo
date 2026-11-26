#!/usr/bin/env bash
# Checks that a change of the generated world selects every game test at every tier (scripts/autotest-affected.sh, WORLD), and that a
# change in one area does not. Usage: scripts/test-affected-world.sh. Exit code 1 when a check fails. Called by: check.yml (build job).
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail() { echo "test-affected-world: $*" >&2; exit 1; }

all=$(( $(ls tests/autotest/*.txt | wc -l) + $(coop_sessions | wc -w) ))
count() { # count <tier> <changed file>...: the number of tests that the script selects
  printf '%s\n' "${@:2}" > "$T/changed"
  CHANGED_FILE="$T/changed" LIST_ONLY=1 AUTOTEST_TIER="$1" scripts/autotest-affected.sh origin/main | sed -n 's/^== affected: //p' | wc -w | tr -d ' '
}
# A rules file that makes a galaxy, and a galaxy spawner in C#: found from the contents, so these stand in for any such file.
galaxy_rules=$(grep -rlE 'MapGenerator' mod --include='*.rules' | head -1)
galaxy_code=$(grep -rlE ': GalaxySpawner|InitGalaxyMap' src/HotCargo --include='*.cs' | head -1)
[[ -n "$galaxy_rules" && -n "$galaxy_code" ]] || fail "no galaxy rules file or galaxy code found to test with"
for tier in smoke full; do
  for f in "$galaxy_rules" "$galaxy_code"; do
    n=$(count "$tier" "$f")
    [[ "$n" == "$all" ]] || fail "$f at tier $tier selects $n tests, not all $all"
  done
done
# One area file: only its tests, not all.
area=$(ls src/HotCargo/Stash/*.cs | head -1)
n=$(count smoke "$area")
(( n < all )) || fail "$area at tier smoke selects all $all tests"
echo "test-affected-world: ok ($all tests for a world change)"
