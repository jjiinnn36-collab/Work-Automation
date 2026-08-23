# 엑셀 양식(xlsx) -> 프로그램이 읽는 payment-master.tsv 변환
#
# 엑셀에서 "다른 이름으로 저장 > 텍스트(탭으로 분리)" 를 쓰면 한글이 CP949 로 저장되어
# 프로그램이 읽을 때 깨진다. 그래서 Excel COM 으로 직접 읽어 UTF-8(BOM) 로 쓴다.

param(
    [string]$Source = "",
    [string]$Target = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ($Source -eq "") { $Source = Join-Path $root "data\payment-master-template.xlsx" }
if ($Target -eq "") { $Target = Join-Path $root "data\payment-master.tsv" }

if (-not (Test-Path $Source)) {
    Write-Host ""
    Write-Host "  [오류] 원본 엑셀 파일이 없습니다:"
    Write-Host "         $Source"
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  원본: $Source"
Write-Host "  대상: $Target"
Write-Host ""

$headers = @("id","기관","비용명","진행흐름","월","일","알림영업일","금액규칙","고정금액","비고")

$xl = $null
$wb = $null
try {
    $xl = New-Object -ComObject Excel.Application
    $xl.Visible = $false
    $xl.DisplayAlerts = $false

    # 읽기 전용으로 연다. 사용자가 같은 파일을 열어두고 있어도 방해하지 않는다.
    $wb = $xl.Workbooks.Open($Source, 0, $true)

    $ws = $null
    foreach ($s in $wb.Worksheets) { if ($s.Name -eq "납부항목") { $ws = $s; break } }
    if ($null -eq $ws) { $ws = $wb.Worksheets.Item(1) }
    Write-Host "  시트: $($ws.Name)"

    # 헤더 위치를 이름으로 찾는다. 열 순서가 바뀌어도 견딘다.
    $colOf = @{}
    for ($c = 1; $c -le 40; $c++) {
        $v = $ws.Cells.Item(1, $c).Text
        if ($v) { $colOf[$v.Trim()] = $c }
    }

    $missing = @()
    foreach ($h in $headers) { if (-not $colOf.ContainsKey($h)) { $missing += $h } }
    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "  [오류] 다음 열을 찾지 못했습니다: $($missing -join ', ')"
        Write-Host "         1행이 열 이름이어야 합니다."
        Write-Host ""
        exit 2
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(($headers -join "`t"))

    $row = 2
    $count = 0
    $blank = 0
    while ($blank -lt 20) {
        $id = $ws.Cells.Item($row, $colOf["id"]).Text
        if ([string]::IsNullOrWhiteSpace($id)) { $blank++; $row++; continue }
        $blank = 0

        $cells = @()
        foreach ($h in $headers) {
            $t = $ws.Cells.Item($row, $colOf[$h]).Text
            if ($null -eq $t) { $t = "" }
            # 탭·줄바꿈이 들어가면 열이 어긋난다
            $t = $t -replace "`t", " " -replace "`r", "" -replace "`n", " "
            $cells += $t.Trim()
        }
        $lines.Add(($cells -join "`t"))
        $count++
        $row++
    }

    if ($count -eq 0) {
        Write-Host ""
        Write-Host "  [오류] 읽어들인 행이 없습니다. id 열이 비어 있지 않은지 확인하세요."
        Write-Host ""
        exit 3
    }

    # 기존 파일은 백업해 둔다. 잘못 변환했을 때 되돌릴 수 있어야 한다.
    if (Test-Path $Target) {
        $bak = "$Target.bak"
        Copy-Item $Target $bak -Force
        Write-Host "  기존 파일 백업: $bak"
    }

    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Target, ($lines -join "`r`n") + "`r`n", $utf8Bom)

    Write-Host ""
    Write-Host "  변환 완료: $count 건"
    Write-Host ""
    Write-Host "  run-tests.bat 을 실행하면 연간 일정표로 검산할 수 있습니다."
    Write-Host ""
    exit 0
}
catch {
    Write-Host ""
    Write-Host "  [오류] $($_.Exception.Message)"
    Write-Host ""
    exit 9
}
finally {
    if ($null -ne $wb) { $wb.Close($false) }
    if ($null -ne $xl) {
        $xl.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
