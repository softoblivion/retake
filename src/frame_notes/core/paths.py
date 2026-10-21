"""Location of the runtime data folder and atomic file writes.

Nothing outside this module builds a path inside the data folder. The contract is `docs/CORE.md` (section
`paths.py`).
"""

from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path

APP_DIR_NAME = "frame-notes_data"
ENV_HOME = "FRAME_NOTES_HOME"


def _base_dir() -> Path:
    """Folder that holds the data folder: the exe folder when frozen, else the project root (parent of `src`)."""
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parents[3]


class AppPaths:
    """Resolved paths of the data folder. The environment is read when the object is created."""

    def __init__(self, home: Path | None = None) -> None:
        if home is None:
            env = os.environ.get(ENV_HOME, "").strip()
            home = Path(env) if env else _base_dir() / APP_DIR_NAME
        self.home: Path = Path(home)
        self.settings_file: Path = self.home / "settings.json"
        self.log_dir: Path = self.home / "logs"
        self.updates_dir: Path = self.home / "updates"
        self.exe_path: Path | None = Path(sys.executable) if getattr(sys, "frozen", False) else None

    def ensure(self) -> None:
        """Create the data folder and its sub-folders."""
        for folder in (self.home, self.log_dir, self.updates_dir):
            folder.mkdir(parents=True, exist_ok=True)


def atomic_write_bytes(path: Path, data: bytes) -> None:
    """Write `data` to `<name>.part` next to `path`, then replace `path` with it."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    part = path.with_name(path.name + ".part")
    try:
        with open(part, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        _replace(part, path)
    except BaseException:
        try:
            part.unlink()
        except OSError:
            pass
        raise


def atomic_write_text(path: Path, text: str) -> None:
    """Write UTF-8 text without BOM; line endings are kept exactly as given."""
    atomic_write_bytes(path, text.encode("utf-8"))


def atomic_write_json(path: Path, obj: object) -> None:
    """Write `obj` as indented UTF-8 JSON."""
    atomic_write_text(path, json.dumps(obj, ensure_ascii=False, indent=2) + "\n")


def _replace(source: Path, target: Path) -> None:
    """os.replace with a short retry: Windows refuses while another process (scanner, indexer) holds the target."""
    for attempt in range(6):
        try:
            os.replace(source, target)
            return
        except PermissionError:
            if attempt == 5:
                raise
            time.sleep(0.05 * (attempt + 1))
