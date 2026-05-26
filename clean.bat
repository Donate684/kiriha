@echo off
setlocal
echo.
echo === Cleaning build artifacts ===
echo.

REM Delete bin, obj, publish, and Releases directories recursively
for /d /r "%~dp0" %%p in (bin,obj,publish) do (
    if exist "%%p" (
        echo Deleting "%%p"...
        rmdir /s /q "%%p"
    )
)

echo.
echo ============================================================================
echo  DONE: Workspace is clean!
echo ============================================================================
pause
exit /b 0
