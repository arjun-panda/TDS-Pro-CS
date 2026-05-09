@echo off
REM ============================================================
REM  TDS Pro v3.1.0 — Publish Script
REM  Run from Developer Command Prompt or PowerShell
REM  Output: publish\win-x64\TDSPro.exe  (~80 MB self-contained)
REM ============================================================

echo.
echo  TDS Pro v3.1.0 — Release Publish
echo  ==================================
echo.

REM -- Clean previous publish
if exist "publish\win-x64" (
    echo  Cleaning previous publish...
    rmdir /s /q "publish\win-x64"
)

echo  Publishing self-contained single EXE...
echo.

dotnet publish TDSPro.App\TDSPro.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishReadyToRun=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o publish\win-x64

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  ERROR: Publish failed. Check errors above.
    pause
    exit /b 1
)

echo.
echo  ============================================================
echo   Publish SUCCESSFUL
echo  ============================================================
echo.
echo   Output : publish\win-x64\TDSPro.exe
echo   Size   : (see above)
echo.
echo   Next step: Run Inno Setup Compiler on TDSPro_Installer.iss
echo              to produce TDSPro_Setup_v3.1.0.exe
echo.
pause
