@echo off
SETLOCAL EnableDelayedExpansion

if not exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
  echo "WARNING: You need VS 2017 version 15.2 or later (for vswhere.exe)"
)

for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do (
  set InstallDir=%%i
)

if exist "!InstallDir!\VC\Auxiliary\Build\vcvars64.bat" (
  echo Found !InstallDir!
  call "!InstallDir!\VC\Auxiliary\Build\vcvars64.bat"
) else (
  echo "Could not find !InstallDir!\VC\Auxiliary\Build\vcvars64.bat"
)
