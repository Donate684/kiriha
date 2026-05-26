@echo off
REM ============================================================================
REM  Kiriha release builder
REM
REM  Usage:  release.bat <version>
REM  Example: release.bat 1.0.3
REM
REM  Pipeline:
REM    1. Patch <Version>X.Y.Z</Version> in Kiriha.csproj (single source of
REM       truth read at runtime by Kiriha.Core.AppInfo, so Welcome / About
REM       UI labels track this number automatically).
REM    2. dotnet test    -> fail fast before producing release artifacts.
REM    3. dotnet publish -> ./publish (Release, win-x64, self-contained, R2R).
REM    4. vpk pack       -> ./Releases (Velopack delta + full installer).
REM ============================================================================

setlocal enabledelayedexpansion

if "%~1"=="" goto usage
set "VERSION=%~1"

echo %VERSION%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 goto badver

set "CSPROJ=Kiriha.csproj"
if not exist "%CSPROJ%" (
    echo ERROR: %CSPROJ% not found. Run this script from the project root.
    pause
    exit /b 1
)

echo.
echo === [1/3] Patching %CSPROJ% ^<Version^> -^> %VERSION% ===
REM In-place rewrite via .NET APIs to preserve UTF-8 / no-BOM encoding that
REM dotnet expects on csproj. Using Set-Content would risk inserting a BOM
REM under Windows PowerShell 5.1.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$f='%CSPROJ%'; $t=[System.IO.File]::ReadAllText($f); $n=[regex]::Replace($t, '<Version>[^<]+</Version>', '<Version>%VERSION%</Version>'); if ($t -eq $n) { Write-Host '  (no <Version> tag matched - aborting)'; exit 2 } [System.IO.File]::WriteAllText($f, $n, (New-Object System.Text.UTF8Encoding($false))); Write-Host '  OK'"
if errorlevel 1 (
    echo ERROR: csproj patch failed.
    pause
    exit /b 1
)

echo.
echo === [2/4] dotnet test ===
dotnet test .\Tests\Kiriha.Tests\Kiriha.Tests.csproj --configuration Release
if errorlevel 1 (
    echo ERROR: tests failed.
    pause
    exit /b 1
)

echo.
echo === [3/4] dotnet publish ===
if exist publish rmdir /s /q publish
dotnet publish -c Release --runtime win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)

echo.
echo === [4/4] vpk pack ===
vpk pack --packId Kiriha --packVersion %VERSION% --packDir ./publish --mainExe Kiriha.exe --icon ./Assets/kiriha.ico
if errorlevel 1 (
    echo ERROR: vpk pack failed.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo  DONE: Kiriha %VERSION% packaged.
echo  Output: .\Releases\
echo ============================================================================
pause
exit /b 0

:usage
echo Usage:   release.bat ^<version^>
echo Example: release.bat 1.0.3
echo.
echo Tip: drag this .bat into a terminal, or run from cmd/powershell with the
echo      version argument - double-clicking it without an argument just shows
echo      this message and waits.
pause
exit /b 1

:badver
echo ERROR: version must be in format X.Y.Z  (e.g. 1.0.3) - got "%VERSION%".
pause
exit /b 1
