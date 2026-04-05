@echo off
title myPrinter - Publish Single EXE
color 0B

echo ============================================
echo   myPrinter - Build Single EXE
echo ============================================
echo.

REM --- Output directory ---
set OUTPUT=%~dp0publish

REM --- Clean previous output ---
echo [*] Cleaning previous publish output...
if exist "%OUTPUT%" rmdir /S /Q "%OUTPUT%"
mkdir "%OUTPUT%"

REM --- Publish desktop project (self-contained, single file) ---
echo [*] Publishing desktop app (self-contained, win-x64)...
dotnet publish "%~dp0desktop\MyPrinter.Desktop.csproj" ^
    -c Release ^
    /p:PublishSingleFile=true ^
    -o "%OUTPUT%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo [!] Publish FAILED. See errors above.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Published successfully!
echo   Output : %OUTPUT%\MyPrinter.exe
echo.
echo   Run as Administrator to use printer features.
echo ============================================
echo.
pause
