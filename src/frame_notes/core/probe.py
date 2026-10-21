"""Container probe: frame rate, duration, size and frame count of MP4 / MOV and WebM / Matroska files.

Reads box and element headers with the stdlib only and never decodes video. Damaged or foreign content gives
`container="unknown"` with empty fields; only an unreadable file raises OSError. The contract is `docs/CORE.md`
(section `probe.py`).
"""

from __future__ import annotations

import os
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, Callable, Iterator

_MAX_BLOCK = 256 * 1024 * 1024  # largest moov / Segment child read into memory
_EBML_MAGIC = b"\x1a\x45\xdf\xa3"
_PARSE_ERRORS = (ValueError, IndexError, struct.error, OverflowError, MemoryError)


@dataclass
class ProbeResult:
    container: str = "unknown"  # "mp4", "webm" or "unknown"
    fps: float | None = None
    duration: float | None = None  # seconds
    width: int | None = None
    height: int | None = None
    frames: int | None = None  # sample count of the video track when known


def probe(path: Path) -> ProbeResult:
    """Probe a video file. Raises OSError when it cannot be opened or read."""
    with open(path, "rb") as handle:
        head = handle.read(12)
        handle.seek(0)
        if head.startswith(_EBML_MAGIC):
            return _probe_webm(handle)
        return _probe_mp4(handle)


# --- MP4 / MOV ----------------------------------------------------------------------------------------------


def _is_box_type(raw: bytes) -> bool:
    return len(raw) == 4 and all(0x20 <= b < 0x7F or b == 0xA9 for b in raw)


def _iter_boxes(buf: bytes, start: int, end: int) -> Iterator[tuple[bytes, int, int]]:
    """Yield (type, payload_start, payload_end) of the boxes in buf[start:end]."""
    pos = start
    while pos + 8 <= end:
        size, kind = struct.unpack_from(">I4s", buf, pos)
        header = 8
        if size == 1:
            if pos + 16 > end:
                return
            size = struct.unpack_from(">Q", buf, pos + 8)[0]
            header = 16
        elif size == 0:
            size = end - pos
        if size < header or pos + size > end:
            return
        yield kind, pos + header, pos + size
        pos += size


def _find(buf: bytes, start: int, end: int, kind: bytes) -> tuple[int, int] | None:
    for box_type, payload_start, payload_end in _iter_boxes(buf, start, end):
        if box_type == kind:
            return payload_start, payload_end
    return None


def _probe_mp4(handle: BinaryIO) -> ProbeResult:
    result = ProbeResult()
    size_total = os.fstat(handle.fileno()).st_size
    pos = 0
    seen_ftyp = False
    moov: bytes | None = None
    try:
        while pos + 8 <= size_total:
            handle.seek(pos)
            header = handle.read(16)
            if len(header) < 8:
                break
            size, kind = struct.unpack_from(">I4s", header, 0)
            if not _is_box_type(kind):
                break
            header_len = 8
            if size == 1:
                if len(header) < 16:
                    break
                size = struct.unpack_from(">Q", header, 8)[0]
                header_len = 16
            elif size == 0:
                size = size_total - pos
            if size < header_len or pos + size > size_total:
                break
            if kind == b"ftyp":
                seen_ftyp = True
            elif kind == b"moov":
                payload_len = size - header_len
                if payload_len > _MAX_BLOCK:
                    break
                handle.seek(pos + header_len)
                moov = handle.read(payload_len)
                break
            pos += size
    except _PARSE_ERRORS:
        pass
    if moov is None and not seen_ftyp:
        return result
    result.container = "mp4"
    if moov is not None:
        try:
            _read_moov(moov, result)
        except _PARSE_ERRORS:
            pass
    return result


def _read_moov(moov: bytes, result: ProbeResult) -> None:
    movie_duration: float | None = None
    mvhd = _find(moov, 0, len(moov), b"mvhd")
    if mvhd:
        start, end = mvhd
        if moov[start] == 1:
            timescale, duration = struct.unpack_from(">IQ", moov, start + 20)
        else:
            timescale, duration = struct.unpack_from(">II", moov, start + 12)
        if timescale > 0:
            movie_duration = duration / timescale
    track_duration: float | None = None
    for box_type, t_start, t_end in _iter_boxes(moov, 0, len(moov)):
        if box_type != b"trak":
            continue
        mdia = _find(moov, t_start, t_end, b"mdia")
        if not mdia:
            continue
        hdlr = _find(moov, mdia[0], mdia[1], b"hdlr")
        if not hdlr or moov[hdlr[0] + 8 : hdlr[0] + 12] != b"vide":
            continue
        tkhd = _find(moov, t_start, t_end, b"tkhd")
        if tkhd:
            offset = tkhd[0] + (88 if moov[tkhd[0]] == 1 else 76)
            width, height = struct.unpack_from(">II", moov, offset)
            result.width = (width >> 16) or None
            result.height = (height >> 16) or None
        media_scale = 0
        mdhd = _find(moov, mdia[0], mdia[1], b"mdhd")
        if mdhd:
            start = mdhd[0]
            if moov[start] == 1:
                media_scale, media_duration = struct.unpack_from(">IQ", moov, start + 20)
            else:
                media_scale, media_duration = struct.unpack_from(">II", moov, start + 12)
            if media_scale > 0:
                track_duration = media_duration / media_scale
        minf = _find(moov, mdia[0], mdia[1], b"minf")
        stbl = _find(moov, minf[0], minf[1], b"stbl") if minf else None
        stts = _find(moov, stbl[0], stbl[1], b"stts") if stbl else None
        if stts and media_scale > 0:
            _read_stts(moov, stts[0], stts[1], media_scale, result)
        break
    if movie_duration is not None and movie_duration > 0:
        result.duration = movie_duration
    elif track_duration is not None and track_duration > 0:
        result.duration = track_duration
    elif result.frames and result.fps:
        result.duration = result.frames / result.fps


def _read_stts(buf: bytes, start: int, end: int, timescale: int, result: ProbeResult) -> None:
    count = struct.unpack_from(">I", buf, start + 4)[0]
    count = min(count, (end - start - 8) // 8)
    totals: dict[int, int] = {}
    frames = 0
    for i in range(count):
        samples, delta = struct.unpack_from(">II", buf, start + 8 + 8 * i)
        frames += samples
        totals[delta] = totals.get(delta, 0) + samples
    if frames > 0:
        result.frames = frames
    delta = max(totals, key=lambda d: totals[d]) if totals else 0
    if delta > 0:
        result.fps = timescale / delta


# --- WebM / Matroska ----------------------------------------------------------------------------------------

_ID_SEGMENT = 0x18538067
_ID_INFO = 0x1549A966
_ID_TRACKS = 0x1654AE6B
_ID_CLUSTER = 0x1F43B675
_ID_TIMECODE_SCALE = 0x2AD7B1
_ID_DURATION = 0x4489
_ID_TRACK_ENTRY = 0xAE
_ID_TRACK_TYPE = 0x83
_ID_DEFAULT_DURATION = 0x23E383
_ID_VIDEO = 0xE0
_ID_PIXEL_WIDTH = 0xB0
_ID_PIXEL_HEIGHT = 0xBA

_UNKNOWN = -1


def _read_element_header(read: Callable[[int], bytes]) -> tuple[int, int, int] | None:
    """Read (id, payload size or _UNKNOWN, header length) with `read(n) -> bytes`; None at end of data."""
    first = read(1)
    if not first:
        return None
    lead = first[0]
    id_len = 0
    for bit in range(4):
        if lead & (0x80 >> bit):
            id_len = bit + 1
            break
    if id_len == 0:
        raise ValueError("bad element id")
    raw_id = first + read(id_len - 1)
    if len(raw_id) != id_len:
        return None
    size_first = read(1)
    if not size_first:
        return None
    lead = size_first[0]
    size_len = 0
    for bit in range(8):
        if lead & (0x80 >> bit):
            size_len = bit + 1
            break
    if size_len == 0:
        raise ValueError("bad element size")
    rest = read(size_len - 1)
    if len(rest) != size_len - 1:
        return None
    value = lead & (0xFF >> size_len)
    for byte in rest:
        value = (value << 8) | byte
    unknown = value == (1 << (7 * size_len)) - 1
    return int.from_bytes(raw_id, "big"), (_UNKNOWN if unknown else value), id_len + size_len


def _iter_elements(buf: bytes) -> Iterator[tuple[int, bytes]]:
    """Yield (id, payload) of the elements in an in-memory master element payload."""
    pos = 0

    def read(n: int) -> bytes:
        nonlocal pos
        chunk = buf[pos : pos + n]
        pos += len(chunk)
        return chunk

    while pos < len(buf):
        header = _read_element_header(read)
        if header is None:
            return
        element_id, size, _ = header
        if size == _UNKNOWN or pos + size > len(buf):
            return
        yield element_id, buf[pos : pos + size]
        pos += size


def _uint(payload: bytes) -> int:
    return int.from_bytes(payload, "big")


def _float(payload: bytes) -> float:
    if len(payload) == 4:
        return struct.unpack(">f", payload)[0]
    if len(payload) == 8:
        return struct.unpack(">d", payload)[0]
    raise ValueError("bad float size")


def _probe_webm(handle: BinaryIO) -> ProbeResult:
    result = ProbeResult(container="webm")
    scale = 1_000_000
    duration_units: float | None = None
    try:
        read = handle.read
        while True:
            header = _read_element_header(read)
            if header is None:
                break
            element_id, size, _ = header
            if element_id == _ID_SEGMENT:
                break  # descend: the children follow directly
            if size == _UNKNOWN:
                return result
            handle.seek(size, os.SEEK_CUR)
        while True:
            header = _read_element_header(read)
            if header is None:
                break
            element_id, size, _ = header
            if element_id == _ID_CLUSTER or size == _UNKNOWN:
                break
            if element_id in (_ID_INFO, _ID_TRACKS):
                if size > _MAX_BLOCK:
                    break
                payload = handle.read(size)
                if len(payload) != size:
                    break
                if element_id == _ID_INFO:
                    for child_id, child in _iter_elements(payload):
                        if child_id == _ID_TIMECODE_SCALE:
                            scale = _uint(child) or scale
                        elif child_id == _ID_DURATION:
                            duration_units = _float(child)
                else:
                    _read_tracks(payload, result)
            else:
                handle.seek(size, os.SEEK_CUR)
    except _PARSE_ERRORS:
        pass
    if duration_units is not None and duration_units > 0:
        result.duration = duration_units * scale / 1e9
    return result


def _read_tracks(payload: bytes, result: ProbeResult) -> None:
    for element_id, entry in _iter_elements(payload):
        if element_id != _ID_TRACK_ENTRY:
            continue
        track_type = None
        default_duration = 0
        width = height = None
        for child_id, child in _iter_elements(entry):
            if child_id == _ID_TRACK_TYPE:
                track_type = _uint(child)
            elif child_id == _ID_DEFAULT_DURATION:
                default_duration = _uint(child)
            elif child_id == _ID_VIDEO:
                for video_id, value in _iter_elements(child):
                    if video_id == _ID_PIXEL_WIDTH:
                        width = _uint(value)
                    elif video_id == _ID_PIXEL_HEIGHT:
                        height = _uint(value)
        if track_type == 1:
            if default_duration > 0:
                result.fps = 1e9 / default_duration
            result.width = width or None
            result.height = height or None
            return
