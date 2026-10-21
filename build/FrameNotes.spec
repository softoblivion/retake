# -*- mode: python ; coding: utf-8 -*-
# One-file, windowed PyInstaller build of Frame Notes. Run through build/build.ps1 (see docs/BUILD.md).
import re
from pathlib import Path

from PyInstaller.utils.hooks import collect_submodules

ROOT = Path(SPECPATH).resolve().parent
SRC = ROOT / "src"
UI_DIR = SRC / "frame_notes" / "ui"
ICON = ROOT / "build" / "icon.ico"
WORK = ROOT / "build" / "work"

if not (UI_DIR / "index.html").is_file():
    raise SystemExit(f"index.html not found in {UI_DIR}: the UI is required for the build")

# Version is read from the one place that owns it, without importing the package.
VERSION = re.search(r'__version__\s*=\s*"([^"]+)"', (SRC / "frame_notes" / "__init__.py").read_text("utf-8")).group(1)

# --- Windows version resource ------------------------------------------------------------------------------------
_parts = (tuple(int(p) for p in VERSION.split(".")) + (0, 0, 0, 0))[:4]
WORK.mkdir(parents=True, exist_ok=True)
_version_file = WORK / "_version_info.txt"
_version_file.write_text(
    f"""VSVersionInfo(
  ffi=FixedFileInfo(filevers={_parts}, prodvers={_parts}, mask=0x3f, flags=0x0, OS=0x40004, fileType=0x1, subtype=0x0, date=(0, 0)),
  kids=[
    StringFileInfo([StringTable('040904B0', [
      StringStruct('FileDescription', 'Frame Notes'),
      StringStruct('FileVersion', '{VERSION}'),
      StringStruct('InternalName', 'FrameNotes'),
      StringStruct('OriginalFilename', 'FrameNotes.exe'),
      StringStruct('ProductName', 'Frame Notes'),
      StringStruct('ProductVersion', '{VERSION}')])]),
    VarFileInfo([VarStruct('Translation', [1033, 1200])])
  ]
)
""",
    encoding="utf-8",
)

# --- bundle contents ---------------------------------------------------------------------------------------------
datas = [(str(UI_DIR), "frame_notes/ui")]  # main.py reads it from sys._MEIPASS / "frame_notes" / "ui"
if ICON.is_file():
    datas.append((str(ICON), "."))  # main.py passes it to pywebview as the window icon

# Every package module (core, shell and the vendored selfupdate); main.py imports some of them late.
hiddenimports = collect_submodules("frame_notes") + [
    "webview.platforms.winforms",
    "webview.platforms.edgechromium",
    "clr_loader",
]

excludes = [
    "tkinter", "unittest", "pydoc", "doctest", "test", "idlelib", "lib2to3",
    "numpy", "PIL", "matplotlib", "scipy", "pandas", "IPython", "pytest", "setuptools", "pip", "wheel",
    "PyQt5", "PyQt6", "PySide2", "PySide6", "qtpy", "gi", "cefpython3", "bottle_ssl",
]

a = Analysis(
    [str(ROOT / "build" / "entry.py")],
    pathex=[str(SRC)],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=[],
    excludes=excludes,
    noarchive=False,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="FrameNotes",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,  # UPX-packed exes trigger antivirus false positives
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    icon=str(ICON) if ICON.is_file() else None,
    version=str(_version_file),
)
