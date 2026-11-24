# Removes Hot Cargo: restores Cosmoteer.runtimeconfig.json, removes the loader and the mod files that this release ships.
# The player's files in the mod folder stay (spec 1989): tunables.rules, telemetry\, screenshots\, testships\ and the log.
# The list of mod files is the install's list: every file of the hot_cargo folder next to this script, but tunables.rules.
# Limit: a file that only an older release shipped stays behind. Nothing loads it without mod.rules. Revisit if one must go.
param([string]$GameDir = "", [string]$ModsDir = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here "install-paths.ps1")
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { throw "Close Cosmoteer first." }
$bin = Join-Path $GameDir "Bin"
$rc = Join-Path $bin "Cosmoteer.runtimeconfig.json"
if (Test-Path "$rc.hot_cargo_orig") { Copy-Item "$rc.hot_cargo_orig" $rc -Force; Remove-Item "$rc.hot_cargo_orig" }
if (Test-Path (Join-Path $bin "HotCargoLoader")) { Remove-Item (Join-Path $bin "HotCargoLoader") -Recurse -Force }
$shipped = Join-Path $here "hot_cargo"
if (-not (Test-Path $shipped)) { throw "No hot_cargo folder next to this script: run the uninstaller from the unzipped release." }
# The full path, so that it is the same prefix as the FullName of each file.
$shipped = (Resolve-Path $shipped).Path
$modDir = Join-Path $ModsDir "hot_cargo"
if (Test-Path $modDir) {
    foreach ($file in Get-ChildItem $shipped -Recurse -File -Force) {
        $rel = $file.FullName.Substring($shipped.Length + 1)
        if ($rel -eq "tunables.rules") { continue }
        $target = Join-Path $modDir $rel
        if (Test-Path $target) { Remove-Item $target -Force }
    }
    # Folders that held only shipped files, deepest first.
    Get-ChildItem $modDir -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Force) } | Remove-Item -Force
}
Write-Host "Hot Cargo is removed. Cosmoteer is back to normal."
if (Test-Path $modDir) { Write-Host "Your files stay in $modDir (tunables, telemetry, screenshots, logs). Delete that folder to remove them too." }
