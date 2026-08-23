@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" goto :nocsc

echo.
echo  빌드 중...
echo.

"%CSC%" /nologo /target:winexe /out:"PaymentAlert.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Xml.dll ^
  /optimize+ ^
  "src\*.cs"

if not exist "PaymentAlert.exe" goto :failed

echo.
echo  빌드 성공: %CD%\PaymentAlert.exe
echo.
echo  다음 단계
echo    1. data\payment-master.tsv 에 납부 항목이 있는지 확인
echo    2. install-task.bat 을 실행해 매일 자동 실행 등록
echo.
pause
exit /b 0

:nocsc
echo.
echo  [오류] C# 컴파일러를 찾을 수 없습니다.
echo         .NET Framework 4.x 가 설치되어 있어야 합니다.
echo         check-env.bat 을 실행해 환경을 확인하세요.
echo.
pause
exit /b 1

:failed
echo.
echo  [오류] 빌드에 실패했습니다. 위의 메시지를 확인하세요.
echo.
pause
exit /b 1
