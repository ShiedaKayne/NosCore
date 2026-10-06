@echo off
echo === Stopping NosCore Servers ===
echo.
powershell -Command "Get-Process -Name 'dotnet','NosCore*' -ErrorAction SilentlyContinue | Stop-Process -Force"
echo All server processes stopped.
pause
