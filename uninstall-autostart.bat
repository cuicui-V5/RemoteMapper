@echo off
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\RemoteMic.lnk"
if exist "%LNK%" (del "%LNK%" && echo Autostart removed.) else (echo No autostart shortcut found.)
timeout /t 2 /nobreak >nul
