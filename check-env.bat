@echo off
setlocal
set OUT=%USERPROFILE%\Desktop\env-check-result.txt
set CSC64=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

echo ============================================ > "%OUT%"
echo  PAYMENT ALERT - ENVIRONMENT CHECK          >> "%OUT%"
echo  %DATE% %TIME%                              >> "%OUT%"
echo ============================================ >> "%OUT%"
echo. >> "%OUT%"

echo [1] WINDOWS >> "%OUT%"
ver >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo [2] USER / ADMIN >> "%OUT%"
echo USER=%USERNAME% >> "%OUT%"
echo DOMAIN=%USERDOMAIN% >> "%OUT%"
net session >nul 2>&1
if %errorlevel%==0 echo ADMIN=YES >> "%OUT%"
if not %errorlevel%==0 echo ADMIN=NO >> "%OUT%"
echo. >> "%OUT%"

echo [3] .NET FRAMEWORK >> "%OUT%"
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo [4] C# COMPILER >> "%OUT%"
if exist "%CSC64%" echo CSC64=FOUND >> "%OUT%"
if not exist "%CSC64%" echo CSC64=NOT_FOUND >> "%OUT%"
if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" echo CSC32=FOUND >> "%OUT%"
if not exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" echo CSC32=NOT_FOUND >> "%OUT%"
if not exist "%CSC64%" goto :skip_compile
echo. >> "%OUT%"

echo [5] COMPILE AND RUN FROM TEMP >> "%OUT%"
set W1=%TEMP%\_pa_probe
rd /s /q "%W1%" >nul 2>&1
mkdir "%W1%" >nul 2>&1
echo class P{static void Main(){System.Console.WriteLine("TEMP_EXEC_OK");}} > "%W1%\p.cs"
"%CSC64%" /nologo /out:"%W1%\p.exe" "%W1%\p.cs" >> "%OUT%" 2>&1
if not exist "%W1%\p.exe" goto :t_fail
"%W1%\p.exe" >> "%OUT%" 2>&1
goto :t_done
:t_fail
echo TEMP_COMPILE=FAILED >> "%OUT%"
:t_done
rd /s /q "%W1%" >nul 2>&1
echo. >> "%OUT%"

echo [6] COMPILE AND RUN FROM DOCUMENTS >> "%OUT%"
set W2=%USERPROFILE%\Documents\_pa_probe
rd /s /q "%W2%" >nul 2>&1
mkdir "%W2%" >nul 2>&1
if not exist "%W2%" goto :d_nodir
echo class P{static void Main(){System.Console.WriteLine("DOCS_EXEC_OK");}} > "%W2%\p.cs"
"%CSC64%" /nologo /out:"%W2%\p.exe" "%W2%\p.cs" >> "%OUT%" 2>&1
if not exist "%W2%\p.exe" goto :d_fail
"%W2%\p.exe" >> "%OUT%" 2>&1
goto :d_done
:d_fail
echo DOCS_COMPILE=FAILED >> "%OUT%"
goto :d_done
:d_nodir
echo DOCS_MKDIR=DENIED >> "%OUT%"
:d_done
rd /s /q "%W2%" >nul 2>&1
echo. >> "%OUT%"

echo [7] WINFORMS GUI >> "%OUT%"
set W3=%TEMP%\_pa_gui
rd /s /q "%W3%" >nul 2>&1
mkdir "%W3%" >nul 2>&1
> "%W3%\g.cs" echo using System.Windows.Forms;
>> "%W3%\g.cs" echo class G{ static void Main(){ Form f=new Form(); f.Text="t"; System.Console.WriteLine("WINFORMS_OK"); } }
"%CSC64%" /nologo /target:exe /out:"%W3%\g.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%W3%\g.cs" >> "%OUT%" 2>&1
if not exist "%W3%\g.exe" goto :g_fail
"%W3%\g.exe" >> "%OUT%" 2>&1
goto :g_done
:g_fail
echo WINFORMS=FAILED >> "%OUT%"
:g_done
rd /s /q "%W3%" >nul 2>&1
goto :after_compile

:skip_compile
echo. >> "%OUT%"
echo [5-7] SKIPPED - no csc.exe >> "%OUT%"

:after_compile
echo. >> "%OUT%"

echo [8] PYTHON >> "%OUT%"
where python >> "%OUT%" 2>&1
if not %errorlevel%==0 echo PYTHON=NOT_FOUND >> "%OUT%"
echo. >> "%OUT%"

echo [9] POWERSHELL >> "%OUT%"
powershell -NoProfile -Command "$PSVersionTable.PSVersion.ToString()" >> "%OUT%" 2>&1
powershell -NoProfile -Command "Get-ExecutionPolicy -List | Out-String" >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo [10] APPLOCKER / WDAC >> "%OUT%"
powershell -NoProfile -Command "$k=Get-ChildItem 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\SrpV2' -EA SilentlyContinue; if($k){'APPLOCKER=CONFIGURED: '+($k.PSChildName -join ',')}else{'APPLOCKER=NONE'}" >> "%OUT%" 2>&1
powershell -NoProfile -Command "$s=Get-Service AppIDSvc -EA SilentlyContinue; if($s){'AppIDSvc='+$s.Status}else{'AppIDSvc=ABSENT'}" >> "%OUT%" 2>&1
powershell -NoProfile -Command "try{$d=Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -EA Stop; 'WDAC_Enforcement='+$d.CodeIntegrityPolicyEnforcementStatus}catch{'WDAC=UNKNOWN'}" >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo [11] TASK SCHEDULER - REAL CREATE TEST >> "%OUT%"
schtasks /create /tn "PA_PROBE_TEST" /tr "cmd.exe /c exit" /sc ONCE /st 23:59 /f >nul 2>&1
if not %errorlevel%==0 goto :sch_denied
echo SCHTASKS_CREATE=OK >> "%OUT%"
schtasks /delete /tn "PA_PROBE_TEST" /f >nul 2>&1
if not %errorlevel%==0 goto :sch_deldenied
echo SCHTASKS_DELETE=OK >> "%OUT%"
goto :sch_done
:sch_deldenied
echo SCHTASKS_DELETE=FAILED >> "%OUT%"
goto :sch_done
:sch_denied
echo SCHTASKS_CREATE=DENIED >> "%OUT%"
:sch_done
echo. >> "%OUT%"

echo [12] STARTUP FOLDER WRITE >> "%OUT%"
set SUF=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
echo test > "%SUF%\_pa_probe.txt" 2>nul
if not exist "%SUF%\_pa_probe.txt" goto :su_denied
echo STARTUP_WRITE=OK >> "%OUT%"
del "%SUF%\_pa_probe.txt" >nul 2>&1
goto :su_done
:su_denied
echo STARTUP_WRITE=DENIED >> "%OUT%"
:su_done
echo. >> "%OUT%"

echo [13] HOLIDAY API REACHABILITY >> "%OUT%"
powershell -NoProfile -Command "try{ $x=[System.Net.Dns]::GetHostAddresses('apis.data.go.kr'); 'DNS=OK  '+($x[0].IPAddressToString) }catch{ 'DNS=FAILED  (name resolution blocked)' }" >> "%OUT%" 2>&1
powershell -NoProfile -Command "try{ $c=New-Object Net.Sockets.TcpClient; $a=$c.BeginConnect('apis.data.go.kr',443,$null,$null); if($a.AsyncWaitHandle.WaitOne(8000)){$c.EndConnect($a);'TCP443=OPEN'}else{'TCP443=TIMEOUT'}; $c.Close() }catch{ 'TCP443=REFUSED' }" >> "%OUT%" 2>&1
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; try{ $r=Invoke-WebRequest -Uri 'https://apis.data.go.kr/B090041_OPEN_API/service/SpcdeInfoService/getRestDeInfo?serviceKey=TEST&solYear=2026&solMonth=01' -UseBasicParsing -TimeoutSec 15; 'HTTPS=REACHABLE  HTTP='+$r.StatusCode } catch [System.Net.WebException] { $we=$_.Exception; if($we.Response){ 'HTTPS=REACHABLE  HTTP='+[int]$we.Response.StatusCode+'  (server responded, network path OK)' } else { 'HTTPS=BLOCKED  STATUS='+$we.Status } } catch { 'HTTPS=ERROR  '+$_.Exception.GetType().Name }" >> "%OUT%" 2>&1
powershell -NoProfile -Command "$p=[System.Net.WebRequest]::GetSystemWebProxy().GetProxy('https://apis.data.go.kr'); 'PROXY='+$p.AbsoluteUri" >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo [14] SECURITY PRODUCTS >> "%OUT%"
powershell -NoProfile -Command "try{Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -EA Stop | Select-Object -Unique -ExpandProperty displayName}catch{'AV_QUERY=FAILED'}" >> "%OUT%" 2>&1
echo. >> "%OUT%"

echo ============================================ >> "%OUT%"
echo  DONE >> "%OUT%"
echo ============================================ >> "%OUT%"

echo.
echo  Check finished.
echo  Result: %OUT%
echo.
notepad "%OUT%"
