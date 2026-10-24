# Release assets of Retake: <OutDir>\Retake.exe and <OutDir>\SHA256SUMS, one line "<sha256 lowercase>  Retake.exe"
# (two spaces, LF, no BOM). Both names are read by every installed copy of the 1.x and 2.x line; renaming either
# strands them. The asset name has one source: AssetName in src\Retake\Update\UpdateSetup.cs.
# Windows PowerShell 5.1 compatible; the script text is ASCII on purpose.
#   -Exe <path>     the built exe (default: src\Retake\bin\Release\Retake.exe)
#   -OutDir <dir>   output folder (default: release\); the exe is copied there under the asset name
# Prints the path of the SHA256SUMS file.
[CmdletBinding()]
param([string]$Exe, [string]$OutDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$Exe) { $Exe = Join-Path $root 'src\Retake\bin\Release\Retake.exe' }
if (!$OutDir) { $OutDir = Join-Path $root 'release' }
if (!(Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "Built exe not found: $Exe" }

$setup = Get-Content -LiteralPath (Join-Path $root 'src\Retake\Update\UpdateSetup.cs') -Raw
$asset = [regex]::Match($setup, 'AssetName\s*=\s*"([^"]+)"').Groups[1].Value
if (!$asset) { throw 'AssetName not found in src\Retake\Update\UpdateSetup.cs' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$target = Join-Path $OutDir $asset
if ([IO.Path]::GetFullPath($Exe) -ne [IO.Path]::GetFullPath($target)) { Copy-Item -LiteralPath $Exe -Destination $target -Force }
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = Join-Path $OutDir 'SHA256SUMS'
[IO.File]::WriteAllText($sums, "$hash  $asset`n", (New-Object Text.UTF8Encoding $false))
Write-Output $sums
