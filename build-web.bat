@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem 웹 화면(jbam-web, Next.js + shadcn/ui)을 정적 파일로 만들어 web\ 폴더에 넣는다 (ADR-0007).
rem 회사 PC 에는 Node 가 없어도 된다. 만든 web\ 폴더를 저장소에 함께 올린다.

where node >nul 2>&1
if errorlevel 1 goto :nonode
if not exist "jbam-web\node_modules" goto :nomodules

echo.
echo  1. 화면 규칙 단위 테스트
pushd jbam-web
call node --test lib/logic.test.ts
if errorlevel 1 goto :testfail

echo.
echo  2. 정적 빌드 (타입 검사 포함)
set NEXT_TELEMETRY_DISABLED=1
call node_modules\.bin\next.cmd build
if errorlevel 1 goto :buildfail
popd

echo.
echo  3. web 폴더로 복사
robocopy "jbam-web\out" "web" /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :copyfail

echo.
echo  [완료] web\ 폴더를 새 화면으로 바꿨습니다. 실행 중인 웹 화면은 새로 고침하면 반영됩니다.
echo.
pause
exit /b 0

:nonode
echo.
echo  [오류] Node.js 가 없습니다. 화면을 고칠 PC 에서만 필요합니다. 쓰기만 하는 PC 는 build.bat 만 쓰면 됩니다.
echo.
pause
exit /b 1

:nomodules
echo.
echo  [오류] jbam-web\node_modules 가 없습니다. jbam-web 폴더에서 패키지를 먼저 설치하세요.
echo.
pause
exit /b 1

:testfail
popd
echo.
echo  [오류] 화면 규칙 테스트가 실패했습니다.
echo.
pause
exit /b 1

:buildfail
popd
echo.
echo  [오류] 화면 빌드에 실패했습니다. 위 메시지를 확인하세요.
echo.
pause
exit /b 1

:copyfail
echo.
echo  [오류] web 폴더로 복사하지 못했습니다.
echo.
pause
exit /b 1
