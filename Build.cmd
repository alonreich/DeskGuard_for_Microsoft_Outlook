@echo off
rem Production build entrypoint: publishes DeskGuard with Native AOT (C# 13 / .NET 9)
rem and packages the single-file installer into .\compiled\DeskGuard.exe via Inno Setup.
dotnet run --project "%~dp0build\DeskGuard.Build\DeskGuard.Build.csproj" -c Release -- %*
exit /b %ERRORLEVEL%
