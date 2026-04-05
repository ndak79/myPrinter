@echo off
title myPrinter - Restart All Services
color 0E

echo ============================================
echo   myPrinter - Restarting All Services
echo ============================================
echo.

REM --- Stop ---
echo [*] Stopping existing services...
taskkill /F /FI "WINDOWTITLE eq myPrinter - Backend*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq myPrinter - Frontend*" >nul 2>&1

for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5036 " 2^>nul') do (
    taskkill /F /PID %%a >nul 2>&1
)
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":8080 " 2^>nul') do (
    taskkill /F /PID %%a >nul 2>&1
)

echo [*] Waiting for ports to be released...
timeout /t 2 /nobreak >nul

REM --- Start Backend ---
echo [*] Starting Backend (http://localhost:5036)...
start "myPrinter - Backend" cmd /k "cd /d %~dp0backend && dotnet run --launch-profile http"

REM --- Start Frontend ---
echo [*] Starting Frontend (http://localhost:8080)...
start "myPrinter - Frontend" cmd /k "cd /d %~dp0frontend && python -m http.server 8080"

echo.
echo ============================================
echo   Services restarted!
echo   Backend  : http://localhost:5036
echo   Frontend : http://localhost:8080
echo   Swagger  : http://localhost:5036/swagger
echo ============================================
echo.
timeout /t 2 /nobreak >nul
