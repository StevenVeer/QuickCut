@echo off
rem Builds QuickCut.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Optional: build.cmd <folder>  puts the exe in that folder instead of the project folder.
setlocal
cd /d "%~dp0"
set OUT=%~1
if "%OUT%"=="" set OUT=.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
rem The WinRT metadata (what's playing, startup task) ships with Windows 10/11, no SDK needed.
set WINMD=%WINDIR%\System32\WinMetadata
set REFS=/r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll /r:%WINMD%\Windows.Foundation.winmd /r:%WINMD%\Windows.Media.winmd /r:%WINMD%\Windows.Storage.winmd /r:%WINMD%\Windows.ApplicationModel.winmd
if not exist build mkdir build
if not exist "%OUT%" mkdir "%OUT%"

rem Pass 1: helper build that draws the icon
"%CSC%" /nologo /target:winexe /optimize+ %REFS% /out:build\iconmaker.exe src\*.cs || exit /b 1
build\iconmaker.exe --make-icon build\QuickCut.ico || exit /b 1

rem Pass 2: the real exe, with icon and manifest
"%CSC%" /nologo /target:winexe /optimize+ /debug- %REFS% /win32icon:build\QuickCut.ico /win32manifest:app.manifest /out:"%OUT%\QuickCut.exe" src\*.cs || exit /b 1
echo Done: %OUT%\QuickCut.exe
