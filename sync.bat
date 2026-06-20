@echo off
setlocal
echo.
echo === Git Quick Sync ===
echo.

REM Show status of modified files
git status --short
echo.

set /p MSG="Enter commit message (or press Enter for 'auto-commit'): "
if "%MSG%"=="" set "MSG=auto-commit"

echo.
echo Saving changes...
git add .
git commit -m "%MSG%"
if errorlevel 1 (
    echo No changes to commit, or commit failed.
)

REM Extract version from Kiriha.csproj
set "CURRENT_VERSION="
for /f "usebackq tokens=3 delims=<>" %%a in (`findstr "<Version>" Kiriha.csproj`) do set "CURRENT_VERSION=%%a"

if "%CURRENT_VERSION%"=="" goto push_main

git rev-parse "refs/tags/v%CURRENT_VERSION%" >nul 2>&1
if not errorlevel 1 goto push_main

echo.
echo ============================================================================
echo New version v%CURRENT_VERSION% found in Kiriha.csproj!
set /p MKTAG="Create and push release tag v%CURRENT_VERSION%? [Y/N] (default: N): "
if /I not "%MKTAG%"=="Y" goto push_main

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
