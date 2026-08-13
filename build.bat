@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

echo Building RemoteMic.exe (driverless + keymap)...
%CSC% /nologo /target:exe /platform:x64 ^
  /r:"C:\Windows\System32\WinMetadata\Windows.Devices.winmd" ^
  /r:"C:\Windows\System32\WinMetadata\Windows.Foundation.winmd" ^
  /r:"C:\Windows\System32\WinMetadata\Windows.Storage.winmd" ^
  /r:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" ^
  /r:System.Web.Extensions.dll ^
  /r:Microsoft.CSharp.dll ^
  /out:RemoteMic.exe ^
  src\RemoteMic.cs src\KeyMapEngine.cs src\KeyMapConfig.cs src\KeyMapper.cs ^
  src\KeyComboSender.cs src\KeySnippet.cs src\RemoteCatalog.cs

if %errorlevel%==0 (echo Build OK.) else (echo Build FAILED.)
endlocal
