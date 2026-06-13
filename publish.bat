@echo off
title myPrinter - Product Build
color 0B

echo ============================================
echo   myPrinter - Product Build
echo ============================================
echo.

powershell.exe -NoProfile -File "%~dp0build-installer.ps1" ^
    -ServerUrl "http://103.82.24.37" ^
    -ProductId "prod_smartprinter" ^
    -TransportKeyId "actenc_prod_c094fac09065_v1" ^
    -TransportPublicKey "OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM" ^
    -AllowInsecureHttp

if %ERRORLEVEL% neq 0 (
    echo.
    echo [!] Product build FAILED. See errors above.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Product build completed.
echo   Publish : %~dp0publish\MyPrinter.exe
echo   Setup   : %~dp0dist\smartPrinter-setup-1.0.0.exe
echo ============================================
echo.
pause
