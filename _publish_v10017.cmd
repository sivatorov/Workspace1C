@echo off
chcp 65001 >nul
cd /d "%~dp0"
rem Сборка артефактов релиза 1.0.1.7 (Windows + Linux) - только локальная сборка,
rem без git/commit/push, без GitHub release, без Telegram.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_build_v10017.ps1"
echo.
pause
