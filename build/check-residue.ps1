# Residue check: fails when anything of the 1.x engine (the Python application on pywebview / WebView2, packed by
# PyInstaller) is left in the tracked tree. Reads the tracked paths (git ls-files) and the working-tree text of every
# tracked text file; run first by build\build.ps1.
# Exit code 0: no hit. Exit code 1: every hit is listed as "<path>:<line>: <term>" (or "<path>: <rule>" for a path).
# Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.
#   -Root <dir>   repository to check (default: the folder above build\)
[CmdletBinding()]
param([string]$Root)

$ErrorActionPreference = 'Stop'
if (!$Root) { $Root = Split-Path -Parent $PSScriptRoot }
$Root = (Resolve-Path -LiteralPath $Root).Path

# ---- the residue: tracked paths, text terms and .gitignore entries of the 1.x stack ----
$pathRules = @(
    @{ Rule = '^src/frame_notes/'; Why = 'the 1.x Python package' },
    @{ Rule = '^tests/test_[^/]*\.py$'; Why = 'a 1.x Python test' },
    @{ Rule = '^tests/__init__\.py$'; Why = 'the 1.x test package' },
    @{ Rule = '^scripts/'; Why = 'a 1.x development script' },
    @{ Rule = '\.spec$'; Why = 'a PyInstaller spec' },
    @{ Rule = '^build/entry\.py$'; Why = 'the PyInstaller entry point' },
    @{ Rule = '^pyproject\.toml$'; Why = 'the 1.x project metadata' },
    @{ Rule = '(^|/)requirements[^/]*\.txt$'; Why = 'a 1.x requirements file' },
    @{ Rule = '^docs/BRIDGE_API\.md$'; Why = 'the 1.x bridge contract' },
    @{ Rule = '\.py$'; Why = 'a Python file' },
    @{ Rule = '(^|/)themes\.json$'; Why = 'a theme file outside src/Retake/Design/' }
)
$terms = @('pywebview', 'PyInstaller', 'WebView2', 'frame_notes', 'FRAME_NOTES_', 'frame-notes_data', 'FrameNotes.exe', 'run_dev', '.venv', 'BRIDGE_API')
$ignoreEntries = @('__pycache__', '*.py[cod]', '.venv/', 'venv/', '*.spec.bak', 'build/dist/', 'build/work/', 'frame-notes_data/', '.pytest_cache/', '.mypy_cache/', '.ruff_cache/', '*.egg-info/')

# ---- the exact exceptions ----
# Path: a regex on the tracked path. Allowed: the terms that may appear there ('*' = all, also the path rules).
# Sections: only inside these level-2 headings of a Markdown file. MaxLines: at most that many lines with a term.
$exceptions = @(
    @{ Path = '^docs/releases/v1\.[^/]*\.md$'; Allowed = '*' },                                       # release history
    @{ Path = '^docs/USER_GUIDE\.md$'; Allowed = @('FrameNotes.exe'); MaxLines = 1 },                 # updating from 1.4.1
    @{ Path = '^docs/SELF_UPDATE\.md$'; Allowed = @('FrameNotes.exe', 'frame-notes_data'); Sections = @('Hand-off from the 1.x line', 'Tests') },
    @{ Path = '^docs/CORE\.md$'; Allowed = @('frame-notes_data'); Sections = @('Paths') },            # the legacy import
    @{ Path = '^src/Retake\.Core/Paths\.cs$'; Allowed = @('frame-notes_data') },                      # the legacy import
    @{ Path = '^src/Retake/Update/LegacyUpdates\.cs$'; Allowed = @('frame-notes_data') },             # the 1.x updates folder
    @{ Path = '^tests/Retake\.Tests/(PathsSettingsTests|UpdateTests)\.cs$'; Allowed = @('frame-notes_data', 'FrameNotes.exe') },
    @{ Path = '^build/e2e-handoff\.(ps1|py)$'; Allowed = '*' },                                       # the hand-off test from 1.4.1
    @{ Path = '^build/check-residue\.ps1$'; Allowed = '*' },                                          # this list
    @{ Path = '^src/Retake/Design/themes\.json$'; Allowed = '*' }                                     # the one theme file
)
function Exception-For($path) { foreach ($e in $exceptions) { if ($path -match $e.Path) { return $e } }; return $null }

# ---- tracked files ----
$savedEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
try { $tracked = @(& git -C $Root -c core.quotepath=false ls-files) } finally { [Console]::OutputEncoding = $savedEncoding }
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed in $Root" }

$hits = New-Object Collections.Generic.List[string]
foreach ($path in $tracked) {
    $exception = Exception-For $path
    $all = $exception -and ($exception.Allowed -is [string]) -and $exception.Allowed -eq '*'
    if (-not $all) {
        foreach ($r in $pathRules) {
            if ($path -match $r.Rule) { $hits.Add("${path}: $($r.Why)") }
        }
    }
    $full = Join-Path $Root $path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $bytes = [IO.File]::ReadAllBytes($full)
    if ([Array]::IndexOf($bytes, [byte]0, 0, [Math]::Min($bytes.Length, 8192)) -ge 0) { continue }   # binary
    if ($all) { continue }
    $lines = [Text.Encoding]::UTF8.GetString($bytes) -split "`n"
    $section = ''; $allowedLines = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($path -like '*.md' -and $line -match '^##\s+(\d+\.\s+)?(.+?)\s*$' -and $line -notmatch '^###') { $section = $Matches[2] }
        if ($path -eq '.gitignore') {
            $entry = $line.Trim()
            if ($ignoreEntries -contains $entry) { $hits.Add("${path}:$($i + 1): ignore entry $entry") }
        }
        foreach ($t in $terms) {
            if ($line.IndexOf($t, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
            $ok = $false
            if ($exception -and ($exception.Allowed -contains $t)) {
                $ok = $true
                if ($exception.Sections -and -not ($exception.Sections -contains $section)) { $ok = $false }
                if ($ok -and $exception.MaxLines) { $allowedLines++; if ($allowedLines -gt $exception.MaxLines) { $ok = $false } }
            }
            if (-not $ok) { $hits.Add("${path}:$($i + 1): $t") }
        }
    }
}

if ($hits.Count -gt 0) {
    Write-Host "Residue of the 1.x engine ($($hits.Count) hit(s)):"
    foreach ($h in $hits) { Write-Host "  $h" }
    exit 1
}
Write-Host "Residue check passed ($($tracked.Count) tracked files)."
