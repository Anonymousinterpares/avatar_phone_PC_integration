@echo off
setlocal
cd /d "%~dp0"

if not exist "android\gradlew.bat" (
  echo The Android project is missing android\gradlew.bat.
  pause
  exit /b 1
)

if not exist "android\local.properties" (
  powershell -NoProfile -Command "$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'; if (-not (Test-Path $sdk)) { Write-Error 'Android SDK not found under %LOCALAPPDATA%\Android\Sdk'; exit 1 }; Set-Content -Path 'android\local.properties' -Value ('sdk.dir=' + ($sdk -replace '\\','/'))"
  if errorlevel 1 (
    echo Could not find the Android SDK.
    pause
    exit /b 1
  )
)

call "%~dp0android\gradlew.bat" -p "%~dp0android" assembleDebug
if errorlevel 1 (
  echo Phone build failed.
  pause
  exit /b 1
)

echo.
echo APK ready:
echo   %~dp0android\app\build\outputs\apk\debug\app-debug.apk
echo Copy that file to the phone and open it to install Bluey.
pause
