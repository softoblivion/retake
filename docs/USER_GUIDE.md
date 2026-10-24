# Frame Notes user guide

Everything about using the app. Installation and the first steps are in the [README](../README.md).

## Usage

1. Open a video: drop it onto the window, press `Ctrl+O`, use "Open with" in Explorer, or pick it on the start
   screen or in the **Recent** menu.
2. Play, step frame by frame with the arrow keys, pause on the frame that needs a change.
3. Press `1` (FIX: correct something), `2` (ADD: something is missing) or `3` (REMOVE: take something out). The
   video pauses first and the note takes the current frame.
4. Optionally drag a rectangle around the element on the video, or click to put a point.
5. Type one line and press `Enter`. `Esc` cancels: a new note is removed, an edit is reverted.

The notes are saved after every change; there is no Save button and nothing to lose when the window closes.
Several windows can be open at once, one review each.

### Region on the frame

While a note is being written, drag a rectangle on the video or click to put a point. The frame is saved as a PNG
with the mark drawn on it into `<name>_notes\frames\` next to the video, and the mark reappears over the video
whenever the note is selected.

### Frame-accurate playback

Seeks land on frame centres; the current frame and its timecode are shown next to the total duration. Arrow keys
step one frame, `Shift` + arrows ten.

### Mini timeline

A ruler with timecodes, the playhead and one marker per note with the first words of its text. Every note type has
its own marker colour. Hover shows the full note and its frame image; a click on a marker seeks to its frame; the
wheel zooms, `Shift` + wheel pans, `0` resets the zoom.

### Notes list

Every note as `timecode  TYPE  text` with a thumbnail. Select with `↑` / `↓` (the video follows), edit with `F2`,
remove with `Delete`.

### Copying timecodes and notes

The current timecode, the duration and the timecode of every note are buttons: a click puts the text on the
clipboard (`0:01:12:05`), ready for the time field of After Effects or Premiere. `Enter` or `Space` does the same
on a focused timecode.

**Copy** in the header copies the data file text (`Ctrl+C` outside a text field); its arrow menu
(**Copy ▾ → Readable text**, `Ctrl+Shift+C`) copies the readable version without the image lines, ready to paste
into a chat.

### Recent videos

The start screen and the **Recent** menu list recent videos with their note counts. Right click an entry to open
it, show its notes folder in Explorer, delete its notes (with a confirmation) or take it off the list.

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

## Settings

| Setting | Where | Meaning |
|---|---|---|
| **Frame rate** | Header control | Shows the frame rate read from the MP4 / MOV or WebM container and overrides it when a file reports a wrong rate. The override is remembered per video. |
| **Download updates automatically** | Version popover | On (default): a new release is downloaded in the background; off: on **Download**. |

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
in Notepad, in a chat and in any Markdown viewer, where the frame images show right under their notes. It lists
every note as `timecode · TYPE · text` with its frame image, under a line with the frame rate, the duration and
the count per type:

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
watched gets no folder.

**Notes from 1.0.** Version 1.0 wrote `promo_v3_notes.txt` beside the video and the images directly into
`promo_v3_notes\`. The first time 1.1 opens such a video it moves both into the layout above (images first, then
the text file) and writes the readable version; nothing in the notes changes.

## Timecodes

Timecodes follow the After Effects timeline display: `h:mm:ss:ff`, frames counted from zero, two digits (three at
100 fps and above). Integer rates and 23.976 use non-drop-frame timecode with `:`; 29.97 and 59.94 use drop-frame
with `;` (`0;00;12;07`), the same as After Effects applies to NTSC footage by default. Typing the timecode into the
AE or Premiere time field lands on the frame the note was made on. Click any timecode in the app to copy it, so
there is nothing to type.

## Supported video

Playback uses the WebView2 engine (Microsoft Edge): MP4 with H.264 or AV1 video and AAC audio, WebM with VP8 /
VP9 / AV1. HEVC plays only when the Windows HEVC Video Extension is installed. ProRes, DNxHD and other
mezzanine codecs do not play; export an H.264 review copy from the editor. A built-in proxy conversion is
planned.

## Data and privacy

The notes folder (`<name>_notes\`) is the only thing written next to your videos. Everything else lives in
`frame-notes_data\` beside `FrameNotes.exe` (override with the `FRAME_NOTES_HOME` environment variable); nothing
is written to the user profile:

| Path | Content |
|---|---|
| `settings.json` | window position, recent files with their last frame and frame-rate override, panel state, update settings |
| `logs\` | `app.log` |
| `updates\` | downloaded app updates and `apply.log` |

Network traffic goes to GitHub only (release check and download). Videos and notes never leave the machine.

## Updates

The app checks GitHub Releases on every launch in the background and then every 5 minutes while it runs. A failed
check is retried later with a growing wait, up to an hour, and nothing is checked while there is no network
connection.

A new version is downloaded automatically (*Download updates automatically* in the version popover, on by
default) or on **Download**, and verified by byte size and SHA-256 against the release's `SHA256SUMS`. When a
newer version is found, a window opens with its release notes and the buttons **Install** and **Later**; **Later**
closes it, and the version label at the right of the header opens it again.

A downloaded version is installed only by **Restart to update**: the downloaded exe waits for the app to exit,
keeps the old file as `FrameNotes.exe.old` until the new one has started, and relaunches it. If anything fails the
current exe stays untouched. The next version of this app is **Retake** (2.0.0, a new build in the same place): this
version finds it as the asset `Retake.exe` next to `SHA256SUMS` and installs it the same way, keeping the file name
and the data folder beside it. Release notes:
[`releases/`](releases/).
