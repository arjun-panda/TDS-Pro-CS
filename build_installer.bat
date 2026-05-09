@echo off
setlocal

echo ============================================================
echo  TDS Pro v2.0 — Build and Package
echo ============================================================

:: 1. Restore and build
echo.
echo [1/3] Publishing TDSPro.App (self-contained, win-x64)...
dotnet publish TDSPro.App\TDSPro.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o TDSPro.App\publish\win-x64 ^
    -p:PublishReadyToRun=true ^
    -p:PublishTrimmed=false

if errorlevel 1 (
    echo.
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)

echo.
echo [2/3] Publish complete. Output: TDSPro.App\publish\win-x64\

:: 2. Check if Inno Setup is installed
set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo.
    echo [3/3] SKIPPED: Inno Setup not found at %ISCC%
    echo        Install Inno Setup 6 from https://jrsoftware.org/isdl.php
    echo        Then re-run this script to build the installer.
    echo.
    echo Publish output is ready at: TDSPro.App\publish\win-x64\
    pause
    exit /b 0
)

echo [3/3] Building installer with Inno Setup...
mkdir installer_output 2>nul
%ISCC% TDSPro_Installer.iss

if errorlevel 1 (
    echo.
    echo ERROR: Inno Setup compilation failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  SUCCESS! Installer: installer_output\TDSPro_Setup_v2.0.0.exe
echo ============================================================
pause
