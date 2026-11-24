#!/usr/bin/env bash
# Checks that the player installers and uninstallers keep the player's files (spec 1989): installs a fake release over a mod
# folder that holds telemetry, a screenshot, a log and tunables, checks that those stay and that the shipped files are new,
# then uninstalls and checks that those still stay, the shipped files and the loader are gone and the game is restored.
# Runs release/install-mac.sh and uninstall-mac.sh on macOS (they need plutil) and release/install.ps1 and uninstall.ps1
# where pwsh exists; skips the other pair.
# Usage: scripts/test-install-keep.sh. Exit code 1 when a check fails or when neither installer can run. Called by: check.yml
# (build job, pwsh on Linux) and by hand on a Mac.
set -euo pipefail
cd "$(dirname "$0")/.."
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail() { echo "test-install-keep: $*" >&2; exit 1; }
ran=0

# A fake release (hot_cargo with a template tunables.rules, the loader) and a fake game folder.
make_release() {
  mkdir -p "$1/hot_cargo/csharp" "$1/hot_cargo/events" "$1/HotCargoLoader"
  echo "Version = 9.9.9" > "$1/hot_cargo/mod.rules"
  echo new > "$1/hot_cargo/csharp/HotCargo.dll"
  echo new > "$1/hot_cargo/events/events.rules"
  echo "// template" > "$1/hot_cargo/tunables.rules"
  echo loader > "$1/HotCargoLoader/HotCargo.Loader.dll"
  mkdir -p "$2/Bin"
  echo '{"runtimeOptions":{"configProperties":{}}}' > "$2/Bin/Cosmoteer.runtimeconfig.json"
}
# A mod folder with the player's files, as a played game leaves it.
make_played() {
  mkdir -p "$1/telemetry/notes" "$1/telemetry/players/Tom" "$1/screenshots" "$1/csharp" "$1/events"
  echo session > "$1/telemetry/r1__s1.jsonl"
  echo '{"e":"note"}' > "$1/telemetry/notes.jsonl"
  echo png > "$1/telemetry/notes/r1__shot.png"
  echo client > "$1/telemetry/players/Tom/r1__s2.jsonl"
  echo png > "$1/screenshots/title.png"
  echo log > "$1/csharp/hot_cargo_log.txt"
  echo old > "$1/csharp/HotCargo.dll"
  echo old > "$1/events/events.rules"
  echo "Heat { HeatPerGameMinute = 3 }" > "$1/tunables.rules"
}
check() {
  local mod="$1" who="$2" f
  for f in telemetry/r1__s1.jsonl telemetry/notes.jsonl telemetry/notes/r1__shot.png telemetry/players/Tom/r1__s2.jsonl \
           screenshots/title.png csharp/hot_cargo_log.txt; do
    [[ -f "$mod/$f" ]] || fail "$who deleted the player's $f"
  done
  grep -q HeatPerGameMinute "$mod/tunables.rules" || fail "$who replaced the player's tunables.rules"
  [[ "$(cat "$mod/csharp/HotCargo.dll")" == new && "$(cat "$mod/events/events.rules")" == new ]] || fail "$who did not update the shipped files"
  grep -q 9.9.9 "$mod/mod.rules" || fail "$who did not copy mod.rules"
  echo "test-install-keep: $who keeps the player's files and updates the shipped ones"
}
# After an uninstall: the player's files stay, the shipped files, their empty folders and the loader are gone, and the game's
# runtimeconfig has no startup hook.
check_removed() {
  local mod="$1" game="$2" who="$3" f
  for f in telemetry/r1__s1.jsonl telemetry/notes.jsonl telemetry/notes/r1__shot.png telemetry/players/Tom/r1__s2.jsonl \
           screenshots/title.png csharp/hot_cargo_log.txt; do
    [[ -f "$mod/$f" ]] || fail "$who deleted the player's $f"
  done
  grep -q HeatPerGameMinute "$mod/tunables.rules" || fail "$who deleted the player's tunables.rules"
  for f in mod.rules csharp/HotCargo.dll events/events.rules; do [[ ! -e "$mod/$f" ]] || fail "$who left the shipped $f"; done
  [[ ! -d "$mod/events" ]] || fail "$who left the empty shipped folder events/"
  [[ ! -e "$game/Bin/HotCargoLoader" ]] || fail "$who left the loader"
  [[ ! -e "$game/Bin/Cosmoteer.runtimeconfig.json.hot_cargo_orig" ]] || fail "$who left the runtimeconfig backup"
  ! grep -q STARTUP_HOOKS "$game/Bin/Cosmoteer.runtimeconfig.json" || fail "$who left the startup hook"
  echo "test-install-keep: $who keeps the player's files and removes the shipped ones"
}

if command -v plutil > /dev/null && [[ "$(uname)" == Darwin ]]; then
  R="$T/mac-release"; G="$T/mac-game"; S="$T/mac-save/12345"
  make_release "$R" "$G"; cp release/install-mac.sh "$R/"
  make_played "$S/Mods/hot_cargo"
  printf 'GameSettings\n{\n\tEnabledMods ["other_mod"]\n}\n' > "$S/settings.rules"
  # The installer refuses while the Mac app runs; a stub pgrep lets the test run while a person plays.
  mkdir -p "$T/bin"; printf '#!/bin/sh\nexit 1\n' > "$T/bin/pgrep"; chmod +x "$T/bin/pgrep"
  PATH="$T/bin:$PATH" GAME="$G" SAVE="$S" bash "$R/install-mac.sh" > "$T/mac.log" || { cat "$T/mac.log"; fail "install-mac.sh failed"; }
  check "$S/Mods/hot_cargo" install-mac.sh
  cp release/uninstall-mac.sh "$R/"
  PATH="$T/bin:$PATH" GAME="$G" SAVE="$S" bash "$R/uninstall-mac.sh" > "$T/mac-un.log" || { cat "$T/mac-un.log"; fail "uninstall-mac.sh failed"; }
  check_removed "$S/Mods/hot_cargo" "$G" uninstall-mac.sh
  grep -q '"other_mod"' "$S/settings.rules" && ! grep -q hot_cargo "$S/settings.rules" || { cat "$S/settings.rules"; fail "uninstall-mac.sh did not take only Hot Cargo out of EnabledMods"; }
  ran=1
fi
if command -v pwsh > /dev/null; then
  R="$T/win-release"; G="$T/win-game"; M="$T/win-mods"
  make_release "$R" "$G"; cp release/install.ps1 release/install-paths.ps1 "$R/"
  make_played "$M/hot_cargo"
  pwsh -NoProfile -File "$R/install.ps1" -GameDir "$G" -ModsDir "$M" > "$T/win.log" || { cat "$T/win.log"; fail "install.ps1 failed"; }
  check "$M/hot_cargo" install.ps1
  cp release/uninstall.ps1 "$R/"
  pwsh -NoProfile -File "$R/uninstall.ps1" -GameDir "$G" -ModsDir "$M" > "$T/win-un.log" || { cat "$T/win-un.log"; fail "uninstall.ps1 failed"; }
  check_removed "$M/hot_cargo" "$G" uninstall.ps1
  ran=1
fi
[[ "$ran" == 1 ]] || fail "neither installer can run here (needs macOS with plutil, or pwsh)"
