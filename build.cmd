@echo off
rem Builds QuickCut.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
if not exist build mkdir build

rem Pass 1: helper build that draws the icon
"%CSC%" /nologo /target:winexe /optimize+ %REFS% /out:build\iconmaker.exe src\*.cs || exit /b 1
build\iconmaker.exe --make-icon build\QuickCut.ico || exit /b 1

rem Pass 2: the real exe, with icon and manifest
"%CSC%" /nologo /target:winexe /optimize+ /debug- %REFS% /win32icon:build\QuickCut.ico /win32manifest:app.manifest /out:QuickCut.exe src\*.cs || exit /b 1
echo Done: %~dp0QuickCut.exe
