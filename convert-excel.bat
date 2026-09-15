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
echo  프로그램 자료(DB)로 가져오는 중...
if not exist "PaymentAlert.exe" goto :noexe
start "" /wait "PaymentAlert.exe" --import-master
set RC=%errorlevel%
if not "%RC%"=="0" goto :importfail

echo  가져오기 완료. 팝업·보드·웹 화면에 반영됩니다.
echo.
pause
exit /b 0

:importfail
echo  [오류] 가져오기에 실패했습니다. 자료 폴더의 run.log 를 확인하세요.
echo.
pause
exit /b %RC%

:noexe
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1

:failed
echo  변환에 실패했습니다. 위 메시지를 확인하세요.
echo.
pause
exit /b %RC%
