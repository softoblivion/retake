# Round-trip fixtures

Reference files written by the core of the 1.x line (release 1.4.0). `RoundTripTests.cs` runs the operations of
each `scenarios/<name>/ops.json` through the C# core on a copy of `in/` and requires the result to equal
`expected/` byte for byte. `settings/settings.json` is a settings file of the same core.

The files are stored as they are. Their generator is not part of this repository: it runs the operations through
the 1.x Python core, so it needs the source tree of tag `v1.4.1`.

The 1.x core also wrote a Markdown export (`<stem>_notes.md`) next to the data file. Retake does not write it and
deletes a stale one: the `in/` folders of the `r_resave*` scenarios keep the 1.x export as input, and no `expected/`
folder contains one. The `a_session` scenario differs from the recorded 1.x run in one operation: the note without text
got a text, because Retake refuses an empty text on add; the `r_resave` scenarios still carry a recorded note with an
empty text.

Scenarios:

| Folder | Covers |
|---|---|
| `a_session` | real MP4 at 25 fps, regions and images, Unicode text, rounding of region values, frame rate changes with image renames, update and remove |
| `b_session_unknown_rate` | video of unknown rate, rate chosen by hand, drop-frame timecodes |
| `e_session_120fps` | three-digit frame numbers |
| `c_layout10` | 1.0 layout moved into the notes folder |
| `d_layout10_extra_lines` | 1.0 layout with BOM, CRLF, a blank header line, foreign lines and an unrelated file |
| `r_resave*` | a folder written by the 1.x core, opened and saved without changes; the stale export is removed |

Files in this folder are byte-exact: `.gitattributes` turns line-ending conversion off.
