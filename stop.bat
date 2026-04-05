@echo off
title myPrinter - Stop All Services
color 0C

echo ============================================
echo   myPrinter - Stopping All Services
echo ============================================
echo.

REM --- Stop by window title ---
echo [*] Stopping Backend...
taskkill /F /FI "WINDOWTITLE eq myPrinter - Backend*" >nul 2>&1

echo [*] Stopping Frontend...
taskkill /F /FI "WINDOWTITLE eq myPrinter - Frontend*" >nul 2>&1

REM --- Kill by port as fallback ---
echo [*] Releasing port 5036 (Backend)...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5036 " 2^>nul') do (
    taskkill /F /PID %%a >nul 2>&1
)

echo [*] Releasing port 8080 (Frontend)...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":8080 " 2^>nul') do (
    taskkill /F /PID %%a >nul 2>&1
)

echo.
echo ============================================
echo   All services stopped.
echo ============================================
echo.
timeout /t 2 /nobreak >nul
