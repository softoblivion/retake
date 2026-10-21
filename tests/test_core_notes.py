"""Tests of core/notes.py: regions, the notes file round trip, image names and format errors."""

from __future__ import annotations

import logging
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.core import notes as nt  # noqa: E402
from frame_notes.core.timecode import timecode_base  # noqa: E402

SAMPLE = (
    "# Frame Notes 1\n"
    "# source: promo_v3.mp4\n"
    "# fps: 25\n"
    "# duration: 0:01:32:10\n"
    "0:00:12:07\tFIX\tlogo flickers for two frames\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n"
    "0:00:40:00\tADD\tsubtitle for the second speaker\n"
    "0:01:02:15\tREMOVE\tsecond take of the handshake\tpoint 0.512,0.300\t0-01-02-15_REMOVE.png\n"
)


class RegionTest(unittest.TestCase):
    def test_text_round_trip(self) -> None:
        r = nt.Region.from_text("rect 0.540,0.360 0.880,0.800")
        self.assertEqual((r.shape, r.x0, r.y0, r.x1, r.y1), ("rect", 0.54, 0.36, 0.88, 0.8))
        self.assertEqual(r.to_text(), "rect 0.540,0.360 0.880,0.800")
        p = nt.Region.from_text("point 0.512,0.300")
        self.assertEqual((p.x0, p.y0, p.x1, p.y1), (0.512, 0.3, 0.512, 0.3))
        self.assertEqual(p.to_text(), "point 0.512,0.300")

    def test_three_decimals_and_normalisation(self) -> None:
        r = nt.Region("rect", 0.88888, 0.8, 0.54, 0.36)
        self.assertEqual(r.to_text(), "rect 0.540,0.360 0.889,0.800")
        d = r.to_dict()
        self.assertEqual(d, {"shape": "rect", "x0": 0.54, "y0": 0.36, "x1": 0.889, "y1": 0.8})
        self.assertEqual(nt.Region("rect", 0, 0, 1, 1).to_text(), "rect 0.000,0.000 1.000,1.000")

    def test_dict_round_trip(self) -> None:
        d = {"shape": "rect", "x0": 0.54, "y0": 0.36, "x1": 0.88, "y1": 0.8}
        self.assertEqual(nt.Region.from_dict(d).to_dict(), d)
        point = nt.Region.from_dict({"shape": "point", "x0": 0.2, "y0": 0.4})
        self.assertEqual(point.to_dict(), {"shape": "point", "x0": 0.2, "y0": 0.4, "x1": 0.2, "y1": 0.4})
        flipped = nt.Region.from_dict({"shape": "rect", "x0": 0.9, "y0": 0.9, "x1": 0.1, "y1": 0.2})
        self.assertEqual((flipped.x0, flipped.y0, flipped.x1, flipped.y1), (0.1, 0.2, 0.9, 0.9))

    def test_junk_is_rejected(self) -> None:
        for bad in ("", "rect", "rect 0.1,0.1", "circle 0.1,0.1", "point 1.5,0.2", "point -0.1,0.2",
                    "rect 0.1,0.1 0.2,nan", "point a,b", "rect 0.1,0.1 0.2,0.2 0.3,0.3", "point 0.1;0.2"):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                nt.Region.from_text(bad)
        for bad in (None, {}, {"shape": "rect"}, {"shape": "blob", "x0": 0, "y0": 0},
                    {"shape": "point", "x0": 2, "y0": 0}, {"shape": "point", "x0": "0.1", "y0": 0},
                    {"shape": "rect", "x0": 0, "y0": 0, "x1": 1}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                nt.Region.from_dict(bad)  # type: ignore[arg-type]


class ImageNameTest(unittest.TestCase):
    def test_names_and_collisions(self) -> None:
        tb = timecode_base(25)
        note = nt.Note(id="a", frame=307, kind="FIX", text="x")
        self.assertEqual(nt.image_name(note, tb, set()), "0-00-12-07_FIX.png")
        self.assertEqual(nt.image_name(note, tb, {"0-00-12-07_FIX.png"}), "0-00-12-07_FIX_2.png")
        self.assertEqual(
            nt.image_name(note, tb, {"0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png"}), "0-00-12-07_FIX_3.png"
        )
        drop = timecode_base(29.97)
        self.assertEqual(nt.image_name(nt.Note("b", 1800, "ADD", ""), drop, set()), "0-01-00-02_ADD.png")

    def test_paths(self) -> None:
        video = Path("D:/clips/promo_v3.mp4")
        self.assertEqual(nt.notes_dir_for(video), Path("D:/clips/promo_v3_notes"))
        self.assertEqual(nt.notes_path_for(video), Path("D:/clips/promo_v3_notes/promo_v3_notes.txt"))
        self.assertEqual(nt.export_path_for(video), Path("D:/clips/promo_v3_notes/promo_v3_notes.md"))
        self.assertEqual(nt.images_dir_for(video), Path("D:/clips/promo_v3_notes/frames"))
        self.assertEqual(nt.legacy_notes_path_for(video), Path("D:/clips/promo_v3_notes.txt"))
        self.assertEqual(nt.notes_path_for(Path("a.b.mov")), Path("a.b_notes/a.b_notes.txt"))
        self.assertTrue(nt.is_plain_name("0-00-12-07_FIX.png"))
        for bad in ("", None, ".", "..", "..\\x.png", "a/b.png", "C:\\x.png"):
            self.assertFalse(nt.is_plain_name(bad), bad)


class ParseRenderTest(unittest.TestCase):
    def test_sample_round_trip(self) -> None:
        nf = nt.parse(SAMPLE)
        self.assertEqual(nf.source, "promo_v3.mp4")
        self.assertEqual(nf.fps, 25.0)
        self.assertEqual(nf.duration_frames, 2310)
        self.assertEqual([n.frame for n in nf.notes], [307, 1000, 1565])
        self.assertEqual([n.kind for n in nf.notes], ["FIX", "ADD", "REMOVE"])
        self.assertEqual(nf.notes[0].region.to_text(), "rect 0.540,0.360 0.880,0.800")  # type: ignore[union-attr]
        self.assertEqual(nf.notes[0].image, "0-00-12-07_FIX.png")
        self.assertIsNone(nf.notes[1].region)
        self.assertIsNone(nf.notes[1].image)
        self.assertEqual(nf.notes[2].region.shape, "point")  # type: ignore[union-attr]
        self.assertEqual(nf.extra, [])
        self.assertEqual(len({n.id for n in nf.notes}), 3)
        for n in nf.notes:
            self.assertRegex(n.id, r"^[0-9a-f]{8}$")
        self.assertEqual(nt.render(nf), SAMPLE)

    def test_extra_lines_are_kept_after_notes(self) -> None:
        text = SAMPLE.replace(
            "0:00:40:00",
            "this line is hand written\n0:00:40:00",
        ) + "0:99:99:99\tFIX\tbad timecode\n0:00:50:00\tBUG\tbad kind\n# trailing comment\n\n"
        nf = nt.parse(text)
        self.assertEqual(len(nf.notes), 3)
        self.assertEqual(
            nf.extra,
            ["this line is hand written", "0:99:99:99\tFIX\tbad timecode", "0:00:50:00\tBUG\tbad kind",
             "# trailing comment"],
        )
        out = nt.render(nf)
        lines = out.split("\n")
        self.assertEqual(lines[-1], "")
        self.assertEqual(lines[-5:-1], nf.extra)
        self.assertEqual(nt.parse(out).extra, nf.extra)

    def test_bad_region_and_extra_column_go_to_extra(self) -> None:
        text = (
            "# Frame Notes 1\n# source: v.mp4\n# fps: 25\n"
            "0:00:01:00\tFIX\ta\trect 2.0,0.1 0.3,0.3\n"
            "0:00:02:00\tFIX\ta\tpic.png\textra.png\n"
        )
        nf = nt.parse(text)
        self.assertEqual(nf.notes, [])
        self.assertEqual(len(nf.extra), 2)

    def test_image_without_region_and_empty_text(self) -> None:
        text = "# Frame Notes 1\n# source: v.mp4\n# fps: 25\n0:00:01:00\tFIX\t\n0:00:02:00\tADD\tx\tshot.png\n"
        nf = nt.parse(text)
        self.assertEqual(nf.notes[0].text, "")
        self.assertEqual(nf.notes[1].image, "shot.png")
        self.assertIsNone(nf.notes[1].region)
        self.assertEqual(nt.render(nf), text)

    def test_duration_is_optional(self) -> None:
        text = "# Frame Notes 1\n# source: v.mp4\n# fps: 29.97\n0;01;00;02\tFIX\tdrop frame\n"
        nf = nt.parse(text)
        self.assertIsNone(nf.duration_frames)
        self.assertEqual(nf.notes[0].frame, 1800)
        self.assertEqual(nt.render(nf), text)
        self.assertNotIn("# duration", nt.render(nf))

    def test_crlf_and_bom_are_accepted(self) -> None:
        nf = nt.parse("\ufeff" + SAMPLE.replace("\n", "\r\n"))
        self.assertEqual(len(nf.notes), 3)
        self.assertNotIn("\r", nt.render(nf))

    def test_render_sorts_by_frame_then_order_and_cleans_text(self) -> None:
        nf = nt.NotesFile(
            source="v.mp4",
            fps=25.0,
            duration_frames=None,
            notes=[
                nt.Note("00000001", 100, "ADD", "second\ton\r\nthe frame", order=3),
                nt.Note("00000002", 100, "FIX", "first", order=2),
                nt.Note("00000003", 5, "REMOVE", "early", order=9),
            ],
        )
        lines = nt.render(nf).split("\n")
        self.assertEqual(lines[3], "0:00:00:05\tREMOVE\tearly")
        self.assertEqual(lines[4], "0:00:04:00\tFIX\tfirst")
        self.assertEqual(lines[5], "0:00:04:00\tADD\tsecond on  the frame")
        self.assertTrue(nt.render(nf).endswith("\n"))
        self.assertEqual(nt.normalize_text("  a\tb\nc \r"), "a b c")

    def test_format_errors(self) -> None:
        with self.assertRaises(nt.NotesFormatError) as ctx:
            nt.parse("# Frame Notes 2\n# source: v.mp4\n# fps: 25\n")
        self.assertIn("newer", str(ctx.exception))
        for bad in (
            "",
            "hello\n",
            "# Frame Notes 1\n# fps: 25\n",
            "# Frame Notes 1\n# source: v.mp4\n",
            "# Frame Notes 1\n# source: v.mp4\n# fps: fast\n",
        ):
            with self.subTest(bad=bad), self.assertRaises(nt.NotesFormatError):
                nt.parse(bad)
        self.assertTrue(issubclass(nt.NotesFormatError, ValueError))

    def test_ids_are_unique_hex(self) -> None:
        taken: set[str] = set()
        for _ in range(500):
            value = nt.new_id(taken)
            self.assertRegex(value, r"^[0-9a-f]{8}$")
            self.assertNotIn(value, taken)
            taken.add(value)


class ReadableTest(unittest.TestCase):
    def test_export_of_the_sample(self) -> None:
        nf = nt.parse(SAMPLE)
        self.assertEqual(
            nt.render_readable(nf),
            "# promo_v3.mp4\n"
            "\n"
            "25 fps · 0:01:32:10 · FIX 1 · ADD 1 · REMOVE 1\n"
            "\n"
            "- **0:00:12:07** · FIX · logo flickers for two frames\n"
            "  ![](frames/0-00-12-07_FIX.png)\n"
            "- **0:00:40:00** · ADD · subtitle for the second speaker\n"
            "- **0:01:02:15** · REMOVE · second take of the handshake\n"
            "  ![](frames/0-01-02-15_REMOVE.png)\n",
        )
        self.assertNotIn("![]", nt.render_readable(nf, images=False))

    def test_empty_unknown_duration_drop_frame_and_odd_names(self) -> None:
        nf = nt.NotesFile(source="clip.mp4", fps=30000 / 1001, duration_frames=None)
        self.assertEqual(nt.render_readable(nf), "# clip.mp4\n\n29.97 fps · FIX 0 · ADD 0 · REMOVE 0\n\nNo notes.\n")
        nf.notes = [nt.Note("a", 1800, "ADD", "", image="my frame.png"), nt.Note("b", 1, "FIX", "x", image="..\\y.png")]
        nf.extra = ["hand written"]
        text = nt.render_readable(nf)
        self.assertIn("- **0;00;00;01** · FIX · x\n- **0;01;00;02** · ADD\n  ![](<frames/my frame.png>)\n", text)
        self.assertNotIn("hand written", text)


class FolderTest(unittest.TestCase):
    DATA = ("# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
            "0:00:01:00\tFIX\ta\trect 0.100,0.100 0.200,0.200\t0-00-01-00_FIX.png\n0:00:02:00\tADD\tb\n")

    def setUp(self) -> None:
        self.dir = Path(tempfile.mkdtemp(prefix="fn_notes_"))
        self.addCleanup(shutil.rmtree, self.dir, True)
        self.video = self.dir / "promo.mp4"
        self.video.write_bytes(b"video")
        self.log = logging.getLogger("fn_notes_test")
        self.log.addHandler(logging.NullHandler())
        self.log.propagate = False

    def make_review(self) -> Path:
        folder = self.dir / "promo_notes"
        (folder / "frames").mkdir(parents=True)
        (folder / "promo_notes.txt").write_text(self.DATA, encoding="utf-8")
        (folder / "promo_notes.md").write_text("# promo.mp4\n", encoding="utf-8")
        (folder / "frames" / "0-00-01-00_FIX.png").write_bytes(b"png")
        return folder

    def test_summary(self) -> None:
        self.assertIsNone(nt.notes_summary(self.video))
        (self.dir / "promo_notes.txt").write_text(self.DATA, encoding="utf-8")
        self.assertEqual(nt.notes_summary(self.video), {"notes": 2, "frames": 1})
        (self.dir / "promo_notes.txt").unlink()
        self.make_review()
        self.assertEqual(nt.notes_summary(self.video), {"notes": 2, "frames": 1})
        (self.dir / "promo_notes" / "promo_notes.txt").write_text(self.DATA.replace("promo.mp4", "promo.mov"),
                                                                 encoding="utf-8")
        self.assertIsNone(nt.notes_summary(self.video))

    def test_delete_review(self) -> None:
        folder = self.make_review()
        (folder / "frames" / "x.png.part").write_bytes(b"")
        (folder / "Thumbs.db").write_bytes(b"")
        result = nt.delete_notes(self.video, self.log)
        self.assertEqual(result, {"notes": 2, "frames": 1, "path": str(folder)})
        self.assertEqual([p.name for p in self.dir.iterdir()], ["promo.mp4"])
        with self.assertRaises(nt.NotesLayoutError) as ctx:
            nt.delete_notes(self.video, self.log)
        self.assertEqual(str(ctx.exception), "There are no notes for promo.mp4.")

    def test_delete_legacy_layout(self) -> None:
        (self.dir / "promo_notes.txt").write_text(self.DATA, encoding="utf-8")
        (self.dir / "promo_notes").mkdir()
        (self.dir / "promo_notes" / "0-00-01-00_FIX.png").write_bytes(b"png")
        (self.dir / "other_notes.txt").write_text("keep", encoding="utf-8")
        self.assertEqual(nt.delete_notes(self.video, self.log)["frames"], 1)
        self.assertEqual(sorted(p.name for p in self.dir.iterdir()), ["other_notes.txt", "promo.mp4"])

    def test_delete_refuses_foreign_content_and_other_videos(self) -> None:
        folder = self.make_review()
        (folder / "my edit.prproj").write_text("keep", encoding="utf-8")
        with self.assertRaises(nt.NotesLayoutError) as ctx:
            nt.delete_notes(self.video, self.log)
        self.assertEqual(str(ctx.exception), "The notes folder promo_notes holds other files. Delete it by hand.")
        (folder / "my edit.prproj").unlink()
        (folder / "frames" / "sub").mkdir()
        with self.assertRaises(nt.NotesLayoutError):
            nt.delete_notes(self.video, self.log)
        (folder / "frames" / "sub").rmdir()
        (folder / "promo_notes.txt").write_text(self.DATA.replace("promo.mp4", "promo.mov"), encoding="utf-8")
        with self.assertRaises(nt.NotesLayoutError) as ctx:
            nt.delete_notes(self.video, self.log)
        self.assertEqual(str(ctx.exception), "The notes folder promo_notes belongs to promo.mov.")
        # nothing was removed by any refusal
        self.assertEqual(sorted(p.name for p in folder.iterdir()), ["frames", "promo_notes.md", "promo_notes.txt"])
        self.assertTrue((folder / "frames" / "0-00-01-00_FIX.png").is_file())

    def test_delete_refuses_a_file_in_place_of_the_folder(self) -> None:
        (self.dir / "promo_notes").write_text("not a folder", encoding="utf-8")
        with self.assertRaises(nt.NotesLayoutError):
            nt.delete_notes(self.video, self.log)
        self.assertTrue((self.dir / "promo_notes").is_file())


if __name__ == "__main__":
    unittest.main()
