"""Tests of core/selfupdate.py: the Frame Notes configuration of the vendored updater (no network)."""

from __future__ import annotations

import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes import __version__  # noqa: E402
from frame_notes.core import selfupdate  # noqa: E402
from frame_notes.core.paths import ENV_HOME, AppPaths  # noqa: E402


class BuildUpdaterTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_su_"))
        self._old_home = os.environ.get(ENV_HOME)
        os.environ[ENV_HOME] = str(self.tmp / "data")

    def tearDown(self) -> None:
        if self._old_home is None:
            os.environ.pop(ENV_HOME, None)
        else:
            os.environ[ENV_HOME] = self._old_home
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_config_values_and_idle_status(self) -> None:
        paths = AppPaths()
        events: list[dict] = []

        def on_status(status: dict) -> None:
            events.append(status)

        updater = selfupdate.build_updater(paths, __version__, on_status)
        cfg = updater.config
        self.assertEqual(cfg.repo, "softoblivion/frame-notes")
        self.assertEqual(cfg.asset_name, "FrameNotes.exe")
        self.assertEqual(cfg.app_name, "Frame Notes")
        self.assertEqual(cfg.version, __version__)
        self.assertEqual(cfg.updates_dir, str(paths.updates_dir))
        self.assertIsNone(cfg.exe_path)
        self.assertIs(cfg.on_status, on_status)
        self.assertIsNone(cfg.on_toast)
        self.assertEqual(cfg.release_url_env, "FRAME_NOTES_RELEASE_URL")
        self.assertEqual(cfg.logger_name, "frame_notes.selfupdate")
        self.assertEqual(cfg.legacy_files, ())
        self.assertEqual(updater.status()["stage"], "idle")
        self.assertEqual(events, [])

    def test_constants_and_reexports(self) -> None:
        self.assertEqual(selfupdate.REPO, "softoblivion/frame-notes")
        self.assertEqual(selfupdate.ASSET_NAME, "FrameNotes.exe")
        self.assertEqual(selfupdate.APP_NAME, "Frame Notes")
        self.assertEqual(selfupdate.RELEASE_URL_ENV, "FRAME_NOTES_RELEASE_URL")
        self.assertEqual(selfupdate.APPLY_FLAG, "--apply-update")
        self.assertTrue(callable(selfupdate.run_apply))
        self.assertTrue(callable(selfupdate.UpdateConfig))
        self.assertTrue(callable(selfupdate.SelfUpdater))

    def test_scheduler_and_interval_keys(self) -> None:
        from frame_notes.shell import bridge

        updater = selfupdate.build_updater(AppPaths(), "1.0.0", lambda status: None)
        scheduler = selfupdate.build_scheduler(updater, "6h")
        self.assertIsInstance(scheduler, selfupdate.UpdateScheduler)
        self.assertEqual(scheduler.interval_key, "6h")
        self.assertIs(scheduler.updater, updater)
        self.assertEqual(scheduler.log.name, "frame_notes.selfupdate.schedule")
        self.assertIsNone(scheduler.next_check_at)
        self.assertEqual(selfupdate.build_scheduler(updater, "bogus").interval_key, "30m")
        self.assertEqual(selfupdate.DEFAULT_CHECK_INTERVAL, "30m")
        self.assertEqual(selfupdate.INTERVAL_KEYS, ("30m", "1h", "6h", "1d", "1w"))
        self.assertEqual(bridge.INTERVAL_KEYS, selfupdate.INTERVAL_KEYS)

    def test_toast_callback_is_passed_through(self) -> None:
        paths = AppPaths()

        def toast(level: str, text: str) -> None:
            pass

        updater = selfupdate.build_updater(paths, "1.0.0", lambda status: None, toast)
        self.assertIs(updater.config.on_toast, toast)


if __name__ == "__main__":
    unittest.main()
