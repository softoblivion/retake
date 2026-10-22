"""Tests of the bridge and the event pump (frame_notes.shell.bridge) against a fake session and a fake window."""

from __future__ import annotations

import base64
import inspect
import json
import os
import re
import shutil
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT / "src") not in sys.path:
    sys.path.insert(0, str(ROOT / "src"))

from frame_notes.core.session import SessionError  # noqa: E402
from frame_notes.shell.bridge import (  # noqa: E402
    Bridge,
    BridgeError,
    EventPump,
    build_event_script,
    deleted_message,
    friendly_message,
    js_literal,
)
from frame_notes.shell.server import MediaServer  # noqa: E402

CONTRACT_METHODS = {
    "ready", "open_video", "get_state", "set_fps", "set_last_frame", "add_note", "update_note", "remove_note",
    "notes_text", "copy_notes_text", "readable_text", "copy_readable_text", "copy_text", "reveal",
    "reveal_recent", "delete_notes", "forget_recent", "set_setting", "log",
    "update_status", "check_updates", "download_update", "apply_update",
}
PNG = b"\x89PNG\r\n\x1a\nfake-image-bytes"


class FakeSettings:
    def __init__(self) -> None:
        self.data = {"recent": [], "last_dir": "", "auto_download_updates": True, "notes_panel_open": True}
        self.saves = 0

    def save(self) -> None:
        self.saves += 1

    def forget_recent(self, path) -> bool:
        before = len(self.data["recent"])
        self.data["recent"] = [r for r in self.data["recent"] if str(r["path"]).lower() != str(path).lower()]
        return len(self.data["recent"]) != before


class FakeSession:
    """Dict-based stand-in for core.session.Session; add_note / update_note return the whole payload like the core."""

    def __init__(self, on_change=None) -> None:
        self.on_change = on_change
        self.video: dict | None = None
        self.notes: list[dict] = []
        self.calls: list[tuple] = []
        self.order = 0
        self.fail_open: Exception | None = None

    def open(self, video: Path) -> dict:
        self.calls.append(("open", video))
        if self.fail_open:
            raise self.fail_open
        base = video.with_suffix("")
        self.video = {"path": str(video), "name": video.name, "dir": str(video.parent), "fps": 25.0,
                      "notes_path": str(base) + "_notes.txt", "images_dir": str(base) + "_notes", "last_frame": 0}
        self.notes = []
        return self.payload()

    def close(self) -> None:
        self.calls.append(("close",))

    def payload(self) -> dict:
        return {"video": dict(self.video) if self.video else None,
                "timecode": {"base": 25} if self.video else None,
                "notes": [dict(n) for n in self.notes],
                "counts": {"FIX": 0, "ADD": 0, "REMOVE": 0}}

    def set_fps(self, fps):
        self.calls.append(("set_fps", fps))
        return self._changed()

    def set_last_frame(self, frame) -> None:
        self.calls.append(("set_last_frame", frame))

    def add_note(self, frame, kind, text, region, image_png):
        self.calls.append(("add_note", frame, kind, text, region, image_png))
        if kind not in ("FIX", "ADD", "REMOVE"):
            raise SessionError("Unknown note type")
        self.order += 1
        note = {"id": f"{self.order:08x}", "frame": frame, "kind": kind, "text": text, "region": region,
                "image": f"img{self.order}.png" if region and image_png else None, "order": self.order}
        self.notes.append(note)
        return self._changed()

    def update_note(self, note_id, text=None, region="keep", image_png=None):
        self.calls.append(("update_note", note_id, text, region, image_png))
        for note in self.notes:
            if note["id"] == note_id:
                if text is not None:
                    note["text"] = text
                if region is None:
                    note["region"], note["image"] = None, None
                return self._changed()
        raise SessionError("Note not found")

    def remove_note(self, note_id) -> None:
        self.calls.append(("remove_note", note_id))
        self.notes = [n for n in self.notes if n["id"] != note_id]
        self._changed()

    def notes_text(self) -> str:
        return "# Frame Notes 1\n"

    def readable_text(self) -> str:
        return "# promo_v3.mp4\n"

    def _changed(self) -> dict:
        payload = self.payload()
        if self.on_change:
            self.on_change(payload)
        return payload


class FakeWindow:
    def __init__(self) -> None:
        self.scripts: list[str] = []
        self.dialog_calls: list[dict] = []
        self.dialog_result = None
        self.title = ""
        self.destroyed = threading.Event()

    def evaluate_js(self, script: str):
        self.scripts.append(script)

    def create_file_dialog(self, kind, **kwargs):
        self.dialog_calls.append({"kind": kind, **kwargs})
        return self.dialog_result

    def destroy(self) -> None:
        self.destroyed.set()


class FakeUpdater:
    def __init__(self, stage: str = "idle") -> None:
        self.stage = stage
        self.called: list[str] = []
        self.apply_result = True

    def status(self) -> dict:
        return {"stage": self.stage, "latest": "1.1.0", "current": "1.0.0", "message": "m", "can_apply": False}

    def startup(self) -> None:
        self.called.append("startup")

    def check_background(self) -> None:
        self.called.append("check_background")

    def download_background(self) -> None:
        self.called.append("download_background")
        self.stage = "downloading"

    def apply(self) -> bool:
        self.called.append("apply")
        return self.apply_result


class FakeScheduler:
    def __init__(self) -> None:
        self.calls: list[tuple] = []

    def start(self) -> None:
        self.calls.append(("start",))

    def stop(self) -> None:
        self.calls.append(("stop",))


class BridgeCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_bridge_"))
        self.video = self.tmp / "promo_v3.mp4"
        self.video.write_bytes(b"0" * 64)
        self.settings = FakeSettings()
        self.server = MediaServer(self.tmp)  # not started: only the URL bookkeeping is used
        self.pump = EventPump(threaded=False, start_delay=0)
        self.clipboard: list[str] = []
        self.revealed: list[str] = []
        self.bridge = Bridge(self.settings, self.server, self.pump, "1.0.0",
                             clipboard=self.clipboard.append, reveal=self.revealed.append)
        self.session = FakeSession(self.bridge._on_change)
        self.window = FakeWindow()
        self.updater = FakeUpdater()
        self.scheduler = FakeScheduler()
        self.bridge._attach_session(self.session)
        self.bridge._attach_window(self.window)
        self.bridge._attach_updater(self.updater, self.scheduler)

    def tearDown(self) -> None:
        self.pump.stop()
        for _ in range(20):
            shutil.rmtree(self.tmp, ignore_errors=True)
            if not self.tmp.exists():
                break
            time.sleep(0.05)

    def events(self) -> list[tuple[str, object]]:
        """All events delivered to the fake window so far, decoded from the scripts."""
        found = []
        for script in self.window.scripts:
            for name, payload in re.findall(r"d\((\"[^\"]+\"),(.*?)\)\}catch\(e\)", script):
                found.append((json.loads(name), json.loads(payload)))
        return found

    def drain(self) -> None:
        self.pump.flush()

    def open_video(self) -> dict:
        return self.bridge.open_video(str(self.video))


class PumpTests(unittest.TestCase):
    def test_buffers_until_ready_then_delivers_in_order(self) -> None:
        delivered: list[str] = []
        pump = EventPump(deliver=delivered.append, threaded=False, start_delay=0)
        pump.emit("toast", {"message": "one"})
        pump.emit("toast", {"message": "two"})
        pump.flush()
        self.assertEqual(delivered, [])
        pump.set_ready()
        pump.flush()
        self.assertEqual(len(delivered), 1)
        script = delivered[0]
        self.assertLess(script.index('"one"'), script.index('"two"'))
        pump.flush()
        self.assertEqual(len(delivered), 1)  # nothing queued any more

    def test_state_events_are_coalesced(self) -> None:
        delivered: list[str] = []
        pump = EventPump(deliver=delivered.append, threaded=False, start_delay=0)
        pump.emit("state", {"n": 1})
        pump.emit("toast", {"message": "keep"})
        pump.emit("state", {"n": 2})
        pump.emit("state", {"n": 3})
        pump.set_ready()
        pump.flush()
        script = delivered[0]
        self.assertEqual(script.count('"state"'), 1)
        self.assertIn('{"n":3}', script)
        self.assertNotIn('{"n":1}', script)
        self.assertIn('"keep"', script)

    def test_script_escapes_line_separators_and_keeps_unicode(self) -> None:
        ls, ps, eacute = chr(0x2028), chr(0x2029), chr(0xE9)
        text = f"caf{eacute} {ls} line {ps} end"
        script = build_event_script([("toast", {"message": text})])
        self.assertNotIn(ls, script)
        self.assertNotIn(ps, script)
        self.assertIn("\\u2028", script)
        self.assertIn("\\u2029", script)
        self.assertIn("caf" + eacute, script)  # ensure_ascii=False
        self.assertIn("window.frameNotes", script)
        self.assertIn("dispatch", script)
        self.assertEqual(js_literal("a" + ls + "b"), r'"a\u2028b"')

    def test_threaded_pump_delivers_after_ready(self) -> None:
        got = threading.Event()
        delivered: list[str] = []

        def deliver(script: str) -> None:
            delivered.append(script)
            got.set()

        pump = EventPump(deliver=deliver, start_delay=0)
        try:
            pump.emit("toast", {"message": "early"})
            time.sleep(0.1)
            self.assertEqual(delivered, [])
            pump.set_ready()
            self.assertTrue(got.wait(2))
            self.assertIn("early", delivered[0])
            got.clear()
            pump.emit("toast", {"message": "late"})
            self.assertTrue(got.wait(2))
            self.assertIn("late", delivered[-1])
        finally:
            pump.stop()

    def test_failed_delivery_does_not_raise(self) -> None:
        def broken(script: str) -> None:
            raise RuntimeError("window closed")

        pump = EventPump(deliver=broken, threaded=False, start_delay=0)
        pump.emit("toast", {"message": "x"})
        pump.set_ready()
        pump.flush()


class ErrorMappingTests(unittest.TestCase):
    def test_messages(self) -> None:
        self.assertEqual(friendly_message(SessionError("The video file was not found")),
                         "The video file was not found")
        self.assertEqual(friendly_message(ValueError("Invalid note.")), "Invalid note.")
        self.assertEqual(friendly_message(BridgeError("No notes file yet.")), "No notes file yet.")
        self.assertEqual(friendly_message(FileNotFoundError(2, "x")), "File not found.")
        self.assertEqual(friendly_message(PermissionError(13, "x")), "No access to the file or folder.")
        self.assertEqual(friendly_message(OSError(5, "Disk error")), "File system error: Disk error.")
        self.assertEqual(friendly_message(KeyError("secret")), "The operation failed. See the log for details.")
        self.assertEqual(friendly_message(RuntimeError("Traceback (most recent call last)")),
                         "The operation failed. See the log for details.")


class ContractTests(BridgeCase):
    def test_public_surface_is_exactly_the_contract(self) -> None:
        public = {name for name in dir(self.bridge) if not name.startswith("_")}
        self.assertEqual(public, CONTRACT_METHODS)

    def test_signatures_are_visible_to_pywebview(self) -> None:
        self.assertEqual(inspect.getfullargspec(self.bridge.add_note).args, ["self", "note"])
        self.assertEqual(inspect.getfullargspec(self.bridge.set_setting).args, ["self", "key", "value"])
        self.assertEqual(inspect.getfullargspec(self.bridge.log).args, ["self", "level", "message"])

    def test_unexpected_errors_become_one_sentence(self) -> None:
        self.session.notes_text = lambda: (_ for _ in ()).throw(RuntimeError("boom with details"))
        with self.assertLogs("frame_notes.bridge", level="ERROR"), self.assertRaises(BridgeError) as ctx:
            self.bridge.notes_text()
        self.assertEqual(str(ctx.exception), "The operation failed. See the log for details.")

    def test_session_error_passes_through(self) -> None:
        self.open_video()
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.add_note({"frame": 1, "kind": "BAD", "text": "", "region": None, "image_png": None})
        self.assertEqual(str(ctx.exception), "Unknown note type")


class StateTests(BridgeCase):
    def test_state_without_video(self) -> None:
        state = self.bridge.get_state()
        self.assertEqual(state["version"], "1.0.0")
        self.assertIsNone(state["video"])
        self.assertEqual(state["notes"], [])
        self.assertEqual(state["settings"], {"auto_download_updates": True, "notes_panel_open": True})
        self.assertEqual(state["recent"], [])

    def test_state_composition_after_open(self) -> None:
        self.settings.data["recent"] = [
            {"path": str(self.video), "last_frame": 307, "opened_at": 1},
            {"path": str(self.tmp / "gone.mp4"), "last_frame": 0, "opened_at": 0},
        ]
        state = self.open_video()
        token = self.server.token
        self.assertEqual(state["video"]["url"], f"{self.server.base_url}/media/{token}/promo_v3.mp4")
        self.assertEqual(state["video"]["name"], "promo_v3.mp4")
        self.assertEqual(state["recent"], [
            {"path": str(self.video), "name": "promo_v3.mp4", "last_frame": 307, "exists": True,
             "notes_count": None, "frames_count": None, "notes_dir": None, "is_open": True},
            {"path": str(self.tmp / "gone.mp4"), "name": "gone.mp4", "last_frame": 0, "exists": False,
             "notes_count": None, "frames_count": None, "notes_dir": None, "is_open": False},
        ])
        self.settings.data["notes_panel_open"] = False
        self.assertFalse(self.bridge.get_state()["settings"]["notes_panel_open"])
        self.assertEqual(self.window.title, "promo_v3.mp4 - Frame Notes")

    def test_open_emits_one_state_with_the_new_token(self) -> None:
        self.bridge.ready()
        self.drain()
        before = self.server.token
        self.open_video()
        self.drain()
        states = [p for name, p in self.events() if name == "state"]
        self.assertNotEqual(self.server.token, before)
        self.assertIn(self.server.token, states[-1]["video"]["url"])

    def test_notes_with_image_get_image_url(self) -> None:
        self.open_video()
        self.bridge.add_note({"frame": 5, "kind": "FIX", "text": "a",
                              "region": {"shape": "point", "x0": 0.5, "y0": 0.5, "x1": 0.5, "y1": 0.5},
                              "image_png": base64.b64encode(PNG).decode("ascii")})
        self.bridge.add_note({"frame": 6, "kind": "ADD", "text": "b", "region": None, "image_png": None})
        notes = self.bridge.get_state()["notes"]
        self.assertEqual(notes[0]["image_url"], f"{self.server.base_url}/image/{self.server.token}/img1.png")
        self.assertNotIn("image_url", notes[1])


class CallTests(BridgeCase):
    def test_open_missing_file(self) -> None:
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.open_video(str(self.tmp / "none.mp4"))
        self.assertEqual(str(ctx.exception), "File not found.")
        self.assertEqual([c for c in self.session.calls if c[0] == "open"], [])

    def test_open_error_from_core_passes_through(self) -> None:
        self.session.fail_open = SessionError("The notes file promo_v3_notes.txt belongs to other.mov, not to promo_v3.mp4")
        with self.assertRaises(BridgeError) as ctx:
            self.open_video()
        self.assertIn("belongs to other.mov", str(ctx.exception))
        self.assertFalse(self.bridge._opening)

    def test_open_dialog_cancel_and_choice(self) -> None:
        self.window.dialog_result = None
        self.assertIsNone(self.bridge.open_video(None))
        call = self.window.dialog_calls[-1]
        self.assertFalse(call["allow_multiple"])
        self.assertEqual(call["file_types"][0], "Video (*.mp4;*.m4v;*.mov;*.webm;*.mkv)")
        self.assertEqual(call["file_types"][1], "All files (*.*)")
        self.window.dialog_result = (str(self.video),)
        state = self.bridge.open_video(None)
        self.assertEqual(state["video"]["name"], "promo_v3.mp4")

    def test_add_note_decodes_base64_and_returns_the_new_note(self) -> None:
        self.open_video()
        region = {"shape": "rect", "x0": 0.1, "y0": 0.1, "x1": 0.5, "y1": 0.5}
        first = self.bridge.add_note({"frame": 10, "kind": "FIX", "text": "t", "region": region,
                                      "image_png": base64.b64encode(PNG).decode("ascii")})
        call = [c for c in self.session.calls if c[0] == "add_note"][-1]
        self.assertEqual(call, ("add_note", 10, "FIX", "t", region, PNG))
        self.assertIsInstance(call[5], bytes)
        self.assertEqual(first["id"], "00000001")
        second = self.bridge.add_note({"frame": 3, "kind": "ADD", "text": None, "region": None, "image_png": None})
        self.assertEqual(second["id"], "00000002")
        self.assertEqual(second["frame"], 3)
        self.assertEqual(self.session.calls[-1], ("add_note", 3, "ADD", "", None, None))

    def test_add_note_accepts_data_url_and_rejects_junk(self) -> None:
        self.open_video()
        data_url = "data:image/png;base64," + base64.b64encode(PNG).decode("ascii")
        self.bridge.add_note({"frame": 1, "kind": "FIX", "text": "", "region": None, "image_png": data_url})
        self.assertEqual(self.session.calls[-1][5], PNG)
        for bad in ({"frame": 1, "kind": "FIX", "text": "", "region": None, "image_png": "***not base64***"},
                    {"frame": -1, "kind": "FIX", "text": "", "region": None, "image_png": None},
                    {"frame": "1", "kind": "FIX", "text": "", "region": None, "image_png": None},
                    {"frame": 1, "kind": 5, "text": "", "region": None, "image_png": None},
                    {"frame": 1, "kind": "FIX", "text": "", "region": "x", "image_png": None},
                    "not a dict"):
            with self.subTest(bad=bad), self.assertRaises(BridgeError) as ctx:
                self.bridge.add_note(bad)
            self.assertEqual(str(ctx.exception), "Invalid note.")

    def test_update_note_keeps_missing_keys(self) -> None:
        self.open_video()
        note = self.bridge.add_note({"frame": 1, "kind": "FIX", "text": "old", "region": None, "image_png": None})
        self.bridge.update_note({"id": note["id"], "text": "new"})
        self.assertEqual(self.session.calls[-1], ("update_note", note["id"], "new", "keep", None))
        changed = self.bridge.update_note({"id": note["id"], "region": None})
        self.assertEqual(self.session.calls[-1], ("update_note", note["id"], None, None, None))
        self.assertEqual(changed["text"], "new")
        self.bridge.update_note({"id": note["id"], "image_png": base64.b64encode(PNG).decode("ascii")})
        self.assertEqual(self.session.calls[-1][4], PNG)
        with self.assertRaises(BridgeError):
            self.bridge.update_note({"text": "no id"})

    def test_remove_and_notes_text(self) -> None:
        self.open_video()
        note = self.bridge.add_note({"frame": 1, "kind": "FIX", "text": "x", "region": None, "image_png": None})
        self.assertIsNone(self.bridge.remove_note(note["id"]))
        self.assertEqual(self.bridge.get_state()["notes"], [])
        self.assertEqual(self.bridge.notes_text(), "# Frame Notes 1\n")

    def test_set_fps_and_last_frame(self) -> None:
        self.assertIsNone(self.bridge.set_last_frame(5))  # no video yet: ignored
        self.assertEqual([c for c in self.session.calls if c[0] == "set_last_frame"], [])
        self.open_video()
        self.assertIsNone(self.bridge.set_last_frame(12))
        self.assertEqual(self.session.calls[-1], ("set_last_frame", 12))
        self.assertEqual(self.bridge.set_fps(29.97)["version"], "1.0.0")
        self.assertEqual(self.session.calls[-1], ("set_fps", 29.97))
        self.bridge.set_fps(None)
        self.assertEqual(self.session.calls[-1], ("set_fps", None))
        with self.assertRaises(BridgeError):
            self.bridge.set_fps(-1)

    def test_copy_notes_text(self) -> None:
        with self.assertRaises(BridgeError):
            self.bridge.copy_notes_text()  # no video
        self.open_video()
        self.assertIsNone(self.bridge.copy_notes_text())
        self.assertEqual(self.clipboard, ["# Frame Notes 1\n"])
        self.pump.set_ready()
        self.drain()
        self.assertIn(("toast", {"message": "Notes copied"}), self.events())

    def test_reveal(self) -> None:
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.reveal("video")
        self.assertEqual(str(ctx.exception), "No video is open.")
        self.open_video()
        self.bridge.reveal("video")
        self.assertEqual(self.revealed, [str(self.video)])
        for which, message in (("notes", "No notes file yet."), ("images", "No images yet.")):
            with self.subTest(which=which), self.assertRaises(BridgeError) as ctx:
                self.bridge.reveal(which)
            self.assertEqual(str(ctx.exception), message)
        notes_file = Path(self.session.video["notes_path"])
        notes_file.write_text("x", encoding="utf-8")
        self.bridge.reveal("notes")
        self.assertEqual(self.revealed[-1], str(notes_file))
        Path(self.session.video["images_dir"]).mkdir()
        self.bridge.reveal("images")
        self.assertEqual(self.revealed[-1], self.session.video["images_dir"])
        with self.assertRaises(BridgeError):
            self.bridge.reveal("other")

    def test_copy_readable_text(self) -> None:
        with self.assertRaises(BridgeError):
            self.bridge.copy_readable_text()
        self.open_video()
        self.assertEqual(self.bridge.readable_text(), "# promo_v3.mp4\n")
        self.assertIsNone(self.bridge.copy_readable_text())
        self.assertEqual(self.clipboard, ["# promo_v3.mp4\n"])
        self.pump.set_ready()
        self.drain()
        self.assertIn(("toast", {"message": "Readable notes copied"}), self.events())

    def test_copy_text(self) -> None:
        self.assertIsNone(self.bridge.copy_text("0:01:12:05"))  # needs no open video
        self.assertEqual(self.clipboard, ["0:01:12:05"])
        for bad in (None, 5, "", "  ", "x" * 201):
            with self.subTest(text=bad), self.assertRaises(BridgeError) as ctx:
                self.bridge.copy_text(bad)
            self.assertEqual(str(ctx.exception), "Invalid text.")
        self.assertEqual(self.clipboard, ["0:01:12:05"])
        self.pump.set_ready()
        self.drain()
        self.assertEqual([e for e in self.events() if e[0] == "toast"], [])  # the page shows its own toast

    def test_set_setting(self) -> None:
        state = self.bridge.set_setting("notes_panel_open", False)
        self.assertFalse(state["settings"]["notes_panel_open"])
        self.assertFalse(self.settings.data["notes_panel_open"])
        self.assertGreaterEqual(self.settings.saves, 1)
        for key, value in (("window", {}), ("notes_panel_open", "yes"), ("auto_download_updates", 1),
                           ("update_check_interval", "1d"), ("update_check_interval", 30)):
            with self.subTest(key=key, value=value), self.assertRaises(BridgeError):
                self.bridge.set_setting(key, value)
        self.assertNotIn("update_check_interval", self.settings.data)
        self.assertEqual(self.scheduler.calls, [])

    def test_log_writes_to_the_ui_logger(self) -> None:
        with self.assertLogs("frame_notes.ui", level="DEBUG") as captured:
            self.assertIsNone(self.bridge.log("warning", "something happened"))
            self.bridge.log("nonsense", "falls back to info")
        self.assertEqual(captured.records[0].levelname, "WARNING")
        self.assertEqual(captured.records[0].getMessage(), "ui: something happened")
        self.assertEqual(captured.records[1].levelname, "INFO")


class RecentTests(BridgeCase):
    DATA = ("# Frame Notes 1\n# source: {name}\n# fps: 25\n"
            "0:00:01:00\tFIX\ta\trect 0.100,0.100 0.200,0.200\t0-00-01-00_FIX.png\n0:00:02:00\tADD\tb\n")

    def make_review(self, video: Path) -> Path:
        folder = video.with_name(video.stem + "_notes")
        (folder / "frames").mkdir(parents=True)
        (folder / f"{video.stem}_notes.txt").write_text(self.DATA.format(name=video.name), encoding="utf-8")
        (folder / "frames" / "0-00-01-00_FIX.png").write_bytes(PNG)
        return folder

    def setUp(self) -> None:
        super().setUp()
        self.other = self.tmp / "other.mp4"
        self.other.write_bytes(b"1" * 64)
        self.settings.data["recent"] = [{"path": str(p), "last_frame": 0, "opened_at": 0}
                                        for p in (self.video, self.other)]

    def test_counts_are_read_and_cached_until_the_file_changes(self) -> None:
        folder = self.make_review(self.other)
        entry = self.bridge.get_state()["recent"][1]
        self.assertEqual((entry["notes_count"], entry["frames_count"]), (2, 1))
        self.assertEqual(entry["notes_dir"], str(folder))
        with mock.patch("frame_notes.shell.bridge.notes_summary") as summary:
            self.bridge.get_state()
            summary.assert_not_called()
        data = folder / "other_notes.txt"
        data.write_text(self.DATA.format(name="other.mp4") + "0:00:03:00\tREMOVE\tc\n", encoding="utf-8")
        os.utime(data, ns=(1, 1))
        self.assertEqual(self.bridge.get_state()["recent"][1]["notes_count"], 3)

    def test_delete_notes_of_another_video(self) -> None:
        self.open_video()
        folder = self.make_review(self.other)
        self.pump.set_ready()
        state = self.bridge.delete_notes(str(self.other).upper())
        self.assertFalse(folder.exists())
        self.assertIsNone(state["recent"][1]["notes_count"])
        self.assertEqual(state["video"]["name"], "promo_v3.mp4")
        self.drain()
        self.assertIn(("toast", {"message": "Deleted 2 notes and 1 frame."}), self.events())
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.delete_notes(str(self.other))
        self.assertEqual(str(ctx.exception), "There are no notes for other.mp4.")

    def test_delete_notes_of_the_open_video_reopens_it(self) -> None:
        self.open_video()
        folder = self.make_review(self.video)
        token = self.server.token
        opens = sum(1 for c in self.session.calls if c[0] == "open")
        self.bridge.delete_notes(str(self.video))
        self.assertFalse(folder.exists())
        self.assertEqual(sum(1 for c in self.session.calls if c[0] == "open"), opens + 1)
        self.assertNotEqual(self.server.token, token)

    def test_delete_refusals_and_unknown_paths(self) -> None:
        folder = self.make_review(self.other)
        (folder / "keep.docx").write_bytes(b"x")
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.delete_notes(str(self.other))
        self.assertIn("holds other files", str(ctx.exception))
        self.assertTrue(folder.is_dir())
        for bad in (None, "", 5, str(self.tmp / "not_recent.mp4")):
            with self.subTest(bad=bad), self.assertRaises(BridgeError):
                self.bridge.delete_notes(bad)

    def test_forget_and_reveal(self) -> None:
        self.bridge.reveal_recent(str(self.other))
        self.assertEqual(self.revealed, [str(self.other)])
        folder = self.make_review(self.other)
        self.bridge.reveal_recent(str(self.other))
        self.assertEqual(self.revealed[-1], str(folder))
        state = self.bridge.forget_recent(str(self.other))
        self.assertEqual([r["name"] for r in state["recent"]], ["promo_v3.mp4"])
        self.assertTrue(folder.is_dir())
        self.assertEqual(self.bridge.forget_recent(str(self.other))["recent"][0]["name"], "promo_v3.mp4")
        with self.assertRaises(BridgeError):
            self.bridge.reveal_recent(str(self.other))  # no longer in the list
        self.video.unlink()
        with self.assertRaises(BridgeError) as ctx:
            self.bridge.reveal_recent(str(self.video))
        self.assertEqual(str(ctx.exception), "File not found.")

    def test_deleted_message(self) -> None:
        self.assertEqual(deleted_message({"notes": 3, "frames": 2}), "Deleted 3 notes and 2 frames.")
        self.assertEqual(deleted_message({"notes": 1, "frames": 0}), "Deleted 1 note.")
        self.assertEqual(deleted_message({"notes": 0, "frames": 0}), "Deleted the empty notes folder.")


class ReadyTests(BridgeCase):
    def wait_for(self, condition, timeout: float = 2.0) -> bool:
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            if condition():
                return True
            time.sleep(0.01)
        return False

    def test_events_wait_for_ready_and_state_follows(self) -> None:
        self.bridge._emit("toast", {"message": "early"})
        self.drain()
        self.assertEqual(self.window.scripts, [])
        state = self.bridge.ready()
        self.assertEqual(state["version"], "1.0.0")
        self.drain()
        names = [n for n, _ in self.events()]
        self.assertEqual(names, ["toast", "state"])

    def test_argv_video_becomes_open_request(self) -> None:
        self.bridge._queue_open(str(self.video))
        self.bridge.ready()
        self.drain()
        self.assertIn(("open_request", {"path": str(self.video)}), self.events())
        self.bridge.ready()
        self.drain()
        self.assertEqual([n for n, _ in self.events()].count("open_request"), 1)

    def test_missing_argv_video_becomes_error_event(self) -> None:
        self.bridge._queue_open(str(self.tmp / "none.mp4"))
        self.bridge.ready()
        self.drain()
        self.assertIn(("error", {"message": "File not found."}), self.events())

    def test_updater_starts_after_ready(self) -> None:
        self.bridge.ready()
        self.assertTrue(self.wait_for(lambda: self.updater.called == ["startup", "check_background"]))
        self.assertTrue(self.wait_for(lambda: self.scheduler.calls == [("start",)]))


class UpdateTests(BridgeCase):
    def test_calls_delegate_to_the_updater(self) -> None:
        self.assertEqual(self.bridge.update_status()["stage"], "idle")
        self.bridge.check_updates()
        self.assertEqual(self.updater.called, ["check_background"])
        self.updater.stage = "available"
        self.bridge.download_update()
        self.assertEqual(self.updater.called[-1], "download_background")

    def test_status_callback_emits_update_event(self) -> None:
        self.bridge.ready()
        self.bridge._on_update_status({"stage": "checking", "message": "Checking"})
        self.drain()
        self.assertIn(("update", {"stage": "checking", "message": "Checking"}), self.events())

    def test_available_starts_download_once_when_auto_is_on(self) -> None:
        self.updater.stage = "available"
        self.bridge._on_update_status(self.updater.status())
        deadline = time.monotonic() + 2
        while "download_background" not in self.updater.called and time.monotonic() < deadline:
            time.sleep(0.01)
        self.assertEqual(self.updater.called.count("download_background"), 1)
        self.updater.stage = "available"
        self.bridge._on_update_status(self.updater.status())  # same version: not started again
        time.sleep(0.2)
        self.assertEqual(self.updater.called.count("download_background"), 1)

    def test_available_does_not_download_when_auto_is_off(self) -> None:
        self.settings.data["auto_download_updates"] = False
        self.updater.stage = "available"
        self.bridge._on_update_status(self.updater.status())
        time.sleep(0.3)
        self.assertNotIn("download_background", self.updater.called)

    def test_apply_true_closes_the_window_after_the_call(self) -> None:
        self.assertTrue(self.bridge.apply_update())
        self.assertTrue(self.window.destroyed.wait(3))

    def test_apply_false_keeps_the_window(self) -> None:
        self.updater.apply_result = False
        self.assertFalse(self.bridge.apply_update())
        self.assertFalse(self.window.destroyed.wait(0.6))


class ShutdownTests(BridgeCase):
    def test_shutdown_is_idempotent_and_saves(self) -> None:
        self.open_video()
        self.bridge._shutdown()
        self.bridge._shutdown()
        self.assertEqual([c for c in self.session.calls if c[0] == "close"], [("close",)])
        self.assertGreaterEqual(self.settings.saves, 1)
        self.assertEqual(self.scheduler.calls, [("stop",)])


if __name__ == "__main__":
    unittest.main()
