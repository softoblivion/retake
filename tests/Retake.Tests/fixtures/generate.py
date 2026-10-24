"""Regenerates the reference fixtures of the round-trip tests with the 1.x Python core.

Usage (from the repository root of a checkout that still holds `src/frame_notes`):

    py -3.14 -I tests/Retake.Tests/fixtures/generate.py

Every scenario is a folder `scenarios/<name>/` with
  * `ops.json`  - the video and the operations both cores run (see `apply`),
  * `in/`       - the folder next to the video before the operations,
  * `expected/` - the same folder after the operations, as written by the 1.x core.
`settings/settings.json` is a settings file written by the 1.x core.
"""

from __future__ import annotations

import json
import logging
import os
import shutil
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "src"))
sys.path.insert(0, str(ROOT / "tests"))

OUT = Path(__file__).resolve().parent

from frame_notes.core import session as ss  # noqa: E402
from frame_notes.core.settings import Settings  # noqa: E402
from test_core_probe import build_mp4  # noqa: E402

REAL = build_mp4([(2310, 512)], 12800, movie_duration=92400)
JUNK = b"not a real video, the probe sees unknown\n" * 4
RECT = {"shape": "rect", "x0": 0.54, "y0": 0.36, "x1": 0.88, "y1": 0.8}
RECT2 = {"shape": "rect", "x0": 0.9, "y0": 0.9, "x1": 0.1, "y1": 0.2}
ROUND = {"shape": "rect", "x0": 0.2345, "y0": 0.0005, "x1": 0.9995, "y1": 0.12349999999999}
POINT = {"shape": "point", "x0": 0.5, "y0": 0.25}
UNICODE_TEXT = "h\u00e9llo \u00fcn\u00efcode \u2014 text \u00b7 \u65e5\u672c\u8a9e"

LEGACY = (
    "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n# duration: 0:01:32:10\n"
    "0:00:12:07\tFIX\tlogo\trect 0.100,0.100 0.200,0.200\t0-00-12-07_FIX.png\n"
    "0:00:40:00\tADD\tsubtitle\n"
)

LEGACY_EXTRA = (
    "\ufeff# Frame Notes 1\n# source: promo.mp4\n# fps: 29.97\n# duration: 0;01;32;10\n\n"
    "0;00;12;07\tFIX\tlogo \u00e9\trect 0.100,0.100 0.200,0.200\t0-00-12-07_FIX.png\n"
    "hand written line\n"
    "0;01;00;02\tREMOVE\tdrop frame minute\tpoint 0.500,0.500\n"
    "0;99;99;99\tFIX\tbad timecode\n"
    "# trailing comment\n"
)


def png(label: str | None) -> bytes | None:
    return None if label is None else b"\x89PNG " + label.encode("utf-8")


def apply(session: ss.Session, video: Path, ops: list[dict]) -> None:
    for op in ops:
        kind = op["op"]
        notes = session.payload()["notes"]
        if kind == "open":
            session.open(video)
        elif kind == "close":
            session.close()
        elif kind == "add":
            session.add_note(op["frame"], op["kind"], op["text"], op.get("region"), png(op.get("image")))
        elif kind == "set_fps":
            session.set_fps(op["fps"])
        elif kind == "update":
            note_id = notes[op["note"]]["id"]
            region = op["region"] if "region" in op else "keep"
            session.update_note(note_id, text=op.get("text"), region=region, image_png=png(op.get("image")))
        elif kind == "remove":
            session.remove_note(notes[op["note"]]["id"])
        else:
            raise ValueError(kind)


def write_text(path: Path, text: str, crlf: bool) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    data = text.encode("utf-8")
    if crlf:
        data = text.replace("\n", "\r\n").encode("utf-8")
    path.write_bytes(data)


def scenario(name: str, video: str, video_bytes: bytes, ops: list[dict], populate=None, source: Path | None = None) -> Path:
    """Runs `ops` with the 1.x core on a copy of the input folder and stores `ops.json`, `in/` and `expected/`."""
    target = OUT / "scenarios" / name
    if target.exists():
        shutil.rmtree(target)
    (target / "in").mkdir(parents=True)
    if source is not None:
        shutil.copytree(source, target / "in", dirs_exist_ok=True)
    else:
        (target / "in" / video).write_bytes(video_bytes)
    if populate:
        populate(target / "in")
    (target / "ops.json").write_text(json.dumps({"video": video, "ops": ops}, indent=1) + "\n", encoding="ascii")

    scratch = Path(tempfile.mkdtemp(prefix="fn_gen_"))
    try:
        os.environ["FRAME_NOTES_HOME"] = str(scratch / "home")
        clips = scratch / "clips"
        shutil.copytree(target / "in", clips)
        settings = Settings(scratch / "home" / "settings.json")
        settings.load()
        logger = logging.getLogger("gen")
        logger.addHandler(logging.NullHandler())
        logger.propagate = False
        session = ss.Session(settings, lambda d: None, logger)
        apply(session, clips / video, ops)
        shutil.copytree(clips, target / "expected")
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
    return target / "expected"


def main() -> None:
    a_ops = [
        {"op": "open"},
        {"op": "add", "frame": 307, "kind": "FIX", "text": "logo\tflickers\nnow ", "region": RECT, "image": "one"},
        {"op": "add", "frame": 307, "kind": "FIX", "text": "same frame, point", "region": POINT, "image": "two"},
        {"op": "add", "frame": 1000, "kind": "ADD", "text": UNICODE_TEXT},
        {"op": "add", "frame": 1565, "kind": "REMOVE", "text": "last"},
        {"op": "add", "frame": 2000, "kind": "ADD", "text": "rounding", "region": ROUND, "image": "four"},
        {"op": "add", "frame": 2001, "kind": "FIX", "text": "", "region": {"shape": "point", "x0": 0.5, "y0": 1.0}},
        {"op": "set_fps", "fps": 29.97},
        {"op": "update", "note": 4, "text": "edited \u00b7 text", "region": RECT2, "image": "three"},
        {"op": "update", "note": 1, "region": None},
        {"op": "remove", "note": 3},
        {"op": "set_fps", "fps": None},
    ]
    a_expected = scenario("a_session", "promo.mp4", REAL, a_ops)

    b_ops = [
        {"op": "open"},
        {"op": "set_fps", "fps": 120},
        {"op": "add", "frame": 125, "kind": "ADD", "text": "three digit frames", "region": RECT, "image": "x"},
        {"op": "add", "frame": 7200, "kind": "FIX", "text": "x"},
        {"op": "set_fps", "fps": 59.94},
        {"op": "set_fps", "fps": 100},
        {"op": "set_fps", "fps": 23.98},
        {"op": "set_fps", "fps": 29.97},
        {"op": "add", "frame": 1800, "kind": "REMOVE", "text": "first frame after a dropped minute start", "region": POINT, "image": "y"},
    ]
    b_expected = scenario("b_session_unknown_rate", "dummy.mp4", JUNK, b_ops)

    e_ops = [
        {"op": "open"},
        {"op": "set_fps", "fps": 120},
        {"op": "add", "frame": 125, "kind": "ADD", "text": "three digit frames", "region": RECT, "image": "x"},
        {"op": "add", "frame": 14399, "kind": "FIX", "text": "last frame of a minute"},
        {"op": "add", "frame": 14400, "kind": "FIX", "text": "first frame of the next minute", "region": POINT, "image": "z"},
    ]
    e_expected = scenario("e_session_120fps", "dummy.mp4", JUNK, e_ops)

    def legacy_plain(folder: Path) -> None:
        write_text(folder / "promo_notes.txt", LEGACY, crlf=True)
        (folder / "promo_notes").mkdir()
        (folder / "promo_notes" / "0-00-12-07_FIX.png").write_bytes(png("legacy"))

    scenario("c_layout10", "promo.mp4", REAL, [{"op": "open"}], populate=legacy_plain)

    def legacy_extra(folder: Path) -> None:
        write_text(folder / "promo_notes.txt", LEGACY_EXTRA, crlf=True)
        (folder / "promo_notes").mkdir()
        (folder / "promo_notes" / "0-00-12-07_FIX.png").write_bytes(png("legacy"))
        (folder / "promo_notes" / "readme.txt").write_bytes(b"left alone")

    scenario(
        "d_layout10_extra_lines",
        "promo.mp4",
        REAL,
        [{"op": "open"}, {"op": "add", "frame": 10, "kind": "ADD", "text": "after the move"}],
        populate=legacy_extra,
    )

    # The folder written by the 1.x core is opened and saved again: every file must stay byte for byte.
    for name, video, data, source in (
        ("r_resave", "promo.mp4", REAL, a_expected),
        ("r_resave_drop_frame", "dummy.mp4", JUNK, b_expected),
        ("r_resave_120fps", "dummy.mp4", JUNK, e_expected),
    ):
        scenario(name, video, data, [{"op": "open"}, {"op": "set_fps", "fps": None}], source=source)
        before = {p.relative_to(source): p.read_bytes() for p in source.rglob("*") if p.is_file()}
        after_root = OUT / "scenarios" / name / "expected"
        after = {p.relative_to(after_root): p.read_bytes() for p in after_root.rglob("*") if p.is_file()}
        assert before == after, "the 1.x core changed its own files on a save without changes"

    # Settings written by the 1.x core.
    scratch = Path(tempfile.mkdtemp(prefix="fn_gen_"))
    try:
        settings = Settings(scratch / "settings.json")
        settings.load()
        settings.data["last_dir"] = "D:\\clips \u00e9\\sub"
        settings.data["window"]["x"] = 120
        settings.data["window"]["y"] = -30
        settings.data["notes_panel_open"] = False
        settings.data["auto_download_updates"] = False
        for index, frame in enumerate((0, 42, 1999)):
            settings.touch_recent(Path("D:/clips") / f"v{index}.mp4", frame)
        settings.set_fps_override(Path("D:/clips/v1.mp4"), 29.97)
        settings.set_fps_override(Path("D:/clips/v0.mp4"), 24)
        settings.set_fps_override(Path("D:/clips/v2.mp4"), 23.976)
        for item in settings.data["recent"]:
            item["opened_at"] = 1760000000 + item["last_frame"]
        settings.save()
        (OUT / "settings").mkdir(exist_ok=True)
        shutil.copyfile(scratch / "settings.json", OUT / "settings" / "settings.json")
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
    differential()
    print("fixtures written to", OUT)


def differential() -> None:
    """Values computed by the 1.x core for pseudo-random inputs; the C# core must give the same text."""
    import random

    from frame_notes.core import notes as nt
    from frame_notes.core import timecode as tc

    rng = random.Random(20261024)
    lines: list[str] = []
    rates = [23.976, 24, 25, 29.97, 30, 48, 50, 59.94, 60, 100, 120, 15, 12.5, 47.952, 119.88, 23.98, 29.9701, 1000, 0.1]
    for fps in rates:
        tb = tc.timecode_base(fps)
        for frame in [0, 1, tb.base - 1, tb.base, 1799, 1800, 17981, 17982, 107892, 2_000_000] + [rng.randrange(0, 3_000_000) for _ in range(40)]:
            lines.append(f"tc\t{fps!r}\t{frame}\t{tc.frame_to_timecode(frame, tb)}")
    for _ in range(300):
        base = rng.choice([23.976, 24, 25, 29.97, 30, 50, 59.94, 60, 120, 15])
        fps = base * (1 + rng.uniform(-0.008, 0.008))
        lines.append(f"nom\t{fps!r}\t{tc.nominal_rate(fps)!r}\t{tc.format_fps(fps)}")
    for _ in range(400):
        value = rng.choice([rng.random(), round(rng.random(), 4), round(rng.random(), 3) + 0.0005, rng.randrange(0, 2001) / 2000])
        value = min(1.0, max(0.0, value))
        text = nt.Region("point", value, 1.0 - value, value, value).to_text()
        lines.append(f"reg\t{value!r}\t{text}")
    for _ in range(300):
        value = rng.choice([rng.uniform(0, 200), rng.uniform(0, 1e6), 10 ** rng.uniform(-8, 20), rng.randrange(1, 10**6) / 1000])
        lines.append(f"rep\t{value!r}\t{json.dumps(value)}")
    seconds = [rng.uniform(0, 4000) for _ in range(200)]
    for s in seconds:
        fps = rng.choice([25, 29.97, 30000 / 1001, 59.94, 24000 / 1001, 120])
        lines.append(f"sec\t{s!r}\t{fps!r}\t{tc.seconds_to_frame(s, fps)}\t{tc.duration_frames(s, fps)}")
    target = OUT / "differential"
    target.mkdir(exist_ok=True)
    (target / "cases.txt").write_bytes(("\n".join(lines) + "\n").encode("utf-8"))


if __name__ == "__main__":
    main()
