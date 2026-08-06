@echo off
cd /d "%~dp0"
title RemoteMic
echo ============================================
echo   RemoteMic - Xiaomi Remote -> WeType
echo ============================================
echo.
echo   Hold the voice button on remote to talk.
echo   Release to stop. Press Ctrl+C to exit.
echo.
RemoteMic.exe
echo.
echo Program exited. Press any key to close...
pause >nul
