@echo off
title TDS Pro v3.1.0 — Startup
color 1F

echo.
echo  ████████╗██████╗ ███████╗    ██████╗ ██████╗  ██████╗
echo     ██╔══╝██╔══██╗██╔════╝    ██╔══██╗██╔══██╗██╔═══██╗
echo     ██║   ██║  ██║███████╗    ██████╔╝██████╔╝██║   ██║
echo     ██║   ██║  ██║╚════██║    ██╔═══╝ ██╔══██╗██║   ██║
echo     ██║   ██████╔╝███████║    ██║     ██║  ██║╚██████╔╝
echo     ╚═╝   ╚═════╝ ╚══════╝    ╚═╝     ╚═╝  ╚═╝ ╚═════╝
echo.
echo  v3.1.0 — Income-tax Act 2025 — Production Build
echo  ─────────────────────────────────────────────
echo.

:: Check .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET 8 SDK not found.
    echo.
    echo  Download from: https://dotnet.microsoft.com/download/dotnet/8.0
    echo  Install and run this file again.
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set DOTNET_VER=%%v
echo  .NET SDK: %DOTNET_VER%
echo.

echo  [1/3] Restoring NuGet packages...
dotnet restore TDSPro.sln --nologo -v q
if errorlevel 1 ( echo  [ERROR] Package restore failed. & pause & exit /b 1 )

echo  [2/3] Building solution (Release)...
dotnet build TDSPro.sln -c Release --nologo -v q
if errorlevel 1 ( echo  [ERROR] Build failed. & pause & exit /b 1 )

echo  [3/3] Launching TDS Pro...
echo.
dotnet run --project TDSPro.UI\TDSPro.UI.csproj -c Release --no-build

pause
