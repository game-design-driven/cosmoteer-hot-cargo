#!/usr/bin/env bash
# Removes Hot Cargo from Cosmoteer for macOS: restores Cosmoteer.runtimeconfig.json, removes the loader, takes Hot Cargo out
# of EnabledMods, and removes the mod files that this release ships. The player's files in the mod folder stay (spec 1989):
# tunables.rules, telemetry/, screenshots/, testships/ and the log. Same list as install-mac.sh: every file of the hot_cargo
# folder next to this script, but tunables.rules.
# Limit: a file that only an older release shipped stays behind. Nothing loads it without mod.rules. Revisit if one must go.
# The game and save folders: GAME and SAVE, else the same lookup as install-mac.sh.
# Usage: uninstall-mac.sh (from the unzipped release). Exit code 1 when the game runs or a folder is missing.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
fail() { echo "uninstall-mac: $*" >&2; exit 1; }
if pgrep -x MacHost > /dev/null; then fail "close Cosmoteer for macOS first"; fi
[[ -d "$HERE/hot_cargo" ]] || fail "no hot_cargo folder next to this script: run it from the unzipped release"
GAME="${GAME:-$(cat "$HOME/Library/Application Support/cosmoteer-macos/game-folder.txt" 2>/dev/null || true)}"
[[ -n "$GAME" && -d "$GAME/Bin" ]] || fail "no game folder: set GAME=<folder>"
SAVES="$HOME/Library/Application Support/Cosmoteer"
if [[ -z "${SAVE:-}" ]]; then
  for dir in $(ls -t "$SAVES" 2>/dev/null); do
    [[ -d "$SAVES/$dir" && "$dir" != *[!0-9]* ]] && { SAVE="$SAVES/$dir"; break; }
  done
fi
[[ -n "${SAVE:-}" && -d "$SAVE" ]] || fail "no Steam save folder in $SAVES: set SAVE=<folder>"

RC="$GAME/Bin/Cosmoteer.runtimeconfig.json"
if [[ -f "$RC.hot_cargo_orig" ]]; then cp "$RC.hot_cargo_orig" "$RC" && rm "$RC.hot_cargo_orig"; fi
rm -rf "$GAME/Bin/HotCargoLoader"

MOD="$SAVE/Mods/hot_cargo"
if [[ -d "$MOD" ]]; then
  (cd "$HERE/hot_cargo" && find . -type f ! -path ./tunables.rules) | while IFS= read -r f; do rm -f "$MOD/$f"; done
  # Folders that held only shipped files.
  find "$MOD" -mindepth 1 -type d -empty -delete
fi

# Hot Cargo out of EnabledMods; other mods stay.
SETTINGS="$SAVE/settings.rules"
if [[ -f "$SETTINGS" ]] && grep -q 'hot_cargo' "$SETTINGS"; then
  # The EnabledMods parser of install-mac.sh (one line or several), without the new entry.
  awk '
    !inlist && $1 ~ /^EnabledMods/ { inlist = 1; indent = substr($0, 1, index($0, "E") - 1); text = "" }
    inlist {
      text = text "\n" $0
      if (index($0, "]") == 0) next
      body = substr(text, index(text, "[") + 1); sub(/\][^\]]*$/, "", body)
      n = split(body, items, /[,\n]/); out = ""
      for (i = 1; i <= n; i++) { item = items[i]; gsub(/^[ \t]+|[ \t]+$/, "", item); if (item == "" || item ~ /hot_cargo"?$/) continue; out = out (out == "" ? "" : ", ") item }
      print indent "EnabledMods [" out "]"; inlist = 0; next
    }
    { print }' "$SETTINGS" > "$SETTINGS.new"
  mv "$SETTINGS.new" "$SETTINGS"
fi
echo "uninstall-mac: Hot Cargo is removed. The game is back to normal."
[[ -d "$MOD" ]] && echo "  Your files stay in $MOD (tunables, telemetry, screenshots, logs). Delete that folder to remove them too."
exit 0
