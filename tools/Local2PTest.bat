@echo off
setlocal EnableDelayedExpansion
title WinterMP - local 2-player test

set "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
set "READY_FLAG=%GAME_DIR%\WinterMP\hostlocal-ready.flag"
set "MAX_WAIT=90"

echo ============================================
echo  WinterMP local 2-player test
echo  Host = your Steam profile, Guest = "Guest"
echo ============================================
echo.
echo [1/2] Starting HOST instance through Steam...
if exist "%READY_FLAG%" del /f /q "%READY_FLAG%" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0seed-mwc-display.ps1" -Width 1280 -Height 720 -Fullscreen 0
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 4164420 -wintermp hostlocal -wintermp-autoload -screen-fullscreen 0 -screen-width 1280 -screen-height 720
echo       Waiting for host to release the single-instance lock (up to %MAX_WAIT%s)...
set READY=0
set /a WAITED=0
:wait_host
if exist "%READY_FLAG%" (
    set READY=1
    goto host_ready
)
if !WAITED! GEQ %MAX_WAIT% goto host_timeout
timeout /t 1 /nobreak >nul
set /a WAITED+=1
goto wait_host

:host_timeout
echo.
echo ERROR: Host never signaled ready within %MAX_WAIT% seconds.
echo        Check BepInEx\LogOutput.log for "Releasing single-instance mutex"
echo        and make sure the latest WinterMP.Core.dll is deployed.
echo.
pause
exit /b 1

:host_ready
echo       Host ready after !WAITED! second(s).
echo [2/2] Starting GUEST instance (windowed, no Steam)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0seed-mwc-display.ps1" -Width 960 -Height 540 -Fullscreen 0
cd /d "%GAME_DIR%"
start "WinterMP Guest" mywintercar.exe -wintermp joinlocal -wintermp-playername Guest -wintermp-autoload -screen-fullscreen 0 -screen-width 960 -screen-height 540
echo.
echo Both instances are starting (FastBoot skips splash and auto-loads when a save exists).
echo.
echo   - Hold TAB in either window: both players + the "World:" sync line.
echo     The [id hash] must be IDENTICAL in both windows.
echo   - Open a door in one window - it opens in the other.
echo   - Carry/throw an item - it moves in the other window.
echo   - Wrench a RIVETT bolt - tightness replicates (both in repair mode).
echo   - IMPORTANT: never save the game in the GUEST window.
echo.
pause
