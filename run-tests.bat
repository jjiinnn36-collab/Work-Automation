@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" goto :nocsc

set CORE=src\Model.cs src\Tsv.cs src\BusinessDays.cs src\Holidays.cs src\Repository.cs src\Scheduler.cs
set OUTDIR=%TEMP%\pa_tests
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo.
echo ============================================
echo   1. 날짜 계산 및 자료 적재 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Tests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll %CORE% test\TestRunner.cs
if not exist "%OUTDIR%\Tests.exe" goto :buildfail
"%OUTDIR%\Tests.exe"
set RC1=%errorlevel%

echo.
echo ============================================
echo   2. 팝업 화면 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Smoke.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll %CORE% src\AlertForm.cs test\FormSmokeTest.cs
if not exist "%OUTDIR%\Smoke.exe" goto :buildfail
"%OUTDIR%\Smoke.exe"
set RC2=%errorlevel%

echo.
echo ============================================
echo   3. 실제 공휴일 기준 연간 일정표
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Report.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll %CORE% test\ScheduleReport.cs
if not exist "%OUTDIR%\Report.exe" goto :buildfail
"%OUTDIR%\Report.exe" %1
set RC3=%errorlevel%

rd /s /q "%OUTDIR%" >nul 2>&1

echo.
if not "%RC3%"=="0" goto :failed
if not "%RC1%"=="0" goto :failed
if not "%RC2%"=="0" goto :failed
echo  전체 통과.
echo.
pause
exit /b 0

:failed
echo  실패한 테스트가 있습니다. 위 내용을 확인하세요.
echo.
pause
exit /b 1

:buildfail
echo.
echo  [오류] 테스트 빌드에 실패했습니다.
echo.
pause
exit /b 1

:nocsc
echo.
echo  [오류] C# 컴파일러를 찾을 수 없습니다.
echo.
pause
exit /b 1
