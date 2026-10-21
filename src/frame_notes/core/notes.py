"""Notes model and the notes file format (`<video stem>_notes.txt`).

Owns parsing and rendering of the file, the region text form and the image naming. The format is a contract
(`docs/ARCHITECTURE.md`, section "Notes file"); the API is `docs/CORE.md` (section `notes.py`).
"""

from __future__ import annotations

import math
import re
import secrets
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .timecode import (
    TimecodeBase,
    format_fps,
    frame_to_timecode,
    parse_fps,
    timecode_base,
    timecode_to_frame,
)

FORMAT_VERSION = 1
KINDS = ("FIX", "ADD", "REMOVE")

_NUMBER_RE = re.compile(r"[0-9]+(?:\.[0-9]*)?|\.[0-9]+")
_VERSION_RE = re.compile(r"#\s*Frame Notes\s+([0-9]+)\s*")
_HEADER_RE = re.compile(r"#\s*([A-Za-z_]+)\s*:\s*(.*)")
_LINE_BREAKS = str.maketrans({c: " " for c in "\t\r\n\x0b\x0c\x85  "})


class NotesFormatError(ValueError):
    """The notes file cannot be used; the message is a user-facing English sentence."""


def normalize_text(text: str) -> str:
    """One-line form of a note text: tabs and line breaks become spaces, ends stripped."""
    return text.translate(_LINE_BREAKS).strip()


def new_id(taken: set[str] | None = None) -> str:
    """Random note id: 8 lowercase hex characters, not in `taken`."""
    while True:
        value = secrets.token_hex(4)
        if not taken or value not in taken:
            return value


def _fraction(value: Any) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError("Region values must be numbers")
    number = float(value)
    if not math.isfinite(number) or not 0.0 <= number <= 1.0:
        raise ValueError("Region values must be between 0 and 1")
    return round(number, 3)


@dataclass
class Region:
    """A rectangle or a point on the frame, in fractions (0..1) of its width and height."""

    shape: str  # "rect" or "point"
    x0: float
    y0: float
    x1: float
    y1: float

    @staticmethod
    def _make(shape: str, x0: float, y0: float, x1: float, y1: float) -> "Region":
        if shape == "point":
            return Region("point", x0, y0, x0, y0)
        if shape == "rect":
            return Region("rect", min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1))
        raise ValueError(f"Unknown region shape: {shape!r}")

    def to_text(self) -> str:
        r = Region._make(self.shape, _fraction(self.x0), _fraction(self.y0), _fraction(self.x1), _fraction(self.y1))
        if r.shape == "point":
            return f"point {r.x0:.3f},{r.y0:.3f}"
        return f"rect {r.x0:.3f},{r.y0:.3f} {r.x1:.3f},{r.y1:.3f}"

    @staticmethod
    def from_text(text: str) -> "Region":
        parts = text.strip().split()
        if not parts:
            raise ValueError("Empty region")
        shape = parts[0]
        expected = {"rect": 3, "point": 2}.get(shape)
        if expected is None or len(parts) != expected:
            raise ValueError(f"Not a region: {text!r}")
        values: list[float] = []
        for pair in parts[1:]:
            pieces = pair.split(",")
            if len(pieces) != 2 or not all(_NUMBER_RE.fullmatch(p) for p in pieces):
                raise ValueError(f"Not a region: {text!r}")
            values.extend(_fraction(float(p)) for p in pieces)
        if shape == "point":
            return Region._make("point", values[0], values[1], values[0], values[1])
        return Region._make("rect", values[0], values[1], values[2], values[3])

    def to_dict(self) -> dict:
        r = Region._make(self.shape, _fraction(self.x0), _fraction(self.y0), _fraction(self.x1), _fraction(self.y1))
        return {"shape": r.shape, "x0": r.x0, "y0": r.y0, "x1": r.x1, "y1": r.y1}

    @staticmethod
    def from_dict(d: dict) -> "Region":
        if not isinstance(d, dict):
            raise ValueError("A region must be an object")
        shape = d.get("shape")
        if shape not in ("rect", "point"):
            raise ValueError(f"Unknown region shape: {shape!r}")
        try:
            x0 = _fraction(d["x0"])
            y0 = _fraction(d["y0"])
            if shape == "point":
                return Region._make("point", x0, y0, x0, y0)
            return Region._make("rect", x0, y0, _fraction(d["x1"]), _fraction(d["y1"]))
        except KeyError as exc:
            raise ValueError(f"The region has no field {exc.args[0]!r}") from None


@dataclass
class Note:
    id: str  # 8 lowercase hex characters; not written to the file
    frame: int
    kind: str  # one of KINDS
    text: str  # one line
    region: Region | None = None
    image: str | None = None  # file name inside the images folder, no path
    order: int = 0  # creation sequence, breaks ties between notes on one frame


@dataclass
class NotesFile:
    source: str  # file name of the video (no path)
    fps: float
    duration_frames: int | None
    notes: list[Note] = field(default_factory=list)
    extra: list[str] = field(default_factory=list)  # lines that did not parse, written back after the notes


def notes_path_for(video: Path) -> Path:
    """`<stem>_notes.txt` next to the video."""
    video = Path(video)
    return video.with_name(video.stem + "_notes.txt")


def images_dir_for(video: Path) -> Path:
    """`<stem>_notes` folder next to the video."""
    video = Path(video)
    return video.with_name(video.stem + "_notes")


def image_name(note: Note, tb: TimecodeBase, taken: set[str]) -> str:
    """`<timecode with '-'>_<KIND>.png`, with `_2`, `_3`... added while the name is in `taken` (case-insensitive)."""
    stem = frame_to_timecode(note.frame, tb).replace(":", "-").replace(";", "-")
    used = {name.lower() for name in taken}
    name = f"{stem}_{note.kind}.png"
    counter = 2
    while name.lower() in used:
        name = f"{stem}_{note.kind}_{counter}.png"
        counter += 1
    return name


def parse(text: str) -> NotesFile:
    """Parse the text of a notes file. See `docs/CORE.md` for the rules."""
    if text.startswith("﻿"):
        text = text[1:]
    lines = [line.rstrip("\r") for line in text.split("\n")]
    index = 0
    while index < len(lines) and not lines[index].strip():
        index += 1
    version_match = _VERSION_RE.fullmatch(lines[index].strip()) if index < len(lines) else None
    if not version_match:
        raise NotesFormatError("This is not a Frame Notes file: the header line is missing")
    version = int(version_match.group(1))
    if version > FORMAT_VERSION:
        raise NotesFormatError(
            f"This notes file uses format {version}, which is newer than this version of Frame Notes understands"
        )
    index += 1

    header: dict[str, str] = {}
    while index < len(lines):
        line = lines[index]
        if not line.strip():
            index += 1
            continue
        match = _HEADER_RE.fullmatch(line.strip()) if line.startswith("#") else None
        if match is None or match.group(1).lower() not in ("source", "fps", "duration") or match.group(1).lower() in header:
            break
        header[match.group(1).lower()] = match.group(2).strip()
        index += 1

    source = header.get("source", "")
    if not source:
        raise NotesFormatError("The notes file has no source line")
    if "fps" not in header:
        raise NotesFormatError("The notes file has no frame rate line")
    try:
        fps = parse_fps(header["fps"])
    except ValueError:
        raise NotesFormatError("The frame rate line of the notes file is not valid") from None
    tb = timecode_base(fps)
    duration: int | None = None
    if header.get("duration"):
        try:
            duration = timecode_to_frame(header["duration"], tb)
        except ValueError:
            duration = None

    nf = NotesFile(source=source, fps=fps, duration_frames=duration)
    taken_ids: set[str] = set()
    for line in lines[index:]:
        if not line.strip():
            continue
        note = _parse_note(line, tb, taken_ids, len(nf.notes) + 1)
        if note is None:
            nf.extra.append(line)
        else:
            taken_ids.add(note.id)
            nf.notes.append(note)
    return nf


def _parse_note(line: str, tb: TimecodeBase, taken_ids: set[str], order: int) -> Note | None:
    columns = line.split("\t")
    if len(columns) < 2 or len(columns) > 5:
        return None
    try:
        frame = timecode_to_frame(columns[0], tb)
    except ValueError:
        return None
    kind = columns[1].strip().upper()
    if kind not in KINDS:
        return None
    body = columns[2] if len(columns) > 2 else ""
    region: Region | None = None
    image: str | None = None
    if len(columns) >= 4 and columns[3].strip():
        col4 = columns[3].strip()
        if col4.startswith(("rect ", "point ")):
            try:
                region = Region.from_text(col4)
            except ValueError:
                return None
            if len(columns) == 5 and columns[4].strip():
                image = columns[4].strip()
        else:
            if len(columns) == 5 and columns[4].strip():
                return None
            image = col4
    elif len(columns) == 5 and columns[4].strip():
        image = columns[4].strip()
    return Note(id=new_id(taken_ids), frame=frame, kind=kind, text=normalize_text(body), region=region,
                image=image, order=order)


def render(nf: NotesFile) -> str:
    """Text of the notes file: header, notes sorted by (frame, order), extra lines; LF endings, trailing newline."""
    tb = timecode_base(nf.fps)
    lines = [
        f"# Frame Notes {FORMAT_VERSION}",
        f"# source: {normalize_text(nf.source)}",
        f"# fps: {format_fps(nf.fps)}",
    ]
    if nf.duration_frames is not None:
        lines.append(f"# duration: {frame_to_timecode(nf.duration_frames, tb)}")
    for note in sorted(nf.notes, key=lambda n: (n.frame, n.order)):
        columns = [frame_to_timecode(note.frame, tb), note.kind, normalize_text(note.text)]
        if note.region is not None:
            columns.append(note.region.to_text())
        if note.image:
            columns.append(note.image)
        lines.append("\t".join(columns))
    lines.extend(nf.extra)
    return "\n".join(lines) + "\n"
