@echo off
title WinterMP - local 2-player test
echo ============================================
echo  WinterMP local 2-player test
echo  Host = your Steam profile, Guest = "Guest"
echo ============================================
echo.
echo [1/2] Starting HOST instance through Steam...
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 4164420 -wintermp hostlocal -wintermp-autoload -screen-fullscreen 0 -screen-width 1280 -screen-height 720
echo       Waiting 20 seconds (host releases the single-instance lock at startup)...
timeout /t 20 /nobreak >nul
echo [2/2] Starting GUEST instance (windowed, no Steam)...
start "" /D "C:\Program Files (x86)\Steam\steamapps\common\My Winter Car" "C:\Program Files (x86)\Steam\steamapps\common\My Winter Car\mywintercar.exe" -wintermp joinlocal -wintermp-playername Guest -wintermp-autoload -screen-fullscreen 0 -screen-width 960 -screen-height 540
echo.
echo Both instances are starting (click "Play!" if a small config window appears).
echo The guest connects to the host automatically at the main menu, then BOTH
echo windows load into the save on their own (autoload) - no clicking needed.
echo.
echo   - Hold TAB in either window: both players + the "World:" sync line.
echo     The [id hash] must be IDENTICAL in both windows.
echo   - Open a door in one window - it opens in the other.
echo   - Carry/throw an item - it moves in the other window.
echo   - Wrench a RIVETT bolt - tightness replicates (both in repair mode).
echo   - IMPORTANT: never save the game in the GUEST window.
echo.
pause
