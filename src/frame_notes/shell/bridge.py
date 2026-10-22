"""pywebview `js_api` object (`Bridge`) and the Python -> JS event pump (`EventPump`).

Contract: docs/BRIDGE_API.md. pywebview exposes every public attribute of the js_api object, so `Bridge` has exactly
the contract methods as public names; everything else starts with an underscore. Do not add public helpers or public
data attributes to it.
"""

from __future__ import annotations

import base64
import binascii
import functools
import inspect
import json
import logging
import os
import threading
import time
from pathlib import Path
from typing import Any, Callable, Optional

from frame_notes.core.notes import (
    NotesLayoutError,
    delete_notes,
    legacy_notes_path_for,
    notes_dir_for,
    notes_path_for,
    notes_summary,
)
from frame_notes.core.session import SessionError

from . import winutil

log = logging.getLogger("frame_notes.bridge")
ui_log = logging.getLogger("frame_notes.ui")

WINDOW_TITLE = "Frame Notes"
VIDEO_PATTERNS = "*.mp4;*.m4v;*.mov;*.webm;*.mkv"
FILE_TYPES = (f"Video ({VIDEO_PATTERNS})", "All files (*.*)")
ALLOWED_SETTINGS = {"auto_download_updates": bool, "notes_panel_open": bool}
SETTING_DEFAULTS = (("auto_download_updates", True), ("notes_panel_open", True))
LOG_LEVELS = {"debug": logging.DEBUG, "info": logging.INFO, "warning": logging.WARNING, "error": logging.ERROR}
MAX_UI_LOG_CHARS = 2000
MAX_COPY_TEXT = 200  # copy_text() carries timecodes, not documents
COALESCED_EVENTS = frozenset({"state"})
_DIALOG_OPEN_FALLBACK = 10  # value of webview.FileDialog.OPEN, used when pywebview cannot be imported


class BridgeError(Exception):
    """An error whose message is already a user-facing English sentence."""


def friendly_message(exc: BaseException) -> str:
    """One English sentence for the UI. Messages written by the bridge and the core pass through unchanged."""
    text = " ".join(str(exc).split())
    if isinstance(exc, (BridgeError, SessionError, ValueError)) and text:
        return text
    if isinstance(exc, PermissionError):
        return "No access to the file or folder."
    if isinstance(exc, FileNotFoundError):
        return "File not found."
    if isinstance(exc, OSError):
        return "File system error" + (f": {exc.strerror}." if exc.strerror else ".")
    return "The operation failed. See the log for details."


def _api(fn: Callable) -> Callable:
    """Wrap a Bridge method: log failures and re-raise them as BridgeError with an English message."""

    @functools.wraps(fn)
    def wrapper(self, *args, **kwargs):
        try:
            return fn(self, *args, **kwargs)
        except BridgeError:
            raise
        except Exception as exc:  # noqa: BLE001 - the UI needs a message for any failure
            if isinstance(exc, (SessionError, ValueError)):
                log.info("bridge.%s rejected: %s", fn.__name__, exc)  # an expected, user-facing failure
            else:
                log.exception("bridge.%s failed", fn.__name__)
            raise BridgeError(friendly_message(exc)) from None

    # pywebview reads parameter names with inspect.getfullargspec, which honours __signature__ but not __wrapped__.
    wrapper.__signature__ = inspect.signature(fn)  # type: ignore[attr-defined]
    return wrapper


# --- event pump --------------------------------------------------------------------------------------------------

def js_literal(value: Any) -> str:
    """JSON text that is also a safe JavaScript expression (U+2028 / U+2029 escaped, non-ASCII kept)."""
    text = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    return text.replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")


def build_event_script(events: list[tuple[str, Any]]) -> str:
    """One JS snippet that dispatches every event to window.frameNotes, isolating failures per event."""
    calls = "".join(
        f"try{{d({js_literal(name)},{js_literal(payload)})}}catch(e){{console.error(e)}};" for name, payload in events
    )
    return "(function(){var h=window.frameNotes;if(!h||typeof h.dispatch!=='function')return;" \
           "var d=function(n,p){h.dispatch(n,p)};" + calls + "})();"


class EventPump:
    """Thread-safe queue of events for the UI.

    - Nothing is delivered before `set_ready()` (the page called ready()); everything emitted earlier is kept.
    - `state` events are coalesced: only the newest one is kept. Other events are delivered in order.
    - Events are delivered in batches through `deliver(script)` (window.evaluate_js) from one daemon thread.
    """

    def __init__(self, deliver: Optional[Callable[[str], Any]] = None, start_delay: float = 0.1,
                 threaded: bool = True) -> None:
        self._deliver = deliver
        self._start_delay = start_delay
        self._threaded = threaded
        self._cond = threading.Condition()
        self._queue: list[tuple[str, Any]] = []
        self._ready = False
        self._ready_at = 0.0
        self._stopped = False
        self._thread: Optional[threading.Thread] = None

    def set_deliver(self, deliver: Callable[[str], Any]) -> None:
        with self._cond:
            self._deliver = deliver
            self._cond.notify_all()

    def emit(self, name: str, payload: Any) -> None:
        with self._cond:
            if self._stopped:
                return
            if name in COALESCED_EVENTS:
                self._queue = [item for item in self._queue if item[0] != name]
            self._queue.append((name, payload))
            self._cond.notify_all()

    def set_ready(self) -> None:
        """The page called ready(): start delivering, after a short grace so the return value of ready() goes first."""
        with self._cond:
            if not self._ready:
                self._ready = True
                self._ready_at = time.monotonic() + self._start_delay
            self._cond.notify_all()
            start = self._threaded and self._thread is None
            if start:
                self._thread = threading.Thread(target=self._run, name="event-pump", daemon=True)
        if start:
            self._thread.start()  # type: ignore[union-attr]

    def stop(self) -> None:
        with self._cond:
            self._stopped = True
            self._cond.notify_all()
        thread = self._thread
        if thread is not None and thread is not threading.current_thread():
            thread.join(timeout=1)

    @property
    def ready(self) -> bool:
        return self._ready

    def flush(self) -> None:
        """Deliver everything queued now (the thread loop and the tests call this)."""
        with self._cond:
            if not self._ready or self._deliver is None or not self._queue:
                return
            events, self._queue = self._queue, []
            deliver = self._deliver
        try:
            deliver(build_event_script(events))
        except Exception:  # noqa: BLE001 - the window may be closing; events are not worth crashing for
            log.debug("event delivery failed", exc_info=True)

    def _run(self) -> None:
        while True:
            with self._cond:
                while not self._stopped:
                    if self._ready and self._deliver is not None and self._queue:
                        delay = self._ready_at - time.monotonic()
                        if delay <= 0:
                            break
                        self._cond.wait(delay)
                    else:
                        self._cond.wait()
                if self._stopped:
                    return
            self.flush()


# --- bridge ------------------------------------------------------------------------------------------------------

class Bridge:
    """The js_api object. Contract: docs/BRIDGE_API.md."""

    def __init__(self, settings: Any, server: Any, pump: EventPump, version: str,
                 clipboard: Optional[Callable[[str], None]] = None,
                 reveal: Optional[Callable[[str], None]] = None) -> None:
        self._settings = settings
        self._server = server
        self._pump = pump
        self._version = version
        self._clipboard = clipboard or winutil.set_clipboard_text
        self._reveal = reveal or winutil.reveal_in_explorer
        self._session: Any = None
        self._updater: Any = None
        self._scheduler: Any = None
        self._window: Any = None
        self._lock = threading.RLock()
        self._opening = False
        self._video_open = False
        self._initial_path: Optional[str] = None
        self._auto_download_for: Optional[str] = None
        self._closing = False
        self._shut_down = False
        self._on_closed: Optional[Callable[[], None]] = None
        self._summaries: dict[str, tuple[list, Optional[dict]]] = {}

    # -- wiring (underscore names are not exposed to JS)
    def _attach_session(self, session: Any) -> None:
        self._session = session

    def _attach_updater(self, updater: Any, scheduler: Any = None) -> None:
        """The updater and its periodic scheduler; the scheduler starts after the launch check."""
        self._updater = updater
        self._scheduler = scheduler

    def _attach_window(self, window: Any) -> None:
        self._window = window
        self._pump.set_deliver(window.evaluate_js)

    def _queue_open(self, path: Optional[str]) -> None:
        """Remember a video path from the command line; it is offered to the page right after ready()."""
        self._initial_path = path or None

    def _set_on_closed(self, callback: Callable[[], None]) -> None:
        self._on_closed = callback

    def _emit(self, name: str, payload: Any) -> None:
        self._pump.emit(name, payload)

    def _toast(self, message: str) -> None:
        self._emit("toast", {"message": message})

    def _report_error(self, message: str) -> None:
        self._emit("error", {"message": message})

    # -- state composition
    def _decorate_note(self, note: dict) -> dict:
        out = dict(note)
        name = out.get("image")
        if name:
            url = self._server.image_url(name)
            if url:
                out["image_url"] = url
        return out

    def _recent(self, open_path: Optional[str] = None) -> list[dict]:
        items = []
        open_key = _path_key(open_path) if open_path else None
        for entry in list(self._settings.data.get("recent") or []):
            path = str(entry.get("path", ""))
            if not path:
                continue
            summary = self._summary(Path(path))
            folder = notes_dir_for(Path(path))
            items.append({
                "path": path,
                "name": Path(path).name,
                "last_frame": int(entry.get("last_frame", 0) or 0),
                "exists": os.path.isfile(path),
                "notes_count": summary["notes"] if summary else None,
                "frames_count": summary["frames"] if summary else None,
                "notes_dir": str(folder) if folder.is_dir() else None,
                "is_open": open_key is not None and _path_key(path) == open_key,
            })
        return items

    def _summary(self, video: Path) -> Optional[dict]:
        """Note and frame counts of a recent video, read again only after its data file changed."""
        stamps: list = []
        for candidate in (notes_path_for(video), legacy_notes_path_for(video)):
            try:
                info = os.stat(candidate)
                stamps.append((info.st_mtime_ns, info.st_size))
            except OSError:
                stamps.append(None)
        key = _path_key(str(video))
        if stamps == [None, None]:
            self._summaries.pop(key, None)
            return None
        cached = self._summaries.get(key)
        if cached is not None and cached[0] == stamps:
            return cached[1]
        summary = notes_summary(video)
        self._summaries[key] = (stamps, summary)
        return summary

    def _recent_entry(self, path: Any) -> str:
        """A path of the recent list (the open video is always in it); anything else is refused."""
        if isinstance(path, str) and path.strip():
            key = _path_key(path)
            for entry in list(self._settings.data.get("recent") or []):
                listed = str(entry.get("path", ""))
                if listed and _path_key(listed) == key:
                    return listed
        raise BridgeError("File not found.")

    def _open_video_path(self) -> Optional[str]:
        video = self._session.payload().get("video") if self._session is not None else None
        return (video or {}).get("path") or None

    def _compose(self, payload: dict) -> dict:
        state = dict(payload)
        state["version"] = self._version
        video = payload.get("video")
        if video:
            video = dict(video)
            video["url"] = self._server.media_url()
        state["video"] = video
        state["notes"] = [self._decorate_note(n) for n in payload.get("notes", [])]
        state["recent"] = self._recent((video or {}).get("path"))
        data = self._settings.data
        state["settings"] = {key: data.get(key, default) for key, default in SETTING_DEFAULTS}
        return state

    def _state(self) -> dict:
        with self._lock:
            return self._compose(self._session.payload())

    def _on_change(self, payload: dict) -> None:
        """Session callback: every change of the session becomes one `state` event."""
        if self._opening:
            return  # open_video emits the state itself, after the new media token exists
        self._emit("state", self._compose(payload))

    def _push_state(self) -> dict:
        state = self._state()
        self._emit("state", state)
        return state

    def _video_info(self) -> dict:
        video = self._session.payload().get("video")
        if not video:
            raise BridgeError("No video is open.")
        return video

    # -- opening
    def _open_path(self, path: str) -> dict:
        target = Path(path)
        if not target.is_file():
            raise BridgeError("File not found.")
        with self._lock:
            self._opening = True
            try:
                payload = self._session.open(target)
                video = payload.get("video") or {}
                self._server.set_media(Path(video.get("path") or target),
                                       Path(video["images_dir"]) if video.get("images_dir") else None)
                self._video_open = True
            finally:
                self._opening = False
            self._set_title(video.get("name") or target.name)
            return self._push_state()

    def _set_title(self, name: str) -> None:
        if self._window is None:
            return
        try:
            self._window.title = f"{name} - {WINDOW_TITLE}"
        except Exception:  # noqa: BLE001 - cosmetic
            log.debug("could not set the window title", exc_info=True)

    def _dialog_open_kind(self) -> Any:
        try:
            import webview

            return webview.FileDialog.OPEN
        except Exception:  # noqa: BLE001 - no pywebview (tests): the window object ignores the value
            return _DIALOG_OPEN_FALLBACK

    def _choose_video(self) -> Optional[str]:
        if self._window is None:
            raise BridgeError("The window is not ready.")
        last_dir = str(self._settings.data.get("last_dir") or "")
        result = self._window.create_file_dialog(
            self._dialog_open_kind(),
            directory=last_dir if last_dir and os.path.isdir(last_dir) else "",
            allow_multiple=False,
            file_types=FILE_TYPES,
        )
        if not result:
            return None
        return str(result[0] if isinstance(result, (list, tuple)) else result)

    # -- self-update (wiring called by main / the updater thread)
    def _on_update_status(self, status: dict) -> None:
        self._emit("update", status)
        if status.get("stage") != "available" or not self._settings.data.get("auto_download_updates"):
            return
        version = str(status.get("latest") or "")
        with self._lock:
            if self._auto_download_for == version:
                return
            self._auto_download_for = version
        # The updater is still inside its check step here, so a download started now would be refused: retry briefly.
        threading.Thread(target=self._auto_download, name="update-auto-download", daemon=True).start()

    def _auto_download(self) -> None:
        updater = self._updater
        if updater is None:
            return
        for _ in range(50):
            if updater.status().get("stage") != "available":
                return
            updater.download_background()
            if updater.status().get("stage") == "downloading":
                return
            time.sleep(0.1)

    def _on_update_toast(self, level: str, text: str) -> None:
        self._toast(text)

    def _start_updates(self) -> None:
        updater = self._updater
        if updater is None:
            return
        try:
            updater.startup()
            updater.check_background()
        except Exception:  # noqa: BLE001
            log.warning("update check could not start", exc_info=True)
        if self._scheduler is not None:
            try:
                self._scheduler.start()
                log.info("periodic update checks every 5 minutes")
            except Exception:  # noqa: BLE001
                log.warning("periodic update checks could not start", exc_info=True)

    # -- shutdown
    def _close_window(self) -> None:
        window = self._window
        if window is not None:
            try:
                window.destroy()
            except Exception:  # noqa: BLE001
                log.debug("window destroy failed", exc_info=True)

    def _shutdown(self) -> None:
        """Idempotent: stop the pump, save settings and the session, stop the server."""
        with self._lock:
            if self._shut_down:
                return
            self._shut_down = True
            if self._scheduler is not None:
                try:
                    self._scheduler.stop()
                except Exception:  # noqa: BLE001
                    log.debug("scheduler stop failed", exc_info=True)
            if self._session is not None:
                try:
                    self._session.close()
                except Exception:  # noqa: BLE001
                    log.warning("session close failed", exc_info=True)
            try:
                self._settings.save()
            except Exception:  # noqa: BLE001
                log.warning("settings save failed", exc_info=True)
        self._pump.stop()
        try:
            self._server.stop()
        except Exception:  # noqa: BLE001
            log.debug("server stop failed", exc_info=True)

    # -- contract: state
    @_api
    def ready(self):
        state = self._state()
        self._pump.set_ready()
        self._emit("state", state)
        initial, self._initial_path = self._initial_path, None
        if initial:
            if os.path.isfile(initial):
                self._emit("open_request", {"path": initial})
            else:
                self._report_error("File not found.")
        threading.Thread(target=self._start_updates, name="update-startup", daemon=True).start()
        return state

    @_api
    def open_video(self, path):
        if path is None:
            path = self._choose_video()
            if path is None:
                return None
        if not isinstance(path, str) or not path.strip():
            raise BridgeError("File not found.")
        return self._open_path(path)

    @_api
    def get_state(self):
        return self._state()

    @_api
    def set_fps(self, fps):
        if fps is not None:
            if isinstance(fps, bool) or not isinstance(fps, (int, float)) or not 0 < fps < 1000:
                raise BridgeError("Invalid frame rate.")
            fps = float(fps)
        with self._lock:
            self._session.set_fps(fps)
            return self._push_state()

    @_api
    def set_last_frame(self, frame):
        if isinstance(frame, bool) or not isinstance(frame, (int, float)) or frame < 0:
            return None
        with self._lock:
            if self._video_open:
                self._session.set_last_frame(int(frame))
        return None

    # -- contract: notes
    @_api
    def add_note(self, note):
        if not isinstance(note, dict):
            raise BridgeError("Invalid note.")
        frame = note.get("frame")
        if isinstance(frame, bool) or not isinstance(frame, int) or frame < 0:
            raise BridgeError("Invalid note.")
        kind = note.get("kind")
        text = note.get("text")
        region = note.get("region")
        if not isinstance(kind, str) or not (text is None or isinstance(text, str)) \
                or not (region is None or isinstance(region, dict)):
            raise BridgeError("Invalid note.")
        image = _decode_png(note.get("image_png"))
        with self._lock:
            result = self._session.add_note(frame, kind, text or "", region, image)
            return self._decorate_note(_note_of(result))

    @_api
    def update_note(self, patch):
        if not isinstance(patch, dict) or not isinstance(patch.get("id"), str):
            raise BridgeError("Invalid note.")
        text = patch.get("text")
        if text is not None and not isinstance(text, str):
            raise BridgeError("Invalid note.")
        region = patch["region"] if "region" in patch else "keep"
        if region != "keep" and not (region is None or isinstance(region, dict)):
            raise BridgeError("Invalid note.")
        image = _decode_png(patch.get("image_png"))
        with self._lock:
            result = self._session.update_note(patch["id"], text=text, region=region, image_png=image)
            return self._decorate_note(_note_of(result, patch["id"]))

    @_api
    def remove_note(self, id):  # noqa: A002 - parameter name is part of the JS contract
        if not isinstance(id, str) or not id:
            raise BridgeError("Invalid note.")
        with self._lock:
            self._session.remove_note(id)
        return None

    @_api
    def notes_text(self):
        with self._lock:
            return self._session.notes_text()

    @_api
    def copy_notes_text(self):
        with self._lock:
            self._video_info()
            text = self._session.notes_text()
        try:
            self._clipboard(text)
        except OSError as exc:
            raise BridgeError(str(exc) or "Could not copy to the clipboard.") from None
        self._toast("Notes copied")
        return None

    @_api
    def readable_text(self):
        with self._lock:
            return self._session.readable_text()

    @_api
    def copy_readable_text(self):
        with self._lock:
            self._video_info()
            text = self._session.readable_text()
        try:
            self._clipboard(text)
        except OSError as exc:
            raise BridgeError(str(exc) or "Could not copy to the clipboard.") from None
        self._toast("Readable notes copied")
        return None

    @_api
    def copy_text(self, text):
        if not isinstance(text, str) or not text.strip() or len(text) > MAX_COPY_TEXT:
            raise BridgeError("Invalid text.")
        try:
            self._clipboard(text)
        except OSError as exc:
            raise BridgeError(str(exc) or "Could not copy to the clipboard.") from None
        return None

    @_api
    def reveal(self, which):
        with self._lock:
            video = self._video_info()
        if which == "video":
            target, missing = video.get("path"), "File not found."
        elif which == "notes":
            target, missing = video.get("notes_path"), "No notes file yet."
        elif which == "images":
            target, missing = video.get("images_dir"), "No images yet."
        else:
            raise BridgeError("Unknown item.")
        if not target or not os.path.exists(target):
            raise BridgeError(missing)
        self._reveal(target)
        return None

    # -- contract: recent list
    @_api
    def reveal_recent(self, path):
        video = Path(self._recent_entry(path))
        folder = notes_dir_for(video)
        if folder.is_dir():
            self._reveal(str(folder))
        elif video.is_file():
            self._reveal(str(video))
        else:
            raise BridgeError("File not found.")
        return None

    @_api
    def delete_notes(self, path):
        video = self._recent_entry(path)
        with self._lock:
            current = self._open_video_path()
            try:
                result = delete_notes(Path(video), log)
            except NotesLayoutError as exc:
                log.info("delete_notes refused for %s: %s", video, exc)
                raise BridgeError(str(exc)) from None
            if current and _path_key(current) == _path_key(video):
                state = self._open_path(current)  # the session starts again with no notes
            else:
                state = self._push_state()
        self._toast(deleted_message(result))
        return state

    @_api
    def forget_recent(self, path):
        if not isinstance(path, str) or not path.strip():
            raise BridgeError("File not found.")
        with self._lock:
            if self._settings.forget_recent(path):
                self._settings.save()
            return self._push_state()

    # -- contract: settings and log
    @_api
    def set_setting(self, key, value):
        expected = ALLOWED_SETTINGS.get(key) if isinstance(key, str) else None
        if expected is None:
            raise BridgeError("Unknown setting.")
        if not isinstance(value, expected):
            raise BridgeError("Invalid setting value.")
        with self._lock:
            self._settings.data[key] = value
            self._settings.save()
            state = self._push_state()
        if key == "auto_download_updates" and value and self._updater is not None:
            status = self._updater.status()
            if status.get("stage") == "available":
                self._on_update_status(status)
        return state

    @_api
    def log(self, level, message):
        text = str(message)[:MAX_UI_LOG_CHARS]
        ui_log.log(LOG_LEVELS.get(str(level).lower(), logging.INFO), "ui: %s", text)
        return None

    # -- contract: self-update
    @_api
    def update_status(self):
        return self._need_updater().status()

    @_api
    def check_updates(self):
        updater = self._need_updater()
        updater.check_background()
        return updater.status()

    @_api
    def download_update(self):
        updater = self._need_updater()
        updater.download_background()
        return updater.status()

    @_api
    def apply_update(self):
        if not self._need_updater().apply():
            return False
        # The helper waits for this process to exit: close the window right after this call has returned.
        timer = threading.Timer(0.3, self._close_window)
        timer.daemon = True
        timer.start()
        return True

    def _need_updater(self) -> Any:
        if self._updater is None:
            raise BridgeError("Updates are not available.")
        return self._updater


def _path_key(path: str) -> str:
    return os.path.normcase(os.path.abspath(path))


def _count(n: int, word: str) -> str:
    return f"{n} {word}" + ("" if n == 1 else "s")


def deleted_message(result: dict) -> str:
    """Toast after delete_notes: `Deleted 3 notes and 2 frames.`, the frames part left out when there were none."""
    notes = int(result.get("notes") or 0)
    frames = int(result.get("frames") or 0)
    if not notes and not frames:
        return "Deleted the empty notes folder."
    parts = [_count(notes, "note")] + ([_count(frames, "frame")] if frames else [])
    return "Deleted " + " and ".join(parts) + "."


def _note_of(result: dict, note_id: Optional[str] = None) -> dict:
    """The Note a session mutation refers to.

    The core returns the whole payload after add_note / update_note; the contract of the bridge returns the Note:
    the one with `note_id`, or the newest one (highest `order`) when no id is given.
    """
    notes = result.get("notes") if isinstance(result, dict) else None
    if notes is None:
        return result
    if note_id is not None:
        for note in notes:
            if note.get("id") == note_id:
                return note
        raise BridgeError("Note not found.")
    if not notes:
        raise BridgeError("Invalid note.")
    return max(notes, key=lambda n: n.get("order", 0))


def _decode_png(value: Any) -> Optional[bytes]:
    """Base64 text (optionally a data URL) -> bytes; None or empty -> None."""
    if value is None or value == "":
        return None
    if not isinstance(value, str):
        raise BridgeError("Invalid note.")
    if value.startswith("data:"):
        value = value.partition(",")[2]
    try:
        return base64.b64decode(value, validate=True)
    except (binascii.Error, ValueError):
        raise BridgeError("Invalid note.") from None
