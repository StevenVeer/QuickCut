@echo off
rem Builds QuickCut.msix for the Microsoft Store. Needs the Windows 10/11 SDK (for makeappx/makepri).
rem The package is unsigned: the Store signs it. To try it on this PC without signing:
rem   build-store.cmd, then in PowerShell (Developer Mode on):
rem   Add-AppxPackage -Register build\store\AppxManifest.xml
setlocal
cd /d "%~dp0"

set SDK=
for /f "delims=" %%d in ('dir /b /ad /o-n "%ProgramFiles(x86)%\Windows Kits\10\bin\10.*" 2^>nul') do (
  if not defined SDK if exist "%ProgramFiles(x86)%\Windows Kits\10\bin\%%d\x64\makeappx.exe" set "SDK=%ProgramFiles(x86)%\Windows Kits\10\bin\%%d\x64"
)
if not defined SDK echo Windows SDK not found (makeappx.exe). Install the Windows SDK. & exit /b 1

set LAYOUT=build\store
if exist %LAYOUT% rmdir /s /q %LAYOUT%

call "%~dp0build.cmd" %LAYOUT% || exit /b 1
build\iconmaker.exe --make-store-logos %LAYOUT%\Assets || exit /b 1
rem The manifest gets the version from src\Program.cs (Program.Version), the only place to change it.
build\iconmaker.exe --stamp-manifest store\AppxManifest.xml %LAYOUT%\AppxManifest.xml || (echo Couldn't put the version from src\Program.cs into the manifest. It must end in .0 for the Store. & exit /b 1)

rem Resource index, so Windows can pick the logo that fits each size and screen scale.
"%SDK%\makepri.exe" createconfig /cf build\priconfig.xml /dq en-US /pv 10.0.0 /o >nul || exit /b 1
"%SDK%\makepri.exe" new /pr %LAYOUT% /cf build\priconfig.xml /mn %LAYOUT%\AppxManifest.xml /of %LAYOUT%\resources.pri /o >nul || exit /b 1

"%SDK%\makeappx.exe" pack /d %LAYOUT% /p QuickCut.msix /o >nul || exit /b 1
echo Done: %~dp0QuickCut.msix
