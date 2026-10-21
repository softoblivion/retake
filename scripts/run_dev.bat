@echo off
rem Run Frame Notes from source with dev tools: run_dev.bat [--ui-dir PATH] [VIDEO]
rem Set FRAME_NOTES_HOME to a scratch folder first; without it the data goes to frame-notes_data\ in the project root.
rem Needs .venv (run build\build.ps1 -SkipBuild once); falls back to "py -3.14" when it is missing.
setlocal
set "ROOT=%~dp0..\"
cd /d "%ROOT%"
set "PYTHONPATH=%ROOT%src;%PYTHONPATH%"
if exist "%ROOT%.venv\Scripts\python.exe" (
    "%ROOT%.venv\Scripts\python.exe" -m frame_notes.shell.main --debug %*
) else (
    py -3.14 -m frame_notes.shell.main --debug %*
)
