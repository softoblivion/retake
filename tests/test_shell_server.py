"""Tests of the local HTTP server (frame_notes.shell.server). No window, only loopback sockets."""

from __future__ import annotations

import http.client
import shutil
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT / "src") not in sys.path:
    sys.path.insert(0, str(ROOT / "src"))

from frame_notes.shell.server import MediaServer, parse_range  # noqa: E402

PAYLOAD = bytes(range(256)) * 8  # 2048 bytes, every offset has a distinct-enough value


class ServerCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="fn_server_"))
        self.ui = self.tmp / "ui"
        (self.ui / "js").mkdir(parents=True)
        (self.ui / "index.html").write_text("<!doctype html><title>x</title>", encoding="utf-8")
        (self.ui / "js" / "app.js").write_bytes(b"export const a = 1;\n")
        (self.tmp / "secret.txt").write_text("outside", encoding="utf-8")
        self.video = self.tmp / "clip one.mp4"
        self.video.write_bytes(PAYLOAD)
        self.images = self.tmp / "clip one_notes"
        self.images.mkdir()
        (self.images / "0-00-01-00_FIX.png").write_bytes(b"\x89PNG\r\n\x1a\nfake")
        self.server = MediaServer(self.ui)
        self.server.start()
        self.token = self.server.set_media(self.video, self.images)

    def tearDown(self) -> None:
        self.server.stop()
        for _ in range(20):
            shutil.rmtree(self.tmp, ignore_errors=True)
            if not self.tmp.exists():
                break
            time.sleep(0.05)

    def request(self, method: str, path: str, headers: dict | None = None):
        conn = http.client.HTTPConnection("127.0.0.1", self.server.port, timeout=5)
        try:
            conn.request(method, path, headers=headers or {})
            response = conn.getresponse()
            body = response.read()
            return response, body
        finally:
            conn.close()

    def media_path(self) -> str:
        return f"/media/{self.token}/clip%20one.mp4"


class PortAndUiTests(ServerCase):
    def test_port_in_range_and_loopback(self) -> None:
        self.assertGreaterEqual(self.server.port, 42011)
        self.assertLessEqual(self.server.port, 42030)
        self.assertEqual(self.server.base_url, f"http://127.0.0.1:{self.server.port}")

    def test_second_server_takes_next_port(self) -> None:
        other = MediaServer(self.ui)
        try:
            other.start()
            self.assertNotEqual(other.port, self.server.port)
        finally:
            other.stop()

    def test_index_and_mime(self) -> None:
        response, body = self.request("GET", "/")
        self.assertEqual(response.status, 200)
        self.assertIn(b"<title>x</title>", body)
        self.assertTrue(response.getheader("Content-Type").startswith("text/html"))
        self.assertEqual(response.getheader("Cache-Control"), "no-store")
        response, body = self.request("GET", "/js/app.js?v=1")
        self.assertEqual(response.status, 200)
        self.assertTrue(response.getheader("Content-Type").startswith("text/javascript"))
        self.assertEqual(body, b"export const a = 1;\n")

    def test_missing_file_and_directory_are_404(self) -> None:
        self.assertEqual(self.request("GET", "/nope.js")[0].status, 404)
        self.assertEqual(self.request("GET", "/js/")[0].status, 404)
        self.assertEqual(self.request("GET", "/js")[0].status, 404)

    def test_dotdot_paths_are_404(self) -> None:
        for path in ("/../secret.txt", "/js/../../secret.txt", "/%2e%2e/secret.txt", "/..%2fsecret.txt",
                     "/js/..%5c..%5csecret.txt", "/C:/Windows/win.ini", "/%2e%2e%5csecret.txt"):
            with self.subTest(path=path):
                response, body = self.request("GET", path)
                self.assertEqual(response.status, 404)
                self.assertNotIn(b"outside", body)

    def test_foreign_host_header_is_rejected(self) -> None:
        response, _ = self.request("GET", "/", headers={"Host": "evil.example"})
        self.assertEqual(response.status, 404)

    def test_post_is_not_allowed(self) -> None:
        self.assertEqual(self.request("POST", "/")[0].status, 405)


class MediaTests(ServerCase):
    def test_full_get(self) -> None:
        response, body = self.request("GET", self.media_path())
        self.assertEqual(response.status, 200)
        self.assertEqual(body, PAYLOAD)
        self.assertEqual(response.getheader("Accept-Ranges"), "bytes")
        self.assertEqual(response.getheader("Content-Type"), "video/mp4")
        self.assertEqual(response.getheader("Content-Length"), str(len(PAYLOAD)))

    def test_range_closed(self) -> None:
        response, body = self.request("GET", self.media_path(), {"Range": "bytes=10-19"})
        self.assertEqual(response.status, 206)
        self.assertEqual(body, PAYLOAD[10:20])
        self.assertEqual(response.getheader("Content-Range"), f"bytes 10-19/{len(PAYLOAD)}")
        self.assertEqual(response.getheader("Content-Length"), "10")

    def test_range_open_ended(self) -> None:
        response, body = self.request("GET", self.media_path(), {"Range": "bytes=100-"})
        self.assertEqual(response.status, 206)
        self.assertEqual(body, PAYLOAD[100:])
        self.assertEqual(response.getheader("Content-Range"), f"bytes 100-{len(PAYLOAD) - 1}/{len(PAYLOAD)}")

    def test_range_suffix_and_clamped_end(self) -> None:
        response, body = self.request("GET", self.media_path(), {"Range": "bytes=-16"})
        self.assertEqual(response.status, 206)
        self.assertEqual(body, PAYLOAD[-16:])
        response, body = self.request("GET", self.media_path(), {"Range": "bytes=2000-99999"})
        self.assertEqual(response.status, 206)
        self.assertEqual(body, PAYLOAD[2000:])

    def test_range_unsatisfiable(self) -> None:
        response, _ = self.request("GET", self.media_path(), {"Range": f"bytes={len(PAYLOAD)}-"})
        self.assertEqual(response.status, 416)
        self.assertEqual(response.getheader("Content-Range"), f"bytes */{len(PAYLOAD)}")

    def test_head(self) -> None:
        response, body = self.request("HEAD", self.media_path())
        self.assertEqual(response.status, 200)
        self.assertEqual(body, b"")
        self.assertEqual(response.getheader("Content-Length"), str(len(PAYLOAD)))
        self.assertEqual(response.getheader("Accept-Ranges"), "bytes")
        response, body = self.request("HEAD", self.media_path(), {"Range": "bytes=0-9"})
        self.assertEqual(response.status, 206)
        self.assertEqual(body, b"")
        self.assertEqual(response.getheader("Content-Length"), "10")

    def test_stale_token_is_404(self) -> None:
        old = self.media_path()
        new_token = self.server.set_media(self.video, self.images)
        self.assertNotEqual(new_token, self.token)
        self.assertEqual(len(new_token), 32)
        self.assertEqual(self.request("GET", old)[0].status, 404)
        self.assertEqual(self.request("GET", f"/media/{new_token}/clip%20one.mp4")[0].status, 200)
        self.assertEqual(self.request("GET", f"/image/{self.token}/0-00-01-00_FIX.png")[0].status, 404)

    def test_wrong_name_and_bad_paths_are_404(self) -> None:
        for path in (f"/media/{self.token}/other.mp4", f"/media/{self.token}", f"/media/{self.token}/a/b",
                     f"/media/{self.token}/..%2fsecret.txt", "/media/", "/media"):
            with self.subTest(path=path):
                self.assertEqual(self.request("GET", path)[0].status, 404)

    def test_media_url_and_clear(self) -> None:
        self.assertEqual(self.server.media_url(), f"{self.server.base_url}/media/{self.token}/clip%20one.mp4")
        self.server.clear_media()
        self.assertIsNone(self.server.media_url())
        self.assertEqual(self.request("GET", self.media_path())[0].status, 404)

    def test_moved_video_is_404(self) -> None:
        self.video.unlink()
        self.assertEqual(self.request("GET", self.media_path())[0].status, 404)


class ImageTests(ServerCase):
    def test_image_route(self) -> None:
        response, body = self.request("GET", f"/image/{self.token}/0-00-01-00_FIX.png")
        self.assertEqual(response.status, 200)
        self.assertEqual(response.getheader("Content-Type"), "image/png")
        self.assertTrue(body.startswith(b"\x89PNG"))
        self.assertEqual(response.getheader("Cache-Control"), "no-store")
        self.assertEqual(self.server.image_url("0-00-01-00_FIX.png"),
                         f"{self.server.base_url}/image/{self.token}/0-00-01-00_FIX.png")

    def test_image_missing_or_unsafe(self) -> None:
        for name in ("missing.png", "..%2fsecret.txt", "..%5csecret.txt", "..", "0-00-01-00_FIX.txt"):
            with self.subTest(name=name):
                self.assertEqual(self.request("GET", f"/image/{self.token}/{name}")[0].status, 404)

    def test_image_created_after_set_media(self) -> None:
        (self.images / "0-00-02-00_ADD.png").write_bytes(b"\x89PNGlater")
        self.assertEqual(self.request("GET", f"/image/{self.token}/0-00-02-00_ADD.png")[0].status, 200)


class ParseRangeTests(unittest.TestCase):
    def test_cases(self) -> None:
        self.assertEqual(parse_range("bytes=0-9", 100), (0, 9))
        self.assertEqual(parse_range("bytes=90-", 100), (90, 99))
        self.assertEqual(parse_range("bytes=-10", 100), (90, 99))
        self.assertEqual(parse_range("bytes=-500", 100), (0, 99))
        self.assertEqual(parse_range("bytes=50-999", 100), (50, 99))
        self.assertEqual(parse_range("bytes=100-", 100), "unsatisfiable")
        self.assertEqual(parse_range("bytes=-0", 100), "unsatisfiable")
        self.assertIsNone(parse_range("bytes=5-2", 100))
        self.assertIsNone(parse_range("bytes=0-1,5-6", 100))
        self.assertIsNone(parse_range("items=0-1", 100))
        self.assertIsNone(parse_range("bytes=-", 100))


if __name__ == "__main__":
    unittest.main()
