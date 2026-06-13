@echo off
setlocal EnableDelayedExpansion
title WinterMP - local 2-player test

rem Override with: set WINTERMP_GAME_DIR=... before running
set "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
if defined WINTERMP_GAME_DIR set "GAME_DIR=!WINTERMP_GAME_DIR!"

set "READY_FLAG=%GAME_DIR%\WinterMP\hostlocal-ready.flag"
set "MAX_WAIT=90"
set "TOOLS=%~dp0"

if not exist "%GAME_DIR%\mywintercar.exe" goto missing_game

echo ============================================
echo  WinterMP local 2-player test
echo  Host = your Steam profile, Guest = "Guest"
echo ============================================
echo.

echo [1/2] Starting HOST instance (direct launch, HostLocal mode)...
if exist "%READY_FLAG%" del /f /q "%READY_FLAG%" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS%patch-mwc-maindata.ps1" -GameDir "%GAME_DIR%"
if errorlevel 1 goto script_failed

powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS%seed-mwc-display.ps1" -Width 960 -Height 540 -Fullscreen 0
if errorlevel 1 goto script_failed

cd /d "%GAME_DIR%"
set "WINTERMP_LOG_ROLE=host"
set "HOST_UNITY_LOG=%GAME_DIR%\BepInEx\LogOutput-host-unity.log"
start "WinterMP Host" mywintercar.exe -no-dialogs -fastboot-dev -wintermp hostlocal -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "%HOST_UNITY_LOG%"

echo       Waiting for host single-instance unlock (up to %MAX_WAIT%s)...
set /a WAITED=0

:wait_host
if exist "%READY_FLAG%" goto host_ready
if !WAITED! GEQ %MAX_WAIT% goto host_timeout
powershell -NoProfile -Command "Start-Sleep -Seconds 1" >nul 2>&1
set /a WAITED+=1
goto wait_host

:host_timeout
echo.
echo ERROR: Host never released the single-instance lock within %MAX_WAIT% seconds.
echo        Check BepInEx\LogOutput-host.log for "HostLocal ready (mutex released)"
echo        and make sure the latest WinterMP.Core.dll is deployed.
echo.
pause
exit /b 1

:host_ready
echo       Host unlocked after !WAITED! second(s).
echo [2/2] Starting GUEST instance...

powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS%patch-mwc-maindata.ps1" -GameDir "%GAME_DIR%"
if errorlevel 1 goto script_failed

powershell -NoProfile -ExecutionPolicy Bypass -File "%TOOLS%seed-mwc-display.ps1" -Width 960 -Height 540 -Fullscreen 0
if errorlevel 1 goto script_failed

cd /d "%GAME_DIR%"
set "WINTERMP_LOG_ROLE=guest"
set "GUEST_UNITY_LOG=%GAME_DIR%\BepInEx\LogOutput-guest-unity.log"
start "WinterMP Guest" mywintercar.exe -no-dialogs -fastboot-dev -wintermp joinlocal -wintermp-playername Guest -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "%GUEST_UNITY_LOG%"

echo.
echo Both instances are starting (FastBoot DEV: fast Continue + ES2 tag skip, async GAME preload).
echo.
echo   Logs: BepInEx\LogOutput-host.log  +  BepInEx\LogOutput-guest.log
echo         WinterMP\boot-trace-host.log + WinterMP\boot-trace-guest.log
echo.
echo   - Hold TAB in either window: both players + the "World:" sync line.
echo     The [id hash] must be IDENTICAL in both windows.
echo   - Open a door in one window - it opens in the other.
echo   - Carry/throw an item - it moves in the other window.
echo   - Wrench a RIVETT bolt - tightness replicates (both in repair mode).
echo   - IMPORTANT: never save the game in the GUEST window.
echo.
pause
exit /b 0

:missing_game
echo ERROR: Game not found at:
echo   "%GAME_DIR%"
echo Edit GAME_DIR in tools\Local2PTest.bat or set WINTERMP_GAME_DIR.
pause
exit /b 1

:script_failed
echo.
echo ERROR: A setup script failed (patch-mwc-maindata.ps1 or seed-mwc-display.ps1).
echo        See the PowerShell error above.
echo.
pause
exit /b 1
