# Downloads the pinned libmpv build into build\libmpv\ and verifies it.
#
# The archive comes from the shinchiro/mpv-winbuild-cmake release named below; its SHA-256 and the SHA-256 of the
# libmpv-2.dll inside it are checked before the DLL is kept. The folder build\libmpv\ is ignored by git; the
# archive and the DLL are never committed. A build of src\Retake embeds build\libmpv\libmpv-2.dll (GZip) when it
# exists. 7-Zip is needed to unpack the .7z archive: 7z.exe on PATH or in the default install folder.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build\fetch-libmpv.ps1 [-Force]

param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Release = '20261021'
$Asset = 'mpv-dev-x86_64-20261021-git-e471496c56.7z'
$Url = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Release/$Asset"
$ArchiveSha256 = 'efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633'
$DllName = 'libmpv-2.dll'
$DllSize = 121108480
$DllSha256 = '04e4d27dc385dc18ddd93fbe69c75e635b864f3c6ec53a9122df54c2f7842f4a'

$Dir = Join-Path $PSScriptRoot 'libmpv'
$Archive = Join-Path $Dir $Asset
$Dll = Join-Path $Dir $DllName

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Find-SevenZip {
    $cmd = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        $candidate = Join-Path $root '7-Zip\7z.exe'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '7-Zip is needed to unpack the archive: install it (7-zip.org) or put 7z.exe on PATH.'
}

if (-not $Force -and (Test-Path -LiteralPath $Dll) -and (Get-Sha256 $Dll) -eq $DllSha256) {
    Write-Host "$DllName is present and verified: $Dll"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Dir | Out-Null

if ($Force -or -not (Test-Path -LiteralPath $Archive) -or (Get-Sha256 $Archive) -ne $ArchiveSha256) {
    Write-Host "Downloading $Url"
    $part = "$Archive.part"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $Url -OutFile $part -UseBasicParsing
    $hash = Get-Sha256 $part
    if ($hash -ne $ArchiveSha256) {
        Remove-Item -LiteralPath $part -Force
        throw "The archive hash does not match: expected $ArchiveSha256, got $hash."
    }
    Move-Item -LiteralPath $part -Destination $Archive -Force
}
Write-Host "Archive verified: $Archive"

$sevenZip = Find-SevenZip
$unpack = Join-Path $Dir 'unpack'
if (Test-Path -LiteralPath $unpack) { Remove-Item -LiteralPath $unpack -Recurse -Force }
& $sevenZip e $Archive "-o$unpack" $DllName -y | Out-Null
if ($LASTEXITCODE -ne 0) { throw "7-Zip failed with exit code $LASTEXITCODE." }
$extracted = Join-Path $unpack $DllName
if (-not (Test-Path -LiteralPath $extracted)) { throw "$DllName is not in the archive." }

$size = (Get-Item -LiteralPath $extracted).Length
$hash = Get-Sha256 $extracted
if ($size -ne $DllSize -or $hash -ne $DllSha256) {
    Remove-Item -LiteralPath $unpack -Recurse -Force
    throw "The DLL does not match: expected $DllSize bytes / $DllSha256, got $size bytes / $hash."
}
Move-Item -LiteralPath $extracted -Destination $Dll -Force
Remove-Item -LiteralPath $unpack -Recurse -Force
Write-Host "$DllName verified ($DllSize bytes): $Dll"
