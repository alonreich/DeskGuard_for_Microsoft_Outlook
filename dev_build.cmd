@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "%~dp0build\DeskGuard.Build\DeskGuard.Build.csproj" -c Release -- --dev %*
exit /b %ERRORLEVEL%
