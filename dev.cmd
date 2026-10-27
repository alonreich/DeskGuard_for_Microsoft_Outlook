@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\DevRun.ps1" -Mode "%~1"
exit /b %errorlevel%
