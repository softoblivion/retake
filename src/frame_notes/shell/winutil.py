"""Windows helpers: clipboard text, Explorer selection, message box, virtual screen. ctypes only, no extra packages."""

from __future__ import annotations

import ctypes
import logging
import os
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path
from typing import Optional

log = logging.getLogger("frame_notes.winutil")

CF_UNICODETEXT = 13
GMEM_MOVEABLE = 0x0002
CREATE_NO_WINDOW = 0x08000000
MB_OK = 0x00
MB_ICONERROR = 0x10
MB_ICONINFORMATION = 0x40
SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79

_IS_WINDOWS = sys.platform == "win32"

if _IS_WINDOWS:
    _user32 = ctypes.WinDLL("user32", use_last_error=True)
    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    _user32.OpenClipboard.argtypes = [wintypes.HWND]
    _user32.OpenClipboard.restype = wintypes.BOOL
    _user32.CloseClipboard.argtypes = []
    _user32.CloseClipboard.restype = wintypes.BOOL
    _user32.EmptyClipboard.argtypes = []
    _user32.EmptyClipboard.restype = wintypes.BOOL
    _user32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
    _user32.SetClipboardData.restype = wintypes.HANDLE
    _user32.GetSystemMetrics.argtypes = [ctypes.c_int]
    _user32.GetSystemMetrics.restype = ctypes.c_int
    _user32.MessageBoxW.argtypes = [wintypes.HWND, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.UINT]
    _user32.MessageBoxW.restype = ctypes.c_int

    _kernel32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
    _kernel32.GlobalAlloc.restype = wintypes.HGLOBAL
    _kernel32.GlobalLock.argtypes = [wintypes.HGLOBAL]
    _kernel32.GlobalLock.restype = wintypes.LPVOID
    _kernel32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
    _kernel32.GlobalUnlock.restype = wintypes.BOOL
    _kernel32.GlobalFree.argtypes = [wintypes.HGLOBAL]
    _kernel32.GlobalFree.restype = wintypes.HGLOBAL


def _open_clipboard(attempts: int = 10, delay: float = 0.02) -> bool:
    """Another process may hold the clipboard for a few milliseconds; retry briefly."""
    for _ in range(attempts):
        if _user32.OpenClipboard(None):
            return True
        time.sleep(delay)
    return False


def set_clipboard_text(text: str) -> None:
    """Put `text` on the Windows clipboard as CF_UNICODETEXT. Raises OSError when the clipboard cannot be written."""
    if not _IS_WINDOWS:
        raise OSError("The clipboard is only available on Windows.")
    data = (text + "\0").encode("utf-16-le")
    if not _open_clipboard():
        raise OSError("The clipboard is in use by another program.")
    try:
        if not _user32.EmptyClipboard():
            raise OSError("Could not clear the clipboard.")
        handle = _kernel32.GlobalAlloc(GMEM_MOVEABLE, len(data))
        if not handle:
            raise OSError("Out of memory.")
        pointer = _kernel32.GlobalLock(handle)
        if not pointer:
            _kernel32.GlobalFree(handle)
            raise OSError("Could not lock clipboard memory.")
        try:
            ctypes.memmove(pointer, data, len(data))
        finally:
            _kernel32.GlobalUnlock(handle)
        if not _user32.SetClipboardData(CF_UNICODETEXT, handle):
            _kernel32.GlobalFree(handle)  # ownership passes to the system only on success
            raise OSError("Could not write to the clipboard.")
    finally:
        _user32.CloseClipboard()


def _explorer_exe() -> str:
    root = os.environ.get("SystemRoot") or os.environ.get("windir") or r"C:\Windows"
    return str(Path(root) / "explorer.exe")


def reveal_in_explorer(path: str | os.PathLike) -> None:
    """Open an Explorer window with `path` (a file or a folder) selected. Raises FileNotFoundError when it is missing."""
    target = os.path.normpath(os.fspath(path))
    if not os.path.exists(target):
        raise FileNotFoundError(target)
    # explorer.exe always exits with 1 and needs no console; the process is not waited for.
    subprocess.Popen(  # noqa: S603 - fixed executable, path passed as one argv item
        [_explorer_exe(), "/select,", target],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=CREATE_NO_WINDOW if _IS_WINDOWS else 0,
        close_fds=True,
    )


def message_box(title: str, text: str, error: bool = False) -> None:
    """Blocking native message box, for failures before a window exists (a windowed exe has no console)."""
    if not _IS_WINDOWS:
        print(f"{title}: {text}", file=sys.stderr)
        return
    _user32.MessageBoxW(None, text, title, MB_OK | (MB_ICONERROR if error else MB_ICONINFORMATION))


def virtual_screen() -> Optional[tuple[int, int, int, int]]:
    """(left, top, width, height) of the union of all monitors in physical pixels, or None when unknown."""
    if not _IS_WINDOWS:
        return None
    width = _user32.GetSystemMetrics(SM_CXVIRTUALSCREEN)
    height = _user32.GetSystemMetrics(SM_CYVIRTUALSCREEN)
    if width <= 0 or height <= 0:
        return None
    return (_user32.GetSystemMetrics(SM_XVIRTUALSCREEN), _user32.GetSystemMetrics(SM_YVIRTUALSCREEN), width, height)


def dpi_scale() -> float:
    """System DPI scale (1.0 at 96 dpi). Falls back to 1.0 when the call is unavailable."""
    if not _IS_WINDOWS:
        return 1.0
    try:
        dpi = ctypes.windll.user32.GetDpiForSystem()
    except (AttributeError, OSError):
        return 1.0
    return dpi / 96.0 if dpi else 1.0
