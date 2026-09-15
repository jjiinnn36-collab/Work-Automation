@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo.
echo ============================================
echo   납부서 판독 -^> amounts.tsv 반영
echo ============================================

if "%~1"=="" goto :usage

powershell -NoProfile -ExecutionPolicy Bypass -File "tools\import-notice.ps1" -Pdf "%~1"
set RC=%errorlevel%
if not "%RC%"=="0" goto :done

echo  프로그램 자료(DB)로 가져오는 중...
if not exist "PaymentAlert.exe" goto :noexe
start "" /wait "PaymentAlert.exe" --import-amounts
set RC=%errorlevel%
if "%RC%"=="0" echo  가져오기 완료. 팝업·보드·웹 화면에 반영됩니다.
if not "%RC%"=="0" echo  [오류] 가져오기에 실패했습니다. 자료 폴더의 run.log 를 확인하세요.

:done
echo.
pause
exit /b %RC%

:noexe
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1

:usage
echo.
echo  사용법: 납부서 PDF 파일을 이 배치 파일 위로 끌어다 놓으세요.
echo.
echo  또는 명령으로:
echo     import-notice.bat "경로\납부서.pdf"
echo.
echo  자동 판독 대상: 국세청 부가가치세 납부서
echo.
echo  감독분담금 통보문과 금융투자협회비 안내문은 스캔 문서라
echo  숫자가 잘못 읽힙니다. data\amounts.tsv 에 직접 입력하세요.
echo.
pause
exit /b 1
