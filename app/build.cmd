@echo off
rem Builds LCDSysInfoMini.exe with the C# compiler that ships with .NET Framework 4.8.
setlocal
cd /d "%~dp0"
if not exist ..\vendor\lhm\LibreHardwareMonitorLib.dll (
  echo LibreHardwareMonitor wird von nuget.org geladen ...
  powershell -NoProfile -ExecutionPolicy Bypass -File ..\tools\fetch-lhm.ps1 || exit /b 1
)
set OUT=LCDSysInfoMini
if not exist %OUT% mkdir %OUT%
copy /y ..\vendor\lhm\*.dll %OUT%\ >nul
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ ^
  /out:%OUT%\LCDSysInfoMini.exe ^
  /r:%OUT%\LibreHardwareMonitorLib.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  src\*.cs
exit /b %errorlevel%
