// 화면 규칙 회귀 시험. 실행: node --test lib/logic.test.ts  (Node 22.6+ 타입 제거 기능 사용)
import { test } from "node:test"
import assert from "node:assert/strict"

import {
  won, md, parseWon, stepStates, stepTooltip, yearStatus, sortRemaining, matchYearFilter,
  shiftMonth, presetRange, parseMonths, groupSum, previewKind, csvCell, toCsv, safeUrl,
  viewFromHash, eventSummary, occurrenceCsv, FLOW_CARDS, flowLabel, dayLabel, itemSummary, settingsConfirm, discardConfirm, amountOverwrite, groupOverwrites, overwriteConfirm,
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
  assert.equal(stepTooltip(["신고서 작성", "신고", "전표발행", "납부"], 1, "past"), "2/4 신고 · 완료")
  assert.equal(stepTooltip(["신고서 작성", "신고", "전표발행", "납부"], 2, "current"), "3/4 전표발행 · 진행중")
  assert.equal(stepTooltip(["고지서수령", "전표발행", "납부"], 2, "future"), "3/3 납부 · 진행예정")
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
