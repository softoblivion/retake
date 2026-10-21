"""Build build/icon.ico (multi-size) for the exe and the window.

    python build/make_icon.py

Draws a dark rounded square with an amber marker shape (a timeline marker: a rounded flag with a notch on a short
stem). Uses src/frame_notes/ui/assets/icon-1024.png instead when that file exists.
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SOURCE_PNG = ROOT / "src" / "frame_notes" / "ui" / "assets" / "icon-1024.png"
OUT_ICO = ROOT / "build" / "icon.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]
S = 1024  # drawing canvas, downscaled with LANCZOS for the smaller sizes

PLATE = (24, 26, 31, 255)
PLATE_EDGE = (52, 56, 66, 255)
AMBER = (255, 176, 32, 255)
AMBER_DARK = (214, 128, 8, 255)


def _gradient(size: int, top: tuple, bottom: tuple) -> Image.Image:
    column = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / (size - 1)
        column.putpixel((0, y), tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(4)))
    return column.resize((size, size))


def draw() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((24, 24, S - 24, S - 24), radius=220, fill=PLATE_EDGE)
    d.rounded_rectangle((40, 40, S - 40, S - 40), radius=206, fill=PLATE)

    # film-frame hint: a thin rounded outline of the frame the note points at
    d.rounded_rectangle((176, 208, S - 176, S - 304), radius=56, outline=(70, 76, 90, 255), width=22)

    # timeline marker: a pentagon (square top, pointed bottom) in amber
    mask = Image.new("L", (S, S), 0)
    m = ImageDraw.Draw(mask)
    left, right, top, shoulder, tip = 332, S - 332, 252, 590, 800
    m.rounded_rectangle((left, top, right, shoulder + 60), radius=64, fill=255)
    m.polygon([(left + 18, shoulder - 40), (right - 18, shoulder - 40), (S / 2, tip)], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.5))
    img.paste(_gradient(S, AMBER, AMBER_DARK), (0, 0), mask)

    # a notch in the marker: the playhead line through it
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((S / 2 - 22, 252, S / 2 + 22, 620), radius=22, fill=PLATE)
    return img


def main() -> int:
    if SOURCE_PNG.is_file():
        base = Image.open(SOURCE_PNG).convert("RGBA")
        origin = SOURCE_PNG.relative_to(ROOT)
    else:
        base = draw()
        origin = "drawn marker"
    if base.width != base.height:
        side = max(base.size)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.paste(base, ((side - base.width) // 2, (side - base.height) // 2))
        base = square
    OUT_ICO.parent.mkdir(parents=True, exist_ok=True)
    base.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in SIZES])
    print(f"icon.ico written from {origin} ({OUT_ICO.stat().st_size // 1024} KB, sizes {SIZES})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
