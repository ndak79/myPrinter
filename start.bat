@echo off
title myPrinter - Start
color 0A

echo ============================================
echo   myPrinter - Launch Options
echo ============================================
echo.
echo   [1] Desktop App  (WinForms + WebView2, recommended)
echo   [2] Browser Mode (backend + browser, dev/legacy)
echo.
set /p CHOICE="Choose [1/2]: "

if "%CHOICE%"=="2" goto BROWSER_MODE

:DESKTOP_MODE
echo.
echo [*] Launching desktop app (requires Administrator)...
set EXE=%~dp0desktop\bin\Debug\net10.0-windows\MyPrinter.exe

if not exist "%EXE%" (
    echo [*] Debug build not found, building now...
    dotnet build "%~dp0desktop\MyPrinter.Desktop.csproj" -c Debug
    if %ERRORLEVEL% neq 0 (
        echo [!] Build failed.
        pause
        exit /b 1
    )
)

REM Launch with elevation request (manifest handles UAC prompt)
start "" "%EXE%"
goto END

:BROWSER_MODE
echo.

REM --- Kill any existing instances first ---
echo [*] Cleaning up existing processes...
taskkill /F /FI "WINDOWTITLE eq myPrinter - Backend*" >nul 2>&1
timeout /t 1 /nobreak >nul

REM --- Start Backend ---
echo [*] Starting Backend (http://localhost:8787)...
start "myPrinter - Backend" cmd /k "cd /d %~dp0backend && dotnet run"

REM --- Wait for backend then open browser ---
echo [*] Waiting for backend to start...
timeout /t 4 /nobreak >nul

echo [*] Opening browser...
start http://localhost:8787/../index.html
REM Frontend is served from backend static files or open directly:
start "%~dp0frontend\index.html"

echo.
echo ============================================
echo   Backend : http://localhost:8787
echo ============================================

:END
echo.
