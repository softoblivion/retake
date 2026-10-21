"""Tests of core/paths.py and core/settings.py."""

from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.core import paths  # noqa: E402
from frame_notes.core.settings import DEFAULTS, Settings  # noqa: E402


class TempCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_ps_"))
        self.addCleanup(shutil.rmtree, self.tmp, True)
        patcher = mock.patch.dict(os.environ, {paths.ENV_HOME: str(self.tmp / "home")})
        patcher.start()
        self.addCleanup(patcher.stop)


class PathsTest(TempCase):
    def test_home_from_environment(self) -> None:
        p = paths.AppPaths()
        self.assertEqual(p.home, self.tmp / "home")
        self.assertEqual(p.settings_file, self.tmp / "home" / "settings.json")
        self.assertEqual(p.log_dir, self.tmp / "home" / "logs")
        self.assertEqual(p.updates_dir, self.tmp / "home" / "updates")
        self.assertIsNone(p.exe_path)
        self.assertFalse(p.home.exists())
        p.ensure()
        self.assertTrue(p.log_dir.is_dir() and p.updates_dir.is_dir())

    def test_home_without_environment_is_beside_the_project_root(self) -> None:
        with mock.patch.dict(os.environ):
            os.environ.pop(paths.ENV_HOME, None)
            p = paths.AppPaths()
        root = Path(paths.__file__).resolve().parents[3]
        self.assertEqual(p.home, root / paths.APP_DIR_NAME)
        self.assertTrue((root / "src").is_dir())

    def test_frozen_home_is_beside_the_exe(self) -> None:
        exe = self.tmp / "app" / "FrameNotes.exe"
        with mock.patch.dict(os.environ):
            os.environ.pop(paths.ENV_HOME, None)
            with mock.patch.object(sys, "frozen", True, create=True), mock.patch.object(sys, "executable", str(exe)):
                p = paths.AppPaths()
        self.assertEqual(p.home, (self.tmp / "app").resolve() / paths.APP_DIR_NAME)
        self.assertEqual(p.exe_path, exe)

    def test_atomic_writes(self) -> None:
        target = self.tmp / "out" / "a.txt"
        paths.atomic_write_text(target, "line1\nline2\n\u00e9")
        self.assertEqual(target.read_bytes(), "line1\nline2\n\u00e9".encode("utf-8"))
        paths.atomic_write_text(target, "second")
        self.assertEqual(target.read_text(encoding="utf-8"), "second")
        self.assertEqual([f.name for f in target.parent.iterdir()], ["a.txt"])
        paths.atomic_write_bytes(self.tmp / "b.bin", b"\x00\x01")
        self.assertEqual((self.tmp / "b.bin").read_bytes(), b"\x00\x01")
        paths.atomic_write_json(self.tmp / "c.json", {"name": "\u00e9", "n": [1, 2]})
        raw = (self.tmp / "c.json").read_bytes()
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertEqual(json.loads(raw.decode("utf-8")), {"name": "\u00e9", "n": [1, 2]})
        self.assertIn("\u00e9", raw.decode("utf-8"))

    def test_failed_write_leaves_no_part_file(self) -> None:
        target = self.tmp / "x.txt"
        with mock.patch("os.replace", side_effect=OSError("boom")):
            with self.assertRaises(OSError):
                paths.atomic_write_text(target, "data")
        self.assertEqual(list(self.tmp.glob("x.txt*")), [])


class SettingsTest(TempCase):
    def make(self) -> Settings:
        return Settings(self.tmp / "home" / "settings.json")

    def test_missing_file_gives_defaults(self) -> None:
        s = self.make()
        s.load()
        self.assertEqual(s.data, DEFAULTS)
        self.assertIsNot(s.data, DEFAULTS)
        s.data["recent"].append({})
        self.assertEqual(DEFAULTS["recent"], [])

    def test_save_and_load(self) -> None:
        s = self.make()
        s.load()
        s.data["last_dir"] = "D:\\clips"
        s.data["window"]["width"] = 1000
        s.touch_recent(self.tmp / "a.mp4", 42)
        s.set_fps_override(self.tmp / "a.mp4", 29.97)
        s.save()
        t = self.make()
        t.load()
        self.assertEqual(t.data["last_dir"], "D:\\clips")
        self.assertEqual(t.data["window"]["width"], 1000)
        self.assertEqual(t.data["window"]["height"], 800)
        self.assertEqual(t.data["recent"][0]["last_frame"], 42)
        self.assertEqual(t.fps_override(self.tmp / "a.mp4"), 29.97)
        self.assertIsNone(t.fps_override(self.tmp / "b.mp4"))

    def test_corrupt_file_is_moved_aside(self) -> None:
        folder = self.tmp / "home"
        folder.mkdir()
        (folder / "settings.json").write_text("{not json", encoding="utf-8")
        s = self.make()
        s.load()
        self.assertEqual(s.data, DEFAULTS)
        self.assertFalse((folder / "settings.json").exists())
        self.assertEqual((folder / "settings.json.bad").read_text(encoding="utf-8"), "{not json")

    def test_wrong_types_fall_back_per_key(self) -> None:
        folder = self.tmp / "home"
        folder.mkdir()
        (folder / "settings.json").write_text(
            json.dumps({"window": {"width": "wide", "x": 10}, "recent": [{"path": 5}, {"path": "p", "last_frame": 3,
                        "opened_at": 1}], "fps_overrides": {"a": 25, "b": -1, "c": "x"}, "extra_key": 1,
                        "auto_download_updates": "yes"}),
            encoding="utf-8",
        )
        s = self.make()
        s.load()
        self.assertEqual(s.data["window"]["width"], 1280)
        self.assertEqual(s.data["window"]["x"], 10)
        self.assertEqual(s.data["recent"], [{"path": "p", "last_frame": 3, "opened_at": 1}])
        self.assertEqual(s.data["fps_overrides"], {"a": 25.0})
        self.assertEqual(s.data["extra_key"], 1)
        self.assertIs(s.data["auto_download_updates"], True)

    def test_recent_list_is_newest_first_unique_and_capped(self) -> None:
        s = self.make()
        s.load()
        for i in range(25):
            s.touch_recent(self.tmp / f"v{i}.mp4", i)
        self.assertEqual(len(s.data["recent"]), 20)
        s.touch_recent(self.tmp / "v10.mp4", 99)
        self.assertEqual(Path(s.data["recent"][0]["path"]), self.tmp / "v10.mp4")
        self.assertEqual(s.data["recent"][0]["last_frame"], 99)
        self.assertEqual(sum(1 for r in s.data["recent"] if r["path"].endswith("v10.mp4")), 1)
        self.assertEqual(s.last_frame(self.tmp / "v10.mp4"), 99)
        self.assertEqual(s.last_frame(self.tmp / "nope.mp4"), 0)

    def test_fps_override_set_and_clear(self) -> None:
        s = self.make()
        s.load()
        video = self.tmp / "clip.mp4"
        s.set_fps_override(video, 24)
        self.assertEqual(s.fps_override(video), 24.0)
        s.set_fps_override(video, None)
        self.assertIsNone(s.fps_override(video))
        with self.assertRaises(ValueError):
            s.set_fps_override(video, 0)


if __name__ == "__main__":
    unittest.main()
