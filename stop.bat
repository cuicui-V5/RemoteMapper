@echo off
taskkill /F /IM RemoteMic.exe >nul 2>&1
if %errorlevel%==0 (echo RemoteMic stopped.) else (echo RemoteMic was not running.)
timeout /t 2 /nobreak >nul
