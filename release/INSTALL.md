# Hot Cargo — install

You stole corporate tech. The corporation wants it back. Get it to the Buyer before the hunters catch you.

## Needs

- Windows, Cosmoteer from Steam, game version 0.30.5a.
- In co-op, every player needs the same Hot Cargo version.

## Install

1. Close Cosmoteer.
2. Unzip this folder anywhere.
3. Double-click `install.bat`.
4. Start Cosmoteer, open **Mods**, turn on **Hot Cargo**, and restart the game when it asks.
5. Start a new career. The map size "Hot Cargo run" is the default.

After a Cosmoteer update or a Steam "verify files", run `install.bat` again.

## macOS

Hot Cargo also runs in Cosmoteer for macOS, the Mac app of the Windows game.

1. Install the app and start it once through Steam (see the app's own install steps). Close it.
2. Open Terminal and run: `bash <unzipped folder>/install-mac.sh`
3. Start the game. Hot Cargo is turned on already.

To play, start Cosmoteer for macOS as usual, or run `bash <unzipped folder>/play-mac.sh`: it also deletes a test script that a test run may have left, which would start the game in test mode.

After a Cosmoteer update, run `install-mac.sh` again. In co-op, every player installs the same zip. To check the version, look at the line `Hot Cargo v<version>` at the bottom left of the title screen. From the repo, give the release tag: `release/install-mac.sh v0.6.0-dev.233`.

## What the installer changes

- It copies the mod files to `Saved Games\Cosmoteer\<your id>\Mods\hot_cargo` (on a Mac: `~/Library/Application Support/Cosmoteer/<your id>/Mods/hot_cargo`). It deletes nothing there, so a reinstall or an update keeps your files: `tunables.rules`, `telemetry` (the session files, playtest notes and their screenshots), `screenshots` and the log.
- It copies `HotCargo.Loader.dll` to `<Cosmoteer>\Bin\HotCargoLoader`.
- It adds a .NET startup hook to `<Cosmoteer>\Bin\Cosmoteer.runtimeconfig.json` and keeps the original as `Cosmoteer.runtimeconfig.json.hot_cargo_orig`.

With the mod turned off in the Mods menu, the game plays vanilla.

## Uninstall

Close Cosmoteer and double-click `uninstall.bat` (on a Mac: `bash <unzipped folder>/uninstall-mac.sh`). It restores the game and removes the mod files that the release installed. Your own files stay in the `hot_cargo` folder: `tunables.rules`, `telemetry`, `screenshots` and the log. Delete that folder to remove them too.

## Keys

- F8: show or hide the Hot Cargo panel.
- 1 to 9: pick an option in an event or a comms dialogue.
- Debug keys (single player, off by default): F9 / Shift+F9 add or remove 10 heat, F10 starts the next heat stage, F11 / Shift+F11 add or remove 1 tech.
  To turn them on, add this to `Mods\hot_cargo\tunables.rules`: `Debug { DebugKeys = true }`.

## Tuning

Open the escape menu and click **Hot Cargo settings**, or edit `Mods\hot_cargo\tunables.rules` while the game runs. `tunables.defaults.rules` next to it lists every value.
In single player, a comms window pauses the game. To turn that off, add `Ux { PauseOnComms = false }`.
