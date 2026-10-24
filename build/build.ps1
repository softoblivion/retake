# Release build of Retake: release\Retake.exe and release\SHA256SUMS.
# Steps, each one stops the build on failure:
#   1. the residue check (build\check-residue.ps1);
#   2. the pinned libmpv in build\libmpv\ verified, or restored when it is missing (build\fetch-libmpv.ps1);
#   3. the test runner built and run (tests\Retake.Tests, RETAKE_HOME in a scratch folder);
#   4. src\Retake built in Release with MSBuild warnings as errors: the version, the assembly and file versions and
#      the manifest's assemblyIdentity come from src\Retake\Version.cs, the icon from build\icon.ico, libmpv embedded,
#      deterministic source paths (the exe records no path of the build machine);
#   5. the exe's version resource checked against Version.cs and the exe checked for paths of this machine, then the
#      release assets written by build\write-sums.ps1.
# Windows PowerShell 5.1 compatible; the script text is ASCII on purpose. Details: docs\BUILD.md.
#   -Version <x.y.z>  build another version from a copy of the sources in a scratch folder (end-to-end update tests):
#                     the working tree and its Version.cs stay unchanged, steps 2 and 3 are skipped, and the assets
#                     go to -OutDir, which is required with -Version
#   -OutDir <dir>     output folder of the assets (default: release\)
#   -SkipTests        skip step 3 (a development build only; a release is never built with it)
# The last line of the output is the path of the built exe.
[CmdletBinding()]
param([string]$Version, [string]$OutDir, [switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$clock = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Native([string]$what, [scriptblock]$command) {
    # Native tools write progress to stderr; only the exit code decides.
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $command | Out-Host } finally { $ErrorActionPreference = $saved }
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}
function Script([string]$name) { Join-Path $PSScriptRoot $name }
function Version-Of([string]$file) {
    $v = [regex]::Match((Get-Content -LiteralPath $file -Raw), 'Version\s*=\s*"(\d+\.\d+\.\d+)"').Groups[1].Value
    if (!$v) { throw "Version not found in $file" }
    return $v
}

if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+$') { throw "-Version must be x.y.z, got '$Version'" }
if ($Version -and !$OutDir) { throw '-OutDir is required with -Version' }
if (!$OutDir) { $OutDir = Join-Path $root 'release' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$libmpv = Join-Path $root 'build\libmpv\libmpv-2.dll'

# 1. residue -------------------------------------------------------------------------------------------------------
Write-Host '== Residue check'
Invoke-Native 'The residue check' { powershell -NoProfile -ExecutionPolicy Bypass -File (Script 'check-residue.ps1') -Root $root }

if (!$Version) {
    # 2. libmpv ----------------------------------------------------------------------------------------------------
    Write-Host '== libmpv'
    Invoke-Native 'fetch-libmpv.ps1' { powershell -NoProfile -ExecutionPolicy Bypass -File (Script 'fetch-libmpv.ps1') }

    # 3. tests -----------------------------------------------------------------------------------------------------
    if ($SkipTests) { Write-Host '== Tests skipped (-SkipTests): this build is not a release' }
    else {
        Write-Host '== Tests'
        Invoke-Native 'The test build' { dotnet build (Join-Path $root 'tests\Retake.Tests\Retake.Tests.csproj') -c Release --nologo -v q -warnaserror }
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ('retake-tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Path $scratch | Out-Null
        $savedHome = $env:RETAKE_HOME
        $env:RETAKE_HOME = $scratch
        try { Invoke-Native 'The tests' { & (Join-Path $root 'tests\Retake.Tests\bin\Release\Retake.Tests.exe') } }
        finally { $env:RETAKE_HOME = $savedHome; Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $sources = $root
} else {
    # A copy of the sources with another version; build\libmpv is read in place (a global MSBuild property).
    $sources = Join-Path ([IO.Path]::GetTempPath()) ("retake-build-$Version-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    Write-Host "== Sources of $Version in $sources"
    New-Item -ItemType Directory -Force -Path (Join-Path $sources 'build') | Out-Null
    & robocopy (Join-Path $root 'src') (Join-Path $sources 'src') /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit code $LASTEXITCODE)" }
    Copy-Item -LiteralPath (Join-Path $root 'Directory.Build.props') -Destination $sources
    Copy-Item -LiteralPath (Join-Path $root 'build\icon.ico') -Destination (Join-Path $sources 'build')
    $versionFile = Join-Path $sources 'src\Retake\Version.cs'
    $current = Version-Of $versionFile
    [IO.File]::WriteAllText($versionFile, (Get-Content -LiteralPath $versionFile -Raw).Replace("""$current""", """$Version"""), (New-Object Text.UTF8Encoding $false))
}

# 4. the application -------------------------------------------------------------------------------------------------
$expected = Version-Of (Join-Path $sources 'src\Retake\Version.cs')
Write-Host "== Retake $expected"
$project = Join-Path $sources 'src\Retake\Retake.csproj'
# Deterministic source paths: the exe records its PDB as /_/src/..., not as a path of this machine.
$properties = @('-p:ContinuousIntegrationBuild=true')
if ($Version) { $properties += "-p:PathMap=$sources\=/_/" }   # the copy is not a git checkout: no source root of its own
if ($Version -and (Test-Path -LiteralPath $libmpv)) { $properties += "-p:LibmpvSource=$libmpv" }
Invoke-Native 'The build of Retake' { dotnet build $project -c Release --nologo -v q -warnaserror @properties }
$built = Join-Path $sources 'src\Retake\bin\Release\Retake.exe'

# 5. release assets --------------------------------------------------------------------------------------------------
$info = (Get-Item -LiteralPath $built).VersionInfo
if ($info.ProductVersion -ne $expected -or $info.FileVersion -ne "$expected.0") {
    throw "The exe's version resource ($($info.ProductVersion) / $($info.FileVersion)) does not match $expected"
}
$text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($built))
foreach ($local in @($root, $sources) | Select-Object -Unique) {
    if ($text.IndexOf($local, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "The exe holds a path of this machine: $local" }
}
$sums = (& powershell -NoProfile -ExecutionPolicy Bypass -File (Script 'write-sums.ps1') -Exe $built -OutDir $OutDir | Select-Object -Last 1)
if ($LASTEXITCODE -ne 0) { throw 'write-sums.ps1 failed' }
$asset = (Get-Content -LiteralPath $sums -Raw).Trim() -replace '^[0-9a-f]{64}  ', ''
$exe = Join-Path $OutDir $asset
if ($Version) { Remove-Item -LiteralPath $sources -Recurse -Force -ErrorAction SilentlyContinue }
if (!(Test-Path -LiteralPath $libmpv)) { Write-Warning 'build\libmpv\libmpv-2.dll is missing: this exe plays nothing (libmpv is not bundled).' }

$size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host "== Done in $([int]$clock.Elapsed.TotalSeconds) s: $asset $expected, $size MB"
Write-Host (Get-Content -LiteralPath $sums -Raw).Trim()
Write-Output $exe
