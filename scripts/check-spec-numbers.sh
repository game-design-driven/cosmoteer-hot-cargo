#!/usr/bin/env bash
# The spec number rules (tools/spec_refs.py check): unique section and requirement numbers, every requirement inside a block
# of its own file in spec/blocks.txt, no overlapping blocks, and no reference to a requirement that does not exist.
# Each problem line says how to fix it. Runs in CI (check.yml job spec-numbers, and pr-text through scripts/check-rules.sh).
# Exit code 1 when a rule breaks.
set -euo pipefail
cd "$(dirname "$0")/.."
exec python3 tools/spec_refs.py check
