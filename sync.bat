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

echo.
echo Pushing to GitHub...
git push origin main
if errorlevel 1 (
    echo.
    echo ERROR: Push failed. Check your internet connection or GitHub access.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo  DONE: Successfully synced with GitHub!
echo ============================================================================
pause
exit /b 0
