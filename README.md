<div align="center">

# Frame Notes: frame-accurate video review notes for editors

**Pause the video, press a key, type a line. Every note lands on the exact frame with an After Effects style timecode, in a plain text file next to the video. One portable exe for Windows, no project files, no account.**

[![Download](https://img.shields.io/badge/Download-FrameNotes.exe-5d8dff?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/softoblivion/frame-notes/releases/latest/download/FrameNotes.exe)
[![Release](https://img.shields.io/github/v/release/softoblivion/frame-notes?display_name=release&label=latest&style=for-the-badge)](https://github.com/softoblivion/frame-notes/releases/latest)

![Platform](https://img.shields.io/badge/platform-Windows_10_|_11-0078D6?logo=windows&logoColor=white)
![Python](https://img.shields.io/badge/Python-3.14-3776AB?logo=python&logoColor=white)
![JavaScript](https://img.shields.io/badge/JavaScript-ES_modules-F7DF1E?logo=javascript&logoColor=black)
![HTML5](https://img.shields.io/badge/HTML5-E34F26?logo=html5&logoColor=white)
![CSS](https://img.shields.io/badge/CSS-663399?logo=css&logoColor=white)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

</div>

---

## Contents

- [Why Frame Notes](#why-frame-notes)
- [Quick start](#quick-start)
- [Features](#features)
- [Keyboard](#keyboard)
- [The notes file](#the-notes-file)
- [Timecodes](#timecodes)
- [Supported video](#supported-video)
- [Data and privacy](#data-and-privacy)
- [Updates](#updates)
- [Building from source](#building-from-source)
- [Project structure](#project-structure)
- [Third-party components](#third-party-components)
- [License](#license)

## Why Frame Notes

- **Three keys, no mouse.** `1` fix, `2` add, `3` remove. The video pauses, the note takes the current frame, you
  type one line and press `Enter`.
- **Timecodes the editor can use.** `0:00:12:07` as in the After Effects timeline (drop-frame `0;00;12;07` for
  29.97 and 59.94), computed from the file's real frame rate.
- **One readable file, next to the video.** `promo_v3.mp4` gets `promo_v3.notes.txt`: a header and one line per
  note. Paste it into a chat, open it in Notepad, hand it to the editor. Open the video again and the notes are
  back on the timeline.
- **A mini timeline.** Every note is a coloured marker; click a marker to jump to its frame, step frame by frame
  with the arrow keys.
- **Portable.** One `FrameNotes.exe` with a data folder next to it. Move the folder, move the app.

## Quick start

1. Download [`FrameNotes.exe`](https://github.com/softoblivion/frame-notes/releases/latest/download/FrameNotes.exe)
   into a folder of its own, for example `D:\Apps\FrameNotes\`.
2. Run it and drop a video onto the window (or press `Ctrl+O`, or use "Open with" in Explorer).
3. Play, pause on a frame, press `1`, `2` or `3`, type the note, press `Enter`. The notes file is saved after every
   change.

**Requirements:** Windows 10 (1809+) or Windows 11 with the Microsoft Edge WebView2 Runtime (preinstalled on
Windows 11 and on up-to-date Windows 10). No installer, no admin rights.

> [!NOTE]
> The executable is not code-signed, so Windows SmartScreen may warn on first start (**More info → Run anyway**).

## Features

| | |
|---|---|
| **Three note types** | FIX (correct something), ADD (something is missing), REMOVE (take something out), bound to `1` / `2` / `3`. Each has its own marker colour on the timeline and its own tag in the file. |
| **Frame-accurate** | Seeks land on frame centres; the current frame and its timecode are shown next to the total duration. Arrow keys step one frame, `Shift` + arrows ten. |
| **Mini timeline** | Ruler with timecodes, playhead, one marker per note. Click to seek, hover for the text, wheel to zoom, `Shift` + wheel to pan. |
| **Notes list** | Every note as `timecode  TYPE  text`; select with `↑` / `↓` (the video follows), edit with `F2`, remove with `Delete`. |
| **Automatic save** | The notes file is rewritten after every change with an atomic replace. No Save button, nothing to lose when the window closes. |
| **Copy to clipboard** | `Ctrl+C` outside a text field copies the whole notes file as text. |
| **Frame rate from the file** | Read from the MP4 / MOV or WebM container; a header control shows it and lets you override it when a file reports a wrong rate. |
| **Self-updating app** | New releases are detected on launch, downloaded on request, verified by size and SHA-256 and installed on restart. |

## Keyboard

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `←` `→` | One frame back / forward |
| `Shift+←` `Shift+→` | Ten frames |
| `Home` `End` | First / last frame |
| `1` `2` `3` | Add a FIX / ADD / REMOVE note at the current frame (pauses first) |
| `Enter` | Save the note text |
| `Esc` | Cancel: a new note is removed, an edit is reverted |
| `F2` | Edit the selected note |
| `Delete` | Remove the selected note |
| `↑` `↓` | Previous / next note (seeks to it) |
| `Ctrl+C` | Copy the notes file text (when no text field is focused) |
| `Ctrl+O` | Open a video |
| `0` | Reset the timeline zoom |

## The notes file

`promo_v3.mp4` → `promo_v3.notes.txt` in the same folder. UTF-8, tab-separated:

```
# Frame Notes 1
# source: promo_v3.mp4
# fps: 25
# duration: 0:01:32:10
0:00:12:07	FIX	logo flickers for two frames
0:00:40:00	ADD	subtitle for the second speaker
0:01:02:15	REMOVE	second take of the handshake
```

Lines are sorted by frame. The `source` line ties the file to its video; lines the app does not understand are
kept where they are, so hand edits survive. Removing every note leaves the header, so the editor can see that the
review happened and found nothing.

## Timecodes

Timecodes follow the After Effects timeline display: `h:mm:ss:ff`, frames counted from zero, two digits (three at
100 fps and above). Integer rates and 23.976 use non-drop-frame timecode with `:`; 29.97 and 59.94 use drop-frame
with `;` (`0;00;12;07`), the same as After Effects applies to NTSC footage by default. Typing the timecode into the
AE or Premiere time field lands on the frame the note was made on.

## Supported video

Playback uses the WebView2 engine (Microsoft Edge): MP4 with H.264 or AV1 video and AAC audio, WebM with VP8 /
VP9 / AV1. HEVC plays only when the Windows HEVC Video Extension is installed. ProRes, DNxHD and other
mezzanine codecs do not play; export an H.264 review copy from the editor. A built-in proxy conversion is
planned.

## Data and privacy

The notes file is the only thing written next to your videos. Everything else lives in `frame-notes_data\` beside
`FrameNotes.exe` (override with the `FRAME_NOTES_HOME` environment variable); nothing is written to the user
profile:

| Path | Content |
|---|---|
| `settings.json` | window placement, recent files with their last frame and frame-rate override, update settings |
| `logs\` | `app.log` |
| `updates\` | downloaded app updates and `apply.log` |

Network traffic goes to GitHub only (release check and download). Videos never leave the machine.

## Updates

The app checks GitHub Releases on every launch in the background. A new version is downloaded on **Download**
(or automatically when *Download updates automatically* is on), verified by byte size and SHA-256 against the
release's `SHA256SUMS`, and installed by **Restart**: the running `FrameNotes.exe` is kept as `FrameNotes.exe.old`
until the new one has started. If anything fails the current exe stays untouched. Every release ships two assets:
`FrameNotes.exe` and `SHA256SUMS`. Release notes live in [`docs/releases/`](docs/releases/).

## Building from source

Requirements: Windows, Python 3.14 (via the `py` launcher), PowerShell 5.1+.

```powershell
git clone https://github.com/softoblivion/frame-notes.git
cd frame-notes
powershell -ExecutionPolicy Bypass -File build\build.ps1
```

The script creates `.venv`, installs the dependencies, runs the unit tests and produces `release\FrameNotes.exe`
(one-file PyInstaller build) with `release\SHA256SUMS`.

| Task | Command |
|---|---|
| Run from source | `scripts\run_dev.bat [video.mp4]` (data in `frame-notes_data\` under the project root) |
| Unit tests | `set FRAME_NOTES_HOME=%TEMP%\fn_tests` then `.venv\Scripts\python.exe -m unittest discover -s tests` |
| UI in a browser with mock data | `python -m http.server 5173 --directory src\frame_notes\ui`, then `http://localhost:5173/?mock=1` |

Architecture and module contracts: [ARCHITECTURE](docs/ARCHITECTURE.md) · [BUILD](docs/BUILD.md)

## Project structure

```
frame-notes/
├── src/frame_notes/
│   ├── core/          stdlib only: timecode, notes file, container probe, session, settings, paths
│   ├── shell/         entry point, window, JS ↔ Python bridge, Windows helpers
│   └── ui/            HTML / CSS / JS interface: player, timeline, notes list
├── tests/             unittest suite
├── build/             PyInstaller spec, build script, icon
├── scripts/           development run
└── docs/              architecture, module docs, build notes, release notes, decisions
```

## Third-party components

Bundled into the executable:

| Component | License |
|---|---|
| [CPython](https://www.python.org/) | PSF-2.0 |
| [pywebview](https://github.com/r0x0r/pywebview) | BSD-3-Clause |
| [pythonnet](https://github.com/pythonnet/pythonnet), clr_loader | MIT |
| bottle, proxy_tools | MIT |
| [PyInstaller](https://pyinstaller.org/) bootloader | GPL-2.0-or-later with the bootloader exception |

## License

Released under the [GNU General Public License v3.0](LICENSE). Any distributed copy or derivative work must stay
open source under the same license and keep the copyright notices.
