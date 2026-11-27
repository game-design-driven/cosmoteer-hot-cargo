#!/usr/bin/env bash
# The balance check (spec 306, BALANCE.md). Runs the balance model and the page renderer on the committed logs:
# - the model fails when a zone beats neutral by more than ZONE_LEAD_LIMIT;
# - the check fails when the committed tools/balance/model.json or docs/balance.html differ from a new run.
# Runs in CI (check.yml job build) with the game refs. Locally it rewrites both files; commit them when it says so.
# Usage: scripts/check-balance.sh [game Data folder, default ../.game/Data]
set -euo pipefail
cd "$(dirname "$0")/.."
data="${1:-../.game/Data}"
logs=tools/balance/logs
python3 tools/balance/model.py "$data" "$logs/census-before.log" "$logs/hunters-before.log" "$logs/census-after.log" > /dev/null
python3 tools/balance/render.py > /dev/null
if ! git diff --quiet -- tools/balance/model.json docs/balance.html; then
  git --no-pager diff --stat -- tools/balance/model.json docs/balance.html
  git --no-pager diff -- tools/balance/model.json | head -40 || true
  echo "check-balance: tools/balance/model.json or docs/balance.html is out of date: run scripts/check-balance.sh and commit both files."
  exit 1
fi
echo "check-balance: OK"
