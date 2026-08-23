@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set TASKNAME=납부기한알림
set EXEPATH=%CD%\PaymentAlert.exe
set RUNTIME=09:00

if not exist "%EXEPATH%" goto :noexe

echo.
echo  작업 이름 : %TASKNAME%
echo  실행 파일 : %EXEPATH%
echo  실행 시각 : 매일 %RUNTIME%
echo.
echo  등록하시겠습니까? 계속하려면 아무 키나 누르세요.
pause >nul

rem ── 1순위: ScheduledTasks 모듈. PC가 꺼져 있어 놓친 실행을 나중에 보충한다.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "try {" ^
  "  $a = New-ScheduledTaskAction -Execute '%EXEPATH%' -WorkingDirectory '%CD%';" ^
  "  $t = New-ScheduledTaskTrigger -Daily -At %RUNTIME%;" ^
  "  $s = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew;" ^
  "  $p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited;" ^
  "  Register-ScheduledTask -TaskName '%TASKNAME%' -Action $a -Trigger $t -Settings $s -Principal $p -Force | Out-Null;" ^
  "  Write-Host 'REGISTERED_PS'; exit 0" ^
  "} catch { Write-Host ('PS_FAILED: ' + $_.Exception.Message); exit 1 }"

if %errorlevel%==0 goto :done

rem ── 2순위: schtasks. 놓친 실행 보충 옵션은 없지만 등록은 된다.
echo.
echo  ScheduledTasks 모듈 등록에 실패했습니다. schtasks 로 다시 시도합니다.
echo.
schtasks /create /tn "%TASKNAME%" /tr "\"%EXEPATH%\"" /sc DAILY /st %RUNTIME% /f
if %errorlevel%==0 goto :done_basic

rem ── 3순위: 시작프로그램 폴더. 로그온할 때마다 실행된다.
echo.
echo  작업 스케줄러 등록이 거부되었습니다. 시작프로그램 등록을 시도합니다.
echo.
set SUF=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%SUF%\납부기한알림.lnk');" ^
  "$s.TargetPath='%EXEPATH%'; $s.WorkingDirectory='%CD%'; $s.Save()" 2>nul

if exist "%SUF%\납부기한알림.lnk" goto :done_startup
goto :allfailed

:done
echo.
echo  [완료] 매일 %RUNTIME% 에 자동 실행되도록 등록했습니다.
echo         PC가 꺼져 있어 실행을 놓친 경우 켜진 뒤 곧바로 실행됩니다.
echo.
echo  등록 확인 : schtasks /query /tn "%TASKNAME%"
echo  해제 방법 : schtasks /delete /tn "%TASKNAME%" /f
echo.
pause
exit /b 0

:done_basic
echo.
echo  [완료] 매일 %RUNTIME% 에 자동 실행되도록 등록했습니다.
echo.
echo  주의: 놓친 실행을 보충하는 옵션은 적용되지 않았습니다.
echo        PC가 꺼져 있던 날은 알림이 뜨지 않습니다.
echo.
pause
exit /b 0

:done_startup
echo.
echo  [완료] 시작프로그램에 등록했습니다. 로그온할 때마다 실행됩니다.
echo.
echo  해제 방법: 아래 폴더에서 '납부기한알림' 바로가기를 삭제하세요.
echo    %SUF%
echo.
pause
exit /b 0

:allfailed
echo.
echo  [오류] 자동 실행 등록에 모두 실패했습니다.
echo         회사 보안 정책으로 막혀 있을 수 있습니다.
echo         check-env.bat 을 실행해 [11] [12] 항목을 확인하세요.
echo.
echo         당분간은 PaymentAlert.exe 를 직접 실행해 사용하세요.
echo.
pause
exit /b 1

:noexe
echo.
echo  [오류] PaymentAlert.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
echo.
pause
exit /b 1
