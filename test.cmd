@echo off
rem Builds and runs the unit tests (tests\*.cs together with src\*.cs) with the C# compiler that
rem ships with Windows, like build.cmd. Exits non-zero when a test fails.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WINMD=%WINDIR%\System32\WinMetadata
set REFS=/r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll /r:%WINMD%\Windows.Foundation.winmd /r:%WINMD%\Windows.Media.winmd /r:%WINMD%\Windows.Storage.winmd /r:%WINMD%\Windows.ApplicationModel.winmd
if not exist build mkdir build

"%CSC%" /nologo /target:exe /main:QuickCutTests.Runner %REFS% /out:build\tests.exe src\*.cs tests\*.cs || exit /b 1
build\tests.exe
exit /b %ERRORLEVEL%
