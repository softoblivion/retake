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
3. Press `1` (FIX: correct something), `2` (ADD: something is missing), `3` (REMOVE: take something out), `5`
   (LIKE: something that works) or `6` (DISLIKE: something that does not), or click the same button in the row
   under the video. The video pauses first and the note takes the current frame. `4` writes a general note about
   the whole video instead ("the music is too loud overall").
4. Optionally drag a rectangle around the element on the video, or click to put a point.
5. Type the note and press `Enter`. A note needs text: with an empty field `Enter` keeps the editor open and asks for
   it. `Esc` cancels: a new note is removed, an edit is reverted.
6. When the review is finished, press **Done**: the notes and their frame images are packed into one zip, ready to
   send.

The notes are saved after every change; there is no Save button and nothing to lose when the window closes.
Several windows can be open at once, one review each. A right click on a note, the video, the timeline or the notes
list shows what can be done there, with the key of each action on its right.

### Marking the element

While a note is being written, drag a rectangle on the video or click to put a point; a new drag replaces it, and
`Backspace` in the empty text field removes it. The frame is saved as a PNG at the video's full size with the mark
drawn on it into `<name>_notes\frames\` next to the video, and the mark reappears over the video whenever the note
is selected.

### Note types

| Type | Key | For |
|---|---|---|
| FIX | `1` | something is wrong and must be corrected |
| ADD | `2` | something is missing |
| REMOVE | `3` | something must be taken out |
| LIKE | `5` | something works and should stay |
| DISLIKE | `6` | something does not work, without a fix in mind yet |

The editor shows where the note belongs (its timecode, or General) and the five types, each with its icon; the type of
the note is lit. A click on another type, or `Ctrl+1` to `Ctrl+6`, changes it, also when editing an older note; the
mark and the saved frame image take the colour of the new type.

### General notes

A general note is about the whole video, not a frame: `4`, the **General** button in the row under the video, or
**New general note** in a right-click menu. It has no mark and no marker on the timeline; the notes list shows it
first, labelled General. In the editor, a click on General or on the timecode (or `Ctrl+4`) moves a note between the
whole video and its frame; a note that becomes general loses its mark.

### Frame-exact playback

Every seek and step lands on an exact frame, whatever the codec: the arrow keys step one frame, `Shift` + arrows
ten, `Home` / `End` go to the first and last frame. `J` steps back while held, `K` pauses, `L` plays and, pressed
again, plays faster (2x, 4x, 8x). The row under the video holds the frame steps, play, the speed, the current
timecode, the duration and the frame number, and the six note buttons with their keys.

### Mini timeline

A ruler with timecodes, the playhead and one marker per note with the first words of its text, coloured by type.
Resting on a marker shows the whole note and its frame image; a click selects the note and moves to its frame, a
double click edits it. **Drag a marker** to move its note to another frame: the video follows while you drag, and a
note with a mark gets a new frame image at the frame where it lands; `Esc` cancels the drag. The mouse wheel zooms,
`Shift` + wheel pans, `0` shows the whole video again; a click or a drag in the ruler moves the playhead.

### Notes list

General notes first, then every frame note with the icon of its type, its timecode, type, text and a thumbnail of
its frame image, in timecode order; the heading counts the notes of each type. `↑` / `↓` select (the video follows), `F2` or a double click edits, `Delete` removes, a right click
offers Edit, Copy the timecode, Copy the text, Move to the playhead and Delete. The list button in the title bar
hides and shows it.

### Window

The window can be resized from its edges and maximized with the button beside minimize (or a double click on the
title bar, or Windows Snap); the video gets the extra room, the notes and the timeline stay. The size, the place
and whether it was maximized are remembered for the next start.

### Frame rate

The frame rate is read from the file and shown in the title bar (`25 fps`). When the file does not say, or the
player reads a different rate, the control turns amber and offers the rate the player found. Its menu sets any
other rate for this video (the timecodes of its notes follow); the choice is remembered per video. Notes need a
known frame rate.

### Copying and sending the notes

The current timecode, the duration, the frame number and the timecode of every note copy themselves on a click
(`0:01:12:05`), ready for the time field of After Effects or Premiere. The **Copy** menu in the title bar copies the
notes as **Text** (`Ctrl+Shift+C`) or the data file text (`Ctrl+C`). The text has no formatting, so it reads the
same in Telegram, WhatsApp or an e-mail:

```
promo_v3.mp4

General
FIX — music is too loud overall

0:00:12:07 FIX — logo flickers for two frames
0:00:40:00 ADD — subtitle for the second speaker
```

**Done** (the button in the title bar, or `Ctrl+E`) packs the same text as `notes.txt` and every frame image into
`<name>_notes\<name>_notes.zip`; the message that confirms it has a **Show** button that selects the zip in Explorer,
ready to drag into a chat or an e-mail. Pressing Done again replaces the zip with the current notes.

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
| `4` | Start a general note, about the whole video |
| `5` `6` | Start a LIKE / DISLIKE note at the current frame |
| `Ctrl+1` … `Ctrl+6` | Change the type of the note being written; `Ctrl+4` moves it between its frame and the whole video |
| `Enter` | Save the note (it needs text) |
| `Esc` | Cancel the note; close a menu or a sheet; cancel a marker drag |
| `Backspace` (empty note field) | Remove the region of the note being written |
| `F2` | Edit the selected note |
| `Delete` | Remove the selected note |
| `↑` `↓` | Previous / next note (moves to it) |
| `Ctrl+Shift+C` | Copy the notes as text |
| `Ctrl+C` | Copy the notes file text |
| `Ctrl+E` | Done: pack the notes and frames into a zip |
| `Ctrl+O` | Open a video |
| `0` | Show the whole video in the timeline |
| `?` | Show the keys |
| `Ctrl+,` | Settings |

## Settings

The gear in the title bar (or `Ctrl+,`) opens Settings. Every change applies at once and is saved.

| Setting | Meaning |
|---|---|
| **Theme** | Crystal Black (dark glass on a near-black scene, the default) or Crystal Sky (light glass on a pale blue scene). |
| **Scale** | The size of the text and controls: 100, 125, 150, 175 or 200 %, on top of the scaling Windows applies. On a large monitor at Windows' 100 % (2560 x 1440, for example), 125 or 150 % reads comfortably. A step whose smallest window does not fit the screen is unavailable. |
| **Download updates automatically** | On (default): a new version downloads in the background and waits for a restart. Off: it waits until you install it. |
| **Author name** | Your name, kept in the settings. This version does not write it into the notes file yet. |

The window opens at 1280 x 800 at 100 % Scale (smaller on a small screen), can be resized down to 960 x 600 at that
Scale or maximized, and remembers its place, size and maximized state. Scale makes text and controls larger; a larger
window shows more room for the video, not larger controls.

## The notes file

Everything of one review sits in one folder next to the video:

```
promo_v3.mp4
promo_v3_notes\
    promo_v3_notes.txt          the notes (the app reads this file back)
    promo_v3_notes.zip          the package made by Done, if you made one
    frames\0-00-12-07_FIX.png   one image per note that has a region
```

**`promo_v3_notes.txt`** is the data file: UTF-8, tab-separated, one line per note.

```
# Frame Notes 1
# source: promo_v3.mp4
# fps: 25
# duration: 0:01:32:10
-	FIX	music is too loud overall
0:00:12:07	FIX	logo flickers for two frames	rect 0.540,0.360 0.880,0.800	0-00-12-07_FIX.png
0:00:40:00	ADD	subtitle for the second speaker
0:01:02:15	REMOVE	second take of the handshake	point 0.512,0.300	0-01-02-15_REMOVE.png
```

The first line names the file format (it is the same format as in Frame Notes 1.x, so both read each other's
folders). The first three columns are what the editor reads: timecode (`-` for a general note), type, text. The
optional fourth column is the region in fractions of the frame, the fifth the image file name inside `frames\`. General
notes come first, then the frame notes sorted by frame. The types are `FIX`, `ADD`, `REMOVE`, `LIKE` and `DISLIKE`;
Frame Notes 1.x knows only the first three and keeps a LIKE or DISLIKE line as it is. The `source` line ties the file to its video; lines the app
does not understand are kept, so hand edits survive.

The file is rewritten with an atomic replace. Removing a note deletes its image; removing every note keeps the folder
with a header-only file, so the editor can see that the review happened and found nothing. A video you only watched
gets no folder. A folder in the older 1.0 layout (`promo_v3_notes.txt` beside the video) is moved into this layout the
first time the video is opened. The readable `promo_v3_notes.md` that earlier versions wrote is no longer made and is
removed from the folder; the readable text now comes from the Copy menu and from Done.

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

A click on the version label in the title bar opens a small panel: the installed version, whether it is the latest,
when Retake last checked, and a **Check for updates** button. When a newer version is found, the label lights up and
a sheet opens with the new version, its size and date and its release notes, and the buttons **Install** and
**Later**. With **Download updates automatically** on, the download has usually finished by then and the button
reads **Restart to update**. **Later** (or `Esc`) closes the sheet; the version label opens it again. Every download
is verified by size and SHA-256 against the release's `SHA256SUMS` before it can be installed.

Installing restarts the app: the new exe waits for the old one to exit, replaces it in place, starts, and shows
`Updated to vX.Y.Z`. If anything fails the current exe stays untouched. Each release has two files, `Retake.exe` and
`SHA256SUMS`. A copy of Frame Notes 1.4.1 updates itself into Retake the same way and keeps its file name,
`FrameNotes.exe`, its data folder and its notes folders.

Release notes: [`releases/`](releases/).

## FAQ

**Does Retake change my video?** No. It only reads the video; the notes go into the notes folder beside it.

**Can I edit the notes file by hand?** Yes. Keep the tab-separated columns; lines Retake does not understand are
kept as they are.

**How do I send the notes to someone?** Press **Done** and send the zip, or paste the text from **Copy → Text for a
chat** into a message.

**Why does the first start take a moment?** The video engine is unpacked from the exe into `retake_data\bin\` once,
and again after an update that brings a new engine.
