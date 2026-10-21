"""Frame Notes configuration of the vendored self-updater.

This module is the one place that knows the application values (repository, asset name, display name); the updater
itself is `frame_notes.vendor.selfupdate` and is never edited here. The contract is `docs/CORE.md` (section
`selfupdate.py`).
"""

from __future__ import annotations

from typing import Callable

from ..vendor.selfupdate import (
    APPLY_FLAG,
    SelfUpdater,
    UpdateConfig,
    UpdateScheduler,
    run_apply,
)
from .paths import AppPaths

REPO = "softoblivion/frame-notes"
ASSET_NAME = "FrameNotes.exe"
APP_NAME = "Frame Notes"
RELEASE_URL_ENV = "FRAME_NOTES_RELEASE_URL"
LOGGER_NAME = "frame_notes.selfupdate"

__all__ = [
    "APPLY_FLAG",
    "APP_NAME",
    "ASSET_NAME",
    "LOGGER_NAME",
    "RELEASE_URL_ENV",
    "REPO",
    "SelfUpdater",
    "UpdateConfig",
    "UpdateScheduler",
    "build_scheduler",
    "build_updater",
    "run_apply",
]


def build_updater(
    paths: AppPaths,
    version: str,
    on_status: Callable[[dict], None],
    on_toast: Callable[[str, str], None] | None = None,
) -> SelfUpdater:
    """Create the updater for this application. No network is touched until a check or download is requested."""
    config = UpdateConfig(
        repo=REPO,
        asset_name=ASSET_NAME,
        app_name=APP_NAME,
        version=version,
        updates_dir=str(paths.updates_dir),
        exe_path=str(paths.exe_path) if paths.exe_path else None,
        on_status=on_status,
        on_toast=on_toast,
        release_url_env=RELEASE_URL_ENV,
        logger_name=LOGGER_NAME,
    )
    return SelfUpdater(config)


def build_scheduler(updater: SelfUpdater) -> UpdateScheduler:
    """Periodic checks about every five minutes (the library fixes the period; there is no setting for it)."""
    return UpdateScheduler(updater, logger_name=LOGGER_NAME + ".schedule")
