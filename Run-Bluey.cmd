@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

echo Building Bluey. The first run takes a minute.
dotnet build "%~dp0pc\Bluey.Host\Bluey.Host.csproj" -c Release --nologo
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

echo Starting Bluey. Look for the blueberry at the bottom of the screen and the tray icon by the clock.
echo If Windows asks about the firewall, allow it on private networks so the phone can connect.
start "" "%~dp0pc\Bluey.Host\bin\Release\net8.0-windows10.0.19041.0\Bluey.Host.exe"
