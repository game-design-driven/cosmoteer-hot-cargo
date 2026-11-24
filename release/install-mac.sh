#!/usr/bin/env bash
# Hot Cargo installer for Cosmoteer for macOS (the Mac app of the Windows game; see the cosmoteer-macos README).
# - Source: the hot_cargo and HotCargoLoader folders next to this script (an unzipped release). Without them (run from the
#   repo), it downloads the release zip of the given tag, for example v0.6.0-dev.233. There is no "newest": co-op players
#   need the same build, so every player installs the same named release.
# Usage: install-mac.sh            (from an unzipped release)
#        install-mac.sh <tag>      (from the repo; tags: gh release list, or the GitHub releases page)
# - Copies the hot_cargo mod files into the Mods folder of your Steam save folder. Your files there stay: tunables.rules,
#   telemetry, screenshots, logs.
# - Copies HotCargo.Loader.dll into <game>/Bin/HotCargoLoader.
# - Adds a .NET startup hook to <game>/Bin/Cosmoteer.runtimeconfig.json (backup: .hot_cargo_orig). The app runs it at start.
# - Turns the mod on in settings.rules of the save folder (EnabledMods).
# The game folder: GAME, else the folder that the app saved (~/Library/Application Support/cosmoteer-macos/game-folder.txt).
# The save folder: SAVE, else the newest Steam save folder (~/Library/Application Support/Cosmoteer/<Steam id>).
# Run it again after a game update, because a new Cosmoteer.runtimeconfig.json has no startup hook.
# Exit code 1 when the game runs, a folder is missing or the download fails.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO=game-design-driven/cosmoteer-hot-cargo
fail() { echo "install-mac: $*" >&2; exit 1; }

if pgrep -x MacHost > /dev/null; then fail "close Cosmoteer for macOS first"; fi
GAME="${GAME:-$(cat "$HOME/Library/Application Support/cosmoteer-macos/game-folder.txt" 2>/dev/null || true)}"
[[ -n "$GAME" ]] || fail "no game folder: start Cosmoteer for macOS once and choose the game folder, or set GAME=<folder>"
RC="$GAME/Bin/Cosmoteer.runtimeconfig.json"
[[ -f "$RC" ]] || fail "not a Cosmoteer folder: $GAME (no Bin/Cosmoteer.runtimeconfig.json)"
SAVES="$HOME/Library/Application Support/Cosmoteer"
if [[ -z "${SAVE:-}" ]]; then
  # The game makes a save folder per Steam account, named after the Steam id (digits only). The newest one wins.
  for dir in $(ls -t "$SAVES" 2>/dev/null); do
    [[ -d "$SAVES/$dir" && "$dir" != *[!0-9]* ]] && { SAVE="$SAVES/$dir"; break; }
  done
fi
[[ -n "${SAVE:-}" && -d "$SAVE" ]] || fail "no Steam save folder in $SAVES: start the game once through Steam, close it, and run this again"

SRC="$HERE"
TAG="${1:-}"
if [[ -d "$SRC/hot_cargo" && -d "$SRC/HotCargoLoader" ]]; then
  [[ -z "$TAG" ]] || fail "this folder holds a release already; run it without a tag"
else
  [[ -n "$TAG" ]] || fail "give the release tag that every player installs, for example: install-mac.sh v0.6.0-dev.233"
  SRC="$(mktemp -d)"; trap 'rm -rf "$SRC"' EXIT
  URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/tags/$TAG" | plutil -extract assets.0.browser_download_url raw -o - -)" \
    || fail "no release $TAG in $REPO"
  echo "install-mac: downloading $URL"
  curl -fsSL "$URL" -o "$SRC/hot-cargo.zip" || fail "download failed: $URL"
  unzip -q "$SRC/hot-cargo.zip" -d "$SRC"
  SRC="$(dirname "$(find "$SRC" -maxdepth 2 -type d -name HotCargoLoader | head -1)")"
  [[ -d "$SRC/hot_cargo" ]] || fail "the zip has no hot_cargo folder: $URL"
fi

# Mod folder (spec 1989): copy the release files over it and delete nothing, so the player's files stay: telemetry/ (sessions,
# notes.jsonl, note screenshots, players/), screenshots/, testships/ and the log. The release's tunables.rules is only a
# template: it goes in only when the player has none. Same rule as release/install.ps1.
# Limit: a file that an older release shipped and this one does not stays behind. Nothing loads such a file: mod.rules names
# every rules file, and the loader loads only csharp/HotCargo.dll. Revisit if a release ever must remove a file.
MOD="$SAVE/Mods/hot_cargo"
mkdir -p "$MOD"
for f in "$SRC/hot_cargo/"* "$SRC/hot_cargo/".[!.]*; do
  [[ -e "$f" ]] || continue
  if [[ "$(basename "$f")" == tunables.rules && -f "$MOD/tunables.rules" ]]; then continue; fi
  cp -R "$f" "$MOD/"
done

# Loader and startup hook, as release/install.ps1 does on Windows.
LOADER="$GAME/Bin/HotCargoLoader"
mkdir -p "$LOADER"
cp "$SRC/HotCargoLoader/"* "$LOADER/"
printf '%s' "$MOD/csharp/HotCargo.dll" > "$LOADER/hot_cargo_default.txt"
[[ -f "$RC.hot_cargo_orig" ]] || cp "$RC" "$RC.hot_cargo_orig"
cp "$RC.hot_cargo_orig" "$RC"
plutil -replace runtimeOptions.configProperties.STARTUP_HOOKS -string "$LOADER/HotCargo.Loader.dll" "$RC"
# A browser download is in quarantine. The app loads these files itself, but clear the flag anyway.
xattr -dr com.apple.quarantine "$MOD" "$LOADER" 2>/dev/null || true

# Turn the mod on: EnabledMods in settings.rules on one line, other mods kept, old hot_cargo entries replaced.
SETTINGS="$SAVE/settings.rules"
ENTRY="\"$MOD\""
if [[ ! -f "$SETTINGS" ]]; then
  printf 'GameSettings\n{\n\tEnabledMods [%s]\n}\n' "$ENTRY" > "$SETTINGS"
elif grep -q 'EnabledMods' "$SETTINGS"; then
  [[ -f "$SETTINGS.hot_cargo_bak" ]] || cp "$SETTINGS" "$SETTINGS.hot_cargo_bak"
  awk -v entry="$ENTRY" '
    !inlist && $1 ~ /^EnabledMods/ { inlist = 1; indent = substr($0, 1, index($0, "E") - 1); text = "" }
    inlist {
      text = text "\n" $0
      if (index($0, "]") == 0) next
      body = substr(text, index(text, "[") + 1); sub(/\][^\]]*$/, "", body)
      n = split(body, items, /[,\n]/); out = ""
      for (i = 1; i <= n; i++) { item = items[i]; gsub(/^[ \t]+|[ \t]+$/, "", item); if (item == "" || item ~ /hot_cargo"?$/) continue; out = out item ", " }
      print indent "EnabledMods [" out entry "]"; inlist = 0; next
    }
    { print }' "$SETTINGS" > "$SETTINGS.new"
  mv "$SETTINGS.new" "$SETTINGS"
else
  fail "settings.rules has no EnabledMods: $SETTINGS. Turn on Hot Cargo in the game's Mods menu."
fi

echo "install-mac: Hot Cargo $(grep -m1 '^Version' "$MOD/mod.rules" | cut -d= -f2 | tr -d ' ') is installed and turned on."
echo "  Mod:   $MOD"
echo "  Game:  $GAME"
echo "  Check: start the game, then look for \"startup hook\" in ~/Library/Logs/cosmoteer-macos/host.log"
