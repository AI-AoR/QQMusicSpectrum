@echo off
setlocal
rem Build with the .NET Framework compiler that ships with Windows.
rem No SDK, no NuGet, no download required.
rem AnyCPU: runs 64-bit on 64-bit Windows, still works on 32-bit.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found - .NET Framework 4.x is required.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /unsafe /optimize+ /debug- /codepage:65001 ^
  /out:"%~dp0Spectrum.exe" ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /win32icon:"%~dp0src\app.ico" ^
  "%~dp0src\Native.cs" "%~dp0src\Portable.cs" "%~dp0src\Audio.cs" ^
  "%~dp0src\Analyzer.cs" "%~dp0src\Overlay.cs" "%~dp0src\Program.cs"

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)
echo.
echo BUILD OK  -^>  %~dp0Spectrum.exe
