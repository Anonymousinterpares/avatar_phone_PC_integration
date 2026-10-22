@echo off
setlocal
cd /d "%~dp0"

echo Bluey can read his replies aloud on this PC. This is optional.
echo He still runs if you skip it. Nothing is sent to a speech service.
echo.

set "BLUEY_PYTHON="
where py >nul 2>&1 && call :trycmd py -3 -c "import sys; print(sys.executable)"
if defined BLUEY_PYTHON goto :foundpy
where python >nul 2>&1 && call :trycmd python -c "import sys; print(sys.executable)"
if defined BLUEY_PYTHON goto :foundpy
where python3 >nul 2>&1 && call :trycmd python3 -c "import sys; print(sys.executable)"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "C:\Python312\python.exe"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "C:\Python311\python.exe"
if defined BLUEY_PYTHON goto :foundpy
call :tryfile "C:\Python310\python.exe"
if defined BLUEY_PYTHON goto :foundpy

echo Python 3 was not found.
echo Install it from https://www.python.org/downloads/
echo On the first page of the installer, turn on "Add python.exe to PATH".
echo Then run Setup-Voice.cmd again.
goto :endfail

:foundpy
echo Using the same Python Bluey starts:
echo   %BLUEY_PYTHON%
echo.

"%BLUEY_PYTHON%" -c "import supertonic" >nul 2>&1
if not errorlevel 1 goto :checkmodel

echo Supertonic is not installed for that Python.
echo.
echo Installing it puts the voice on this computer. Bluey then finds it by itself.
echo The voice files are about 400 MB and download once, into your user folder.
choice /C YN /N /M "Install Supertonic now? [Y/N] "
if errorlevel 2 goto :declined

echo.
echo Installing Supertonic...
"%BLUEY_PYTHON%" -m pip install supertonic
if errorlevel 1 (
  echo.
  echo That install did not finish. Trying again for this Windows user only...
  "%BLUEY_PYTHON%" -m pip install --user supertonic
)
"%BLUEY_PYTHON%" -c "import supertonic" >nul 2>&1
if errorlevel 1 (
  echo.
  echo Supertonic is still not available to that Python.
  echo Bluey will not speak until this install works.
  goto :endfail
)

echo.
echo Downloading the voice. This can take a few minutes.
set PYTHONUNBUFFERED=1
"%BLUEY_PYTHON%" -c "from supertonic import TTS; TTS(model='supertonic-3', auto_download=True); print('Voice model is ready.')"
if errorlevel 1 (
  echo.
  echo Supertonic is installed, but the voice download did not finish.
  echo Run Setup-Voice.cmd again when this PC is online.
  goto :endfail
)
goto :ready

:checkmodel
echo Supertonic is already installed.
echo.
"%BLUEY_PYTHON%" -c "from supertonic.loader import get_cache_dir, has_all_onnx_modules; import sys; sys.exit(0 if has_all_onnx_modules(get_cache_dir('supertonic-3')) else 2)"
if not errorlevel 1 goto :ready
echo The spoken voice is not on this PC yet. It is about 400 MB.
choice /C YN /N /M "Download it now? [Y/N] "
if errorlevel 2 goto :later
echo.
echo Downloading the voice. This can take a few minutes.
set PYTHONUNBUFFERED=1
"%BLUEY_PYTHON%" -c "from supertonic import TTS; TTS(model='supertonic-3', auto_download=True); print('Voice model is ready.')"
if errorlevel 1 (
  echo.
  echo The voice download did not finish. Run Setup-Voice.cmd again when this PC is online.
  goto :endfail
)

:ready
echo.
echo The voice is ready. Bluey uses it when Speak replies is on.
goto :how

:later
echo.
echo Supertonic is installed. The first time you turn Speak replies on, Bluey downloads the voice.
goto :how

:declined
echo.
echo Supertonic was not installed. Bluey still runs. He will not speak replies.
goto :endok

:how
echo.
echo Right-click the Bluey tray icon and check Speak replies.
echo Voice sex picks male or female. Test TTS voice says a short line when it is ready.
goto :endok

:endfail
echo.
pause
exit /b 1

:endok
echo.
pause
exit /b 0

:trycmd
set "TRY="
for /f "delims=" %%i in ('%* 2^>nul') do set "TRY=%%i"
if not defined TRY exit /b 1
if not exist "%TRY%" exit /b 1
"%TRY%" -c "import sys" >nul 2>&1
if errorlevel 1 exit /b 1
set "BLUEY_PYTHON=%TRY%"
exit /b 0

:tryfile
if not exist "%~1" exit /b 1
"%~1" -c "import sys" >nul 2>&1
if errorlevel 1 exit /b 1
set "BLUEY_PYTHON=%~1"
exit /b 0
