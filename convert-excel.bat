@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo.
echo ============================================
echo   엑셀 양식 -^> payment-master.tsv 변환
echo ============================================

powershell -NoProfile -ExecutionPolicy Bypass -File "tools\convert-excel.ps1" %*
set RC=%errorlevel%

if not "%RC%"=="0" goto :failed

echo  data\payment-master.tsv 가 갱신되었습니다.
echo.
pause
exit /b 0

:failed
echo  변환에 실패했습니다. 위 메시지를 확인하세요.
echo.
pause
exit /b %RC%
