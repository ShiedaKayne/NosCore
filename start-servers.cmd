@echo off
echo === NosCore Server Startup ===
echo.

echo Starting PostgreSQL (Docker)...
docker compose up -d db
timeout /t 5 /nobreak >nul

echo.
echo Starting MasterServer (port 5000)...
start "NosCore - MasterServer" cmd /k "cd /d %~dp0 && dotnet run --project src/NosCore.MasterServer"
timeout /t 15 /nobreak >nul

echo.
echo Starting WebApi (port 8080)...
start "NosCore - WebApi" cmd /k "cd /d %~dp0 && set ASPNETCORE_URLS=http://localhost:8080 && dotnet run --project src/NosCore.WebApi"
timeout /t 10 /nobreak >nul

echo.
echo Starting LoginServer (port 4000)...
start "NosCore - LoginServer" cmd /k "cd /d %~dp0 && dotnet run --project src/NosCore.LoginServer"
timeout /t 5 /nobreak >nul

echo.
echo Starting WorldServer (port 1337)...
start "NosCore - WorldServer" cmd /k "cd /d %~dp0 && dotnet run --project src/NosCore.WorldServer"

echo.
echo All servers starting. Wait for them to be ready before launching the client.
echo   MasterServer: http://localhost:5000
echo   WebApi:       http://localhost:8080
echo   LoginServer:  port 4000
echo   WorldServer:  port 1337
echo.
pause
