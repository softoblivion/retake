"""Tests of core/timecode.py against the reference values of docs/CORE.md."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.core import timecode as tc  # noqa: E402

TABLE = [
    (25, 0, "0:00:00:00"),
    (25, 307, "0:00:12:07"),
    (25, 90000, "1:00:00:00"),
    (23.976, 24, "0:00:01:00"),
    (29.97, 1799, "0;00;59;29"),
    (29.97, 1800, "0;01;00;02"),
    (29.97, 17982, "0;10;00;00"),
    (29.97, 107892, "1;00;00;00"),
    (59.94, 3600, "0;01;00;04"),
    (120, 125, "0:00:01:005"),
]


class TableTest(unittest.TestCase):
    def test_frame_to_timecode_rows(self) -> None:
        for fps, frame, expected in TABLE:
            with self.subTest(fps=fps, frame=frame):
                self.assertEqual(tc.frame_to_timecode(frame, tc.timecode_base(fps)), expected)

    def test_timecode_to_frame_rows(self) -> None:
        for fps, frame, text in TABLE:
            with self.subTest(fps=fps, text=text):
                self.assertEqual(tc.timecode_to_frame(text, tc.timecode_base(fps)), frame)

    def test_round_trip_every_frame(self) -> None:
        for fps in (25, 23.976, 29.97, 59.94, 30, 120):
            tb = tc.timecode_base(fps)
            for frame in range(0, 130000, 7):
                text = tc.frame_to_timecode(frame, tb)
                self.assertEqual(tc.timecode_to_frame(text, tb), frame, (fps, frame, text))

    def test_drop_frame_numbers_are_continuous(self) -> None:
        tb = tc.timecode_base(29.97)
        seen = [tc.frame_to_timecode(n, tb) for n in range(0, 40000)]
        self.assertEqual(len(set(seen)), len(seen))
        self.assertEqual(seen[1799], "0;00;59;29")
        self.assertEqual(seen[1800], "0;01;00;02")
        self.assertNotIn("0;01;00;00", seen)
        self.assertNotIn("0;01;00;01", seen)
        self.assertIn("0;10;00;00", seen)


class BaseTest(unittest.TestCase):
    def test_base_fields(self) -> None:
        tb = tc.timecode_base(29.97)
        self.assertEqual((tb.base, tb.drop, tb.digits, tb.separator), (30, True, 2, ";"))
        self.assertAlmostEqual(tb.fps, 30000 / 1001, places=9)
        tb = tc.timecode_base(59.94)
        self.assertEqual((tb.base, tb.drop, tb.separator), (60, True, ";"))
        self.assertAlmostEqual(tb.fps, 60000 / 1001, places=9)
        tb = tc.timecode_base(25)
        self.assertEqual((tb.base, tb.drop, tb.digits, tb.separator, tb.fps), (25, False, 2, ":", 25.0))
        tb = tc.timecode_base(120)
        self.assertEqual((tb.base, tb.digits), (120, 3))

    def test_23_976_variants_share_one_base(self) -> None:
        for value in (23.98, 23.976, 24000 / 1001):
            with self.subTest(value=value):
                self.assertEqual(tc.nominal_rate(value), 23.976)
                tb = tc.timecode_base(value)
                self.assertEqual((tb.base, tb.drop), (24, False))
                self.assertEqual(tb.fps, 24000 / 1001)

    def test_nominal_rate_snapping(self) -> None:
        self.assertEqual(tc.nominal_rate(30000 / 1001), 29.97)
        self.assertEqual(tc.nominal_rate(29.97), 29.97)
        self.assertEqual(tc.nominal_rate(30.0), 30.0)
        self.assertEqual(tc.nominal_rate(25.0003), 25.0)
        self.assertEqual(tc.nominal_rate(24), 24.0)
        self.assertEqual(tc.nominal_rate(15.0), 15.0)
        self.assertEqual(tc.nominal_rate(26.0), 26.0)

    def test_unusual_rate_uses_rounded_base_without_drop(self) -> None:
        tb = tc.timecode_base(15)
        self.assertEqual((tb.base, tb.drop, tb.separator), (15, False, ":"))
        self.assertEqual(tc.frame_to_timecode(31, tb), "0:00:02:01")

    def test_nominal_rate_rejects_bad_values(self) -> None:
        for bad in (0, -5, float("nan"), float("inf")):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                tc.nominal_rate(bad)


class ParseTest(unittest.TestCase):
    def test_short_forms_and_separators(self) -> None:
        tb = tc.timecode_base(25)
        self.assertEqual(tc.timecode_to_frame("12:07", tb), 307)
        self.assertEqual(tc.timecode_to_frame("1:02:03", tb), 1 * 60 * 25 + 2 * 25 + 3)
        self.assertEqual(tc.timecode_to_frame("  0;00;12;07 ", tb), 307)
        self.assertEqual(tc.timecode_to_frame("0:00:12:07", tb), 307)

    def test_value_errors(self) -> None:
        tb = tc.timecode_base(25)
        for bad in ("0:00:12:25", "", "abc", "1", "0:00:60:00", "0:60:00:00", "1:2:3:4:5", "0:00:-1:00", "0:00:12:0x"):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                tc.timecode_to_frame(bad, tb)
        drop = tc.timecode_base(29.97)
        for dropped in ("0;01;00;00", "0;01;00;01", "0;09;00;01"):
            with self.subTest(dropped=dropped), self.assertRaises(ValueError):
                tc.timecode_to_frame(dropped, drop)
        self.assertEqual(tc.timecode_to_frame("0;10;00;00", drop), 17982)
        self.assertEqual(tc.timecode_to_frame("0;00;00;01", drop), 1)
        with self.assertRaises(ValueError):
            tc.frame_to_timecode(-1, tb)


class ConversionTest(unittest.TestCase):
    def test_seconds_and_frames(self) -> None:
        self.assertEqual(tc.seconds_to_frame(12.28, 25), 307)
        self.assertEqual(tc.seconds_to_frame(-1.0, 25), 0)
        self.assertEqual(tc.seconds_to_frame(0.04, 25), 1)
        self.assertAlmostEqual(tc.frame_to_seconds(307, 25), 12.30)
        fps = 30000 / 1001
        for frame in (0, 1, 1799, 100000):
            self.assertEqual(tc.seconds_to_frame(tc.frame_to_seconds(frame, fps), fps), frame)
        self.assertEqual(tc.duration_frames(92.4, 25), 2310)

    def test_fps_text(self) -> None:
        self.assertEqual(tc.format_fps(25), "25")
        self.assertEqual(tc.format_fps(25.0), "25")
        self.assertEqual(tc.format_fps(29.97), "29.97")
        self.assertEqual(tc.format_fps(30000 / 1001), "29.97")
        self.assertEqual(tc.format_fps(24000 / 1001), "23.976")
        self.assertEqual(tc.format_fps(59.94), "59.94")
        self.assertEqual(tc.format_fps(100), "100")

    def test_parse_fps(self) -> None:
        self.assertEqual(tc.parse_fps("25"), 25.0)
        self.assertEqual(tc.parse_fps(" 29.97 "), 29.97)
        self.assertEqual(tc.parse_fps("23.976"), 23.976)
        for bad in ("", "abc", "0", "-3", "1e3", "nan", "inf", "25 fps", "1_0"):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                tc.parse_fps(bad)
        for fps in (25, 29.97, 23.976, 59.94):
            self.assertEqual(tc.parse_fps(tc.format_fps(fps)), fps)


if __name__ == "__main__":
    unittest.main()
