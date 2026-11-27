# Runs a suite of game tests on the PC by itself, with no call from the Mac while it runs. scripts/pc-suite.sh uploads it
# with the suite folder (%USERPROFILE%\HotCargoSlots\_suites\<id>) and starts it once; it keeps running when the T3 link
# drops or the agent's turn ends. The Mac reads the results later (pc-suite.sh wait).
# Two modes, both this file:
# - the scheduler (-Suite <folder>): hands the suite's slots to the jobs, one worker process for each job, until every job has
#   a result, the suite's deadline passes or the file "stop" appears. Then it stops every game of its slots.
# - a worker (-Suite <folder> -Job <tag> -Slots t1,t2): runs one job: installs the build and the script in its slots, starts
#   the games, waits for the end, stops the games and copies the logs.
# The suite folder (written by pc-suite.sh start):
#   suite.txt      "set <key> <value>" lines (hash, slots, retry, deadline, whilePlaying, minFreeGB) and one
#                  "job <tag> <players> <timeout seconds>" line for each job, in the order that they start
#   jobs\<tag>.host.txt, jobs\<tag>.client.txt   the game scripts; "{PORT}" becomes 35870 + the host slot number
#   paths.ps1      PS_PATHS_BODY and PS_START of scripts/pc.sh: the variables and functions of one slot
# It writes:
#   started        the scheduler's process id
#   results\<tag>.<attempt>.<role>.log, .vanilla.log, .deploy.log, .timing   role: host, client1, client2, ...
#   results\<tag>.done   "attempts=<n> slots=<t1-t2>", written last: the job's files are complete
#   done           the end state: done, stopped or deadline
#   runner.log     what the scheduler did
# The verdict (pass, fail, co-op report) is made on the Mac from the logs. This file only decides if a job runs again.
param([Parameter(Mandatory = $true)][string]$Suite, [string]$Job = "", [string]$Slots = "")
$ErrorActionPreference = "Stop"

$settings = @{}; $jobs = @()
foreach ($line in Get-Content (Join-Path $Suite "suite.txt")) {
  $f = $line.Split(" ")
  if ($f[0] -eq "set" -and $f.Count -eq 3) { $settings[$f[1]] = $f[2] }
  elseif ($f[0] -eq "job" -and $f.Count -eq 4) { $jobs += [pscustomobject]@{ Tag = $f[1]; Players = [int]$f[2]; Timeout = [int]$f[3] } }
  elseif ($line.Trim()) { throw "bad line in suite.txt: $line" }
}
function Need([string]$key) { if (-not $settings.ContainsKey($key)) { throw "suite.txt has no '$key'" }; $settings[$key] }

$results = Join-Path $Suite "results"
$pathsText = Get-Content (Join-Path $Suite "paths.ps1") -Raw
$cache = Join-Path $env:USERPROFILE ("HotCargoSlots\_builds\" + (Need "hash"))
$installText = Get-Content (Join-Path $cache "slot-install.ps1") -Raw

function Note([string]$text) { Add-Content (Join-Path $Suite "runner.log") ((Get-Date -Format "HH:mm:ss") + " " + $text) }

# Runs $body with the variables and functions of one slot (paths.ps1: $root, $modDir, $pidFile, Get-SlotProcess, $startGame).
function In-Slot([string]$slot, [scriptblock]$body) { $windowed = $false; . ([scriptblock]::Create($pathsText)); . $body }

function Test-SlotRuns([string]$slot) { In-Slot $slot { [bool](Get-SlotProcess) } }

function Stop-Slot([string]$slot) {
  if ($slot -eq "main") { throw "Stop-Slot: never for the main slot" }
  In-Slot $slot {
    $p = @(Get-SlotProcess | Where-Object { $_ })
    if ($p.Count -gt 0) { $p | Stop-Process -Force; $p | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }
    if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
  }
}

# The slot's Hot Cargo log. The game appends to it, so the read shares the file; a failed read gives no text for this poll.
function Read-SlotLog([string]$slot) {
  In-Slot $slot {
    $path = Join-Path $modDir "csharp\hot_cargo_log.txt"
    if (-not (Test-Path $path)) { return "" }
    try {
      $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
      try { (New-Object IO.StreamReader $stream).ReadToEnd() } finally { $stream.Close() }
    } catch [IO.IOException] { "" }
  }
}

# Installs the build and the script in the slot and starts its game (the build's slot-install.ps1, as scripts/deploy.sh does).
# False when the install or the start failed; the reason is in $deployLog.
function Start-Slot([string]$slot, [string]$scriptFile, [string]$deployLog) {
  try {
    In-Slot $slot {
      $autotestFile = $scriptFile; $cacheState = "cached build " + (Need "hash"); $minFreeGB = [int](Need "minFreeGB")
      . ([scriptblock]::Create($installText))
    } *>> $deployLog
    return $true
  } catch {
    Add-Content $deployLog ("start of slot $slot failed: " + $_.Exception.Message)
    return $false
  }
}

# Copies the slot's Hot Cargo log and the tail of its newest vanilla game log to the results.
function Save-SlotLogs([string]$slot, [string]$base) {
  In-Slot $slot {
    $log = Join-Path $modDir "csharp\hot_cargo_log.txt"
    if (Test-Path $log) { Copy-Item $log "$base.log" -Force }
    $vanilla = Get-ChildItem (Join-Path $root "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($vanilla) { @("=== " + $vanilla.Name) + @(Get-Content $vanilla.FullName -Tail 150) | Set-Content "$base.vanilla.log" }
  }
}

# One run of a job. Returns true when the job must run again:
# - a single-player test that did not pass (the pass rule of log_passed in scripts/autotest-lib.sh: AUTOTEST DONE and no
#   AUTOTEST FAIL or ERROR line);
# - a co-op session in which nothing ran (no script line in the host log). A session that ran reports each step, and a
#   rerun would hide a flaky step.
function Invoke-Attempt($job, [string[]]$slots, [int]$attempt, [int]$queuedSeconds) {
  $tag = $job.Tag; $base = Join-Path $results "$tag.$attempt"; $deployLog = "$base.deploy.log"
  $port = 35870 + [int]$slots[0].Substring(1)
  $roles = @("host"); for ($i = 1; $i -lt $slots.Count; $i++) { $roles += "client$i" }
  $scriptOf = {
    param([int]$i)
    $kind = if ($i -eq 0) { "host" } else { "client" }
    $file = "$base." + $roles[$i] + ".script.txt"
    [IO.File]::WriteAllText($file, [IO.File]::ReadAllText((Join-Path $Suite "jobs\$tag.$kind.txt")).Replace("{PORT}", [string]$port))
    $file
  }
  $t0 = Get-Date
  $started = @()
  if (Start-Slot $slots[0] (& $scriptOf 0) $deployLog) { $started += 0 }
  $tDeployed = Get-Date; $tFirstLine = $null; $log = ""
  # A single-player test has no client to wait for.
  $clientsStarted = $slots.Count -eq 1; $dones = 0
  $deadline = $tDeployed.AddSeconds($job.Timeout)
  while ($started.Count -gt 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $log = Read-SlotLog $slots[0]; $hostRuns = Test-SlotRuns $slots[0]
    if (-not $tFirstLine -and $log.Contains("AUTOTEST line")) { $tFirstLine = Get-Date }
    if (-not $clientsStarted) {
      # The clients join when the host log shows "AUTOTEST hosting".
      if ($log.Contains("AUTOTEST hosting")) {
        $clientsStarted = $true
        for ($i = 1; $i -lt $slots.Count; $i++) { if (Start-Slot $slots[$i] (& $scriptOf $i) $deployLog) { $started += $i } }
        if ($started.Count -lt $slots.Count) { break }
      } elseif (-not $hostRuns) { break }
      continue
    }
    $anyRuns = $hostRuns
    for ($i = 1; $i -lt $slots.Count; $i++) { if (Test-SlotRuns $slots[$i]) { $anyRuns = $true } }
    if (-not $anyRuns) { break }
    # A single-player game that logged AUTOTEST DONE gets one more poll to exit by itself.
    if ($slots.Count -eq 1 -and $log.Contains("AUTOTEST DONE")) { $dones++; if ($dones -ge 2) { break } }
  }
  $tEnd = Get-Date
  foreach ($slot in $slots) { Stop-Slot $slot }
  # A slot whose start failed can still hold the log of an older run: never copy it.
  foreach ($i in $started) { Save-SlotLogs $slots[$i] ("$base." + $roles[$i]) }
  $hostLog = if (Test-Path "$base.host.log") { [IO.File]::ReadAllText("$base.host.log") } else { "" }
  $seconds = { param($from, $to) [int]($to - $from).TotalSeconds }
  $firstLine = if ($tFirstLine) { $tFirstLine } else { $tEnd }
  Set-Content "$base.timing" ("wait=$queuedSeconds deploy=" + (& $seconds $t0 $tDeployed) + " start=" + (& $seconds $tDeployed $firstLine) +
    " script=" + (& $seconds $firstLine $tEnd) + " teardown=" + (& $seconds $tEnd (Get-Date)) + " total=" + ($queuedSeconds + (& $seconds $t0 (Get-Date))))
  if ($slots.Count -gt 1) { return -not $hostLog.Contains("AUTOTEST line") }
  return -not ($hostLog.Contains("AUTOTEST DONE") -and $hostLog -notmatch "AUTOTEST FAIL| ERROR ")
}

function Write-JobDone([string]$tag, [string]$text) {
  $tmp = Join-Path $results "$tag.done.tmp"
  Set-Content $tmp $text
  Move-Item $tmp (Join-Path $results "$tag.done") -Force
}

function Invoke-Job([string]$tag, [string[]]$slots) {
  $job = $jobs | Where-Object { $_.Tag -eq $tag }
  if (-not $job) { throw "suite.txt has no job '$tag'" }
  if ($slots.Count -ne $job.Players) { throw "job $tag needs $($job.Players) slots, got $($slots.Count)" }
  $queued = [int]((Get-Date) - (Get-Item (Join-Path $Suite "started")).LastWriteTime).TotalSeconds
  $attempt = 0
  try {
    do { $attempt++; $again = Invoke-Attempt $job $slots $attempt $queued } while ($again -and $attempt -le [int](Need "retry"))
  } finally { foreach ($slot in $slots) { Stop-Slot $slot } }
  Write-JobDone $tag ("attempts=$attempt slots=" + ($slots -join "-"))
}

function Invoke-Suite {
  New-Item -ItemType Directory -Force $results | Out-Null
  Set-Content (Join-Path $Suite "started") $PID
  $start = Get-Date
  $pool = @((Need "slots").Split(","))
  $free = New-Object Collections.Generic.List[string]; $free.AddRange([string[]]$pool)
  $pending = New-Object Collections.Generic.List[object]; $pending.AddRange([object[]]$jobs)
  $running = @{}
  $deadline = $start.AddSeconds([int](Need "deadline")); $whilePlaying = [int](Need "whilePlaying")
  $state = "done"
  Note "suite starts: $($jobs.Count) jobs, slots $($pool -join ' '), deadline $deadline"
  try {
    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
      foreach ($tag in @($running.Keys)) {
        $worker = $running[$tag]
        if (-not $worker.Process.HasExited) { continue }
        $free.AddRange([string[]]$worker.Slots); $running.Remove($tag)
        if (-not (Test-Path (Join-Path $results "$tag.done"))) {
          Note "the worker of $tag ended without a result"
          foreach ($slot in $worker.Slots) { Stop-Slot $slot }
          Write-JobDone $tag ("attempts=0 slots=" + ($worker.Slots -join "-") + " reason=worker-crashed")
        }
      }
      if (Test-Path (Join-Path $Suite "stop")) { $state = "stopped"; break }
      if ((Get-Date) -gt $deadline) { $state = "deadline"; break }
      # While the player's own game runs, the suite uses at most $whilePlaying slots, so that game keeps its frame rate.
      # Limit: the cap counts this suite only, not the suites of other worktrees. Revisit if the player sees it.
      $cap = if (Test-SlotRuns "main") { $whilePlaying } else { $pool.Count }
      $busy = $pool.Count - $free.Count
      # The first waiting job that fits. A job larger than the cap still runs when nothing else does.
      $next = $pending | Where-Object { $_.Players -le $free.Count -and ($busy + $_.Players -le $cap -or $busy -eq 0) } | Select-Object -First 1
      if (-not $next) { Start-Sleep -Seconds 2; continue }
      $slots = @($free.GetRange(0, $next.Players)); $free.RemoveRange(0, $next.Players); [void]$pending.Remove($next)
      $out = Join-Path $results ($next.Tag + ".worker")
      $process = Start-Process powershell -WindowStyle Hidden -PassThru -RedirectStandardOutput "$out.out.log" -RedirectStandardError "$out.err.log" -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'), "-Suite", ('"' + $Suite + '"'), "-Job", $next.Tag, "-Slots", ($slots -join ","))
      $running[$next.Tag] = @{ Process = $process; Slots = $slots }
      Note "job $($next.Tag) starts in $($slots -join ' ')"
    }
  } catch {
    $state = "failed"
    Note ("the scheduler failed: " + $_.Exception.Message + " " + $_.ScriptStackTrace)
  } finally {
    foreach ($worker in $running.Values) { if (-not $worker.Process.HasExited) { $worker.Process | Stop-Process -Force } }
    foreach ($slot in $pool) { Stop-Slot $slot }
    foreach ($job in $jobs) { if (-not (Test-Path (Join-Path $results ($job.Tag + ".done")))) { Write-JobDone $job.Tag "attempts=0 slots=none reason=$state" } }
    Note "suite ends: $state"
    Set-Content (Join-Path $Suite "done") $state
  }
}

if ($Job) { Invoke-Job $Job @($Slots.Split(",")) } else { Invoke-Suite }
