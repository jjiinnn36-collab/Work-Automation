@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "PaymentAlert.exe" goto :noexe

rem 알릴 건이 없으면 팝업 없이 조용히 끝난다. 그게 정상이다.
start "" "PaymentAlert.exe" %*
exit /b 0

:noexe
echo.
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1
