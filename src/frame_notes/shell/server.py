"""Local HTTP server: the UI folder, the opened video and the note images (docs/BRIDGE_API.md, section 4).

One ThreadingHTTPServer on 127.0.0.1 serves the UI and the video from a single origin, so the page may draw the
video into a canvas and read the pixels back. The video and image routes carry a token that changes on every
`set_media` call; a stale token answers 404.
"""

from __future__ import annotations

import logging
import os
import re
import secrets
import threading
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Optional
from urllib.parse import quote, unquote, urlsplit

log = logging.getLogger("frame_notes.server")

PORT_FIRST = 42011
PORT_LAST = 42030
CHUNK = 256 * 1024

MIME = {
    ".html": "text/html; charset=utf-8",
    ".htm": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".mjs": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".map": "application/json; charset=utf-8",
    ".txt": "text/plain; charset=utf-8",
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".webp": "image/webp",
    ".ico": "image/x-icon",
    ".woff": "font/woff",
    ".woff2": "font/woff2",
    ".ttf": "font/ttf",
    ".otf": "font/otf",
    ".mp4": "video/mp4",
    ".m4v": "video/mp4",
    ".mov": "video/quicktime",
    ".webm": "video/webm",
    ".mkv": "video/x-matroska",
}

_RANGE = re.compile(r"^bytes=(\d*)-(\d*)$")


def content_type_for(path: Path) -> str:
    return MIME.get(path.suffix.lower(), "application/octet-stream")


def parse_range(header: str, size: int) -> Optional[tuple[int, int]] | str:
    """Parse a single `bytes=` range against a file of `size` bytes.

    Returns (first, last) inclusive, None when the header is absent or not understood (serve the whole file), or
    the string "unsatisfiable" when the range lies outside the file.
    """
    match = _RANGE.match(header.strip())
    if not match:
        return None
    start_text, end_text = match.groups()
    if not start_text and not end_text:
        return None
    if not start_text:  # suffix range: the last N bytes
        count = int(end_text)
        if count == 0 or size == 0:
            return "unsatisfiable"
        return max(0, size - count), size - 1
    start = int(start_text)
    if start >= size:
        return "unsatisfiable"
    end = int(end_text) if end_text else size - 1
    if end < start:
        return None
    return start, min(end, size - 1)


class _Server(ThreadingHTTPServer):
    daemon_threads = True
    # On Windows SO_REUSEADDR lets a second process bind a port that is already in use, which would defeat the
    # first-free-port search (several instances may run at once).
    allow_reuse_address = False
    request_queue_size = 32

    def __init__(self, address, handler, owner: "MediaServer") -> None:
        self.owner = owner
        super().__init__(address, handler)

    def handle_error(self, request, client_address) -> None:  # noqa: D401 - quiet: aborted connections are normal
        log.debug("connection error from %s", client_address, exc_info=True)


class _Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "FrameNotes"
    sys_version = ""

    # -- logging goes to the app logger at DEBUG, never to stderr
    def log_message(self, format: str, *args) -> None:  # noqa: A002 - signature of the base class
        log.debug("%s %s", self.address_string(), format % args)

    @property
    def _owner(self) -> "MediaServer":
        return self.server.owner  # type: ignore[attr-defined]

    def do_GET(self) -> None:
        self._handle(send_body=True)

    def do_HEAD(self) -> None:
        self._handle(send_body=False)

    def do_POST(self) -> None:
        self._reply(HTTPStatus.METHOD_NOT_ALLOWED, extra={"Allow": "GET, HEAD"})

    do_PUT = do_DELETE = do_PATCH = do_POST

    # -- routing
    def _handle(self, send_body: bool) -> None:
        try:
            if not self._host_ok():
                self._reply(HTTPStatus.NOT_FOUND)
                return
            raw = urlsplit(self.path).path
            segments = [unquote(part) for part in raw.split("/")[1:]]
            if segments and segments[0] == "media":
                target = self._owner._resolve_media(segments[1:])
            elif segments and segments[0] == "image":
                target = self._owner._resolve_image(segments[1:])
            else:
                target = self._owner._resolve_ui(segments)
            if target is None:
                self._reply(HTTPStatus.NOT_FOUND)
                return
            self._send_file(target, send_body)
        except (ConnectionError, TimeoutError):
            self.close_connection = True
        except Exception:  # noqa: BLE001 - keep the server alive
            log.debug("request failed: %s", self.path, exc_info=True)
            self.close_connection = True

    def _host_ok(self) -> bool:
        """Reject foreign Host headers (DNS rebinding): only the loopback name and address are accepted."""
        host = self.headers.get("Host", "")
        name = host.rsplit(":", 1)[0] if ":" in host and not host.endswith("]") else host
        return name.lower() in ("127.0.0.1", "localhost", "[::1]")

    # -- responses
    def _reply(self, status: HTTPStatus, body: bytes = b"", extra: Optional[dict] = None) -> None:
        self.send_response(status.value)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for key, value in (extra or {}).items():
            self.send_header(key, value)
        self.end_headers()
        if body and self.command != "HEAD":
            self.wfile.write(body)

    def _send_file(self, path: Path, send_body: bool) -> None:
        try:
            handle = path.open("rb")
        except OSError:
            self._reply(HTTPStatus.NOT_FOUND)
            return
        with handle:
            size = os.fstat(handle.fileno()).st_size
            ctype = content_type_for(path)
            rng = parse_range(self.headers.get("Range", ""), size) if self.headers.get("Range") else None
            if rng == "unsatisfiable":
                self._reply(HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE, extra={"Content-Range": f"bytes */{size}"})
                return
            self.send_response((HTTPStatus.PARTIAL_CONTENT if rng else HTTPStatus.OK).value)
            self.send_header("Content-Type", ctype)
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            if rng:
                first, last = rng  # type: ignore[misc]
                length = last - first + 1
                self.send_header("Content-Range", f"bytes {first}-{last}/{size}")
            else:
                first, length = 0, size
            self.send_header("Content-Length", str(length))
            self.end_headers()
            if not send_body or length == 0:
                return
            handle.seek(first)
            remaining = length
            while remaining > 0:
                chunk = handle.read(min(CHUNK, remaining))
                if not chunk:
                    break
                self.wfile.write(chunk)
                remaining -= len(chunk)


class MediaServer:
    """Owns the HTTP server, the UI root and the currently opened media."""

    def __init__(self, ui_dir: Path, port_first: int = PORT_FIRST, port_last: int = PORT_LAST) -> None:
        self._ui_root = Path(ui_dir).resolve()
        self._port_first = port_first
        self._port_last = port_last
        self._lock = threading.Lock()
        self._token: Optional[str] = None
        self._video: Optional[Path] = None
        self._images: Optional[Path] = None
        self._httpd: Optional[_Server] = None
        self._thread: Optional[threading.Thread] = None
        self.port = 0

    # -- lifecycle
    def start(self) -> int:
        if self._httpd is not None:
            return self.port
        last_error: Optional[OSError] = None
        for port in range(self._port_first, self._port_last + 1):
            try:
                self._httpd = _Server(("127.0.0.1", port), _Handler, self)
            except OSError as exc:
                last_error = exc
                continue
            self.port = port
            break
        if self._httpd is None:
            raise OSError(f"No free port in {self._port_first}..{self._port_last}: {last_error}")
        self._thread = threading.Thread(target=self._httpd.serve_forever, name="http-server", daemon=True,
                                        kwargs={"poll_interval": 0.2})
        self._thread.start()
        log.info("serving %s on %s", self._ui_root, self.base_url)
        return self.port

    def stop(self) -> None:
        httpd, self._httpd = self._httpd, None
        if httpd is None:
            return
        try:
            httpd.shutdown()
            httpd.server_close()
        except OSError:
            log.debug("server close failed", exc_info=True)
        if self._thread is not None:
            self._thread.join(timeout=2)
            self._thread = None

    @property
    def base_url(self) -> str:
        return f"http://127.0.0.1:{self.port}"

    # -- media
    def set_media(self, video_path: Optional[Path], images_dir: Optional[Path]) -> str:
        """Register the opened video and its images folder. Returns the new token; the previous one stops working."""
        token = secrets.token_hex(16)
        with self._lock:
            self._token = token
            self._video = Path(video_path) if video_path else None
            self._images = Path(images_dir) if images_dir else None
        return token

    def clear_media(self) -> None:
        with self._lock:
            self._token = None
            self._video = None
            self._images = None

    @property
    def token(self) -> Optional[str]:
        return self._token

    def media_url(self) -> Optional[str]:
        with self._lock:
            token, video = self._token, self._video
        if not token or video is None:
            return None
        return f"{self.base_url}/media/{token}/{quote(video.name)}"

    def image_url(self, name: str) -> Optional[str]:
        with self._lock:
            token, images = self._token, self._images
        if not token or images is None or not name:
            return None
        return f"{self.base_url}/image/{token}/{quote(name)}"

    # -- request resolution (called from handler threads)
    def _resolve_media(self, parts: list[str]) -> Optional[Path]:
        with self._lock:
            token, video = self._token, self._video
        if token is None or video is None or len(parts) != 2:
            return None
        if not secrets.compare_digest(parts[0].encode(), token.encode()) or parts[1] != video.name:
            return None
        return video if video.is_file() else None

    def _resolve_image(self, parts: list[str]) -> Optional[Path]:
        with self._lock:
            token, images = self._token, self._images
        if token is None or images is None or len(parts) != 2:
            return None
        name = parts[1]
        if not secrets.compare_digest(parts[0].encode(), token.encode()) or not _plain_name(name):
            return None
        if not name.lower().endswith(".png"):
            return None
        try:
            candidate = (images / name).resolve()
            if candidate.parent != images.resolve():
                return None
        except OSError:
            return None
        return candidate if candidate.is_file() else None

    def _resolve_ui(self, parts: list[str]) -> Optional[Path]:
        parts = [p for p in parts if p != ""]
        if not parts:
            parts = ["index.html"]
        for part in parts:
            if not _plain_name(part):
                return None
        try:
            candidate = self._ui_root.joinpath(*parts).resolve()
            candidate.relative_to(self._ui_root)
        except (OSError, ValueError):
            return None
        return candidate if candidate.is_file() else None


def _plain_name(name: str) -> bool:
    """True for one path component without separators, drive letters, NUL or stream markers."""
    if not name or name in (".", ".."):
        return False
    return not any(ch in name for ch in ("/", "\\", ":", "\x00"))
