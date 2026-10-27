# Build, test and release

How `Retake.exe` is built from this repository, how it is tested and how a release is made.

## 1. Prerequisites

| Tool | Version | Used for |
|---|---|---|
| Windows | 10 or 11, x64 | building and running |
| .NET SDK | 8.0 (8.0.403 verified) | `dotnet build` of the SDK-style projects |
| .NET Framework 4.8 Developer Pack | 4.8 | the `net48` reference assemblies (the targeting pack; no NuGet package is restored) |
| Windows PowerShell | 5.1 | the scripts in `build\` |
| Git | any | the residue check reads `git ls-files` |
| 7-Zip | any (`7z.exe` on `PATH` or in `C:\Program Files\7-Zip\`) | unpacking libmpv, only when `build\libmpv\` is empty |
| FFmpeg; Python 3 | on `PATH` | the hand-off test from 1.4.1 only (section 5); Python for its headless mode |

The exe needs nothing on the target machine but Windows 10 or 11 (the .NET Framework 4.8 is part of it).

## 2. Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
```

Steps, each one stops the build on failure:

1. The residue check, `build\check-residue.ps1` (section 6).
2. libmpv: `build\fetch-libmpv.ps1` verifies `build\libmpv\libmpv-2.dll` against its pinned SHA-256, or downloads and
   unpacks it when it is missing (section 3).
3. Tests: `tests\Retake.Tests` is built and `Retake.Tests.exe` runs with `RETAKE_HOME` in a scratch folder.
4. `src\Retake` is built in Release with MSBuild warnings treated as errors and deterministic source paths
   (`ContinuousIntegrationBuild`: the exe records its PDB as `/_/src/...`, never a folder of the build machine).
5. The exe's version resource is checked against `Version.cs` and the exe is checked for paths of the build
   machine; `build\write-sums.ps1` copies it to `release\Retake.exe` and writes `release\SHA256SUMS`. The script prints the size and the checksum line; its last line is the
   path of the exe.

Output in `release\` (ignored by git): `Retake.exe` and `SHA256SUMS` (one line `<sha256 lowercase>  Retake.exe`, two
spaces, LF, no byte order mark). These two files are the release assets.

Options:

| Option | Effect |
|---|---|
| `-Version <x.y.z>` | builds another version from a copy of the sources in a scratch folder (the working tree and its `Version.cs` stay unchanged); steps 2 and 3 are skipped; requires `-OutDir`. Used by the end-to-end update tests. |
| `-OutDir <dir>` | the folder of the two assets instead of `release\` |
| `-SkipTests` | skips step 3; a development build only, never a release |

What the project files do:

- **Version.** `src\Retake\Version.cs` is the only place of the version. Before compiling, the `StampVersion` task of
  `Retake.csproj` writes `obj\version\AssemblyInfo.g.cs` (title and product `Retake`, copyright, assembly and file
  version `x.y.z.0`, product version `x.y.z`) and `obj\version\app.manifest` (`src\Retake\app.manifest` with the
  `assemblyIdentity` version `x.y.z.0`); the exe embeds that manifest (per-monitor V2 DPI awareness, long paths).
- **Icon.** `build\icon.ico` is the exe icon (`ApplicationIcon`). The WPF window takes the exe's icon.
- **One exe.** The sources of `Retake.Core` are compiled into `Retake.exe`; the fonts and `themes.json` are
  resources; libmpv is embedded (section 3). `Retake.exe.config` is not needed at runtime.

Development builds without the script:

```powershell
dotnet build Retake.sln -c Release
$env:RETAKE_HOME = "$env:TEMP\retake-scratch"
tests\Retake.Tests\bin\Release\Retake.Tests.exe [name filter]
src\Retake\bin\Release\Retake.exe [video]
```

Command line of the exe, besides a video path:

| Argument | Effect |
|---|---|
| `--theme black\|sky`, `--scale 100..200` | theme and Scale for this run only |
| `--fake-player` | generated frames instead of libmpv (the frame number in large digits and a barcode of it); for checks of the views |
| `--gallery [--capture <png>] [--diag <txt>]` | the control gallery of the design layer; `--capture` renders it to a PNG and exits, `--diag` adds a font and theme report |

Run from the build output, the data folder is `src\Retake\retake_data\` unless `RETAKE_HOME` is set.

## 3. libmpv

The DLL is not in the repository. `build\fetch-libmpv.ps1` fetches the pinned build once per checkout:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\fetch-libmpv.ps1 [-Force]
```

- Source: `shinchiro/mpv-winbuild-cmake`, release `20261021`, asset `mpv-dev-x86_64-20261021-git-e471496c56.7z`
  (SHA-256 `efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633`); inside it `libmpv-2.dll`,
  121,108,480 bytes, SHA-256 `04e4d27dc385dc18ddd93fbe69c75e635b864f3c6ec53a9122df54c2f7842f4a`.
- The script downloads the archive into `build\libmpv\` (ignored by git), checks the archive hash, unpacks the DLL
  with 7-Zip, checks the DLL's size and hash, and stops with a message on any mismatch. A verified DLL already in
  place is kept unless `-Force` is given.
- A build of `src\Retake` packs `build\libmpv\libmpv-2.dll` with GZip into `src\Retake\obj\libmpv\` and embeds it
  with its SHA-256 (the exe grows to about 48 MB). Without the DLL the build still succeeds and `build.ps1` warns;
  that exe reports `libmpv is not bundled in this build`, and `--fake-player` still works.
- At start the exe writes the DLL to `retake_data\bin\libmpv-2.dll` when it is missing or its hash differs, then
  loads it from there.

A new libmpv build is pinned by changing the release, asset, sizes and hashes at the top of `fetch-libmpv.ps1` and
the build facts in `THIRD_PARTY.md`, then re-running the engine checks of the views.

## 4. Icon

`build\make-icon.ps1` writes `build\icon.ico` (PNG frames of 16 to 256 px) and `docs\assets\icon.svg` from the Retake
mark. The mark has one source, the class `AppGlyph` in `src\Retake\Design\Shell.cs` (the title-bar glyph): its three
paths on a 20 x 20 grid and their theme tokens, coloured with the default theme of `themes.json`. After a change of
the mark the script is run and both files are committed; the build reads `build\icon.ico` as it is.

## 5. End-to-end update tests

Both scripts open real windows on the desktop for about a minute and drive them through UI Automation; the desktop
should be left alone meanwhile. Both serve a release from `127.0.0.1` through a GitHub style release document.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\e2e-update.ps1 [-Mode auto|install|both] [-Scale 150] [-Captures <dir>] [-Keep]
```

`e2e-update.ps1` updates the release build `release\Retake.exe` (version N of `Version.cs`; `-Exe` names another) to
version N+1 built by `build.ps1 -Version`. Two runs: automatic download (the sheet offers Restart to update) and
Install with automatic download off. `-Scale` sets the Scale
setting of the runs; `-Captures` saves the update sheet and the updated window as PNGs.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\e2e-handoff.ps1 -OldExe <1.4.1 exe> [-Captures <dir>]
git worktree add <dir> v1.4.1
powershell -NoProfile -ExecutionPolicy Bypass -File build\e2e-handoff.ps1 -OldExe <1.4.1 exe> -Driver <driver.py> -OldSource <dir> [-Captures <dir>]
```

`e2e-handoff.ps1` updates a copy of release 1.4.1 into `release\Retake.exe`. `-OldExe` is the exe of release 1.4.1
(the release asset of tag `v1.4.1`). Without `-Driver` the script drives the real 1.4.1 window: its update sheet
opens by itself and the script presses Install (the desktop must be free for about a minute). The headless mode
runs the 1.4.1 updater from its Python source instead, and needs two inputs: `-OldSource`, a checkout of tag
`v1.4.1`, and `-Driver`, the path of the driver script that calls that updater. The driver is not part of this
repository; it runs with the `python` on `PATH` and takes the 1.4.1 `src` folder, the application folder and the
release document URL. The script needs FFmpeg on `PATH` for its test video. The worktree is removed afterwards with
`git worktree remove <dir>`.

## 6. Residue check

`build\check-residue.ps1` fails, listing every hit, when the tracked tree still holds anything of the 1.x engine:
tracked paths of the Python application (package, tests, development scripts, project metadata, requirements,
the packager's spec and entry point, bridge contract, any `.py` file), a second
`themes.json`, the Python entries of `.gitignore`, and the text terms of the old stack (its packages, its build
tool, its browser engine, its data folder, exe name and environment variables). The exceptions are exact and
listed in the script: the 1.x release notes, the sentence of `USER_GUIDE.md` on updating from 1.4.1, the legacy settings import in the core,
the 1.x updates folder hand-over and its tests, and the hand-off test script. `build.ps1` runs it first.

## 7. Release

1. `docs\releases\vX.Y.Z.md` in the layout of `docs\releases\README.md`, and the version in `src\Retake\Version.cs`.
2. `build\build.ps1` on a clean working tree; for a release that touches the update path, `build\e2e-update.ps1`
   (and `build\e2e-handoff.ps1` while 1.4.1 copies are expected to update).
3. Commit `Release vX.Y.Z`, tag `vX.Y.Z` on `dev`.
4. GitHub Release titled `vX.Y.Z`, the notes file as its body, with exactly the two assets `release\Retake.exe` and
   `release\SHA256SUMS` attached. Every installed copy finds the newest release by these two names.

## 8. Troubleshooting

### Retake.exe is about 1 MB and plays nothing
- Symptom: the error banner reads `libmpv is not bundled in this build`.
- Cause: `build\libmpv\libmpv-2.dll` was missing when the exe was built.
- Fix: run `build\fetch-libmpv.ps1` (or `build\build.ps1`, which runs it) and build again.

### An update test leaves a window open
- Symptom: a Retake window from a scratch folder stays after the script ended with an error.
- Cause: the script stops its processes in its cleanup, but a failure before the cleanup leaves them.
- Fix: close the window; `-Keep` names the scratch folder, which can then be deleted.
