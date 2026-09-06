@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "PaymentAlert.exe" goto :noexe

start "" "PaymentAlert.exe" --board
exit /b 0

:noexe
echo.
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1
