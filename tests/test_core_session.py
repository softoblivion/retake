"""Tests of core/session.py: notes file, images, callbacks, frame rate handling."""

from __future__ import annotations

import logging
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.core import session as ss  # noqa: E402
from frame_notes.core.paths import ENV_HOME, AppPaths  # noqa: E402
from frame_notes.core.settings import Settings  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_core_probe import build_mp4  # noqa: E402

PNG = b"\x89PNG fake image bytes"
RECT = {"shape": "rect", "x0": 0.54, "y0": 0.36, "x1": 0.88, "y1": 0.8}


class SessionCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_sess_"))
        self.addCleanup(shutil.rmtree, self.tmp, True)
        patcher = mock.patch.dict(os.environ, {ENV_HOME: str(self.tmp / "home")})
        patcher.start()
        self.addCleanup(patcher.stop)
        self.paths = AppPaths()
        self.settings = Settings(self.paths.settings_file)
        self.settings.load()
        self.events: list[dict] = []
        log = logging.getLogger("fn_test")
        log.addHandler(logging.NullHandler())
        log.propagate = False
        self.session = ss.Session(self.settings, self.events.append, log)
        self.clips = self.tmp / "clips"
        self.clips.mkdir()

    def make_video(self, name: str = "promo.mp4", real: bool = True, **kw) -> Path:
        path = self.clips / name
        if real:
            path.write_bytes(build_mp4([(2310, 512)], 12800, movie_duration=92400, **kw))
        else:
            path.write_bytes(b"not a real video, the probe sees unknown\n" * 4)
        return path

    def open_unknown(self, fps: float = 25.0, name: str = "dummy.mp4") -> Path:
        video = self.make_video(name, real=False)
        self.session.open(video)
        self.session.set_fps(fps)
        self.events.clear()
        return video


class OpenTest(SessionCase):
    def test_open_real_probe(self) -> None:
        video = self.make_video()
        data = self.session.open(video)
        v = data["video"]
        self.assertEqual((v["name"], v["width"], v["height"]), ("promo.mp4", 1920, 1080))
        self.assertEqual((v["fps"], v["fps_text"], v["fps_source"]), (25.0, "25", "file"))
        self.assertEqual(v["duration_frames"], 2310)
        self.assertAlmostEqual(v["duration"], 92.4)
        self.assertEqual(v["notes_path"], str(self.clips / "promo_notes.txt"))
        self.assertEqual(v["images_dir"], str(self.clips / "promo_notes"))
        self.assertEqual(data["timecode"], {"base": 25, "drop": False, "digits": 2, "separator": ":"})
        self.assertEqual(data["notes"], [])
        self.assertEqual(data["counts"], {"FIX": 0, "ADD": 0, "REMOVE": 0})
        self.assertFalse((self.clips / "promo_notes.txt").exists())

    def test_empty_payload_without_video(self) -> None:
        self.assertEqual(
            self.session.payload(),
            {"video": None, "timecode": None, "notes": [], "counts": {"FIX": 0, "ADD": 0, "REMOVE": 0}},
        )

    def test_missing_video(self) -> None:
        with self.assertRaises(ss.SessionError):
            self.session.open(self.clips / "nope.mp4")

    def test_unknown_fps_blocks_notes(self) -> None:
        self.session.open(self.make_video("dummy.mp4", real=False))
        data = self.session.payload()
        self.assertEqual(data["video"]["fps_source"], "unknown")
        self.assertIsNone(data["video"]["fps"])
        self.assertIsNone(data["timecode"])
        with self.assertRaises(ss.SessionError):
            self.session.add_note(0, "FIX", "x", None, None)
        self.assertFalse((self.clips / "dummy_notes.txt").exists())

    def test_notes_file_of_another_source_is_refused(self) -> None:
        video = self.make_video()
        (self.clips / "promo_notes.txt").write_text(
            "# Frame Notes 1\n# source: promo.mov\n# fps: 25\n0:00:01:00\tFIX\tx\n", encoding="utf-8"
        )
        with self.assertRaises(ss.SessionError) as ctx:
            self.session.open(video)
        self.assertIn("promo.mov", str(ctx.exception))
        self.assertIsNone(self.session.payload()["video"])

    def test_newer_format_is_refused(self) -> None:
        video = self.make_video()
        (self.clips / "promo_notes.txt").write_text(
            "# Frame Notes 9\n# source: promo.mp4\n# fps: 25\n", encoding="utf-8"
        )
        with self.assertRaises(ss.SessionError):
            self.session.open(video)

    def test_existing_notes_are_loaded_and_extra_lines_kept(self) -> None:
        video = self.make_video()
        text = (
            "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
            "0:00:40:00\tADD\tsecond\n0:00:12:07\tFIX\tfirst\trect 0.100,0.100 0.200,0.200\n"
            "hand written line\n"
        )
        (self.clips / "promo_notes.txt").write_text(text, encoding="utf-8")
        data = self.session.open(video)
        self.assertEqual([n["frame"] for n in data["notes"]], [307, 1000])
        self.assertEqual(data["notes"][0]["timecode"], "0:00:12:07")
        self.assertEqual(data["counts"], {"FIX": 1, "ADD": 1, "REMOVE": 0})
        self.session.add_note(10, "REMOVE", "third", None, None)
        saved = (self.clips / "promo_notes.txt").read_text(encoding="utf-8")
        self.assertTrue(saved.endswith("hand written line\n"))
        self.assertIn("0:00:00:10\tREMOVE\tthird\n", saved)
        self.assertIn("# duration: 0:01:32:10\n", saved)

    def test_last_frame_is_remembered(self) -> None:
        video = self.make_video()
        self.session.open(video)
        self.session.set_last_frame(123)
        self.session.close()
        reloaded = Settings(self.paths.settings_file)
        reloaded.load()
        self.assertEqual(reloaded.data["recent"][0]["last_frame"], 123)
        self.assertEqual(reloaded.data["last_dir"], str(self.clips))
        again = ss.Session(reloaded, lambda d: None, logging.getLogger("fn_test"))
        self.assertEqual(again.open(video)["video"]["last_frame"], 123)

    def test_close_writes_no_notes_file(self) -> None:
        self.session.open(self.make_video())
        self.session.close()
        self.assertEqual([p.name for p in self.clips.iterdir()], ["promo.mp4"])
        self.assertIsNone(self.session.payload()["video"])


class NotesTest(SessionCase):
    def test_add_note_writes_file_and_calls_back(self) -> None:
        video = self.open_unknown()
        data = self.session.add_note(307, "FIX", "logo\tflickers\nnow ", None, None)
        notes_file = self.clips / "dummy_notes.txt"
        self.assertEqual(
            notes_file.read_text(encoding="utf-8"),
            "# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n0:00:12:07\tFIX\tlogo flickers now\n",
        )
        self.assertEqual(self.events, [data])
        self.assertEqual(data["notes"][0]["text"], "logo flickers now")
        self.assertEqual(data["notes"][0]["timecode"], "0:00:12:07")
        self.assertRegex(data["notes"][0]["id"], r"^[0-9a-f]{8}$")
        self.assertFalse((self.clips / "dummy_notes").exists())
        self.assertEqual(self.session.notes_text(), notes_file.read_text(encoding="utf-8"))
        self.assertTrue(video.exists())

    def test_note_with_region_and_image(self) -> None:
        self.open_unknown()
        data = self.session.add_note(307, "FIX", "logo", dict(RECT), PNG)
        note = data["notes"][0]
        self.assertEqual(note["image"], "0-00-12-07_FIX.png")
        self.assertEqual(note["image_path"], str(self.clips / "dummy_notes" / "0-00-12-07_FIX.png"))
        self.assertEqual(note["region"], RECT)
        self.assertEqual((self.clips / "dummy_notes" / "0-00-12-07_FIX.png").read_bytes(), PNG)
        text = (self.clips / "dummy_notes.txt").read_text(encoding="utf-8")
        self.assertIn("0:00:12:07\tFIX\tlogo\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n", text)

    def test_image_name_collision_and_no_image_without_region(self) -> None:
        self.open_unknown()
        self.session.add_note(307, "FIX", "a", dict(RECT), PNG)
        data = self.session.add_note(307, "FIX", "b", {"shape": "point", "x0": 0.5, "y0": 0.5}, PNG + b"2")
        self.assertEqual([n["image"] for n in data["notes"]], ["0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png"])
        data = self.session.add_note(308, "ADD", "c", None, PNG)
        self.assertIsNone(data["notes"][2]["image"])
        self.assertEqual(sorted(p.name for p in (self.clips / "dummy_notes").iterdir()),
                         ["0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png"])

    def test_notes_are_sorted_with_order_tie_break(self) -> None:
        self.open_unknown()
        self.session.add_note(100, "ADD", "late first", None, None)
        self.session.add_note(50, "FIX", "early", None, None)
        data = self.session.add_note(100, "REMOVE", "late second", None, None)
        self.assertEqual([n["text"] for n in data["notes"]], ["early", "late first", "late second"])
        self.assertEqual([n["order"] for n in data["notes"]], [2, 1, 3])

    def test_remove_note_deletes_image_and_empty_folder_keeps_header_file(self) -> None:
        self.open_unknown()
        first = self.session.add_note(307, "FIX", "a", dict(RECT), PNG)["notes"][0]["id"]
        second = self.session.add_note(400, "ADD", "b", dict(RECT), PNG)["notes"][1]["id"]
        self.events.clear()
        self.session.remove_note(first)
        self.assertFalse((self.clips / "dummy_notes" / "0-00-12-07_FIX.png").exists())
        self.assertTrue((self.clips / "dummy_notes").is_dir())
        self.assertEqual(len(self.events), 1)
        self.assertEqual(len(self.events[0]["notes"]), 1)
        self.session.remove_note(second)
        self.assertFalse((self.clips / "dummy_notes").exists())
        self.assertEqual(
            (self.clips / "dummy_notes.txt").read_text(encoding="utf-8"),
            "# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n",
        )
        self.assertEqual(self.events[-1]["notes"], [])
        self.assertEqual(len(self.events), 2)

    def test_update_text_region_and_image(self) -> None:
        self.open_unknown()
        note_id = self.session.add_note(307, "FIX", "old", dict(RECT), PNG)["notes"][0]["id"]
        image = self.clips / "dummy_notes" / "0-00-12-07_FIX.png"
        data = self.session.update_note(note_id, text="new\ntext")
        self.assertEqual(data["notes"][0]["text"], "new text")
        self.assertEqual(data["notes"][0]["region"], RECT)
        # a new region without a new image keeps the old image file
        moved = {"shape": "rect", "x0": 0.1, "y0": 0.1, "x1": 0.2, "y1": 0.2}
        data = self.session.update_note(note_id, region=moved)
        self.assertEqual(data["notes"][0]["region"], moved)
        self.assertEqual(data["notes"][0]["image"], "0-00-12-07_FIX.png")
        self.assertEqual(image.read_bytes(), PNG)
        # a new image replaces the bytes under the same name
        self.session.update_note(note_id, region=moved, image_png=b"new bytes")
        self.assertEqual(image.read_bytes(), b"new bytes")
        self.assertEqual([p.name for p in image.parent.iterdir()], [image.name])
        # removing the region removes the image
        data = self.session.update_note(note_id, region=None)
        self.assertIsNone(data["notes"][0]["region"])
        self.assertIsNone(data["notes"][0]["image"])
        self.assertFalse(image.parent.exists())
        text = (self.clips / "dummy_notes.txt").read_text(encoding="utf-8")
        self.assertTrue(text.endswith("0:00:12:07\tFIX\tnew text\n"))
        # a region added later gets an image name when an image comes with it
        data = self.session.update_note(note_id, region=dict(RECT), image_png=PNG)
        self.assertEqual(data["notes"][0]["image"], "0-00-12-07_FIX.png")
        self.assertTrue(image.is_file())

    def test_errors(self) -> None:
        self.open_unknown()
        with self.assertRaises(ss.SessionError) as ctx:
            self.session.add_note(1, "BUG", "x", None, None)
        self.assertEqual(str(ctx.exception), "Unknown note type")
        with self.assertRaises(ss.SessionError):
            self.session.add_note(1, "FIX", "x", {"shape": "rect", "x0": 2, "y0": 0, "x1": 1, "y1": 1}, None)
        with self.assertRaises(ss.SessionError):
            self.session.add_note(-1, "FIX", "x", None, None)
        with self.assertRaises(ss.SessionError):
            self.session.update_note("deadbeef", text="x")
        with self.assertRaises(ss.SessionError):
            self.session.remove_note("deadbeef")
        self.assertFalse((self.clips / "dummy_notes.txt").exists())
        self.assertEqual(self.events, [])

    def test_mutation_without_video_is_refused(self) -> None:
        with self.assertRaises(ss.SessionError):
            self.session.add_note(1, "FIX", "x", None, None)
        with self.assertRaises(ss.SessionError):
            self.session.set_fps(25)

    def test_write_failure_keeps_note_in_memory_and_reports(self) -> None:
        self.open_unknown()
        with mock.patch.object(ss, "atomic_write_text", side_effect=OSError("disk full")):
            with self.assertRaises(ss.SessionError) as ctx:
                self.session.add_note(10, "FIX", "kept", None, None)
        self.assertIn("could not be saved", str(ctx.exception))
        self.assertEqual(len(self.events), 1)
        self.assertEqual(self.events[0]["notes"][0]["text"], "kept")
        self.session.add_note(20, "ADD", "next", None, None)
        saved = (self.clips / "dummy_notes.txt").read_text(encoding="utf-8")
        self.assertIn("kept", saved)
        self.assertIn("next", saved)

    def test_unsafe_image_name_from_a_file_is_never_deleted(self) -> None:
        video = self.make_video()
        victim = self.clips / "victim.txt"
        victim.write_text("keep me", encoding="utf-8")
        (self.clips / "promo_notes.txt").write_text(
            "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:01:00\tFIX\tx\t..\\victim.txt\n", encoding="utf-8"
        )
        data = self.session.open(video)
        self.assertIsNone(data["notes"][0]["image_path"])
        self.session.remove_note(data["notes"][0]["id"])
        self.assertEqual(victim.read_text(encoding="utf-8"), "keep me")


class FpsTest(SessionCase):
    def test_precedence_override_then_probe(self) -> None:
        video = self.make_video()
        self.settings.set_fps_override(video, 30)
        data = self.session.open(video)
        self.assertEqual((data["video"]["fps"], data["video"]["fps_source"]), (30.0, "override"))
        data = self.session.set_fps(None)
        self.assertEqual((data["video"]["fps"], data["video"]["fps_source"]), (25.0, "file"))
        self.assertIsNone(self.settings.fps_override(video))

    def test_set_fps_records_override_only_when_different_from_probe(self) -> None:
        video = self.make_video()
        self.session.open(video)
        data = self.session.set_fps(29.97)
        self.assertEqual((data["video"]["fps_text"], data["video"]["fps_source"]), ("29.97", "override"))
        self.assertTrue(data["timecode"]["drop"])
        self.assertEqual(self.settings.fps_override(video), 29.97)
        data = self.session.set_fps(25.0)
        self.assertEqual(data["video"]["fps_source"], "file")
        self.assertIsNone(self.settings.fps_override(video))
        with self.assertRaises(ss.SessionError):
            self.session.set_fps(0)
        with self.assertRaises(ss.SessionError):
            self.session.set_fps(float("nan"))

    def test_set_fps_rewrites_timecodes_and_image_names(self) -> None:
        self.open_unknown(25.0)
        self.session.add_note(307, "FIX", "a", dict(RECT), PNG)
        self.session.add_note(1800, "ADD", "b", dict(RECT), PNG + b"b")
        images = self.clips / "dummy_notes"
        self.assertEqual(sorted(p.name for p in images.iterdir()), ["0-00-12-07_FIX.png", "0-01-12-00_ADD.png"])
        self.events.clear()
        data = self.session.set_fps(29.97)
        self.assertEqual(len(self.events), 1)
        self.assertEqual([n["frame"] for n in data["notes"]], [307, 1800])
        self.assertEqual([n["timecode"] for n in data["notes"]], ["0;00;10;07", "0;01;00;02"])
        self.assertEqual([n["image"] for n in data["notes"]], ["0-00-10-07_FIX.png", "0-01-00-02_ADD.png"])
        self.assertEqual(sorted(p.name for p in images.iterdir()), ["0-00-10-07_FIX.png", "0-01-00-02_ADD.png"])
        self.assertEqual((images / "0-01-00-02_ADD.png").read_bytes(), PNG + b"b")
        text = (self.clips / "dummy_notes.txt").read_text(encoding="utf-8")
        self.assertIn("# fps: 29.97\n", text)
        self.assertIn("0;01;00;02\tADD\tb\t", text)
        self.assertIn("0-01-00-02_ADD.png", text)
        # the new file loads again with the same frames
        other = ss.Session(self.settings, lambda d: None, logging.getLogger("fn_test"))
        again = other.open(self.clips / "dummy.mp4")
        self.assertEqual([n["frame"] for n in again["notes"]], [307, 1800])

    def test_set_fps_without_notes_writes_no_file(self) -> None:
        self.session.open(self.make_video("dummy.mp4", real=False))
        self.session.set_fps(24)
        self.assertFalse((self.clips / "dummy_notes.txt").exists())

    def test_unknown_probe_uses_fps_of_existing_notes_file(self) -> None:
        video = self.make_video("dummy.mp4", real=False)
        (self.clips / "dummy_notes.txt").write_text(
            "# Frame Notes 1\n# source: dummy.mp4\n# fps: 30\n0:00:01:00\tFIX\tx\n", encoding="utf-8"
        )
        data = self.session.open(video)
        self.assertEqual(data["video"]["fps"], 30.0)
        self.assertEqual(data["notes"][0]["frame"], 30)


if __name__ == "__main__":
    unittest.main()
