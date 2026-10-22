"""Application entry point: logging, settings, local server, bridge and the pywebview window.

    python -m frame_notes.shell.main [--debug] [--ui-dir PATH] [VIDEO]

The frozen exe starts here through build/entry.py.
"""

from __future__ import annotations

import argparse
import logging
import logging.handlers
import os
import sys
import threading
from pathlib import Path
from typing import Any, Optional

from frame_notes import __version__
from frame_notes.core.selfupdate import APPLY_FLAG, build_scheduler, build_updater, run_apply

WINDOW_TITLE = "Frame Notes"
BACKGROUND = "#111214"
# The window is not resizable and cannot be maximized. Its size is one of these steps (logical pixels, 16:10),
# the first one that fits the monitor's work area; the last one wins when none fits.
WINDOW_SIZES = ((1280, 800), (1152, 720), (1024, 640), (960, 600))
LOG_NAME = "app.log"

log = logging.getLogger("frame_notes.main")
VIDEO_EXTENSIONS = frozenset({".mp4", ".m4v", ".mov", ".webm", ".mkv"})  # same set as the open dialog filter


def parse_args(argv: Optional[list[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="FrameNotes", description="Frame Notes")
    parser.add_argument("--debug", action="store_true", help="enable WebView2 dev tools and console logging")
    parser.add_argument("--ui-dir", help="serve the UI from this folder instead of the bundled one")
    parser.add_argument("video", nargs="?", help="video file to open at start")
    return parser.parse_args(argv)


def resolve_ui_dir(override: Optional[str] = None) -> Path:
    """The folder with index.html: --ui-dir, the PyInstaller extraction folder when frozen, else the source tree."""
    if override:
        return Path(override).resolve()
    if getattr(sys, "frozen", False):
        return Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent)) / "frame_notes" / "ui"
    return Path(__file__).resolve().parents[1] / "ui"


def resolve_icon() -> Optional[str]:
    if getattr(sys, "frozen", False):
        candidate = Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent)) / "icon.ico"
    else:
        candidate = Path(__file__).resolve().parents[3] / "build" / "icon.ico"
    return str(candidate) if candidate.is_file() else None


def setup_logging(log_dir: Path, debug: bool) -> None:
    """Rotating file log (1 MB x 3) and, with --debug, the console. Safe in a windowed exe (no stderr)."""
    level = logging.DEBUG if debug else logging.INFO
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(name)s [%(threadName)s] %(message)s")
    handlers: list[logging.Handler] = []
    try:
        log_dir.mkdir(parents=True, exist_ok=True)
        handlers.append(logging.handlers.RotatingFileHandler(
            log_dir / LOG_NAME, maxBytes=1_000_000, backupCount=3, encoding="utf-8"))
    except OSError:
        pass
    if debug and sys.stderr is not None:
        console = logging.StreamHandler(sys.stderr)
        console.setLevel(logging.DEBUG)
        handlers.append(console)
    for name in ("frame_notes", "pywebview"):
        logger = logging.getLogger(name)
        logger.setLevel(level if name == "frame_notes" else (logging.DEBUG if debug else logging.WARNING))
        for handler in handlers:
            handler.setFormatter(fmt)
            logger.addHandler(handler)

    def _excepthook(exc_type, exc, tb):
        logging.getLogger("frame_notes.crash").critical("unhandled exception", exc_info=(exc_type, exc, tb))

    def _thread_excepthook(args):
        logging.getLogger("frame_notes.crash").critical(
            "unhandled exception in thread %s", getattr(args.thread, "name", "?"),
            exc_info=(args.exc_type, args.exc_value, args.exc_traceback))

    sys.excepthook = _excepthook
    threading.excepthook = _thread_excepthook


def choose_window_size(area_width: float, area_height: float) -> tuple[int, int]:
    """First entry of WINDOW_SIZES that fits `area_width` x `area_height` (logical pixels); the last when none does."""
    for width, height in WINDOW_SIZES:
        if width <= area_width and height <= area_height:
            return width, height
    return WINDOW_SIZES[-1]


def restore_position(x: Any, y: Any, width: int, height: int,
                     screen: Optional[tuple[int, int, int, int]], scale: float) -> tuple[Optional[int], Optional[int]]:
    """The saved logical x / y, or (None, None) when they are missing or would leave the window off every monitor.

    `screen` is (left, top, width, height) of the virtual screen in physical pixels, `scale` the DPI scale.
    """
    if not (isinstance(x, int) and isinstance(y, int)) or isinstance(x, bool) or isinstance(y, bool):
        return None, None
    if screen is None:
        return x, y
    left, top, sw, sh = screen
    px, py = x * scale, y * scale
    margin = 100  # at least this much of the window must be reachable
    if left - width * scale + margin <= px <= left + sw - margin and top <= py <= top + sh - margin:
        return x, y
    return None, None


def _start_geometry(window_settings: dict) -> dict:
    """Fixed window size for the monitor the window opens on, plus the saved position when it is still on screen."""
    from . import winutil

    scale = winutil.dpi_scale()
    x, y = window_settings.get("x"), window_settings.get("y")
    area = winutil.work_area(x * scale, y * scale) if isinstance(x, int) and isinstance(y, int) else winutil.work_area()
    width, height = choose_window_size(area[2] / scale, area[3] / scale) if area else WINDOW_SIZES[0]
    x, y = restore_position(x, y, width, height, winutil.virtual_screen(), scale)
    return {"width": width, "height": height, "x": x, "y": y}


def main(argv: Optional[list[str]] = None) -> int:
    raw = list(sys.argv[1:] if argv is None else argv)
    if raw and raw[0] == APPLY_FLAG:
        # The downloaded exe swaps the installed one and restarts it: no logging, settings or window.
        return int(run_apply(raw[1:]) or 0)
    args = parse_args(raw)

    from frame_notes.core.paths import AppPaths
    from frame_notes.core.session import Session
    from frame_notes.core.settings import Settings

    from . import winutil
    from .bridge import Bridge, EventPump
    from .server import MediaServer

    paths = AppPaths()
    try:
        paths.ensure()
    except OSError as exc:
        winutil.message_box(WINDOW_TITLE, f"Could not create the data folder:\n{paths.home}\n{exc}", error=True)
        return 3
    setup_logging(paths.log_dir, args.debug)
    ui_dir = resolve_ui_dir(args.ui_dir)
    log.info("Frame Notes %s starting (debug=%s, ui=%s, data=%s)", __version__, args.debug, ui_dir, paths.home)
    if not (ui_dir / "index.html").is_file():
        winutil.message_box(WINDOW_TITLE, f"UI file not found:\n{ui_dir / 'index.html'}", error=True)
        return 2

    settings = Settings(paths.settings_file)
    settings.load()

    server = MediaServer(ui_dir)
    try:
        server.start()
    except OSError as exc:
        log.error("cannot start the local server: %s", exc)
        winutil.message_box(WINDOW_TITLE, f"Could not start the local server:\n{exc}", error=True)
        return 4

    pump = EventPump()
    bridge = Bridge(settings, server, pump, __version__)
    session = Session(settings, bridge._on_change, logging.getLogger("frame_notes.session"))
    bridge._attach_session(session)
    updater = build_updater(paths, __version__, bridge._on_update_status, bridge._on_update_toast)
    bridge._attach_updater(updater, build_scheduler(updater))
    if args.video:
        bridge._queue_open(str(Path(args.video).expanduser().resolve()))

    import webview  # imported late: pulls in pythonnet and WinForms

    geometry = _start_geometry(settings.data.get("window") or {})
    window = webview.create_window(
        WINDOW_TITLE,
        url=server.base_url + "/",
        js_api=bridge,
        width=geometry["width"],
        height=geometry["height"],
        x=geometry["x"],
        y=geometry["y"],
        resizable=False,
        background_color=BACKGROUND,
        text_select=True,
    )
    bridge._attach_window(window)
    _wire_window_events(window, settings, bridge)

    try:
        webview.start(gui="edgechromium", debug=args.debug, private_mode=True, icon=resolve_icon())
    except Exception:  # noqa: BLE001 - a windowed exe has no console; show the reason
        log.exception("the window could not be started")
        winutil.message_box(WINDOW_TITLE, "The window could not be started. Check that the Microsoft Edge "
                            "WebView2 Runtime is installed. Details are in the log.", error=True)
    finally:
        bridge._shutdown()
        log.info("Frame Notes stopped")
        logging.shutdown()
    # Non-daemon .NET / WebView2 helper threads can keep the process alive after the last window is gone.
    os._exit(0)


def _wire_window_events(window: Any, settings: Any, bridge: Bridge) -> None:
    state: dict[str, bool] = {}

    def on_closing() -> None:
        saved = {key: value for key, value in (settings.data.get("window") or {}).items() if key in ("x", "y")}
        try:
            x, y = int(window.x), int(window.y)
            if x > -10000 and y > -10000:  # a minimized window reports (-32000, -32000): keep the last real place
                saved.update(x=x, y=y)
        except Exception:  # noqa: BLE001
            log.debug("could not read the window position", exc_info=True)
        settings.data["window"] = saved
        bridge._shutdown()

    def on_drop(event: dict) -> None:
        # pywebview adds `pywebviewFullPath` to the files of this event dict only because a Python-side listener
        # exists (BRIDGE_API.md, section 5); the page's own drop handler never sees the path.
        files = ((event or {}).get("dataTransfer") or {}).get("files") or []
        for item in files:
            path = item.get("pywebviewFullPath") if isinstance(item, dict) else None
            if path and Path(path).suffix.lower() in VIDEO_EXTENSIONS:
                bridge._emit("open_request", {"path": path})
                return
        if files:
            bridge._emit("error", {"message": "Drop a video file (mp4, m4v, mov, webm, mkv)."})

    def on_loaded() -> None:
        if state.get("drop_wired"):
            return
        try:
            window.dom.document.events.drop += on_drop
            state["drop_wired"] = True
        except Exception:  # noqa: BLE001
            log.warning("drag and drop is not available", exc_info=True)

    window.events.loaded += on_loaded
    window.events.closing += on_closing


if __name__ == "__main__":
    sys.exit(main())
