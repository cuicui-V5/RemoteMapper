@echo off
setlocal
set "HERE=%~dp0"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "LNK=%STARTUP%\RemoteMic.lnk"

powershell -NoProfile -Command ^
  "$ws = New-Object -ComObject WScript.Shell;" ^
  "$lnk = $ws.CreateShortcut('%LNK%');" ^
  "$lnk.TargetPath = '%HERE%start.vbs';" ^
  "$lnk.WorkingDirectory = '%HERE%';" ^
  "$lnk.IconLocation = '%HERE%RemoteMic.exe,0';" ^
  "$lnk.Description = 'RemoteMic background launcher';" ^
  "$lnk.Save()"

if exist "%LNK%" (echo Autostart installed: %LNK%) else (echo FAILED to create shortcut.)
timeout /t 3 /nobreak >nul
