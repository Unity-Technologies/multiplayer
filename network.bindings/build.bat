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

set project_dir=..\sampleproject
set network_bindings_dir=%project_dir%\..\com.unity.transport\Runtime\Bindings

set bin_dir=bin
set dll=%bin_dir%\network.bindings.dll
set pdb=%bin_dir%\network.bindings.pdb

set cs_bindings=source\network.bindings.cs
set dllmeta=source\network.bindings.dll.meta
set pdbmeta=source\network.bindings.pdb.meta

if not exist %cs_bindings% (
    set errno=missing %cs_bindings%
    goto error
)

msbuild /P:Configuration=Release network.bindings.sln

xcopy %dll% %network_bindings_dir%\ /Y >nul 2>nul
xcopy %pdb% %network_bindings_dir%\ /Y >nul 2>nul
xcopy %cs_bindings% %network_bindings_dir%\ /Y >nul 2>nul
xcopy %cs_bindings%.meta %network_bindings_dir%\ /Y >nul 2>nul
xcopy %dllmeta% %network_bindings_dir%\ /Y >nul 2>nul
xcopy %pdbmeta% %network_bindings_dir%\ /Y >nul 2>nul

goto end
:error
set esc=
set red=%esc%[31m
set white=%esc%[37m
echo %red% %errno% %white%
:end
