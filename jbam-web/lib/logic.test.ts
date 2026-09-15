// 화면 규칙 회귀 시험. 실행: node --test lib/logic.test.ts  (Node 22.6+ 타입 제거 기능 사용)
import { test } from "node:test"
import assert from "node:assert/strict"

import {
  won, md, parseWon, stepStates, stepTooltip, yearStatus, sortRemaining, matchYearFilter,
  shiftMonth, presetRange, parseMonths, groupSum, previewKind, csvCell, toCsv, safeUrl,
  viewFromHash, eventSummary, occurrenceCsv,
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
