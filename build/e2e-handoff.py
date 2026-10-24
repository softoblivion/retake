"""Drives the update path of Frame Notes 1.4.1 (the Python updater) against a local release server, headless.

Used by build\\e2e-handoff.ps1. The 1.4.1 updater is run from its source tree (the `src` folder that holds the
`frame_notes` package of tag v1.4.1), configured exactly as the packaged exe configures it: the data folder
`frame-notes_data` and the exe `FrameNotes.exe` of the application folder, the release document of
FRAME_NOTES_RELEASE_URL. It checks, downloads, verifies and starts the downloaded exe in apply mode
(`<download> --apply-update <pid> <FrameNotes.exe>`), then exits, so that the swap can happen.

    python -I e2e-handoff.py <1.4.1 src folder> <application folder> <release document URL>

Exit code 0: the apply helper was started; the swap itself is checked by the caller.
"""
import os
import sys
import types
from pathlib import Path


def main(argv):
    source, app, document = Path(argv[1]), Path(argv[2]), argv[3]
    os.environ["FRAME_NOTES_RELEASE_URL"] = document
    sys.path.insert(0, str(source))
    from frame_notes import __version__
    from frame_notes.core import selfupdate

    print("updater version:", __version__)
    updates = app / "frame-notes_data" / "updates"
    paths = types.SimpleNamespace(updates_dir=updates, exe_path=app / "FrameNotes.exe")
    events = []
    up = selfupdate.build_updater(paths, __version__, events.append)
    up.startup()
    status = up.check()
    print("check:", status["stage"], status.get("latest_version") or status.get("version"), status.get("message"))
    assert status["stage"] == "available", status
    status = up.download()
    print("download:", status["stage"], status.get("message"))
    assert status["stage"] == "ready", status
    started = up.apply()
    print("apply started:", started)
    assert started
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
