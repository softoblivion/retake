"""Persistent application settings (`settings.json` in the data folder).

Owns the defaults, tolerant loading (a corrupt file is moved aside), the recent-files list and the per-video frame
rate overrides. The contract is `docs/CORE.md` (section `settings.py`).
"""

from __future__ import annotations

import copy
import json
import math
import os
import threading
import time
from pathlib import Path
from typing import Any

from .paths import atomic_write_json

MAX_RECENT = 20

DEFAULTS: dict[str, Any] = {
    "window": {"x": None, "y": None, "width": 1280, "height": 800, "maximized": False},
    "last_dir": "",
    "recent": [],  # [{"path": str, "last_frame": int, "opened_at": int}], newest first, at most 20
    "fps_overrides": {},  # {absolute video path: fps}
    "auto_download_updates": True,
    "notes_panel_open": True,
    "update_check_interval": "30m",  # a key of the self-updater's CHECK_INTERVALS; the shell validates it
}


def _path_key(path: Path | str) -> str:
    """Case-insensitive, absolute form of a path used as a lookup key."""
    return os.path.normcase(os.path.abspath(str(path)))


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _merge(default: Any, value: Any) -> Any:
    """Return `value` when it has the type of `default`, else a copy of `default`; dicts merge key by key."""
    if default is None:
        return value if _is_int(value) else None
    if isinstance(default, bool):
        return value if isinstance(value, bool) else default
    if _is_int(default):
        return value if _is_int(value) else default
    if isinstance(default, str):
        return value if isinstance(value, str) else default
    if isinstance(default, list):
        return copy.deepcopy(value) if isinstance(value, list) else copy.deepcopy(default)
    if isinstance(default, dict):
        result = copy.deepcopy(default)
        if isinstance(value, dict):
            for key, item in value.items():
                result[key] = _merge(default[key], item) if key in default else copy.deepcopy(item)
        return result
    return copy.deepcopy(default)


def _clean_recent(items: Any) -> list[dict[str, Any]]:
    cleaned: list[dict[str, Any]] = []
    if not isinstance(items, list):
        return cleaned
    for item in items:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str) or not item["path"]:
            continue
        last_frame = item.get("last_frame")
        opened_at = item.get("opened_at")
        cleaned.append(
            {
                "path": item["path"],
                "last_frame": last_frame if _is_int(last_frame) and last_frame >= 0 else 0,
                "opened_at": opened_at if _is_int(opened_at) else 0,
            }
        )
    return cleaned[:MAX_RECENT]


def _clean_overrides(items: Any) -> dict[str, float]:
    cleaned: dict[str, float] = {}
    if not isinstance(items, dict):
        return cleaned
    for key, value in items.items():
        if isinstance(key, str) and isinstance(value, (int, float)) and not isinstance(value, bool):
            if math.isfinite(value) and value > 0:
                cleaned[key] = float(value)
    return cleaned


class Settings:
    """Settings stored as JSON. All methods are safe to call from several threads."""

    def __init__(self, path: Path) -> None:
        self.path = Path(path)
        self.data: dict[str, Any] = copy.deepcopy(DEFAULTS)
        self._lock = threading.RLock()

    def load(self) -> None:
        """Read the file. Missing or corrupt files give the defaults; a corrupt file becomes `settings.json.bad`."""
        with self._lock:
            self.data = copy.deepcopy(DEFAULTS)
            try:
                raw = self.path.read_text(encoding="utf-8-sig")
            except FileNotFoundError:
                return
            except (OSError, UnicodeDecodeError):
                self._move_aside()
                return
            try:
                loaded = json.loads(raw)
            except ValueError:
                loaded = None
            if not isinstance(loaded, dict):
                self._move_aside()
                return
            self.data = _merge(DEFAULTS, loaded)
            self.data["recent"] = _clean_recent(self.data["recent"])
            self.data["fps_overrides"] = _clean_overrides(self.data["fps_overrides"])

    def _move_aside(self) -> None:
        bad = self.path.with_name(self.path.name + ".bad")
        try:
            os.replace(self.path, bad)
        except OSError:
            pass

    def save(self) -> None:
        """Write the file atomically."""
        with self._lock:
            atomic_write_json(self.path, self.data)

    def touch_recent(self, path: Path, last_frame: int) -> None:
        """Move `path` to the front of the recent list with the given last frame."""
        key = _path_key(path)
        with self._lock:
            recent = [item for item in self.data["recent"] if _path_key(item["path"]) != key]
            recent.insert(
                0,
                {
                    "path": os.path.abspath(str(path)),
                    "last_frame": max(0, int(last_frame)),
                    "opened_at": int(time.time()),
                },
            )
            self.data["recent"] = recent[:MAX_RECENT]

    def forget_recent(self, path: Path | str) -> bool:
        """Remove `path` from the recent list. True when it was listed."""
        key = _path_key(path)
        with self._lock:
            recent = [item for item in self.data["recent"] if _path_key(item["path"]) != key]
            found = len(recent) != len(self.data["recent"])
            self.data["recent"] = recent
        return found

    def last_frame(self, path: Path) -> int:
        """Last frame remembered for `path`, 0 when unknown."""
        key = _path_key(path)
        with self._lock:
            for item in self.data["recent"]:
                if _path_key(item["path"]) == key:
                    return int(item["last_frame"])
        return 0

    def set_fps_override(self, path: Path, fps: float | None) -> None:
        """Remember a frame rate for a video; None removes the override."""
        key = _path_key(path)
        with self._lock:
            overrides = self.data["fps_overrides"]
            if fps is None:
                overrides.pop(key, None)
                return
            value = float(fps)
            if not math.isfinite(value) or value <= 0:
                raise ValueError("The frame rate must be a positive number")
            overrides[key] = value

    def fps_override(self, path: Path) -> float | None:
        """Frame rate remembered for a video, or None."""
        key = _path_key(path)
        with self._lock:
            value = self.data["fps_overrides"].get(key)
        return float(value) if value is not None else None
