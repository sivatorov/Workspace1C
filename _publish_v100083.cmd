@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-release.ps1" -Version 1.0.0.83 -Sha 70324f4915ab2e2481b3e3840b16081cdc7b6c84
echo.
pause
