@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Publish artifacts v1.0.1.5 (Windows + Linux), build only ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_build_v10015.ps1"
set "EC=%ERRORLEVEL%"
echo.
if "%EC%"=="0" goto ok
echo BUILD FAILED (code %EC%). Log: _publish_10015.log
exit /b %EC%
:ok
echo DONE. Log: _publish_10015.log; artifacts: publish\out-1.0.1.5\
exit /b 0
