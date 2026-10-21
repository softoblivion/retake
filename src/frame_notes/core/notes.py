"""Notes model, the notes file format and the notes folder (`<video stem>_notes/`).

Owns parsing and rendering of the data file, the readable export, the region text form, the image naming, the
folder layout with the migration of the 1.0 layout, and deleting a review. The format is a contract
(`docs/ARCHITECTURE.md`, section "Notes file"); the API is `docs/CORE.md` (section `notes.py`).
"""

from __future__ import annotations

import logging
import math
import os
import re
import secrets
import stat
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
FRAMES_DIR_NAME = "frames"
_LEFTOVER_SUFFIXES = (".part", ".rename")

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


def notes_dir_for(video: Path) -> Path:
    """`<stem>_notes` folder next to the video: everything of the review lives inside it."""
    video = Path(video)
    return video.with_name(video.stem + "_notes")


def notes_path_for(video: Path) -> Path:
    """The data file `<stem>_notes/<stem>_notes.txt`."""
    video = Path(video)
    return notes_dir_for(video) / (video.stem + "_notes.txt")


def export_path_for(video: Path) -> Path:
    """The readable export `<stem>_notes/<stem>_notes.md`."""
    video = Path(video)
    return notes_dir_for(video) / (video.stem + "_notes.md")


def images_dir_for(video: Path) -> Path:
    """The frame images folder `<stem>_notes/frames`."""
    return notes_dir_for(video) / FRAMES_DIR_NAME


def legacy_notes_path_for(video: Path) -> Path:
    """The 1.0 data file `<stem>_notes.txt` beside the video."""
    video = Path(video)
    return video.with_name(video.stem + "_notes.txt")


def is_plain_name(name: str | None) -> bool:
    """True for a bare file name that is safe to join to a folder (no path component, not "." or "..")."""
    return bool(name) and Path(name).name == name and name not in (".", "..") and "/" not in name and "\\" not in name


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


def render_readable(nf: NotesFile, images: bool = True) -> str:
    """The readable export: heading, summary line, one list item per note (see `docs/ARCHITECTURE.md`, section 6).

    `images=False` leaves the image lines out (the clipboard copy). Lines that did not parse are not exported.
    """
    tb = timecode_base(nf.fps)
    notes = sorted(nf.notes, key=lambda n: (n.frame, n.order))
    counts = {kind: 0 for kind in KINDS}
    for note in notes:
        counts[note.kind] += 1
    summary = [f"{format_fps(nf.fps)} fps"]
    if nf.duration_frames is not None:
        summary.append(frame_to_timecode(nf.duration_frames, tb))
    summary.extend(f"{kind} {counts[kind]}" for kind in KINDS)
    lines = [f"# {normalize_text(nf.source)}", "", " · ".join(summary), ""]
    if not notes:
        lines.append("No notes.")
    for note in notes:
        parts = [f"**{frame_to_timecode(note.frame, tb)}**", note.kind]
        text = normalize_text(note.text)
        if text:
            parts.append(text)
        lines.append("- " + " · ".join(parts))
        if images and note.image and is_plain_name(note.image):
            target = f"{FRAMES_DIR_NAME}/{note.image}"
            if any(c in note.image for c in " ()<>"):
                target = f"<{target}>"
            lines.append(f"  ![]({target})")
    return "\n".join(lines) + "\n"


# --- the notes folder ------------------------------------------------------------------------------------------

class NotesLayoutError(OSError):
    """Moving or deleting the notes folder was refused or failed; the message is a user-facing English sentence."""


def _read_notes_file(path: Path) -> NotesFile | None:
    """Parsed data file, or None when it is missing, unreadable or not a notes file."""
    try:
        return parse(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeDecodeError, NotesFormatError):
        return None


def _is_plain_dir(path: Path) -> bool:
    """A real directory: not a file, a symbolic link or a junction."""
    try:
        info = os.lstat(path)
    except OSError:
        return False
    return stat.S_ISDIR(info.st_mode) and not path.is_symlink() and not path.is_junction()


def _loose_images(folder: Path) -> list[Path]:
    """`*.png` files directly inside `folder` (the 1.0 images folder), sorted by name."""
    if not _is_plain_dir(folder):
        return []
    try:
        return sorted(p for p in folder.iterdir() if p.suffix.lower() == ".png" and p.is_file())
    except OSError:
        return []


def migrate_layout(video: Path, log: logging.Logger) -> bool:
    """Move a 1.0 layout into the notes folder: images into `frames/` first, then the data file. True when moved.

    Every step is one rename on the same volume, so a crash midway leaves readable files and the next call finishes
    the move. A legacy data file that does not parse or names another video is left alone (the caller reports it).
    """
    video = Path(video)
    folder = notes_dir_for(video)
    legacy = legacy_notes_path_for(video)
    target = notes_path_for(video)
    loose = _loose_images(folder)
    has_legacy = legacy.is_file()
    if not has_legacy and not loose:
        return False
    if target.exists():
        log.warning("1.0 notes files of %s are left in place: %s already exists", video.name, target)
        return False
    if has_legacy:
        legacy_nf = _read_notes_file(legacy)
        if legacy_nf is None or legacy_nf.source.casefold() != video.name.casefold():
            log.info("%s is not moved: it is not a notes file of %s", legacy, video.name)
            return False
    moved = 0
    try:
        if loose:
            frames = images_dir_for(video)
            frames.mkdir(parents=True, exist_ok=True)
            for image in loose:
                destination = frames / image.name
                if destination.exists():
                    log.warning("%s is left in place: %s already exists", image, destination)
                    continue
                os.replace(image, destination)
                moved += 1
        if has_legacy:
            folder.mkdir(parents=True, exist_ok=True)
            os.replace(legacy, target)
    except OSError as exc:
        log.error("moving the notes of %s into %s failed: %s", video, folder, exc)
        raise NotesLayoutError(f"The notes of {video.name} could not be moved into the {folder.name} folder.") from exc
    log.info("moved the notes of %s into %s (data file: %s, images: %d)", video.name, folder,
             "moved" if has_legacy else "none", moved)
    return has_legacy or moved > 0


def notes_summary(video: Path) -> dict | None:
    """`{"notes": n, "frames": m}` from the data file of `video` (new layout, else 1.0); None when it has none."""
    video = Path(video)
    for path in (notes_path_for(video), legacy_notes_path_for(video)):
        if path.is_file():
            nf = _read_notes_file(path)
            if nf is None or nf.source.casefold() != video.name.casefold():
                return None
            return {"notes": len(nf.notes), "frames": sum(1 for n in nf.notes if is_plain_name(n.image))}
    return None


def _allowed_leftover(name: str, base: str | None = None) -> bool:
    lower = name.lower()
    if lower in ("thumbs.db", "desktop.ini"):
        return True
    if lower.endswith(".rename"):
        return True
    if lower.endswith(".part"):
        lower = lower[: -len(".part")]
    if base is not None and lower == base.lower():
        return True
    return lower.endswith(".png")


def delete_notes(video: Path, log: logging.Logger) -> dict:
    """Remove the notes folder of `video` (and a 1.0 data file beside it) after checking that it holds only our files.

    Returns `{"notes": n, "frames": m, "path": folder}`. Raises NotesLayoutError when there is nothing to delete, when
    the folder belongs to another video or holds files the app did not write, or when a removal fails.
    """
    video = Path(video)
    folder = notes_dir_for(video)
    data_file = notes_path_for(video)
    export = export_path_for(video)
    legacy = legacy_notes_path_for(video)
    frames = images_dir_for(video)

    folder_exists = os.path.lexists(folder)
    if folder_exists and not _is_plain_dir(folder):
        raise NotesLayoutError(f"The notes folder {folder.name} holds other files. Delete it by hand.")
    legacy_nf = _read_notes_file(legacy) if legacy.is_file() else None
    legacy_ours = legacy_nf is not None and legacy_nf.source.casefold() == video.name.casefold()
    if not folder_exists and not legacy_ours:
        raise NotesLayoutError(f"There are no notes for {video.name}.")

    files: list[Path] = []
    pngs = 0
    notes_count = 0
    if folder_exists:
        nf = _read_notes_file(data_file) if data_file.is_file() else None
        if nf is not None and nf.source.casefold() != video.name.casefold():
            raise NotesLayoutError(f"The notes folder {folder.name} belongs to {nf.source}.")
        if nf is not None:
            notes_count = len(nf.notes)
        elif legacy_ours and legacy_nf is not None:
            notes_count = len(legacy_nf.notes)
        foreign = f"The notes folder {folder.name} holds other files. Delete it by hand."
        try:
            entries = list(folder.iterdir())
            for entry in entries:
                if entry.name.lower() == FRAMES_DIR_NAME:
                    if not _is_plain_dir(entry):
                        raise NotesLayoutError(foreign)
                    for item in entry.iterdir():
                        if not item.is_file() or item.is_symlink() or not _allowed_leftover(item.name):
                            raise NotesLayoutError(foreign)
                        files.append(item)
                        pngs += item.suffix.lower() == ".png"
                    continue
                if not entry.is_file() or entry.is_symlink():
                    raise NotesLayoutError(foreign)
                if entry.name.lower() in (data_file.name.lower(), export.name.lower()) \
                        or _allowed_leftover(entry.name, data_file.name) or _allowed_leftover(entry.name, export.name):
                    files.append(entry)
                    pngs += entry.suffix.lower() == ".png"
                    continue
                raise NotesLayoutError(foreign)
        except NotesLayoutError:
            raise
        except OSError as exc:
            raise NotesLayoutError(f"The notes folder {folder.name} cannot be read.") from exc
    elif legacy_nf is not None:
        notes_count = len(legacy_nf.notes)

    # Images first, the data file last: an interrupted delete keeps the notes readable.
    order = {data_file.name.lower(): 2, export.name.lower(): 1}
    files.sort(key=lambda p: order.get(p.name.lower(), 0))
    try:
        for path in files:
            path.unlink()
        if folder_exists:
            if _is_plain_dir(frames):
                frames.rmdir()
            folder.rmdir()
        if legacy_ours:
            legacy.unlink()
    except OSError as exc:
        log.error("deleting the notes of %s failed: %s", video, exc)
        raise NotesLayoutError(f"The notes folder {folder.name} could not be deleted completely.") from exc
    log.info("deleted the notes of %s: %s (%d notes, %d frame images)", video.name, folder, notes_count, pngs)
    return {"notes": notes_count, "frames": pngs, "path": str(folder)}
