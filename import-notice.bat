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

echo.
pause
exit /b %RC%

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
