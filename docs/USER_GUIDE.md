# Retake user guide

Everything about using Retake. The short version is in the [README](../README.md).

## Install

Retake is one file, `Retake.exe`, with no installer. Put it into a folder of its own (for example `D:\Apps\Retake\`)
and run it. On first start it creates `retake_data\` beside itself for its settings, log and updates (see
[Data and privacy](#data-and-privacy)); moving the folder moves the whole app. The exe is not code-signed, so Windows
SmartScreen may warn on the first start: **More info → Run anyway**.

Requirements: Windows 10 or 11, 64-bit. Nothing else is installed: the video engine is inside the exe.

## Usage

1. Open a video: drop it onto the window, press `Ctrl+O`, use "Open with" in Explorer, or pick it from the recent
   list on the start screen or in the **Recent** menu of the title bar.
2. Play, step frame by frame with the arrow keys, pause on the frame that needs a change.
3. Press `1` (FIX: correct something), `2` (ADD: something is missing) or `3` (REMOVE: take something out). The
   video pauses first and the note takes the current frame.
4. Optionally drag a rectangle around the element on the video, or click to put a point.
5. Type the note and press `Enter`. `Esc` cancels: a new note is removed, an edit is reverted.

The notes are saved after every change; there is no Save button and nothing to lose when the window closes.
Several windows can be open at once, one review each.

### Marking the element

While a note is being written, drag a rectangle on the video or click to put a point; a new drag replaces it, and
`Backspace` in the empty text field removes it. The frame is saved as a PNG at the video's full size with the mark
drawn on it into `<name>_notes\frames\` next to the video, and the mark reappears over the video whenever the note
is selected.

### Frame-exact playback

Every seek and step lands on an exact frame, whatever the codec: the arrow keys step one frame, `Shift` + arrows
ten, `Home` / `End` go to the first and last frame. `J` steps back while held, `K` pauses, `L` plays and, pressed
again, plays faster (2x, 4x, 8x). The current timecode, the duration and the frame number are shown under the
video.

### Mini timeline

A ruler with timecodes, the playhead and one marker per note with the first words of its text, coloured by type.
Resting on a marker shows the whole note and its frame image; a click selects the note and moves to its frame, a
double click edits it. The mouse wheel zooms, `Shift` + wheel pans, `0` shows the whole video again; a click or a
drag in the ruler moves the playhead.

### Notes list

Every note with its timecode, type, text and a thumbnail of its frame image, in timecode order. `↑` / `↓` select
(the video follows), `F2` or a double click edits, `Delete` removes. The list button in the title bar hides and
shows it.

### Frame rate

The frame rate is read from the file and shown in the title bar (`25 fps`). When the file does not say, or the
player reads a different rate, the control turns amber and offers the rate the player found. Its menu sets any
other rate for this video (the timecodes of its notes follow); the choice is remembered per video. Notes need a
known frame rate.

### Copying timecodes and notes

The current timecode, the duration, the frame number and the timecode of every note copy themselves on a click
(`0:01:12:05`), ready for the time field of After Effects or Premiere. The **Copy** menu in the title bar copies the
data file text (`Ctrl+C`) or the readable text without the image lines, ready to paste into a chat
(`Ctrl+Shift+C`).

### Recent videos

The start screen and the **Recent** menu list recent videos with their last position and note counts; a click
opens a video at the frame it was left on. Right click an entry to open it, show its notes folder in Explorer,
delete its notes (asks once more first) or take it off the list.

## Keyboard

The keys work when no text field has focus; `?` shows them in the app.

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `←` `→` | One frame back / forward |
| `Shift+←` `Shift+→` | Ten frames |
| `Home` `End` | First / last frame |
| `J` `K` `L` | Step back while held / pause / play, again for 2x, 4x, 8x |
| `1` `2` `3` | Start a FIX / ADD / REMOVE note at the current frame (pauses first); then drag on the video to mark a region, or click to put a point |
| `Enter` | Save the note |
| `Esc` | Cancel the note; close a menu or a sheet |
| `Backspace` (empty note field) | Remove the region of the note being written |
| `F2` | Edit the selected note |
| `Delete` | Remove the selected note |
| `↑` `↓` | Previous / next note (moves to it) |
| `Ctrl+C` | Copy the notes file text |
| `Ctrl+Shift+C` | Copy the readable text |
| `Ctrl+O` | Open a video |
| `0` | Show the whole video in the timeline |
| `?` | Show the keys |
| `Ctrl+,` | Settings |

## Settings

The gear in the title bar (or `Ctrl+,`) opens Settings. Every change applies at once and is saved.

| Setting | Meaning |
|---|---|
| **Theme** | Crystal Black (dark glass on a near-black scene, the default) or Crystal Sky (light glass on a pale blue scene). |
| **Scale** | The size of the whole window and its text: 100, 125, 150, 175 or 200 %, on top of the scaling Windows applies. A step too large for the screen is unavailable. |
| **Download updates automatically** | On (default): a new version downloads in the background and waits for a restart. Off: it waits until you install it. |
| **Author name** | Your name, kept in the settings. This version does not write it into the notes file yet. |

The window has one size (1280 x 800 at 100 % Scale, smaller on a small screen) and remembers its position.

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

The first line names the file format (it is the same format as in Frame Notes 1.x, so both read each other's
folders). The first three columns are what the editor reads: timecode, type, text. The optional fourth column is
the region in fractions of the frame, the fifth the image file name inside `frames\`. Lines are sorted by frame.
The `source` line ties the file to its video; lines the app does not understand are kept, so hand edits survive.

**`promo_v3_notes.md`** is the readable version, rewritten after every change. Plain Markdown, so it reads well in
Notepad, in a chat and in any Markdown viewer, where the frame images show right under their notes:

```
# promo_v3.mp4

25 fps · 0:01:32:10 · FIX 1 · ADD 1 · REMOVE 1

- **0:00:12:07** · FIX · logo flickers for two frames
  ![](frames/0-00-12-07_FIX.png)
- **0:00:40:00** · ADD · subtitle for the second speaker
- **0:01:02:15** · REMOVE · second take of the handshake
  ![](frames/0-01-02-15_REMOVE.png)
```

Both files are rewritten with an atomic replace. Removing a note deletes its image; removing every note keeps the
folder with a header-only file, so the editor can see that the review happened and found nothing. A video you only
watched gets no folder. A folder in the older 1.0 layout (`promo_v3_notes.txt` beside the video) is moved into this
layout the first time the video is opened.

## Timecodes

Timecodes follow the After Effects timeline display: `h:mm:ss:ff`, frames counted from zero, two digits (three at
100 fps and above). Integer rates and 23.976 use non-drop-frame timecode with `:`; 29.97 and 59.94 use drop-frame
with `;` (`0;00;12;07`), the same as After Effects applies to NTSC footage by default. Typing the timecode into the
AE or Premiere time field lands on the frame the note was made on.

## Supported video

Retake plays the formats editors render and deliver: H.264, HEVC (8 and 10 bit), ProRes, DNxHD / DNxHR, VP9, AV1
and the other common codecs, in MP4, MOV, MKV and WebM, with no codec packs or system extensions. Seeking and
stepping are frame-exact; this is measured on H.264, HEVC, ProRes, DNxHD, VP9 and AV1 at 23.976 to 60 fps.

Limits: cinema RAW formats (R3D, BRAW, ARRIRAW, ProRes RAW) and image sequences do not open; HDR video is not
tested, and its saved frame images are not tone-mapped. Stepping backwards through long-GOP files (H.264, HEVC, AV1 with
keyframes seconds apart) costs a short decode per step, up to about a second for 4K on a fast machine; intra-frame
codecs (ProRes, DNxHD) step instantly.

## Data and privacy

The notes folder (`<name>_notes\`) is the only thing written next to your videos. Everything else lives in
`retake_data\` beside `Retake.exe` (the `RETAKE_HOME` environment variable moves it); nothing is written to the user
profile:

| Path | Content |
|---|---|
| `settings.json` | window position, recent videos with their last frame and frame-rate override, panel state, theme, scale, author name, update setting |
| `logs\` | `app.log` and up to three older logs |
| `updates\` | downloaded app updates and `apply.log` |
| `bin\` | the video engine (`libmpv-2.dll`), unpacked from the exe on first start |

Network traffic goes to GitHub only (release check and download). Videos and notes never leave the machine.

## Updates

Retake checks GitHub Releases on every launch in the background and then every 5 minutes while it runs. A failed
check is retried later with a growing wait, up to an hour, and nothing is checked while there is no network
connection.

When a newer version is found, the version label in the title bar lights up and a sheet opens with the new
version, its size and date and its release notes, and the buttons **Install** and **Later**. With **Download
updates automatically** on, the download has usually finished by then and the button reads **Restart to update**.
**Later** (or `Esc`) closes the sheet; the version label opens it again. Every download is verified by size and
SHA-256 against the release's `SHA256SUMS` before it can be installed.

Installing restarts the app: the new exe waits for the old one to exit, replaces it in place, starts, and shows
`Updated to vX.Y.Z`. If anything fails the current exe stays untouched. Each release has two files, `Retake.exe` and
`SHA256SUMS`. A copy of Frame Notes 1.4.1 updates itself into Retake the same way and keeps its file name, `FrameNotes.exe`, its data folder and its notes folders.

Release notes: [`releases/`](releases/).

## FAQ

**Does Retake change my video?** No. It only reads the video; the notes go into the notes folder beside it.

**Can I edit the notes file by hand?** Yes. Keep the tab-separated columns; lines Retake does not understand are
kept as they are.

**Why does the first start take a moment?** The video engine is unpacked from the exe into `retake_data\bin\` once,
and again after an update that brings a new engine.
