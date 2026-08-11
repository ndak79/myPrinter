@echo off
title Smart Printer - Community Build
color 0B

echo ============================================
echo   Smart Printer - Community Build
echo ============================================
echo.

powershell.exe -NoProfile -File "%~dp0build-installer.ps1" ^
    -Version "1.0.0"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [!] Community build FAILED. See errors above.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Community build completed.
echo   Publish : %~dp0publish\MyPrinter.exe
echo   Setup   : %~dp0dist\smartPrinter-setup-1.0.0.exe
echo ============================================
echo.
pause
