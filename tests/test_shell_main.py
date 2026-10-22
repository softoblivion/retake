"""Tests of the fixed window geometry helpers of frame_notes.shell.main."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.shell.main import WINDOW_SIZES, choose_window_size, restore_position  # noqa: E402


class ChooseWindowSizeTest(unittest.TestCase):
    def test_sizes_are_16_10_and_descending(self) -> None:
        for width, height in WINDOW_SIZES:
            self.assertEqual(width * 10, height * 16)
        self.assertEqual(list(WINDOW_SIZES), sorted(WINDOW_SIZES, reverse=True))

    def test_large_work_area_gets_the_full_size(self) -> None:
        self.assertEqual(choose_window_size(1920, 1032), (1280, 800))
        self.assertEqual(choose_window_size(1280, 800), (1280, 800))

    def test_first_step_that_fits_wins(self) -> None:
        self.assertEqual(choose_window_size(1920, 780), (1152, 720))   # too low for 800, enough for 720
        self.assertEqual(choose_window_size(1200, 1000), (1152, 720))  # too narrow for 1280
        self.assertEqual(choose_window_size(1100, 700), (1024, 640))
        self.assertEqual(choose_window_size(1000, 620), (960, 600))

    def test_both_dimensions_must_fit(self) -> None:
        self.assertEqual(choose_window_size(1280, 799), (1152, 720))
        self.assertEqual(choose_window_size(1279, 800), (1152, 720))

    def test_last_step_when_nothing_fits(self) -> None:
        self.assertEqual(choose_window_size(800, 500), (960, 600))
        self.assertEqual(choose_window_size(0, 0), (960, 600))

    def test_scaled_work_area_is_compared_in_logical_pixels(self) -> None:
        # 1920x1080 physical at 150 %: 1280x720 logical minus a 48 px taskbar = 1280x672
        self.assertEqual(choose_window_size(1920 / 1.5, 1032 / 1.5), (1024, 640))


class RestorePositionTest(unittest.TestCase):
    SCREEN = (0, 0, 1920, 1080)

    def test_position_on_screen_is_kept(self) -> None:
        self.assertEqual(restore_position(100, 50, 1280, 800, self.SCREEN, 1.0), (100, 50))
        self.assertEqual(restore_position(-300, 0, 1280, 800, self.SCREEN, 1.0), (-300, 0))

    def test_missing_or_invalid_position_is_dropped(self) -> None:
        for x, y in ((None, None), (10, None), ("a", 5), (True, 5), (1.5, 2)):
            self.assertEqual(restore_position(x, y, 1280, 800, self.SCREEN, 1.0), (None, None))

    def test_position_off_every_monitor_is_dropped(self) -> None:
        self.assertEqual(restore_position(4000, 50, 1280, 800, self.SCREEN, 1.0), (None, None))
        self.assertEqual(restore_position(-1500, 50, 1280, 800, self.SCREEN, 1.0), (None, None))
        self.assertEqual(restore_position(100, 1200, 1280, 800, self.SCREEN, 1.0), (None, None))
        self.assertEqual(restore_position(100, -40, 1280, 800, self.SCREEN, 1.0), (None, None))

    def test_dpi_scale_applies_to_the_logical_position(self) -> None:
        self.assertEqual(restore_position(1250, 100, 1280, 800, self.SCREEN, 1.5), (None, None))  # 1875 px > 1920 - 100
        self.assertEqual(restore_position(1000, 100, 1280, 800, self.SCREEN, 1.5), (1000, 100))

    def test_unknown_screen_keeps_the_position(self) -> None:
        self.assertEqual(restore_position(100, 50, 1280, 800, None, 1.0), (100, 50))


if __name__ == "__main__":
    unittest.main()
