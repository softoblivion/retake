"""Tests of core/probe.py with MP4 and WebM files built in code (no binary fixtures)."""

from __future__ import annotations

import os
import random
import shutil
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from frame_notes.core import probe as pr  # noqa: E402
from frame_notes.core.timecode import nominal_rate  # noqa: E402


# --- MP4 writer -------------------------------------------------------------------------------------------


def box(kind: bytes, payload: bytes = b"", large: bool = False) -> bytes:
    if large:
        return struct.pack(">I4sQ", 1, kind, 16 + len(payload)) + payload
    return struct.pack(">I4s", 8 + len(payload), kind) + payload


def full(version: int, body: bytes) -> bytes:
    return bytes([version, 0, 0, 0]) + body


def mvhd(timescale: int, duration: int, version: int = 0) -> bytes:
    if version == 1:
        return box(b"mvhd", full(1, struct.pack(">QQIQ", 0, 0, timescale, duration) + b"\0" * 80))
    return box(b"mvhd", full(0, struct.pack(">IIII", 0, 0, timescale, duration) + b"\0" * 80))


def tkhd(width: int, height: int, version: int = 0) -> bytes:
    if version == 1:
        head = struct.pack(">QQIIQ", 0, 0, 1, 0, 0)
    else:
        head = struct.pack(">IIIII", 0, 0, 1, 0, 0)
    body = head + b"\0" * 8 + b"\0" * 8 + b"\0" * 36 + struct.pack(">II", width << 16, height << 16)
    return box(b"tkhd", full(version, body))


def mdhd(timescale: int, duration: int, version: int = 0) -> bytes:
    if version == 1:
        return box(b"mdhd", full(1, struct.pack(">QQIQ", 0, 0, timescale, duration) + b"\0\0\0\0"))
    return box(b"mdhd", full(0, struct.pack(">IIII", 0, 0, timescale, duration) + b"\0\0\0\0"))


def hdlr(handler: bytes) -> bytes:
    return box(b"hdlr", full(0, struct.pack(">I4s", 0, handler) + b"\0" * 12 + b"name\0"))


def stts(entries: list[tuple[int, int]]) -> bytes:
    body = struct.pack(">I", len(entries)) + b"".join(struct.pack(">II", c, d) for c, d in entries)
    return box(b"stts", full(0, body))


def trak(handler: bytes, width: int, height: int, timescale: int, duration: int,
         entries: list[tuple[int, int]], version: int = 0) -> bytes:
    stbl = box(b"stbl", box(b"stsd", full(0, struct.pack(">I", 0))) + stts(entries))
    minf = box(b"minf", stbl)
    mdia = box(b"mdia", mdhd(timescale, duration, version) + hdlr(handler) + minf)
    return box(b"trak", tkhd(width, height, version) + mdia)


def build_mp4(video_entries: list[tuple[int, int]], video_timescale: int, *, movie_timescale: int = 1000,
              movie_duration: int = 0, width: int = 1920, height: int = 1080, version: int = 0,
              moov_last: bool = False, big_mdat: bool = False, mdat_to_end: bool = False) -> bytes:
    frames = sum(c for c, _ in video_entries)
    video_duration = sum(c * d for c, d in video_entries)
    audio = trak(b"soun", 0, 0, 48000, 48000 * 3, [(144, 1024)], version)
    video = trak(b"vide", width, height, video_timescale, video_duration, video_entries, version)
    moov = box(b"moov", mvhd(movie_timescale, movie_duration, version) + audio + video)
    ftyp = box(b"ftyp", b"isom\0\0\2\0isomavc1")
    payload = b"\x00" * 64
    if mdat_to_end:
        mdat = struct.pack(">I4s", 0, b"mdat") + payload
    else:
        mdat = box(b"mdat", payload, large=big_mdat)
    if moov_last:
        return ftyp + mdat + moov
    return ftyp + moov + mdat


# --- WebM writer ------------------------------------------------------------------------------------------


def ebml_id(value: int) -> bytes:
    return value.to_bytes((value.bit_length() + 7) // 8, "big")


def ebml_size(size: int | None) -> bytes:
    if size is None:
        return b"\x01\xff\xff\xff\xff\xff\xff\xff"
    for length in range(1, 9):
        if size < (1 << (7 * length)) - 1:
            return (size | (1 << (7 * length))).to_bytes(length, "big")
    raise ValueError(size)


def el(element_id: int, payload: bytes, unknown_size: bool = False) -> bytes:
    return ebml_id(element_id) + ebml_size(None if unknown_size else len(payload)) + payload


def uint(value: int) -> bytes:
    return value.to_bytes(max(1, (value.bit_length() + 7) // 8), "big")


def build_webm(fps: float | None, width: int, height: int, duration_ms: float | None = 3000.0,
               timescale: int | None = 1_000_000) -> bytes:
    header = el(0x1A45DFA3, el(0x4282, b"webm"))
    info_children = b""
    if timescale is not None:
        info_children += el(0x2AD7B1, uint(timescale))
    if duration_ms is not None:
        info_children += el(0x4489, struct.pack(">d", duration_ms))
    info = el(0x1549A966, info_children)
    audio_entry = el(0xAE, el(0xD7, uint(1)) + el(0x83, uint(2)) + el(0x23E383, uint(20_000_000)))
    video_children = el(0xD7, uint(2)) + el(0x83, uint(1))
    if fps is not None:
        video_children += el(0x23E383, uint(round(1e9 / fps)))
    video_children += el(0xE0, el(0xB0, uint(width)) + el(0xBA, uint(height)))
    tracks = el(0x1654AE6B, audio_entry + el(0xAE, video_children))
    # A Cluster carries bogus data that must never be read.
    cluster = el(0x1F43B675, el(0xE7, uint(0)) + el(0x1549A966, el(0x4489, struct.pack(">d", 1.0))))
    return header + el(0x18538067, info + tracks + cluster, unknown_size=True)


class ProbeTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_probe_"))
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def write(self, name: str, data: bytes) -> Path:
        path = self.tmp / name
        path.write_bytes(data)
        return path


class Mp4Test(ProbeTestCase):
    def test_25_fps(self) -> None:
        data = build_mp4([(2310, 512)], 12800, movie_timescale=1000, movie_duration=92400)
        r = pr.probe(self.write("a.mp4", data))
        self.assertEqual(r.container, "mp4")
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)
        self.assertEqual((r.width, r.height, r.frames), (1920, 1080, 2310))
        self.assertAlmostEqual(r.duration or 0, 92.4, places=6)

    def test_29_97_from_stts_delta_1001(self) -> None:
        data = build_mp4([(900, 1001)], 30000, movie_duration=30030)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.fps or 0, 30000 / 1001, places=6)
        self.assertEqual(nominal_rate(r.fps or 0), 29.97)
        self.assertEqual(r.frames, 900)

    def test_23_976(self) -> None:
        data = build_mp4([(240, 1001)], 24000, movie_duration=10010, width=1280, height=720)
        r = pr.probe(self.write("a.mp4", data))
        self.assertEqual(nominal_rate(r.fps or 0), 23.976)
        self.assertEqual((r.width, r.height), (1280, 720))

    def test_most_frequent_delta_wins_and_frames_are_summed(self) -> None:
        data = build_mp4([(1, 1000), (100, 512), (3, 700)], 12800)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)
        self.assertEqual(r.frames, 104)

    def test_audio_track_before_video_is_skipped(self) -> None:
        data = build_mp4([(100, 512)], 12800)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)
        self.assertEqual(r.frames, 100)  # the audio track has 144 samples

    def test_version_1_boxes(self) -> None:
        data = build_mp4([(100, 512)], 12800, movie_duration=4000, version=1)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)
        self.assertEqual((r.width, r.height), (1920, 1080))
        self.assertAlmostEqual(r.duration or 0, 4.0, places=6)

    def test_moov_after_mdat_with_64_bit_size(self) -> None:
        data = build_mp4([(100, 512)], 12800, moov_last=True, big_mdat=True)
        r = pr.probe(self.write("a.mp4", data))
        self.assertEqual(r.container, "mp4")
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)

    def test_mdat_running_to_end_of_file(self) -> None:
        # moov in front, mdat with size 0 behind it
        data = build_mp4([(100, 512)], 12800, mdat_to_end=True)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)

    def test_duration_falls_back_to_the_track(self) -> None:
        data = build_mp4([(100, 512)], 12800, movie_duration=0)
        r = pr.probe(self.write("a.mp4", data))
        self.assertAlmostEqual(r.duration or 0, 4.0, places=6)

    def test_truncated_file_does_not_raise(self) -> None:
        data = build_mp4([(100, 512)], 12800)
        for cut in (3, 8, 20, 60, len(data) - 100):
            r = pr.probe(self.write("cut.mp4", data[:cut]))
            self.assertIn(r.container, ("mp4", "unknown"))


class WebmTest(ProbeTestCase):
    def test_25_fps(self) -> None:
        r = pr.probe(self.write("a.webm", build_webm(25.0, 1920, 1080, 3000.0)))
        self.assertEqual(r.container, "webm")
        self.assertAlmostEqual(r.fps or 0, 25.0, places=6)
        self.assertEqual((r.width, r.height), (1920, 1080))
        self.assertAlmostEqual(r.duration or 0, 3.0, places=6)
        self.assertIsNone(r.frames)

    def test_29_97_and_23_976(self) -> None:
        r = pr.probe(self.write("a.webm", build_webm(30000 / 1001, 640, 360)))
        self.assertEqual(nominal_rate(r.fps or 0), 29.97)
        r = pr.probe(self.write("b.webm", build_webm(24000 / 1001, 640, 360)))
        self.assertEqual(nominal_rate(r.fps or 0), 23.976)

    def test_timecode_scale(self) -> None:
        r = pr.probe(self.write("a.webm", build_webm(25.0, 320, 240, duration_ms=5000.0, timescale=500_000)))
        self.assertAlmostEqual(r.duration or 0, 2.5, places=6)

    def test_missing_fields(self) -> None:
        r = pr.probe(self.write("a.webm", build_webm(None, 320, 240, duration_ms=None, timescale=None)))
        self.assertEqual(r.container, "webm")
        self.assertIsNone(r.fps)
        self.assertIsNone(r.duration)
        self.assertEqual((r.width, r.height), (320, 240))

    def test_truncated_file_does_not_raise(self) -> None:
        data = build_webm(25.0, 1920, 1080)
        for cut in (4, 5, 20, 40, 70):
            r = pr.probe(self.write("cut.webm", data[:cut]))
            self.assertEqual(r.container, "webm")


class UnknownTest(ProbeTestCase):
    def test_random_bytes(self) -> None:
        rng = random.Random(7)
        for i in range(20):
            r = pr.probe(self.write(f"r{i}.bin", bytes(rng.randrange(256) for _ in range(4096))))
            self.assertEqual(r.container, "unknown")
            self.assertEqual((r.fps, r.duration, r.width, r.height, r.frames), (None,) * 5)

    def test_empty_and_text_files(self) -> None:
        for name, data in (("e.bin", b""), ("t.txt", b"hello world, this is not a video\n" * 10)):
            r = pr.probe(self.write(name, data))
            self.assertEqual(r.container, "unknown")
            self.assertIsNone(r.fps)

    def test_missing_file_raises_oserror(self) -> None:
        with self.assertRaises(OSError):
            pr.probe(self.tmp / "nope.mp4")

    def test_directory_raises_oserror(self) -> None:
        with self.assertRaises(OSError):
            pr.probe(self.tmp)


if __name__ == "__main__":
    unittest.main()
