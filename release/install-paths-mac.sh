# Shared folder rules of the Mac scripts (install-mac.sh, uninstall-mac.sh, play-mac.sh). Source it after defining fail().
# Sets GAME: the GAME variable, else the folder that the app saved (~/Library/Application Support/cosmoteer-macos/game-folder.txt).
# Sets SAVE: the SAVE variable, else the newest Steam save folder (~/Library/Application Support/Cosmoteer/<Steam id>).
GAME="${GAME:-$(cat "$HOME/Library/Application Support/cosmoteer-macos/game-folder.txt" 2>/dev/null || true)}"
[[ -n "$GAME" && -d "$GAME/Bin" ]] || fail "no game folder: start Cosmoteer for macOS once and choose the game folder, or set GAME=<folder>"
SAVES="$HOME/Library/Application Support/Cosmoteer"
if [[ -z "${SAVE:-}" ]]; then
  # The game makes a save folder per Steam account, named after the Steam id (digits only). The newest one wins.
  for dir in $(ls -t "$SAVES" 2>/dev/null); do
    [[ -d "$SAVES/$dir" && "$dir" != *[!0-9]* ]] && { SAVE="$SAVES/$dir"; break; }
  done
fi
[[ -n "${SAVE:-}" && -d "$SAVE" ]] || fail "no Steam save folder in $SAVES: start the game once through Steam, close it, and run this again"
