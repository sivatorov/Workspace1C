@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-release.ps1" -Version 1.0.0.84 -Sha c6015f58bfcbc9d409a939d515d7323500debecf
echo.
pause
