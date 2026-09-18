@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" goto :nocsc

set CORE=src\Model.cs src\Tsv.cs src\BusinessDays.cs src\Holidays.cs src\Repository.cs src\Scheduler.cs src\Attachments.cs src\Db.cs src\DataPaths.cs src\Importer.cs src\Ops.cs src\SheetReader.cs src\LoanSchedule.cs
set OUTDIR=%TEMP%\pa_tests
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo.
echo ============================================
echo   1. 날짜 계산 및 자료 적재 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Tests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll %CORE% test\TestRunner.cs test\LoanScheduleTests.cs
if not exist "%OUTDIR%\Tests.exe" goto :buildfail
"%OUTDIR%\Tests.exe"
set RC1=%errorlevel%

echo.
echo ============================================
echo   2. 팝업 화면 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Smoke.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll %CORE% src\UiKit.cs src\AlertForm.cs test\FormSmokeTest.cs
if not exist "%OUTDIR%\Smoke.exe" goto :buildfail
"%OUTDIR%\Smoke.exe"
set RC2=%errorlevel%

echo.
echo ============================================
echo   3. 실제 공휴일 기준 연간 일정표
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\Report.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll %CORE% test\ScheduleReport.cs
if not exist "%OUTDIR%\Report.exe" goto :buildfail
"%OUTDIR%\Report.exe" %1
set RC3=%errorlevel%

echo.
echo ============================================
echo   4. 자료 DB (SQLite) 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\DbTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll %CORE% test\DbTests.cs
if not exist "%OUTDIR%\DbTests.exe" goto :buildfail
"%OUTDIR%\DbTests.exe"
set RC4=%errorlevel%

echo.
echo ============================================
echo   5. 웹 화면 서버 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\WebTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll %CORE% src\WebJson.cs src\WebServer.cs src\WebServer.Features.cs src\WebServer.Loans.cs test\WebTests.cs test\WebLoanTests.cs
if not exist "%OUTDIR%\WebTests.exe" goto :buildfail
"%OUTDIR%\WebTests.exe"
set RC5=%errorlevel%

echo.
echo ============================================
echo   6. 운영 부품 (백업·기록·경고) 테스트
echo ============================================
"%CSC%" /nologo /target:exe /out:"%OUTDIR%\OpsTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll %CORE% test\OpsTests.cs
if not exist "%OUTDIR%\OpsTests.exe" goto :buildfail
"%OUTDIR%\OpsTests.exe"
set RC6=%errorlevel%

echo.
echo ============================================
echo   7. 웹 화면 규칙 테스트 (Node 가 있을 때만)
echo ============================================
set RC7=0
where node >nul 2>&1
if errorlevel 1 goto :nonode
pushd jbam-web
node --test lib/logic.test.ts
set RC7=%errorlevel%
popd
goto :afternode
:nonode
echo  Node.js 가 없어 건너뜁니다. 화면을 고치는 PC 에서만 필요합니다.
:afternode

rd /s /q "%OUTDIR%" >nul 2>&1

echo.
if not "%RC3%"=="0" goto :failed
if not "%RC1%"=="0" goto :failed
if not "%RC2%"=="0" goto :failed
if not "%RC4%"=="0" goto :failed
if not "%RC5%"=="0" goto :failed
if not "%RC6%"=="0" goto :failed
if not "%RC7%"=="0" goto :failed
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
