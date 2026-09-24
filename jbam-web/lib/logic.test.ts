// 화면 규칙 회귀 시험. 실행: node --test lib/logic.test.ts  (Node 22.6+ 타입 제거 기능 사용)
import { test } from "node:test"
import assert from "node:assert/strict"

import {
  won, md, parseWon, stepStates, stepTooltip, yearStatus, sortRemaining, matchYearFilter,
  shiftMonth, presetRange, parseMonths, groupSum, previewKind, parseCsv, csvCell, toCsv, safeUrl,
  viewFromHash, eventSummary, occurrenceCsv, FLOW_CARDS, flowLabel, dayLabel, itemSummary, settingsConfirm, discardConfirm, amountOverwrite, groupOverwrites, overwriteConfirm,
  yearRange, loanKey, loanDefaultKeys, loanSaveSheets, loanBasis, loanOverrides, loanNameParams, visibleSteps, progressText,
} from "./logic.ts"
import type { Occurrence } from "./types.ts"

function occ(p: Partial<Occurrence>): Occurrence {
  return {
    year: 2026, id: "x", org: "기관", name: "비용", flow: "납부만", stages: ["고지서수령", "전표발행", "납부"],
    stage: 0, stageName: "고지서수령", nextStage: "전표발행", nextAction: "전표 발행", paid: true,
    siteName: "", siteUrl: "", group: "", done: false, due: "2026-09-18", dueDow: "금", payDue: "2026-09-18",
    payDow: "금", shifted: false, alertDate: "2026-09-15", daysLeft: 3, statusText: "D-3영업일", severity: "soon",
    amount: null, amountText: "미확인", amountEntered: false, amountRule: "변동", confirmedToday: false,
    changedAt: null, attachments: 0, memo: "", ...p,
  }
}

test("금액 표시와 입력 해석", () => {
  assert.equal(won(1234560), "1,234,560")
  assert.equal(won(null), "")
  assert.equal(md("2026-09-18"), "09-18")
  assert.equal(parseWon("1,234,500원"), 1234500)
  assert.equal(parseWon(" 7 000 "), 7000)
  assert.equal(parseWon("-5"), null)
  assert.equal(parseWon("12.5"), null)
  assert.equal(parseWon(""), null)
})

test("지점 도형: 끝낸 곳 회색, 다음 한 곳만 파랑, 끝나면 전부 회색 (AC-W45·W58·W70)", () => {
  assert.deepEqual(stepStates(0, 3, false), ["past", "current", "future"])
  assert.deepEqual(stepStates(1, 4, false), ["past", "past", "current", "future"])
  assert.deepEqual(stepStates(2, 3, true), ["past", "past", "past"])
  const s = stepStates(1, 4, false)
  assert.equal(s.filter((x) => x === "current").length, 1)
  assert.equal(stepTooltip(["신고서 작성", "신고", "전표발행", "납부"], 1, "past"), "신고 · 완료")
  assert.equal(stepTooltip(["신고서 작성", "신고", "전표발행", "납부"], 2, "current"), "전표발행 · 진행중")
  assert.equal(stepTooltip(["고지서수령", "전표발행", "납부"], 2, "future"), "납부 · 진행예정")
})

test("연간 상태 분류와 남은 건 정렬 — 놓친 기한이 맨 위 (AC-W55)", () => {
  const today = "2026-09-16"
  assert.equal(yearStatus(occ({ done: true, severity: "done" }), today), "done")
  assert.equal(yearStatus(occ({ severity: "overdue" }), today), "overdue")
  // 진행중·진행예정은 알림일이 아니라 밟은 단계로 가른다 (Q3)
  assert.equal(yearStatus(occ({ stage: 1, alertDate: "2026-10-01", severity: "normal" }), today), "progress")
  assert.equal(yearStatus(occ({ stage: 0, alertDate: "2026-09-15" }), today), "upcoming")
  // 추적 시작일 이전 건은 할 일이 아니다 (Q1)
  assert.equal(yearStatus(occ({ beforeStart: true, severity: "before" }), today), "before")
  assert.equal(yearStatus(occ({ beforeStart: true, done: true, severity: "done" }), today), "done")

  const sorted = sortRemaining([
    occ({ id: "late-due", payDue: "2026-12-01", severity: "normal" }),
    occ({ id: "missed", payDue: "2026-09-10", severity: "overdue" }),
    occ({ id: "soon", payDue: "2026-09-18" }),
  ])
  assert.deepEqual(sorted.map((o) => o.id), ["missed", "soon", "late-due"])
})

test("연간 필터: 월·기관·상태·검색 (AC-W5)", () => {
  const today = "2026-09-16"
  const o = occ({ org: "국세청", name: "부가세 3분기", due: "2026-10-25", alertDate: "2026-10-17", severity: "normal", stage: 0 })
  const base = { month: "", org: "", status: "" as const, q: "" }
  assert.equal(matchYearFilter(o, base, today), true)
  assert.equal(matchYearFilter(o, { ...base, month: "10" }, today), true)
  assert.equal(matchYearFilter(o, { ...base, month: "9" }, today), false)
  assert.equal(matchYearFilter(o, { ...base, org: "금융감독원" }, today), false)
  assert.equal(matchYearFilter(o, { ...base, status: "upcoming" }, today), true)
  assert.equal(matchYearFilter(o, { ...base, q: "부가" }, today), true)
  assert.equal(matchYearFilter(o, { ...base, q: "국세" }, today), true)
  assert.equal(matchYearFilter(o, { ...base, q: "회비" }, today), false)
})

test("달 옮기기와 이력 빠른 선택 (AC-W64·W65)", () => {
  assert.equal(shiftMonth("2026-01", -1), "2025-12")
  assert.equal(shiftMonth("2026-12", 1), "2027-01")
  assert.equal(shiftMonth("2026-09", 15), "2027-12")
  assert.deepEqual(presetRange("this", "2026-09-16"), { from: "2026-01-01", to: "2026-12-31" })
  assert.deepEqual(presetRange("last", "2026-09-16"), { from: "2025-01-01", to: "2025-12-31" })
  assert.deepEqual(presetRange("12m", "2026-09-16"), { from: "2025-09-17", to: "2026-09-16" })
  assert.deepEqual(presetRange("12m", "2026-02-28"), { from: "2025-03-01", to: "2026-02-28" })
})

test("분할납부 월 입력과 회차 합계 — 나눠 채우지 않는다 (AC-W32·W35·W37)", () => {
  assert.deepEqual(parseMonths("7, 5,6").months, [5, 6, 7])
  assert.deepEqual(parseMonths("3").months, [3])
  assert.ok(parseMonths("5,5").error)
  assert.ok(parseMonths("5,13").error)
  assert.ok(parseMonths(" , ").error)

  const rows = [
    { id: "a", amount: 1000 },
    { id: "b", amount: null },
    { id: "c", amount: 500 },
  ]
  assert.equal(groupSum(rows, {}), 1500)
  assert.equal(groupSum(rows, { b: "2,000" }), 3500)
  assert.equal(groupSum(rows, { a: "900", c: "" }), 1400)
  assert.equal(groupSum(rows, { b: "틀림" }), 1500)
})

test("미리보기 형식 (AC-W105·W106)", () => {
  assert.equal(previewKind("통보문.PDF"), "pdf")
  assert.equal(previewKind("영수증.jpeg"), "image")
  assert.equal(previewKind("산출내역.xlsx"), "none")
  assert.equal(previewKind("신고서.hwp"), "none")
  // 차입 원본 스케줄은 CSV 라 창 안에서 표로 본다 (사용자 결정 2026-09-23).
  assert.equal(previewKind("충북 진천 물류단지.csv"), "csv")
})

test("차입 원본 CSV 읽기: BOM·감싸기·수식 막기 되돌리기", () => {
  const 글 = "﻿기준일자,현금흐름구분,액면이자금액\r\n2026-06-23,이자지급,\"12,340,000\"\r\n'=SUM(A1),메모,0\r\n"
  const rows = parseCsv(글)
  assert.deepEqual(rows[0], ["기준일자", "현금흐름구분", "액면이자금액"])
  assert.deepEqual(rows[1], ["2026-06-23", "이자지급", "12,340,000"])
  // 쓸 때 붙인 앞따옴표는 떼어 원래 값으로 보여 준다.
  assert.deepEqual(rows[2], ["=SUM(A1)", "메모", "0"])
  assert.equal(rows.length, 3)
})

test("CSV: BOM, 수식 막기, 감싸기 (AC-W16)", () => {
  assert.equal(csvCell("=SUM(A1)"), "'=SUM(A1)")
  assert.equal(csvCell("-5"), "'-5")
  assert.equal(csvCell('a,"b"'), '"a,""b"""')
  assert.equal(csvCell(null), "")
  const text = toCsv(["이름", "금액"], [["회비", 1234560]])
  assert.ok(text.startsWith("﻿"))
  assert.equal(text.slice(1), "이름,금액\r\n회비,1234560")
  const c = occurrenceCsv([occ({ amount: null, amountText: "미확인" })])
  assert.equal(c.rows[0][8], "미확인")
})

test("링크·해시 경로·기록 요약", () => {
  assert.equal(safeUrl("https://hometax.go.kr"), "https://hometax.go.kr/")
  assert.equal(safeUrl("javascript:alert(1)"), null)
  assert.equal(safeUrl("not a url"), null)
  assert.equal(viewFromHash("#month"), "month")
  assert.equal(viewFromHash("#/settings"), "settings")
  assert.equal(viewFromHash("#nope"), "alerts")
  assert.equal(viewFromHash(""), "alerts")
  assert.equal(eventSummary({ action: "진행", from: "고지서수령", to: "전표발행", detail: "전표발행" }), "고지서수령 → 전표발행")
  assert.equal(eventSummary({ action: "금액", from: null, to: null, detail: "1,000원" }), "1,000원")
})

test("항목 창: 선택 카드 이름과 한 줄 요약 (ADR-0020)", () => {
  assert.deepEqual(FLOW_CARDS.map((c) => c.flow), ["신고납부", "납부만", "제출만", "사용자설정"])
  assert.equal(flowLabel("신고납부"), "신고 후 납부")
  assert.equal(flowLabel("사용자설정"), "직접 정하기")
  assert.equal(flowLabel("모름"), "모름")
  assert.equal(dayLabel("말일"), "말일")
  assert.equal(dayLabel("20"), "20일")
  const base = { name: "회비", month: "5", day: "20", flow: "납부만", paid: true, rule: "변동", fixed: "", lead: "3" }
  assert.equal(itemSummary(base), "회비 · 매년 5월 20일 · 납부만 · 변동 · 3영업일 전 알림")
  assert.equal(itemSummary({ ...base, rule: "고정", fixed: "1234560" }), "회비 · 매년 5월 20일 · 납부만 · 고정 1,234,560원 · 3영업일 전 알림")
  assert.equal(itemSummary({ ...base, rule: "고정", fixed: "" }), "회비 · 매년 5월 20일 · 납부만 · 고정 (금액 필요) · 3영업일 전 알림")
  assert.equal(itemSummary({ ...base, flow: "제출만", paid: false, day: "말일" }), "회비 · 매년 5월 말일 · 제출만 · 3영업일 전 알림")
  assert.equal(itemSummary({ ...base, name: " ", month: "5,6,7" }), "이름 없음 · 매년 5,6,7월 20일 · 납부만 · 변동 · 3영업일 전 알림")
})
test("설정의 되돌리기 어려운 동작은 확인 창을 거친다 (ADR-0021)", () => {
  const del = settingsConfirm("apikey-delete")
  assert.equal(del.destructive, true)
  assert.equal(del.action, "지우기")
  assert.match(del.description, /공휴일 자료는 그대로/)
  const rep = settingsConfirm("apikey-replace")
  assert.equal(rep.destructive, false)
  assert.equal(rep.action, "바꾸기")
  const clr = settingsConfirm("start-clear", "2026-09-01")
  assert.equal(clr.destructive, true)
  assert.match(clr.description, /^2026-09-01 이전/)
  assert.match(settingsConfirm("start-clear", null).description, /^예전/)
})

test("입력 중 닫기·금액 덮어쓰기 확인 (ADR-0022)", () => {
  const d = discardConfirm("내용")
  assert.equal(d.destructive, true)
  assert.equal(d.action, "닫기")
  assert.match(d.description, /입력한 내용/)

  assert.equal(amountOverwrite(null, false, 1000), null)
  assert.equal(amountOverwrite(1000, false, 2000), null)
  assert.equal(amountOverwrite(1000, true, 1000), null)
  assert.deepEqual(amountOverwrite(1000, true, 2000), { from: 1000, to: 2000 })

  const rows = [
    { id: "a", label: "5월", amount: 100, entered: true },
    { id: "b", label: "6월", amount: 200, entered: true },
    { id: "c", label: "7월", amount: null, entered: false },
    { id: "d", label: "8월", amount: 300, entered: false },
  ]
  const ch = groupOverwrites(rows, { a: "150", b: "200", c: "999", d: "1", x: "5" })
  assert.deepEqual(ch, [{ label: "5월", from: 100, to: 150 }])
  assert.deepEqual(groupOverwrites(rows, { a: "", b: "abc" }), [])

  const one = overwriteConfirm(ch)
  assert.equal(one.title, "이미 넣은 금액을 바꿀까요?")
  assert.equal(one.description, "5월 100원 → 150원")
  const many = overwriteConfirm(Array.from({ length: 7 }, (_, i) => ({ label: `${i + 1}월`, from: 1, to: 2 })))
  assert.equal(many.title, "이미 넣은 금액 7건을 바꿀까요?")
  assert.match(many.description, / 외 2건$/)
})

test("차입 스케줄 가져오기: 유효연도 표시·기본 선택·근거 (ADR-0023)", () => {
  assert.equal(yearRange(null, null), "")
  assert.equal(yearRange(2026, 2026), "2026년만")
  assert.equal(yearRange(2026, 2028), "2026~2028년")
  assert.equal(yearRange(2027, null), "2027년부터")
  assert.equal(yearRange(undefined, 2028), "2028년까지")

  const a = [
    { sheet: "차입처A", ok: true, newCount: 4 },
    { sheet: "이미 넣음", ok: true, newCount: 0 },
    { sheet: "오류", ok: false },
  ]
  const b = [{ sheet: "차입처A", ok: true, newCount: 1 }]
  const keys = loanDefaultKeys([{ file: "a.xlsx", loans: a }, { file: "b.csv", loans: b }])
  // 이미 다 등록된 시트도 골라 둔다 — 같은 파일을 다시 올려 원본 스케줄만 증빙에 채울 수 있어야 한다
  // (사용자 요청 2026-09-23). 읽지 못한 시트(ok:false)만 뺀다.
  assert.deepEqual(keys, [loanKey("a.xlsx", "차입처A"), loanKey("a.xlsx", "이미 넣음"), loanKey("b.csv", "차입처A")])
  assert.notEqual(loanKey("a.xlsx", "차입처A"), loanKey("b.csv", "차입처A"))
  assert.deepEqual(loanSaveSheets("a.xlsx", a, keys), ["차입처A", "이미 넣음"])
  assert.deepEqual(loanSaveSheets("a.xlsx", a, [loanKey("a.xlsx", "이미 넣음")]), ["이미 넣음"])
  assert.deepEqual(loanSaveSheets("b.csv", b, [loanKey("a.xlsx", "차입처A")]), [])

  assert.equal(loanBasis([{ date: "2026-09-30", amount: 4660274 }, { date: "2026-12-04", amount: 560959 }]), "9/30 4,660,274 + 12/4 560,959")

  const pays = [
    { scheduled: "2026-12-04", amount: 15750000, state: "new" },
    { scheduled: "2027-03-04", amount: 15750000, state: "new" },
    { scheduled: "2027-06-04", amount: 15750000, state: "exists" },
    { scheduled: "2027-09-03", amount: 15750000, state: "new" },
  ]
  assert.deepEqual(
    loanOverrides("차입처A", pays, { "2026-12-04": "15,750,000", "2027-03-04": "15,750,001원", "2027-06-04": "1", "2027-09-03": "abc" }),
    { ov: ["2027-03-04|15750001|차입처A"], invalid: ["2027-09-03"] }
  )
  assert.deepEqual(loanOverrides("x", pays, {}), { ov: [], invalid: [] })
})

test("차입명·약칭: 시트|차입명|약칭, 차입명이 비면 등록 못 함", () => {
  assert.deepEqual(
    loanNameParams(["A", "B", "C"], { A: { name: " 가짜 차입 ", short: "가짜PF" }, B: { name: "가짜B", short: " " }, C: { name: "  ", short: "C" } }),
    { nm: ["A|가짜 차입|가짜PF", "B|가짜B|"], missing: ["C"] }
  )
  assert.deepEqual(loanNameParams(["X"], {}), { nm: [], missing: ["X"] })
})

test("신고 후 납부는 시작점을 그리지 않는다 (hideStart)", () => {
  const 신고 = { stages: ["신고 전", "신고", "전표발행", "납부"], hideStart: true }
  assert.deepEqual(visibleSteps({ ...신고, stage: 0 }), { names: ["신고", "전표발행", "납부"], stage: -1 })
  assert.equal(progressText({ ...신고, stage: 0, stageName: "신고 전" }), "진행 전")
  assert.equal(progressText({ ...신고, stage: 1, stageName: "신고" }), "신고 완료")
  const 납부 = { stages: ["고지서수령", "전표발행", "납부"], stage: 0, stageName: "고지서수령" }
  assert.deepEqual(visibleSteps(납부).names, ["고지서수령", "전표발행", "납부"])
  assert.equal(progressText(납부), "고지서수령 완료")
})
