# 납부서 PDF 에서 납부기한과 금액을 읽어 data/amounts.tsv 에 반영한다.
#
# 자동 판독은 텍스트 레이어가 온전한 문서에만 적용한다.
# 스캔 문서는 OCR 오독(콤마 소실, 자릿수 오인) 위험이 있어 자동 반영하지 않는다.
# 금액은 세목 합계와 '계' 값을 대조해 일치할 때만 채택한다.

param(
    [Parameter(Mandatory = $true)][string]$Pdf,
    [switch]$Yes,           # 확인 없이 반영 (테스트용)
    [switch]$DryRun         # 읽기만 하고 쓰지 않음
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$masterPath  = Join-Path $root "data\payment-master.tsv"
$amountsPath = Join-Path $root "data\amounts.tsv"

function Fail([string]$msg, [int]$code) {
    Write-Host ""
    Write-Host "  [중단] $msg"
    Write-Host ""
    exit $code
}

if (-not (Test-Path $Pdf)) { Fail "파일이 없습니다: $Pdf" 1 }
$Pdf = (Resolve-Path $Pdf).Path

Write-Host ""
Write-Host "  파일: $(Split-Path -Leaf $Pdf)"

# ── 1. 텍스트 추출 ────────────────────────────────────────
$word = $null
$lines = @()
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($Pdf, $false, $true, $false, "", "", $false,
                                "", "", 0, $false, $false, $false, $true, $false)
    $raw = $doc.Content.Text
    $doc.Close($false)
    $lines = ($raw -replace "[\x00-\x08\x0b\x0c\x0e-\x1f]", "`n") -split "`n" |
             ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}
catch {
    Fail "PDF 를 읽지 못했습니다: $($_.Exception.Message)" 2
}
finally {
    if ($null -ne $word) {
        $word.Quit()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    }
}

Write-Host "  추출 라인: $($lines.Count)"

# ── 2. 문서 종류 판별 ─────────────────────────────────────
$all = $lines -join " "
if ($all -notmatch "국세징수법" -or $all -notmatch "부가가치세") {
    Write-Host ""
    Write-Host "  이 도구는 국세청 부가가치세 납부서만 자동 판독합니다."
    Write-Host ""
    Write-Host "  감독분담금 통보문과 금융투자협회비 안내문은 스캔 문서라"
    Write-Host "  숫자가 잘못 읽힙니다 (콤마 소실, 날짜 자릿수 붙음)."
    Write-Host "  이런 문서는 data\amounts.tsv 에 직접 입력하세요."
    Write-Host ""
    exit 3
}

# ── 3. 납부기한 ──────────────────────────────────────────
$dueMatches = [regex]::Matches($all, "납부기한\s*(\d{4})년\s*(\d{1,2})월\s*(\d{1,2})일")
if ($dueMatches.Count -eq 0) { Fail "납부기한을 찾지 못했습니다." 4 }

$dues = @{}
foreach ($m in $dueMatches) {
    $k = "{0}-{1:00}-{2:00}" -f [int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value
    $dues[$k] = $true
}
if ($dues.Keys.Count -ne 1) {
    Fail "납부기한이 여러 개로 읽혔습니다: $($dues.Keys -join ', ')" 4
}
$due = [datetime]::ParseExact(($dues.Keys)[0], "yyyy-MM-dd", $null)

# ── 4. 세목별 금액과 '계' ────────────────────────────────
function Get-AmountAfter([string[]]$src, [string]$label) {
    $vals = @()
    for ($i = 0; $i -lt $src.Count - 1; $i++) {
        if ($src[$i] -eq $label) {
            $n = $src[$i + 1] -replace "[,\s]", ""
            if ($n -match "^\d+$") { $vals += [decimal]$n }
        }
    }
    return $vals
}

$vat    = Get-AmountAfter $lines "부가가치세"
$edu    = Get-AmountAfter $lines "교육/방위세"
$farm   = Get-AmountAfter $lines "농어촌특별세"
$surch  = Get-AmountAfter $lines "가산금"
$totals = Get-AmountAfter $lines "계"

if ($totals.Count -eq 0) { Fail "합계('계')를 찾지 못했습니다." 5 }

# 같은 값이 여러 면에 반복된다. 서로 다르면 판독을 신뢰할 수 없다.
$distinct = $totals | Select-Object -Unique
if ($distinct.Count -ne 1) {
    Fail "면마다 합계가 다르게 읽혔습니다: $($distinct -join ', '). 직접 입력하세요." 5
}
$total = $distinct[0]

# ── 5. 검산: 세목 합계 == 계 ─────────────────────────────
function FirstOrZero($a) { if ($a.Count -gt 0) { return $a[0] } return [decimal]0 }
$sum = (FirstOrZero $vat) + (FirstOrZero $edu) + (FirstOrZero $farm) + (FirstOrZero $surch)

Write-Host ""
Write-Host "  ── 판독 결과 ─────────────────────────────"
Write-Host ("  납부기한   : {0:yyyy-MM-dd} ({1})" -f $due, $due.ToString("ddd", [Globalization.CultureInfo]::GetCultureInfo("ko-KR")))
Write-Host ("  부가가치세 : {0,18:N0}" -f (FirstOrZero $vat))
Write-Host ("  교육/방위세: {0,18:N0}" -f (FirstOrZero $edu))
Write-Host ("  농어촌특별세:{0,18:N0}" -f (FirstOrZero $farm))
Write-Host ("  가산금     : {0,18:N0}" -f (FirstOrZero $surch))
Write-Host ("  " + ("-" * 40))
Write-Host ("  세목 합계  : {0,18:N0}" -f $sum)
Write-Host ("  문서상 계  : {0,18:N0}" -f $total)

if ($sum -ne $total) {
    Write-Host ""
    Write-Host "  [중단] 세목 합계와 '계' 가 일치하지 않습니다."
    Write-Host "         판독이 잘못되었을 수 있으므로 반영하지 않습니다."
    Write-Host "         data\amounts.tsv 에 직접 입력하세요."
    Write-Host ""
    exit 6
}
Write-Host "  검산 통과 (세목 합계 = 계)"

# ── 6. 마스터에서 대상 항목 찾기 ─────────────────────────
if (-not (Test-Path $masterPath)) { Fail "납부 마스터가 없습니다: $masterPath" 7 }

$master = @()
$hdr = $null
foreach ($ln in [System.IO.File]::ReadAllLines($masterPath, [System.Text.Encoding]::UTF8)) {
    if ($ln.Trim() -eq "" -or $ln.StartsWith("#")) { continue }
    $c = $ln -split "`t"
    if ($null -eq $hdr) { $hdr = $c | ForEach-Object { $_.Trim().TrimStart([char]0xFEFF) }; continue }
    $o = @{}
    for ($i = 0; $i -lt $hdr.Count; $i++) { $o[$hdr[$i]] = if ($i -lt $c.Count) { $c[$i].Trim() } else { "" } }
    $master += ,$o
}

# 부가세 항목 중 원기한 월이 납부기한 월과 같은 건
# @() 로 감싸지 않으면 결과가 1건일 때 해시테이블 자체가 반환되어
# .Count 가 행 수가 아니라 열 개수를 세게 된다.
$cands = @($master | Where-Object {
    ($_["비용명"] -match "부가") -and ([int]$_["월"] -eq $due.Month)
})

if ($cands.Count -eq 0) {
    Fail "납부기한 $($due.Month)월에 해당하는 부가세 항목을 마스터에서 찾지 못했습니다." 8
}
if ($cands.Count -gt 1) {
    Fail "후보가 여러 개입니다: $(($cands | ForEach-Object { $_['id'] }) -join ', '). 직접 입력하세요." 8
}
$item = $cands[0]

Write-Host ""
Write-Host "  ── 반영 대상 ─────────────────────────────"
Write-Host ("  id     : {0}" -f $item["id"])
Write-Host ("  항목   : {0} ({1})" -f $item["비용명"], $item["기관"])
Write-Host ("  정의   : 매년 {0}월 {1}일" -f $item["월"], $item["일"])
Write-Host ("  연도   : {0}" -f $due.Year)
Write-Host ("  금액   : {0:N0}원" -f $total)

# ── 7. 기존 값 확인 ──────────────────────────────────────
$rows = @()
$aHdr = @("연도","id","금액","출처","확인일","비고")
$comments = @()
$existing = $null

if (Test-Path $amountsPath) {
    $seenHdr = $false
    foreach ($ln in [System.IO.File]::ReadAllLines($amountsPath, [System.Text.Encoding]::UTF8)) {
        $t = $ln.TrimStart([char]0xFEFF)
        if ($t.StartsWith("#")) { $comments += $t; continue }
        if ($t.Trim() -eq "") { continue }
        $c = $t -split "`t"
        if (-not $seenHdr) { $seenHdr = $true; continue }
        if ($c[0].Trim() -eq [string]$due.Year -and $c[1].Trim() -eq $item["id"]) {
            $existing = $c
            continue    # 새 값으로 대체
        }
        $rows += ,$c
    }
}

if ($null -ne $existing) {
    $oldAmt = ($existing[2] -replace "[,\s]", "")
    Write-Host ""
    if ($oldAmt -eq [string]$total) {
        Write-Host "  기존 값과 같습니다. 변경 사항이 없습니다."
    } else {
        Write-Host ("  기존 값 {0:N0}원 -> 새 값 {1:N0}원 으로 바뀝니다." -f [decimal]$oldAmt, $total)
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "  (읽기만 함. 파일을 쓰지 않았습니다)"
    Write-Host ""
    exit 0
}

# ── 8. 확인 ──────────────────────────────────────────────
if (-not $Yes) {
    Write-Host ""
    $ans = Read-Host "  위 내용으로 반영할까요? (y/N)"
    if ($ans -ne "y" -and $ans -ne "Y") {
        Write-Host ""
        Write-Host "  반영하지 않았습니다."
        Write-Host ""
        exit 0
    }
}

# ── 9. 저장 ──────────────────────────────────────────────
$rows += ,@(
    [string]$due.Year,
    $item["id"],
    [string]$total,
    (Split-Path -Leaf $Pdf),
    (Get-Date -Format "yyyy-MM-dd"),
    ("납부기한 " + $due.ToString("yyyy-MM-dd") + " / 자동 판독")
)

$rows = $rows | Sort-Object { $_[0] }, { $_[1] }

if ($comments.Count -eq 0) {
    $comments = @("#연도별 실제 납부금액. payment-master.tsv 는 매년 반복되는 정의이고, 이 파일은 그 해의 실제 금액이다.")
}

if (Test-Path $amountsPath) { Copy-Item $amountsPath "$amountsPath.bak" -Force }

$out = New-Object System.Collections.Generic.List[string]
foreach ($c in $comments) { $out.Add($c) }
$out.Add(($aHdr -join "`t"))
foreach ($r in $rows) { $out.Add(($r -join "`t")) }

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($amountsPath, ($out -join "`r`n") + "`r`n", $utf8Bom)

Write-Host ""
Write-Host "  반영 완료: data\amounts.tsv ($($rows.Count)건)"
Write-Host "  이전 파일은 amounts.tsv.bak 으로 백업했습니다."
Write-Host ""
exit 0
