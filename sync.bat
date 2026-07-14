@echo off
setlocal EnableDelayedExpansion

echo.
echo === Git Quick Sync ===
echo.

REM Show status of modified files
git status --short
echo.

REM Extract version from src\Kiriha.csproj
set "CURRENT_VERSION="
for /f "usebackq tokens=3 delims=<>" %%a in (`findstr "<Version>" src\Kiriha.csproj`) do set "CURRENT_VERSION=%%a"

if "%CURRENT_VERSION%"=="" (
    echo ERROR: Version tag not found in src\Kiriha.csproj!
    pause
    exit /b 1
)

echo Current version is: v%CURRENT_VERSION%
echo.
echo Do you want to bump the version?
echo [1] Major
echo [2] Minor
echo [3] Patch
echo [4] Custom
echo [5] Skip (Do not bump)
set /p BUMP_CHOICE="Select option [1-5] (Default: 5): "

if "%BUMP_CHOICE%"=="" set "BUMP_CHOICE=5"

set "NEW_VERSION="
if "%BUMP_CHOICE%"=="4" (
    set /p NEW_VERSION="Enter custom version (e.g. 2.0.0-beta): "
) else if not "%BUMP_CHOICE%"=="5" (
    for /f "tokens=1,2,3 delims=." %%A in ("%CURRENT_VERSION%") do (
        set /a MAJOR=%%A
        set /a MINOR=%%B
        set /a PATCH=%%C
    )
    if "%BUMP_CHOICE%"=="1" (
        set /a MAJOR+=1
        set MINOR=0
        set PATCH=0
    ) else if "%BUMP_CHOICE%"=="2" (
        set /a MINOR+=1
        set PATCH=0
    ) else if "%BUMP_CHOICE%"=="3" (
        set /a PATCH+=1
    )
    set "NEW_VERSION=!MAJOR!.!MINOR!.!PATCH!"
)

set "MSG="
if defined NEW_VERSION (
    if not "!NEW_VERSION!"=="%CURRENT_VERSION%" (
        echo Bumped version to v!NEW_VERSION!
        REM Replace version in csproj
        powershell -NoProfile -Command "$t = [System.IO.File]::ReadAllText('src\Kiriha.csproj'); $t = $t -replace '<Version>.*?</Version>', '<Version>!NEW_VERSION!</Version>'; [System.IO.File]::WriteAllText('src\Kiriha.csproj', $t)"
        
        set "CURRENT_VERSION=!NEW_VERSION!"
        set "MSG=Update v!NEW_VERSION!"
    )
)

:after_bump
if "!MSG!"=="" (
    set /p MSG="Enter commit message (or press Enter for 'auto-commit'): "
    if "!MSG!"=="" set "MSG=auto-commit"
) else (
    echo.
    echo Using commit message: !MSG!
)

echo.
echo Saving changes...
git add .
git commit -m "!MSG!"
if errorlevel 1 (
    echo No changes to commit, or commit failed.
)

set "PUSH_TAG="
git rev-parse "refs/tags/v%CURRENT_VERSION%" >nul 2>&1
if not errorlevel 1 goto push_main

echo.
echo ============================================================================
if defined NEW_VERSION (
    set /p MKTAG="Create and push release tag v%CURRENT_VERSION%? [Y/N] (default: Y): "
    if "!MKTAG!"=="" set "MKTAG=Y"
) else (
    echo New version v%CURRENT_VERSION% found in Kiriha.csproj!
    set /p MKTAG="Create and push release tag v%CURRENT_VERSION%? [Y/N] (default: N): "
    if "!MKTAG!"=="" set "MKTAG=N"
)

if /I not "!MKTAG!"=="Y" goto push_main

git tag -a v%CURRENT_VERSION% -m "Release %CURRENT_VERSION%"
echo Tag v%CURRENT_VERSION% created.
set "PUSH_TAG=v%CURRENT_VERSION%"

:push_main
echo.
echo Pushing to GitHub...
git push origin main
if errorlevel 1 (
    echo.
    echo ERROR: Push failed. Check your internet connection or GitHub access.
    pause
    exit /b 1
)

if not "%PUSH_TAG%"=="" (
    echo.
    echo Pushing release tag %PUSH_TAG%...
    git push origin %PUSH_TAG%
    if errorlevel 1 (
        echo ERROR: Failed to push tag %PUSH_TAG%.
    ) else (
        echo.
        echo GitHub Actions will now build and publish the release!
    )
)

echo.
echo ============================================================================
echo  DONE: Successfully synced with GitHub!
echo ============================================================================
pause
exit /b 0
