@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%switch-rayci-camera-mode.ps1"

if not exist "%PS_SCRIPT%" (
    echo Missing script: "%PS_SCRIPT%"
    pause
    exit /b 1
)

:menu
cls
echo ============================================
echo RayCi Camera Mode Switch
echo ============================================
echo [1] Compatible camera mode
echo [2] Native camera mode
echo [3] Compatible mode and launch RayCi
echo [4] Native mode and launch RayCi
echo [Q] Quit
echo.
set /p "CHOICE=Select an option: "

if /i "%CHOICE%"=="1" goto compatible
if /i "%CHOICE%"=="2" goto native
if /i "%CHOICE%"=="3" goto compatible_launch
if /i "%CHOICE%"=="4" goto native_launch
if /i "%CHOICE%"=="Q" goto end

echo Invalid selection.
timeout /t 2 >nul
goto menu

:compatible
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -Mode compatible
goto done

:native
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -Mode native
goto done

:compatible_launch
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -Mode compatible -Launch
goto done

:native_launch
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -Mode native -Launch
goto done

:done
echo.
pause

:end
endlocal
