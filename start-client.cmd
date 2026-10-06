@echo off
echo === NosCore Client Launcher ===
echo.
cd /d %~dp0
dotnet run --project NosLocalLauncher
pause
