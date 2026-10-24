# End-to-end test of the hand-off from the last Frame Notes 1.x release to Retake: a copy of the 1.4.1 exe in a scratch
# folder with spaces in its path, with a 1.x data folder (recent list, window position), updates itself through a
# local release server into the exe built here. The 1.4.1 updater runs headless from its source tree
# (build\e2e-handoff.py); the swap and everything after it is the real Retake.exe.
# Asserts: the file at the 1.x path (FrameNotes.exe) is the served Retake.exe (hash equal) and starts from there;
# libmpv is extracted into retake_data\bin\ (the pinned hash); retake_data\settings.json carries the recent list and
# the window position over; FrameNotes.exe.old and the download are gone; no console window; the review window opens
# the recent video.
# Exit code 0: every assert passed. Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.
#   -OldExe <path>   the 1.4.1 exe (the build of tag v1.4.1; the script copies it, never changes it)
#   -OldSource <dir> the src folder of the 1.4.1 Python tree (the folder that holds the frame_notes package)
#   -Exe <path>      the Retake exe to serve (default: src\Retake\bin\Release\Retake.exe)
#   -Tag <vX.Y.Z>    the release tag to serve (default: v + the version in src\Retake\Version.cs)
#   -Port <n> (default 8766)   -Keep: leave the scratch folder   -Captures <dir>: PNGs of the updated window
#   -Real            drive the real 1.4.1 window instead of the headless updater: it opens its update sheet by itself,
#                    the script presses Install through UI Automation (the desktop must be free for about a minute)
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OldExe, [Parameter(Mandatory = $true)][string]$OldSource,
    [string]$Exe, [string]$Tag, [int]$Port = 8766, [switch]$Keep, [string]$Captures, [switch]$Real
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'e2e-common.ps1')
if (!$Exe) { $Exe = Join-Path $root 'src\Retake\bin\Release\Retake.exe' }
if (!$Tag) { $Tag = 'v' + (Current-Version $root).N }
foreach ($p in @($OldExe, $Exe, (Join-Path $OldSource 'frame_notes\__init__.py'))) { if (!(Test-Path -LiteralPath $p)) { throw "not found: $p" } }
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)
if (!$ffmpeg) { throw 'ffmpeg is needed to make the test video' }
$python = (Get-Command python -ErrorAction Stop).Source

$scratch = Join-Path $env:TEMP ('retake-handoff-e2e-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$serve = Join-Path $scratch 'serve'
$app = Join-Path $scratch 'Frame Notes copy (1.4.1)'
$legacy = Join-Path $app 'frame-notes_data'
New-Item -ItemType Directory -Force -Path $serve, $legacy | Out-Null

# ---- the old application: the 1.4.1 exe and its data folder ----
$target = Join-Path $app 'FrameNotes.exe'
Copy-Item -LiteralPath $OldExe -Destination $target
$clip = Join-Path $app 'clip one.mp4'
& $ffmpeg.Source -v error -f lavfi -i 'testsrc=duration=4:size=320x240:rate=25' -pix_fmt yuv420p -y $clip
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
$windowX = 140; $windowY = 90
$clipJson = $clip.Replace('\', '\\')
$settings = "{`"window`": {`"x`": $windowX, `"y`": $windowY}, `"last_dir`": `"$($app.Replace('\', '\\'))`", `"recent`": [{`"path`": `"$clipJson`", `"last_frame`": 12, `"opened_at`": 1790000000}], `"fps_overrides`": {}, `"auto_download_updates`": true, `"notes_panel_open`": true}`n"
[IO.File]::WriteAllText((Join-Path $legacy 'settings.json'), $settings, (New-Object Text.UTF8Encoding $false))
$oldSettingsBytes = [IO.File]::ReadAllBytes((Join-Path $legacy 'settings.json'))

# ---- the release: Retake.exe and SHA256SUMS, served from 127.0.0.1 ----
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'write-sums.ps1') -Exe $Exe -OutDir $serve | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'write-sums.ps1 failed' }
$hashServed = (Get-FileHash -LiteralPath (Join-Path $serve 'Retake.exe') -Algorithm SHA256).Hash
$libmpvHash = if (Test-Path -LiteralPath (Join-Path $root 'build\libmpv\libmpv-2.dll')) { (Get-FileHash -LiteralPath (Join-Path $root 'build\libmpv\libmpv-2.dll') -Algorithm SHA256).Hash } else { $null }
$base = "http://127.0.0.1:$Port/"
Write-ReleaseDocument $serve $Tag "## What is new`n- Retake replaces Frame Notes" $base @('Retake.exe', 'SHA256SUMS')
$stopFile = Join-Path $scratch 'stop-server'
$server = Start-ReleaseServer $serve $Port $stopFile

$savedOldUrl = [Environment]::GetEnvironmentVariable('FRAME_NOTES_RELEASE_URL')
$savedRetake = [Environment]::GetEnvironmentVariable('RETAKE_HOME'); $savedRetakeUrl = [Environment]::GetEnvironmentVariable('RETAKE_RELEASE_URL')
[Environment]::SetEnvironmentVariable('RETAKE_HOME', $null); [Environment]::SetEnvironmentVariable('RETAKE_RELEASE_URL', $null)
try {
    Write-Host "== 1.4.1 -> Retake ($Tag), folder: $app"
    $header = Get-FileHash -LiteralPath $target -Algorithm SHA256
    Write-Host "old exe sha256 $($header.Hash.Substring(0, 8))"
    # The 1.4.1 updater: check, download, verify, start the downloaded exe in apply mode, exit.
    $consoles = @(); $fresh = $null; $oldIds = @()
    if ($Real) {
        # The real 1.4.1 window: its update sheet opens by itself; Install downloads, verifies and restarts.
        [Environment]::SetEnvironmentVariable('FRAME_NOTES_RELEASE_URL', "$($base)latest.json")
        [void](Start-Process -FilePath $target -WorkingDirectory $app -PassThru)
        # With automatic download on (the default) the file may already be downloaded when the sheet shows: then the button reads "Restart to update".
        $install = Wait-Until { $b = Find-InWindow 'Frame Notes' 'Install'; if ($b) { $b } else { Find-InWindow 'Frame Notes' 'Restart to update' } } 90
        Check ($install -ne $null) 'the 1.4.1 window shows its update sheet with Install or Restart to update'
        if ($install -eq $null) { throw 'the 1.4.1 window did not offer the update' }
        if ($Captures) { Start-Sleep -Milliseconds 800; [void](Save-ScreenCapture (Join-Path $Captures 'handoff-141-sheet.png')) }
        $oldIds = @(Processes-Under $app | ForEach-Object { $_.ProcessId })
        Press-Element $install
        if ($Captures) { Start-Sleep -Seconds 2; [void](Save-ScreenCapture (Join-Path $Captures 'handoff-141-after-click.png')) }
    } else {
        $driver = Start-Process -FilePath $python -ArgumentList @('-I', "`"$(Join-Path $PSScriptRoot 'e2e-handoff.py')`"", "`"$OldSource`"", "`"$app`"", "`"$($base)latest.json`"") -PassThru -NoNewWindow
        [void]$driver.Handle   # keeps the process handle, so ExitCode is readable after the exit (Windows PowerShell 5.1)
        $driver.WaitForExit()
        Check ($driver.ExitCode -eq 0) 'the 1.4.1 updater checked, downloaded, verified and started the apply mode'
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 90 -and $fresh -eq $null) {
        $all = @(Get-CimInstance Win32_Process)
        $ours = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.ProcessId })
        $consoles += @($all | Where-Object { ($_.Name -eq 'conhost.exe' -or $_.Name -eq 'cmd.exe') -and $ours -contains $_.ParentProcessId } | ForEach-Object { $_.Name })
        $fresh = $all | Where-Object { $_.ExecutablePath -and $oldIds -notcontains $_.ProcessId -and [string]::Equals($_.ExecutablePath, $target, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($fresh -eq $null) { Start-Sleep -Milliseconds 250 }
    }
    Check ($fresh -ne $null) 'Retake starts from the 1.x path (FrameNotes.exe)'
    if ($fresh -eq $null) { throw 'Retake did not start' }
    Check ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $hashServed) 'the file at the 1.x path is the served Retake.exe (hash equal)'
    $window = Wait-Until { Find-Element $fresh.ProcessId 'v2.0.0' } 30
    Check ($window -ne $null) 'the review window is up (pill v2.0.0)'
    $retakeData = Join-Path $app 'retake_data'
    Check (Test-Path -LiteralPath $retakeData -PathType Container) 'retake_data\ is created beside the exe'
    if ($libmpvHash) {
        $dll = Join-Path $retakeData 'bin\libmpv-2.dll'
        $extracted = Wait-Until { Test-Path -LiteralPath $dll } 30
        Check ($extracted -eq $true) 'libmpv is extracted into retake_data\bin\'
        if ($extracted) { Check ((Wait-Until { try { (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -eq $libmpvHash } catch { $false } } 30) -eq $true) 'the extracted libmpv is the pinned build (hash equal)' }
    }
    # The recent list carried over: the start screen lists the 1.x video; opening it loads it (the frame rate shows).
    $row = Wait-Until { Find-Element $fresh.ProcessId 'clip one.mp4' ([Windows.Automation.ControlType]::Button) } 15
    Check ($row -ne $null) 'the start screen lists the recent 1.x video'
    if ($row) {
        if ($Captures) { [void](Save-WindowCapture $fresh.ProcessId (Join-Path $Captures 'handoff-start.png')) }
        Invoke-Element $row
        Check ((Wait-Until { Find-Element $fresh.ProcessId '25 fps' } 40) -ne $null) 'the recent video opens (25 fps shown)'
    }
    if ($Captures) { [void](Save-WindowCapture $fresh.ProcessId (Join-Path $Captures 'handoff-retake.png')) }
    Check ((Wait-Until { -not (Test-Path -LiteralPath "$target.old") } 15) -eq $true) 'FrameNotes.exe.old is gone within 15 s'
    $downloads = Join-Path $legacy 'updates'
    $clean = Wait-Until { @(Get-ChildItem -LiteralPath $downloads -Filter '*.exe' -ErrorAction SilentlyContinue).Count -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $downloads 'applied.json')) } 15
    Check ($clean -eq $true) 'the download and the marker of the 1.x updates folder are gone'
    Check ($consoles.Count -eq 0) "no console window ($($consoles -join ', '))"
    $process = Get-Process -Id $fresh.ProcessId -ErrorAction SilentlyContinue
    if ($process) { [void]$process.CloseMainWindow(); if (!$process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force } }
    $carried = Get-Content -LiteralPath (Join-Path $retakeData 'settings.json') -Raw | ConvertFrom-Json
    Check ($carried.window.x -eq $windowX -and $carried.window.y -eq $windowY) 'retake_data\settings.json carries the window position over'
    Check (@($carried.recent).Count -eq 1 -and $carried.recent[0].path -eq $clip) 'retake_data\settings.json carries the recent list over'
    # The real 1.4.1 window may save its own settings on exit, so the byte comparison is for the headless run only.
    if (-not $Real) { Check (([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $legacy 'settings.json')))) -eq ([Convert]::ToBase64String($oldSettingsBytes))) 'the 1.x settings.json is not changed' }
} finally {
    [Environment]::SetEnvironmentVariable('RETAKE_HOME', $savedRetake); [Environment]::SetEnvironmentVariable('RETAKE_RELEASE_URL', $savedRetakeUrl)
    [Environment]::SetEnvironmentVariable('FRAME_NOTES_RELEASE_URL', $savedOldUrl)
    foreach ($p in Processes-Under $scratch) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType File -Force -Path $stopFile | Out-Null
    Wait-Job $server -Timeout 10 | Out-Null; Remove-Job $server -Force
    if (!$Keep) { Start-Sleep -Milliseconds 500; Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "scratch: $scratch" }
}
if ($script:failures -gt 0) { Write-Host "$($script:failures) assert(s) failed"; exit 1 }
Write-Host 'The hand-off from 1.4.1 to Retake works end to end.'
