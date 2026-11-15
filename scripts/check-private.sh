#!/usr/bin/env bash
# Fail if tracked files contain personal machine data: absolute user home paths or Steam IDs.
# Use %USERPROFILE%, $HOME or a placeholder in docs and code instead.
# Runs in CI (check.yml job private-data, dev-release.yml), in release.sh and in integrate.sh. Run it before you push.
# Exit code 1 when a tracked file has such data.
set -euo pipefail
cd "$(dirname "$0")/.."
PATTERN='/Users/[A-Za-z0-9._-]+/|/home/[A-Za-z0-9._-]+/|[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9._-]+|7656119[0-9]{10}'
if git grep -n -I -E "$PATTERN" -- . ':!scripts/check-private.sh'; then
  echo "check-private: personal paths or IDs found (lines above). Replace them with a placeholder."
  exit 1
fi
echo "check-private: OK"
