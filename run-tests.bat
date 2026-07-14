@echo off
setlocal

echo.
echo === Running Kiriha tests ===
dotnet test .\tests\Kiriha.Tests\Kiriha.Tests.csproj --configuration Release
if errorlevel 1 (
    echo.
    echo ERROR: tests failed.
    pause
    exit /b 1
)

echo.
echo DONE: tests passed.
pause
exit /b 0
