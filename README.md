<div align="center">

<img src="docs/assets/hero.svg" alt="Frame Notes" width="100%">

[![Download](https://img.shields.io/badge/Download-FrameNotes.exe-5d8dff?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/softoblivion/frame-notes/releases/latest/download/FrameNotes.exe)
[![Release](https://img.shields.io/github/v/release/softoblivion/frame-notes?display_name=release&label=latest&style=for-the-badge)](https://github.com/softoblivion/frame-notes/releases/latest)

![Platform](https://img.shields.io/badge/platform-Windows_10_|_11-0078D6?logo=windows&logoColor=white)
![Python](https://img.shields.io/badge/Python-3.14-3776AB?logo=python&logoColor=white)
![JavaScript](https://img.shields.io/badge/JavaScript-ES_modules-F7DF1E?logo=javascript&logoColor=black)
![HTML5](https://img.shields.io/badge/HTML5-E34F26?logo=html5&logoColor=white)
![CSS](https://img.shields.io/badge/CSS-663399?logo=css&logoColor=white)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

**Frame-accurate video review notes for editors and reviewers on Windows. Pause the video, press 1, 2 or 3, type a line: every note lands on the exact frame with an After Effects style timecode, in a notes folder next to the MP4 or WebM render, with a marker on a mini timeline. One portable exe, no installer, no project files, no account; updates itself from GitHub Releases.**

<img src="docs/assets/screenshot.png" alt="Frame Notes main window" width="860">

<sub>Pause on a frame · press 1, 2 or 3 and mark the element · type the note.</sub>

</div>

---

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
- **Free, open source, portable.** GPL-3.0, one `FrameNotes.exe` with a data folder next to it; move the folder,
  move the app. No account, no telemetry; videos and notes never leave the machine.

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
| **Three note types** | FIX (correct something), ADD (something is missing), REMOVE (take something out), bound to `1` / `2` / `3`, each with its own marker colour and its own tag in the file. |
| **Region on the frame** | Drag a rectangle on the video or click to put a point while a note is written; the frame is saved as a PNG with the mark drawn on it. |
| **Frame-accurate** | Seeks land on frame centres; arrow keys step one frame, `Shift` + arrows ten. The frame rate is read from the MP4 / MOV or WebM container and can be overridden in the header. |
| **Mini timeline** | Ruler with timecodes, playhead and one marker per note; hover shows the note and its frame image, click seeks, the wheel zooms. |
| **Notes list** | Every note as `timecode  TYPE  text` with a thumbnail; `↑` / `↓` select (the video follows), `F2` edits, `Delete` removes. |
| **Notes folder** | The data file and a readable Markdown version are rewritten after every change with an atomic replace. No Save button. |
| **Copy for the editor** | A click on any timecode copies it for the time field of After Effects or Premiere; `Ctrl+C` copies the notes, `Ctrl+Shift+C` the readable text for a chat. |
| **Recent videos** | The start screen and the **Recent** menu list recent videos with their note counts and open them at the last frame. |
| **Self-updating** | New releases are found on launch and then every 5 minutes, downloaded in the background (or on request), verified by size and SHA-256 and installed on **Restart to update**. |

## Keyboard

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `←` `→` / `Shift+←` `Shift+→` | One frame / ten frames back or forward |
| `1` `2` `3` | Start a FIX / ADD / REMOVE note at the current frame |
| `Enter` / `Esc` | Save the note / cancel it |
| `↑` `↓` | Previous / next note (seeks to it) |
| `F2` / `Delete` | Edit / remove the selected note |
| `Ctrl+O` | Open a video |
| `?` | Show the key map |

The full key map is in the [user guide](docs/USER_GUIDE.md#keyboard).

## Documentation

| Document | What it covers |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Usage, keyboard, settings, the notes file, timecodes, supported video, data folder, updates |
| [Architecture](docs/ARCHITECTURE.md) | Stack, layout, modules, behaviour, notes file format, timecode rules, data flow |
| [Build](docs/BUILD.md) | Running from source, tests, building the exe, releasing, troubleshooting |
| [Core](docs/CORE.md) · [Bridge API](docs/BRIDGE_API.md) · [UI](docs/UI.md) | Module contracts: core API, JS ↔ Python calls and events, interface |
| [Release notes](docs/releases/) | What changed in every version |
| [Third-party components](docs/THIRD_PARTY.md) | What is bundled into the exe, with licences |

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

## Disclaimer

After Effects and Premiere Pro are trademarks of Adobe. Frame Notes is an independent tool, not affiliated with
Adobe; it only writes timecodes in the form those applications display.

## License

Released under the [GNU General Public License v3.0](LICENSE). Any distributed copy or derivative work must stay
open source under the same license and keep the copyright notices.
