"""Frame <-> seconds <-> timecode conversion; the only module that does frame math.

Timecodes follow the After Effects display: `h:mm:ss:ff`, drop-frame (`;`) at 29.97 and 59.94. The contract and
the reference values are in `docs/CORE.md` (section `timecode.py`).
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass

NOMINAL_RATES = (23.976, 24.0, 25.0, 29.97, 30.0, 48.0, 50.0, 59.94, 60.0, 100.0, 120.0)

# Exact rates behind the rounded NTSC names.
_EXACT_RATES = {23.976: 24000 / 1001, 29.97: 30000 / 1001, 59.94: 60000 / 1001}
_DROP_RATES = (29.97, 59.94)
_SNAP_TOLERANCE = 0.005

_TC_RE = re.compile(r"[0-9]+(?:[:;][0-9]+){1,3}")
_FPS_RE = re.compile(r"[0-9]+(?:\.[0-9]*)?|\.[0-9]+")


@dataclass(frozen=True)
class TimecodeBase:
    """Frame-count rules of one frame rate."""

    fps: float  # exact rate used for frame <-> seconds (23.976 is 24000/1001, ...)
    base: int  # frames per timecode second
    drop: bool  # True only for 29.97 and 59.94
    digits: int  # frame digits: 2, or 3 when base >= 100
    separator: str  # ":" non-drop, ";" drop

    @property
    def drop_per_minute(self) -> int:
        """Frame numbers skipped at the start of a non-tenth minute (0 for non-drop bases)."""
        return self.base // 15 if self.drop else 0


def nominal_rate(fps: float) -> float:
    """Snap `fps` to the nearest entry of NOMINAL_RATES when within 0.5 %, else return it unchanged."""
    value = float(fps)
    if not math.isfinite(value) or value <= 0:
        raise ValueError("The frame rate must be a positive number")
    best = None
    best_distance = None
    for rate in NOMINAL_RATES:
        distance = abs(value - rate) / rate
        if distance <= _SNAP_TOLERANCE and (best_distance is None or distance < best_distance):
            best, best_distance = rate, distance
    return best if best is not None else value


def timecode_base(fps: float) -> TimecodeBase:
    """Build the TimecodeBase of a frame rate (snapped with nominal_rate)."""
    nominal = nominal_rate(fps)
    exact = _EXACT_RATES.get(nominal, nominal)
    base = max(1, int(math.floor(nominal + 0.5)))
    drop = nominal in _DROP_RATES
    digits = max(2, len(str(base - 1)))
    return TimecodeBase(fps=exact, base=base, drop=drop, digits=digits, separator=";" if drop else ":")


def frame_to_timecode(frame: int, tb: TimecodeBase) -> str:
    """Format a zero-based frame number as `h:mm:ss:ff` (`;` separators for drop-frame)."""
    if frame < 0:
        raise ValueError("The frame number cannot be negative")
    shown = int(frame)
    d = tb.drop_per_minute
    if d:
        per_minute = tb.base * 60 - d
        per_ten_minutes = per_minute * 10 + d
        q, m = divmod(shown, per_ten_minutes)
        shown = shown + 9 * d * q + (d * ((m - d) // per_minute) if m > d else 0)
    ff = shown % tb.base
    total_seconds = shown // tb.base
    ss = total_seconds % 60
    mm = (total_seconds // 60) % 60
    hh = total_seconds // 3600
    sep = tb.separator
    return f"{hh}{sep}{mm:02d}{sep}{ss:02d}{sep}{ff:0{tb.digits}d}"


def timecode_to_frame(text: str, tb: TimecodeBase) -> int:
    """Parse `h:mm:ss:ff`, `mm:ss:ff` or `ss:ff` (either separator) into a zero-based frame number."""
    if not isinstance(text, str):
        raise ValueError("The timecode must be text")
    cleaned = text.strip()
    if not _TC_RE.fullmatch(cleaned):
        raise ValueError(f"Not a timecode: {text!r}")
    numbers = [int(part) for part in re.split(r"[:;]", cleaned)]
    ff = numbers[-1]
    ss = numbers[-2]
    mm = numbers[-3] if len(numbers) >= 3 else 0
    hh = numbers[-4] if len(numbers) == 4 else 0
    if ff >= tb.base:
        raise ValueError(f"The frame part {ff} is not below {tb.base}")
    if ss >= 60 or mm >= 60:
        raise ValueError(f"Minutes and seconds must be below 60: {text!r}")
    d = tb.drop_per_minute
    if d and ss == 0 and mm % 10 != 0 and ff < d:
        raise ValueError(f"{text!r} is a dropped frame number")
    frame = (hh * 3600 + mm * 60 + ss) * tb.base + ff
    if d:
        total_minutes = hh * 60 + mm
        frame -= d * (total_minutes - total_minutes // 10)
    return frame


def seconds_to_frame(seconds: float, fps: float) -> int:
    """Frame shown at `seconds`: floor(seconds * fps + 1e-4), never negative."""
    return max(0, int(math.floor(seconds * fps + 1e-4)))


def frame_to_seconds(frame: int, fps: float) -> float:
    """Centre of a frame in seconds, used for seeking."""
    return (frame + 0.5) / fps


def duration_frames(seconds: float, fps: float) -> int:
    """Frame count of a duration: round(seconds * fps)."""
    return int(round(seconds * fps))


def format_fps(fps: float) -> str:
    """Text form of a frame rate: up to three decimals, trailing zeros cut ("25", "29.97", "23.976")."""
    text = f"{float(fps):.3f}"
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return text


def parse_fps(text: str) -> float:
    """Inverse of format_fps; ValueError on junk or a non-positive value."""
    if not isinstance(text, str):
        raise ValueError("The frame rate must be text")
    cleaned = text.strip()
    if not _FPS_RE.fullmatch(cleaned):
        raise ValueError(f"Not a frame rate: {text!r}")
    value = float(cleaned)
    if not math.isfinite(value) or value <= 0:
        raise ValueError(f"Not a frame rate: {text!r}")
    return value
