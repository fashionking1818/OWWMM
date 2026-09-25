@echo off
setlocal
cd /d "%~dp0"
title OWWMM

echo [OWWMM] Starting...
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK was not found. Install .NET 10 SDK first.
  pause
  exit /b 1
)

dotnet run --project "%~dp0src\OWWMM\OWWMM.csproj" --configuration Release
set "APP_EXIT=%ERRORLEVEL%"
if not "%APP_EXIT%"=="0" echo [ERROR] Application exited with code %APP_EXIT%.
echo [OWWMM] Closed.
pause
exit /b %APP_EXIT%
