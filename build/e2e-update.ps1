# End-to-end test of Retake's self-update against a local HTTP server, run before every release that touches the
# update path. Version N is the release build (release\Retake.exe of build\build.ps1, the version of
# src\Retake\Version.cs); N+1 is built by build\build.ps1 -Version from a copy of the sources. Serves N+1, its
# SHA256SUMS and a GitHub "latest release" document (latest.json) from 127.0.0.1, points RETAKE_RELEASE_URL at it,
# runs N from a scratch folder with spaces in its path and presses the sheet's button through UI Automation. Two runs:
#   auto     automatic download on (the default): the sheet opens by itself, the download finishes, "Restart to update"
#   install  automatic download off: the sheet opens at "Install", the click downloads and restarts by itself
# Asserts per run: the sheet opens with the new version and its notes; N+1 runs from the same path (file hash equal
# to the served exe); no console window (no conhost / cmd child of the app's processes); "Retake.exe.old" is gone
# within 15 s; the marker and the used download are cleaned up; the data folder is kept (settings.json keeps its
# values, other files are untouched); the "Updated to v<N+1>" toast and the pill "v<N+1>" are shown.
# Exit code 0: every assert passed. Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.
#   -Mode auto|install|both (default both)   -Port <n> (default 8765)   -Keep: leave the scratch folder
#   -Exe <path>       version N (default: release\Retake.exe; its version must be the one of Version.cs)
#   -Scale <n>        the Scale setting of the runs (100, 125, 150, 175, 200; default 100)
#   -Captures <dir>   save PNGs of the sheet and of the updated window there (names end in -<scale>)
[CmdletBinding()]
param([ValidateSet('auto', 'install', 'both')][string]$Mode = 'both', [int]$Port = 8765, [switch]$Keep, [string]$Captures,
    [string]$Exe, [ValidateSet(100, 125, 150, 175, 200)][int]$Scale = 100)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'e2e-common.ps1')

$setup = Get-Content -LiteralPath (Join-Path $root 'src\Retake\Update\UpdateSetup.cs') -Raw
function Constant($name) { $v = [regex]::Match($setup, $name + '\s*=\s*"([^"]*)"').Groups[1].Value; if (!$v) { throw "$name not found in UpdateSetup.cs" }; return $v }
$asset = Constant 'AssetName'; $urlVariable = Constant 'ReleaseUrlVariable'
$v = Current-Version $root; $n = $v.N; $n1 = $v.N1

$scratch = Join-Path $env:TEMP ('retake-update-e2e-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$serve = Join-Path $scratch 'serve'
New-Item -ItemType Directory -Force -Path $serve | Out-Null

# ---- builds ----
if (!$Exe) { $Exe = Join-Path $root 'release\Retake.exe' }
if (!(Test-Path -LiteralPath $Exe)) { throw "version $n not found: $Exe (run build\build.ps1 first)" }
if ((Exe-Version $Exe) -ne $n) { throw "$Exe is version $(Exe-Version $Exe), not $n" }
$exeN = $Exe
$exeN1 = Build-Retake $root $scratch $n1
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'write-sums.ps1') -Exe $exeN1 -OutDir $serve | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'write-sums.ps1 failed' }
$hashN1 = (Get-FileHash -LiteralPath (Join-Path $serve $asset) -Algorithm SHA256).Hash

# ---- release document and server ----
$base = "http://127.0.0.1:$Port/"
$notes = "## What is new`n- The update path is tested **end to end**`n- Release notes render ``code`` and [links](https://github.com/)`n`nMore text."
Write-ReleaseDocument $serve "v$n1" $notes $base @($asset, 'SHA256SUMS')
$stopFile = Join-Path $scratch 'stop-server'
$server = Start-ReleaseServer $serve $Port $stopFile

$savedUrl = [Environment]::GetEnvironmentVariable($urlVariable); $savedHome = [Environment]::GetEnvironmentVariable('RETAKE_HOME')
[Environment]::SetEnvironmentVariable($urlVariable, $base + 'latest.json'); [Environment]::SetEnvironmentVariable('RETAKE_HOME', $null)
try {
    $modes = @('auto', 'install'); if ($Mode -ne 'both') { $modes = @($Mode) }
    foreach ($run in $modes) {
        Write-Host "== $run ($n -> $n1)"
        $folder = Join-Path $scratch "run $run (portable)"; $data = Join-Path $folder 'retake_data'
        New-Item -ItemType Directory -Force -Path $data | Out-Null
        $exe = Join-Path $folder $asset; Copy-Item -LiteralPath $exeN -Destination $exe
        [IO.File]::WriteAllText((Join-Path $data 'keep.txt'), 'user data', (New-Object Text.UTF8Encoding $false))
        $autoValue = if ($run -eq 'install') { 'false' } else { 'true' }
        $settingsText = "{`"recent`": [], `"auto_download_updates`": $autoValue, `"theme`": `"sky`", `"author`": `"E2E Tester`", `"ui_scale`": $Scale}`n"
        [IO.File]::WriteAllText((Join-Path $data 'settings.json'), $settingsText, (New-Object Text.UTF8Encoding $false))
        $old = Start-Process -FilePath $exe -WorkingDirectory $folder -PassThru
        $button = if ($run -eq 'install') { 'Install' } else { 'Restart to update' }
        $target = Wait-Until { Find-Element $old.Id $button ([Windows.Automation.ControlType]::Button) -Enabled } 60
        Check ($target -ne $null) "the sheet opens by itself and offers '$button'"
        if ($target -eq $null) { Stop-Process -Id $old.Id -Force -ErrorAction SilentlyContinue; continue }
        Check ((Find-Element $old.Id "Retake $n1") -ne $null) "the sheet is titled 'Retake $n1'"
        Check ((Find-Element $old.Id 'What is new') -ne $null) 'the sheet shows the release notes'
        if ($Captures) { Start-Sleep -Milliseconds 600; [void](Save-WindowCapture $old.Id (Join-Path $Captures "sheet-$run-$Scale.png")) }
        Invoke-Element $target
        # Watch for console processes under the app's processes until the new instance runs.
        $consoles = @(); $fresh = $null; $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 90 -and $fresh -eq $null) {
            $all = @(Get-CimInstance Win32_Process)
            $ours = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.ProcessId })
            $consoles += @($all | Where-Object { ($_.Name -eq 'conhost.exe' -or $_.Name -eq 'cmd.exe') -and $ours -contains $_.ParentProcessId } | ForEach-Object { $_.Name })
            $fresh = $all | Where-Object { $_.ProcessId -ne $old.Id -and $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $exe, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
            if ($fresh -eq $null) { Start-Sleep -Milliseconds 250 }
        }
        Check ($old.WaitForExit(5000)) "version $n exited"
        Check ($fresh -ne $null) 'a new instance runs from the same path'
        if ($fresh -eq $null) { continue }
        $toast = Wait-Until { Find-Element $fresh.ProcessId "Updated to v$n1" } 15
        Check ($toast -ne $null) "the new instance shows 'Updated to v$n1'"
        Check ((Wait-Until { Find-Element $fresh.ProcessId "v$n1" } 10) -ne $null) "the pill shows v$n1"
        if ($Captures) { [void](Save-WindowCapture $fresh.ProcessId (Join-Path $Captures "updated-$run-$Scale.png")) }
        Check ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $hashN1) "the exe at the path is the served $n1 (hash equal)"
        Check ((Wait-Until { -not (Test-Path -LiteralPath "$exe.old") } 15) -eq $true) "$asset.old is gone within 15 s"
        Check ($consoles.Count -eq 0) "no console window ($($consoles -join ', '))"
        $updates = Join-Path $data 'updates'
        $clean = Wait-Until { -not (Test-Path -LiteralPath (Join-Path $updates 'applied.json')) -and @(Get-ChildItem -LiteralPath $updates -Filter '*.exe' -ErrorAction SilentlyContinue).Count -eq 0 } 15
        Check ($clean -eq $true) 'the marker and the used download are cleaned up'
        $process = Get-Process -Id $fresh.ProcessId -ErrorAction SilentlyContinue
        if ($process) { [void]$process.CloseMainWindow(); if (!$process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force } }
        # After the new instance saved its settings on close: the user's values are still there.
        $after = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
        Check (($after.author -eq 'E2E Tester') -and ($after.theme -eq 'sky') -and ($after.auto_download_updates.ToString().ToLowerInvariant() -eq $autoValue)) 'settings.json keeps the user values'
        Check ((Get-Content -LiteralPath (Join-Path $data 'keep.txt') -Raw) -eq 'user data') 'the other files of the data folder are untouched'
    }
} finally {
    [Environment]::SetEnvironmentVariable($urlVariable, $savedUrl); [Environment]::SetEnvironmentVariable('RETAKE_HOME', $savedHome)
    foreach ($p in Processes-Under $scratch) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType File -Force -Path $stopFile | Out-Null
    Wait-Job $server -Timeout 10 | Out-Null; Remove-Job $server -Force
    if (!$Keep) { Start-Sleep -Milliseconds 500; Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "scratch: $scratch" }
}
if ($script:failures -gt 0) { Write-Host "$($script:failures) assert(s) failed"; exit 1 }
Write-Host 'The update path works end to end.'
