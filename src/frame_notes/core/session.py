"""The review session: one open video, its notes in memory and the notes folder on disk.

Owns probing the video, loading and saving the data file and the readable export, the note images and the frame rate
choice; the 1.0 layout is moved into the notes folder on open. Every mutation writes the files and then calls
`on_change(payload)`. The contract and the payload shape are in `docs/CORE.md`
(section `session.py`); nothing here knows about URLs or the window.
"""

from __future__ import annotations

import logging
import math
import os
import threading
import time
from pathlib import Path
from typing import Callable, Literal

from .notes import (
    KINDS,
    Note,
    NotesFile,
    NotesFormatError,
    NotesLayoutError,
    Region,
    export_path_for,
    image_name,
    images_dir_for,
    is_plain_name,
    legacy_notes_path_for,
    migrate_layout,
    new_id,
    normalize_text,
    notes_dir_for,
    notes_path_for,
    parse,
    render,
    render_readable,
)
from .paths import atomic_write_bytes, atomic_write_text
from .probe import ProbeResult, probe
from .settings import Settings
from .timecode import TimecodeBase, format_fps, frame_to_timecode, nominal_rate, timecode_base

_SETTINGS_SAVE_INTERVAL = 2.0
_MIN_FPS = 0.1
_MAX_FPS = 1000.0


class SessionError(Exception):
    """A failure the reviewer can act on; the message is a user-facing English sentence."""


def _usable_fps(value: float | None) -> float | None:
    """Snap a frame rate to the nominal table; None when it is missing or absurd."""
    if value is None:
        return None
    try:
        snapped = nominal_rate(value)
    except (ValueError, TypeError):
        return None
    return snapped if _MIN_FPS <= snapped <= _MAX_FPS else None


class Session:
    """State of the open video. All public methods are serialised with one lock.

    `on_change` is called while the lock is held, so it must only hand the payload over (for example enqueue it).
    """

    def __init__(self, settings: Settings, on_change: Callable[[dict], None], log: logging.Logger) -> None:
        self._settings = settings
        self._on_change = on_change
        self._log = log
        self._lock = threading.RLock()
        self._reset()
        self._last_settings_save = 0.0

    def _reset(self) -> None:
        self._video: Path | None = None
        self._probe: ProbeResult | None = None
        self._probe_fps: float | None = None
        self._file_fps: float | None = None
        self._fps: float | None = None
        self._fps_source = "unknown"
        self._notes: list[Note] = []
        self._extra: list[str] = []
        self._file_duration: int | None = None
        self._notes_file_exists = False
        self._last_frame = 0
        self._next_order = 1

    # --- opening and closing ------------------------------------------------------------------------------

    def open(self, video: Path) -> dict:
        """Probe the video, load its notes file and make it the current session."""
        with self._lock:
            path = Path(os.path.abspath(str(video)))
            if not path.is_file():
                raise SessionError("The video file was not found")
            try:
                info = probe(path)
            except OSError as exc:
                self._log.warning("probe failed for %s: %s", path, exc)
                raise SessionError("The video file cannot be read") from None

            try:
                migrated = migrate_layout(path, self._log)
            except NotesLayoutError as exc:
                raise SessionError(str(exc)) from None

            notes: list[Note] = []
            extra: list[str] = []
            file_fps: float | None = None
            file_duration: int | None = None
            notes_path = notes_path_for(path)
            exists = notes_path.is_file()
            if not exists and legacy_notes_path_for(path).is_file():
                # Left in place by the migration: report it the way 1.0 did (another source, newer format).
                notes_path = legacy_notes_path_for(path)
                exists = True
                legacy = True
            else:
                legacy = False
            if exists:
                try:
                    text = notes_path.read_text(encoding="utf-8-sig")
                except (OSError, UnicodeDecodeError):
                    raise SessionError(f"The notes file {notes_path.name} cannot be read") from None
                try:
                    parsed = parse(text)
                except NotesFormatError as exc:
                    raise SessionError(str(exc)) from None
                if parsed.source.casefold() != path.name.casefold():
                    raise SessionError(
                        f"The notes file {notes_path.name} belongs to {parsed.source}, not to {path.name}"
                    )
                if legacy:
                    # Readable but not movable (e.g. the folder could not be created): refuse rather than split.
                    raise SessionError(
                        f"The notes of {path.name} could not be moved into the {notes_dir_for(path).name} folder."
                    )
                notes, extra = parsed.notes, parsed.extra
                file_fps = _usable_fps(parsed.fps)
                file_duration = parsed.duration_frames

            self._reset()
            self._video = path
            self._probe = info
            self._probe_fps = _usable_fps(info.fps)
            self._file_fps = file_fps
            self._notes = notes
            self._extra = extra
            self._file_duration = file_duration
            self._notes_file_exists = exists
            self._next_order = max((n.order for n in notes), default=0) + 1
            self._resolve_fps()
            self._last_frame = self._settings.last_frame(path)
            self._settings.touch_recent(path, self._last_frame)
            self._settings.data["last_dir"] = str(path.parent)
            self._save_settings()
            if migrated and exists:
                error = self._write_export()
                if error:
                    self._log.warning("%s", error)
            self._log.info("opened %s (fps %s, %d notes)", path, self._fps, len(notes))
            return self.payload()

    def close(self) -> None:
        """Save the settings (recent list, last frame) and forget the video. No other file is written."""
        with self._lock:
            if self._video is not None:
                self._settings.touch_recent(self._video, self._last_frame)
                self._save_settings()
            self._reset()

    def _save_settings(self) -> None:
        try:
            self._settings.save()
            self._last_settings_save = time.monotonic()
        except OSError as exc:
            self._log.warning("settings were not saved: %s", exc)

    # --- frame rate -----------------------------------------------------------------------------------------

    def _resolve_fps(self) -> None:
        """Pick the frame rate: settings override, else the probe, else the rate in an existing notes file."""
        assert self._video is not None
        override = _usable_fps(self._settings.fps_override(self._video))
        if override is not None:
            self._fps, self._fps_source = override, "override"
        elif self._probe_fps is not None:
            self._fps, self._fps_source = self._probe_fps, "file"
        elif self._file_fps is not None:
            self._fps, self._fps_source = self._file_fps, "override"
        else:
            self._fps, self._fps_source = None, "unknown"

    def set_fps(self, fps: float | None) -> dict:
        """Change the frame rate (None clears the override). Frame numbers stay, timecodes are rewritten."""
        with self._lock:
            video = self._require_video()
            if fps is None:
                self._settings.set_fps_override(video, None)
            else:
                try:
                    value = float(fps)
                except (TypeError, ValueError):
                    raise SessionError("The frame rate is not valid") from None
                snapped = _usable_fps(value) if math.isfinite(value) else None
                if snapped is None:
                    raise SessionError("The frame rate is not valid")
                if self._probe_fps is not None and snapped == self._probe_fps:
                    self._settings.set_fps_override(video, None)
                else:
                    self._settings.set_fps_override(video, snapped)
            self._save_settings()
            old_tb = self._tb()
            self._resolve_fps()
            new_tb = self._tb()
            if old_tb is not None and new_tb is not None and old_tb != new_tb:
                self._rename_images(new_tb)
            error = self._write_notes()
            return self._finish(error)

    # --- queries --------------------------------------------------------------------------------------------

    def _tb(self) -> TimecodeBase | None:
        return timecode_base(self._fps) if self._fps is not None else None

    def _duration_frames(self) -> int | None:
        if self._probe is not None and self._probe.frames:
            return self._probe.frames
        if self._probe is not None and self._probe.duration and self._fps:
            return max(1, int(round(self._probe.duration * self._fps)))
        return self._file_duration

    def payload(self) -> dict:
        """The state sent to the UI (see `docs/CORE.md`)."""
        with self._lock:
            counts = {kind: 0 for kind in KINDS}
            if self._video is None:
                return {"video": None, "timecode": None, "notes": [], "counts": counts}
            video = self._video
            info = self._probe
            tb = self._tb()
            images_dir = images_dir_for(video)
            duration_frames = self._duration_frames()
            duration = info.duration if info is not None else None
            if self._probe is not None and self._probe.frames and tb is not None:
                duration = self._probe.frames / tb.fps
            notes_out = []
            for note in sorted(self._notes, key=lambda n: (n.frame, n.order)):
                counts[note.kind] += 1
                image_file = self._image_file(note.image)
                notes_out.append(
                    {
                        "id": note.id,
                        "frame": note.frame,
                        "timecode": frame_to_timecode(note.frame, tb) if tb is not None else "",
                        "kind": note.kind,
                        "text": note.text,
                        "region": note.region.to_dict() if note.region is not None else None,
                        "image": note.image,
                        "image_path": str(image_file) if image_file is not None else None,
                        "order": note.order,
                    }
                )
            return {
                "video": {
                    "path": str(video),
                    "name": video.name,
                    "dir": str(video.parent),
                    "width": info.width if info is not None else None,
                    "height": info.height if info is not None else None,
                    "duration": duration,
                    "duration_frames": duration_frames,
                    "fps": tb.fps if tb is not None else None,
                    "fps_text": format_fps(self._fps) if self._fps is not None else "",
                    "fps_source": self._fps_source,
                    "notes_dir": str(notes_dir_for(video)),
                    "notes_path": str(notes_path_for(video)),
                    "export_path": str(export_path_for(video)),
                    "images_dir": str(images_dir),
                    "last_frame": self._last_frame,
                },
                "timecode": (
                    {"base": tb.base, "drop": tb.drop, "digits": tb.digits, "separator": tb.separator}
                    if tb is not None
                    else None
                ),
                "notes": notes_out,
                "counts": counts,
            }

    def notes_text(self) -> str:
        """Text of the notes file for the current state ("" when no video is open or the frame rate is unknown)."""
        with self._lock:
            if self._video is None or self._fps is None:
                return ""
            return render(self._notes_file())

    def readable_text(self) -> str:
        """The readable export without the image lines, for the clipboard ("" like `notes_text`)."""
        with self._lock:
            if self._video is None or self._fps is None:
                return ""
            return render_readable(self._notes_file(), images=False)

    def set_last_frame(self, frame: int) -> None:
        """Remember the playback position; the settings file is written at most once per two seconds."""
        with self._lock:
            if self._video is None:
                return
            self._last_frame = max(0, int(frame))
            self._settings.touch_recent(self._video, self._last_frame)
            if time.monotonic() - self._last_settings_save >= _SETTINGS_SAVE_INTERVAL:
                self._save_settings()

    # --- mutations ------------------------------------------------------------------------------------------

    def add_note(self, frame: int, kind: str, text: str, region: dict | None, image_png: bytes | None) -> dict:
        """Add a note at `frame`; a region may come with the PNG of the frame."""
        with self._lock:
            self._require_video()
            tb = self._tb()
            if tb is None:
                raise SessionError("The frame rate is unknown. Choose it before adding notes")
            kind = self._check_kind(kind)
            if isinstance(frame, bool) or not isinstance(frame, int) or frame < 0:
                raise SessionError("The frame number is not valid")
            note = Note(
                id=new_id({n.id for n in self._notes}),
                frame=frame,
                kind=kind,
                text=normalize_text(str(text or "")),
                region=self._check_region(region),
                order=self._next_order,
            )
            if note.region is not None and image_png:
                note.image = image_name(note, tb, self._taken_images())
                self._write_image(note.image, image_png)
            self._next_order += 1
            self._notes.append(note)
            return self._finish(self._write_notes())

    def update_note(
        self,
        note_id: str,
        text: str | None = None,
        region: dict | None | Literal["keep"] = "keep",
        image_png: bytes | None = None,
    ) -> dict:
        """Change the text and / or region of a note; a new region normally comes with a new image."""
        with self._lock:
            self._require_video()
            note = self._find(note_id)
            tb = self._tb()
            if tb is None:
                raise SessionError("The frame rate is unknown. Choose it before editing notes")
            new_region = note.region if isinstance(region, str) else self._check_region(region)
            if isinstance(region, str) and region != "keep":
                raise SessionError("The region is not valid")
            old_image = note.image
            drop_old_image = False
            if new_region is None:
                drop_old_image = old_image is not None
                note.image = None
            elif image_png:
                if old_image is None:
                    note.image = image_name(note, tb, self._taken_images(exclude=note))
                self._write_image(note.image or "", image_png)
            note.region = new_region
            if text is not None:
                note.text = normalize_text(str(text))
            error = self._write_notes()
            if drop_old_image:
                self._delete_image(old_image)
                self._prune_images_dir()
            return self._finish(error)

    def remove_note(self, note_id: str) -> None:
        """Delete a note, its image and the images folder when it became empty."""
        with self._lock:
            self._require_video()
            note = self._find(note_id)
            self._notes.remove(note)
            error = self._write_notes()
            self._delete_image(note.image)
            self._prune_images_dir()
            self._finish(error)

    # --- helpers --------------------------------------------------------------------------------------------

    def _require_video(self) -> Path:
        if self._video is None:
            raise SessionError("No video is open")
        return self._video

    @staticmethod
    def _check_kind(kind: str) -> str:
        value = str(kind).strip().upper()
        if value not in KINDS:
            raise SessionError("Unknown note type")
        return value

    @staticmethod
    def _check_region(region: dict | None) -> Region | None:
        if region is None:
            return None
        try:
            return Region.from_dict(region)
        except ValueError:
            raise SessionError("The marked region is not valid") from None

    def _find(self, note_id: str) -> Note:
        for note in self._notes:
            if note.id == note_id:
                return note
        raise SessionError("The note was not found")

    def _notes_file(self) -> NotesFile:
        assert self._video is not None and self._fps is not None
        return NotesFile(
            source=self._video.name,
            fps=self._fps,
            duration_frames=self._duration_frames(),
            notes=list(self._notes),
            extra=list(self._extra),
        )

    def _write_notes(self) -> str | None:
        """Write the notes file when it exists or has notes. Returns an error sentence or None."""
        if self._video is None or self._fps is None:
            return None
        if not self._notes and not self._notes_file_exists:
            return None
        path = notes_path_for(self._video)
        try:
            atomic_write_text(path, render(self._notes_file()))
        except OSError as exc:
            self._log.error("cannot write %s: %s", path, exc)
            return f"The notes could not be saved to {path}"
        self._notes_file_exists = True
        return self._write_export()

    def _write_export(self) -> str | None:
        """Write the readable export next to the data file. Returns an error sentence or None."""
        if self._video is None or self._fps is None:
            return None
        path = export_path_for(self._video)
        try:
            atomic_write_text(path, render_readable(self._notes_file()))
        except OSError as exc:
            self._log.error("cannot write %s: %s", path, exc)
            return f"The notes could not be saved to {path}"
        return None

    def _finish(self, error: str | None) -> dict:
        """Publish the new state; a failed write is reported after the UI has seen the in-memory notes."""
        data = self.payload()
        try:
            self._on_change(data)
        except Exception:  # noqa: BLE001
            self._log.exception("on_change callback failed")
        if error:
            raise SessionError(error)
        return data

    def _taken_images(self, exclude: Note | None = None) -> set[str]:
        return {n.image for n in self._notes if n.image and n is not exclude}

    def _image_file(self, name: str | None) -> Path | None:
        """Path of an image in the images folder; None for no image or a name that is not a plain file name."""
        if not name or self._video is None or not is_plain_name(name):
            return None
        return images_dir_for(self._video) / name

    def _write_image(self, name: str, data: bytes) -> None:
        target = self._image_file(name)
        if target is None:
            raise SessionError("The image name is not valid")
        try:
            atomic_write_bytes(target, bytes(data))
        except OSError as exc:
            self._log.error("cannot write %s: %s", target, exc)
            raise SessionError(f"The image could not be saved to {target}") from None

    def _delete_image(self, name: str | None) -> None:
        target = self._image_file(name)
        if target is None:
            return
        try:
            target.unlink(missing_ok=True)
        except OSError as exc:
            self._log.warning("cannot delete %s: %s", target, exc)

    def _prune_images_dir(self) -> None:
        if self._video is None:
            return
        folder = images_dir_for(self._video)
        try:
            if folder.is_dir() and not any(folder.iterdir()):
                folder.rmdir()
        except OSError as exc:
            self._log.warning("cannot remove %s: %s", folder, exc)

    def _rename_images(self, tb: TimecodeBase) -> None:
        """Rename the image files to the timecodes of the new frame rate (two steps, all or nothing)."""
        plan: list[tuple[Note, str, str]] = []
        taken: set[str] = set()
        for note in sorted(self._notes, key=lambda n: (n.frame, n.order)):
            if not note.image or self._image_file(note.image) is None:
                continue
            new_name = image_name(note, tb, taken)
            taken.add(new_name)
            if new_name != note.image:
                plan.append((note, note.image, new_name))
        if not plan:
            return
        moved: list[tuple[Path, Path]] = []
        try:
            for note, old, _ in plan:
                source = self._image_file(old)
                assert source is not None
                if not source.exists():
                    continue
                temp = source.with_name(f"{note.id}.rename")
                os.replace(source, temp)
                moved.append((temp, source))
        except OSError as exc:
            self._log.warning("images were not renamed: %s", exc)
            for temp, source in moved:
                try:
                    os.replace(temp, source)
                except OSError:
                    pass
            return
        for note, old, new in plan:
            target = self._image_file(new)
            temp = self._image_file(old).with_name(f"{note.id}.rename")  # type: ignore[union-attr]
            assert target is not None
            try:
                if temp.exists():
                    os.replace(temp, target)
                note.image = new
            except OSError as exc:
                self._log.warning("cannot rename image %s to %s: %s", old, new, exc)
