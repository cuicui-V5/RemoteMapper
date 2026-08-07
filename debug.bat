@echo off
cd /d "%~dp0"
title RemoteMic (debug - foreground)
echo ============================================
echo   RemoteMic - Xiaomi Remote -> WeType
echo   (foreground debug mode, see realtime output)
echo ============================================
echo.
echo   Hold the voice button on remote to talk.
echo   Release to stop. Press Ctrl+C to exit.
echo.
RemoteMic.exe
echo.
echo Program exited. Press any key to close...
pause >nul