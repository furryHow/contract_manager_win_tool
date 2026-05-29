@echo off

cd /d "%~dp0"

echo ============================================
echo   ContractManager - Publish
echo ============================================
echo.

dotnet publish -c Release --self-contained false -r win-x64 -o ./publish

if %errorlevel% neq 0 (
    echo.
    echo [FAILED] Publish error!
    pause
    exit /b 1
)

echo.
echo [OK] Output: %~dp0publish\
pause
