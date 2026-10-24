# Round-trip fixtures

Reference files written by the Frame Notes 1.4.0 core. `RoundTripTests.cs` runs the operations of each
`scenarios/<name>/ops.json` through the C# core on a copy of `in/` and requires the result to equal `expected/`
byte for byte. `settings/settings.json` is a settings file of the same core.

`generate.py` regenerates every file from a checkout that still holds the Python core (`src/frame_notes`):

```
py -3.14 -I tests/Retake.Tests/fixtures/generate.py
```

Scenarios:

| Folder | Covers |
|---|---|
| `a_session` | real MP4 at 25 fps, regions and images, Unicode text, rounding of region values, frame rate changes with image renames, update and remove |
| `b_session_unknown_rate` | video of unknown rate, rate chosen by hand, drop-frame timecodes |
| `e_session_120fps` | three-digit frame numbers |
| `c_layout10` | 1.0 layout moved into the notes folder, export written |
| `d_layout10_extra_lines` | 1.0 layout with BOM, CRLF, a blank header line, foreign lines and an unrelated file |
| `r_resave*` | a folder written by the core, opened and saved without changes |

Files in this folder are byte-exact: `.gitattributes` turns line-ending conversion off.
