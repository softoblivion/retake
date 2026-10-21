<div align="center">

# Frame Notes: frame-accurate video review notes for editors

**Pause the video, press a key, type a line. Every note lands on the exact frame with an After Effects style timecode, in a notes folder next to the video. One portable exe for Windows, no project files, no account.**

[![Download](https://img.shields.io/badge/Download-FrameNotes.exe-5d8dff?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/softoblivion/frame-notes/releases/latest/download/FrameNotes.exe)
[![Release](https://img.shields.io/github/v/release/softoblivion/frame-notes?display_name=release&label=latest&style=for-the-badge)](https://github.com/softoblivion/frame-notes/releases/latest)

![Platform](https://img.shields.io/badge/platform-Windows_10_|_11-0078D6?logo=windows&logoColor=white)
![Python](https://img.shields.io/badge/Python-3.14-3776AB?logo=python&logoColor=white)
![JavaScript](https://img.shields.io/badge/JavaScript-ES_modules-F7DF1E?logo=javascript&logoColor=black)
![HTML5](https://img.shields.io/badge/HTML5-E34F26?logo=html5&logoColor=white)
![CSS](https://img.shields.io/badge/CSS-663399?logo=css&logoColor=white)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

<img src="docs/assets/screenshot.png" alt="Frame Notes main window" width="860">

<sub>Pause on a frame · press 1, 2 or 3 · drag a rectangle around the element · type the note. Saved in a folder next to the video.</sub>

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
- **Point at the thing.** After the key, drag a rectangle around the element on the frame (or click to put a
  point). The note gets a PNG of that frame with the mark drawn on it, so "this title" needs no description.
- **Timecodes the editor can use.** `0:00:12:07` as in the After Effects timeline (drop-frame `0;00;12;07` for
  29.97 and 59.94), computed from the file's real frame rate.
- **One folder next to the video.** `promo_v3.mp4` gets `promo_v3_notes\` with the notes as a plain text file,
  the same notes as a readable Markdown page with the frame images, and the images themselves. Paste the text into
  a chat, open the page in any Markdown viewer, hand the folder to the editor. Open the video again and the notes
  are back on the timeline.
- **A mini timeline.** Every note is a coloured marker; click a marker to jump to its frame, step frame by frame
  with the arrow keys.
- **Portable.** One `FrameNotes.exe` with a data folder next to it. Move the folder, move the app.

## Quick start

1. Download [`FrameNotes.exe`](https://github.com/softoblivion/frame-notes/releases/latest/download/FrameNotes.exe)
   into a folder of its own, for example `D:\Apps\FrameNotes\`.
2. Run it and drop a video onto the window (or press `Ctrl+O`, or use "Open with" in Explorer).
3. Play, pause on a frame, press `1`, `2` or `3`, drag a rectangle around the element if it helps, type the note,
   press `Enter`. The notes are saved after every change.

**Requirements:** Windows 10 (1809+) or Windows 11 with the Microsoft Edge WebView2 Runtime (preinstalled on
Windows 11 and on up-to-date Windows 10). No installer, no admin rights.

> [!NOTE]
> The executable is not code-signed, so Windows SmartScreen may warn on first start (**More info → Run anyway**).

## Features

| | |
|---|---|
| **Three note types** | FIX (correct something), ADD (something is missing), REMOVE (take something out), bound to `1` / `2` / `3`. Each has its own marker colour on the timeline and its own tag in the file. |
| **Region on the frame** | While a note is being written, drag a rectangle on the video or click to put a point. The frame is saved as a PNG with the mark drawn on it into `<name>_notes\frames\` next to the video, and the mark reappears over the video whenever the note is selected. |
| **Frame-accurate** | Seeks land on frame centres; the current frame and its timecode are shown next to the total duration. Arrow keys step one frame, `Shift` + arrows ten. |
| **Mini timeline** | Ruler with timecodes, playhead, one marker per note with the first words of its text. Hover shows the full note and its frame image; click seeks; wheel zooms, `Shift` + wheel pans. |
| **Notes list** | Every note as `timecode  TYPE  text` with a thumbnail; select with `↑` / `↓` (the video follows), edit with `F2`, remove with `Delete`. |
| **Automatic save** | The notes file and its readable version are rewritten after every change with an atomic replace. No Save button, nothing to lose when the window closes. |
| **Readable export** | `<name>_notes.md` lists every note as `timecode · TYPE · text` with its frame image, under a line with the frame rate, duration and the count per type. |
| **Copy to clipboard** | `Ctrl+C` outside a text field copies the notes file text; `Ctrl+Shift+C` (or **Copy ▾ → Readable text**) copies the readable version for a chat. |
| **Recent videos** | The start screen and the **Recent** menu list recent videos with their note counts. Right click an entry to open it, show its notes folder in Explorer, delete its notes (with a confirmation) or take it off the list. |
| **Frame rate from the file** | Read from the MP4 / MOV or WebM container; a header control shows it and lets you override it when a file reports a wrong rate. |
| **Portable** | One exe, one data folder next to it, no installer. Several windows can be open at once, one review each. |
| **Self-updating app** | New releases are detected on launch and then every 30 minutes (or hourly, every 6 hours, daily, weekly), downloaded in the background (or on request), verified by size and SHA-256 and installed on **Restart**. |

## Keyboard

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `←` `→` | One frame back / forward |
| `Shift+←` `Shift+→` | Ten frames |
| `Home` `End` | First / last frame |
| `1` `2` `3` | Start a FIX / ADD / REMOVE note at the current frame (pauses first); then drag on the video to mark a region, or click to put a point |
| `Enter` | Save the note |
| `Esc` | Cancel: a new note is removed, an edit is reverted |
| `F2` | Edit the selected note |
| `Delete` | Remove the selected note |
| `↑` `↓` | Previous / next note (seeks to it) |
| `Ctrl+C` | Copy the notes file text (when no text field is focused) |
| `Ctrl+Shift+C` | Copy the readable text |
| `Ctrl+O` | Open a video |
| `0` | Reset the timeline zoom |
| `?` | Show the key map |

## The notes file

Everything of one review sits in one folder next to the video:

```
promo_v3.mp4
promo_v3_notes\
    promo_v3_notes.txt          the notes (the app reads this file back)
    promo_v3_notes.md           the same notes, written for people
    frames\0-00-12-07_FIX.png   one image per note that has a region
```

**`promo_v3_notes.txt`** is the data file: UTF-8, tab-separated, one line per note.

```
# Frame Notes 1
# source: promo_v3.mp4
# fps: 25
# duration: 0:01:32:10
0:00:12:07	FIX	logo flickers for two frames	rect 0.540,0.360 0.880,0.800	0-00-12-07_FIX.png
0:00:40:00	ADD	subtitle for the second speaker
0:01:02:15	REMOVE	second take of the handshake	point 0.512,0.300	0-01-02-15_REMOVE.png
```

The first three columns are what the editor reads: timecode, type, text. The optional fourth column is the region
in fractions of the frame, the fifth the image file name inside `frames\`. Lines are sorted by frame. The `source`
line ties the file to its video; lines the app does not understand are kept, so hand edits survive.

**`promo_v3_notes.md`** is the readable version, rewritten after every change. Plain Markdown, so it reads well
in Notepad, in a chat and in any Markdown viewer, where the frame images show right under their notes:

```
# promo_v3.mp4

25 fps · 0:01:32:10 · FIX 1 · ADD 1 · REMOVE 1

- **0:00:12:07** · FIX · logo flickers for two frames
  ![](frames/0-00-12-07_FIX.png)
- **0:00:40:00** · ADD · subtitle for the second speaker
- **0:01:02:15** · REMOVE · second take of the handshake
  ![](frames/0-01-02-15_REMOVE.png)
```

**Copy** in the header copies the data file text (`Ctrl+C`); its arrow menu also offers the readable text
(`Ctrl+Shift+C`), without the image lines, ready to paste into a chat.

Removing a note deletes its image; removing every note keeps the folder with a header-only file, so the editor
can see that the review happened and found nothing. A video you only watched gets no folder.

**Notes from 1.0.** Version 1.0 wrote `promo_v3_notes.txt` beside the video and the images directly into
`promo_v3_notes\`. The first time 1.1 opens such a video it moves both into the layout above (images first, then
the text file) and writes the readable version; nothing in the notes changes.

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

The notes folder (`<name>_notes\`) is the only thing written next to your videos. Everything else lives in `frame-notes_data\` beside
`FrameNotes.exe` (override with the `FRAME_NOTES_HOME` environment variable); nothing is written to the user
profile:

| Path | Content |
|---|---|
| `settings.json` | window placement, recent files with their last frame and frame-rate override, panel state, update settings |
| `logs\` | `app.log` |
| `updates\` | downloaded app updates and `apply.log` |

Network traffic goes to GitHub only (release check and download). Videos and notes never leave the machine.

## Updates

The app checks GitHub Releases on every launch in the background and then on a timer: every 30 minutes by
default, or every hour, 6 hours, day or week (*Check for updates* in the version popover). A new version is downloaded automatically
(*Download updates automatically*, on by default) or on **Download**, verified by byte size and SHA-256 against
the release's `SHA256SUMS`, and installed by **Restart to update**: the downloaded exe waits for the app to exit,
keeps the old file as `FrameNotes.exe.old` until the new one has started, and relaunches it. If anything fails
the current exe stays untouched. Every release ships two assets: `FrameNotes.exe` and `SHA256SUMS`. Release notes
live in [`docs/releases/`](docs/releases/).

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
