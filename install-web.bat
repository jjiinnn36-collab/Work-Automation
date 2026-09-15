@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set TASKNAME=납부기한알림웹
set EXEPATH=%CD%\PaymentAlert.exe

if not exist "%EXEPATH%" goto :noexe

echo.
echo  로그온할 때마다 웹 화면 서버를 알림 영역에 띄우도록 등록합니다.
echo  브라우저는 열지 않습니다. 알림 영역 아이콘을 두 번 누르면 열립니다.
echo.
echo  작업 이름 : %TASKNAME%
echo  실행 파일 : %EXEPATH% --web --no-browser
echo.
echo  계속하려면 아무 키나 누르세요.
pause >nul

rem ── 1순위: 로그온 트리거 예약 작업
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "try {" ^
  "  $a = New-ScheduledTaskAction -Execute '%EXEPATH%' -Argument '--web --no-browser' -WorkingDirectory '%CD%';" ^
  "  $t = New-ScheduledTaskTrigger -AtLogOn;" ^
  "  $s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -DontStopOnIdleEnd;" ^
  "  $p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited;" ^
  "  Register-ScheduledTask -TaskName '%TASKNAME%' -Action $a -Trigger $t -Settings $s -Principal $p -Force | Out-Null;" ^
  "  exit 0" ^
  "} catch { Write-Host ('실패: ' + $_.Exception.Message); exit 1 }"

if %errorlevel%==0 goto :done

rem ── 2순위: schtasks 로그온 트리거
echo.
echo  ScheduledTasks 모듈 등록에 실패했습니다. schtasks 로 다시 시도합니다.
echo.
schtasks /create /tn "%TASKNAME%" /tr "\"%EXEPATH%\" --web --no-browser" /sc ONLOGON /f
if %errorlevel%==0 goto :done

rem ── 3순위: 시작프로그램 폴더 바로가기
echo.
echo  예약 작업 등록이 거부되었습니다. 시작프로그램 등록을 시도합니다.
echo.
set SUF=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%SUF%\납부기한알림웹.lnk');" ^
  "$s.TargetPath='%EXEPATH%'; $s.Arguments='--web --no-browser'; $s.WorkingDirectory='%CD%'; $s.Save()" 2>nul

if exist "%SUF%\납부기한알림웹.lnk" goto :done_startup
goto :failed

:done
echo.
echo  [완료] 로그온할 때마다 웹 화면 서버가 알림 영역에 뜹니다.
echo.
echo  지금 바로 띄우려면 start-web.bat 을 실행하세요.
echo  해제 방법 : schtasks /delete /tn "%TASKNAME%" /f
echo.
pause
exit /b 0

:done_startup
echo.
echo  [완료] 시작프로그램에 등록했습니다.
echo.
echo  해제 방법: 아래 폴더에서 '납부기한알림웹' 바로가기를 삭제하세요.
echo    %SUF%
echo.
pause
exit /b 0

:failed
echo.
echo  [오류] 자동 실행 등록에 모두 실패했습니다. 회사 보안 정책으로 막혀 있을 수 있습니다.
echo         start-web.bat 을 직접 실행해 사용하세요.
echo.
pause
exit /b 1

:noexe
echo.
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1
