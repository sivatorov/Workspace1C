@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-release.ps1" -Version 1.0.0.100 -Sha 495a0135
echo.
pause
