@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Publish artifacts v1.0.1.4 (Windows single-file), build only ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_build_v10014.ps1"
set "EC=%ERRORLEVEL%"
echo.
if "%EC%"=="0" goto ok
echo BUILD FAILED (code %EC%). Log: _publish_10014.log
exit /b %EC%
:ok
echo DONE. Log: _publish_10014.log; artifact: dist\release\Workspace1C.exe
exit /b 0
