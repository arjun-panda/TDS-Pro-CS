@echo off
title TDS Pro v3.1.0 — Publish Single EXE
color 2F

echo.
echo  TDS Pro v3.1.0 — Build Single Distributable EXE
echo  ════════════════════════════════════════════
echo  Output:  publish\TDSPro.exe
echo  Target:  Windows x64, self-contained (.NET bundled)
echo  Size:    ~60-80 MB (no install needed on client)
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET 8 SDK not found.
    echo  Download: https://dotnet.microsoft.com/download/dotnet/8.0
    pause & exit /b 1
)

echo  Restoring packages...
dotnet restore TDSPro.sln --nologo -v q

echo  Publishing self-contained single EXE...
dotnet publish TDSPro.UI\TDSPro.UI.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false ^
  -o publish\ ^
  --nologo -v q

if errorlevel 1 (
    echo  [ERROR] Publish failed. Check error output above.
    pause & exit /b 1
)

echo.
echo  ════════════════════════════════════════════
echo  SUCCESS — EXE ready at:  publish\TDSPro.exe
echo.
echo  Distribute this single file to clients.
echo  No .NET installation required on client PC.
echo  ════════════════════════════════════════════
echo.
pause
