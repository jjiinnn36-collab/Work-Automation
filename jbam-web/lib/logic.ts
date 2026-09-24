// 화면 규칙 중 브라우저·React 와 무관한 것. node --test 로 회귀 시험한다 (lib/logic.test.ts).
// 서버가 판정한 값(statusText·severity·nextAction…)을 다시 계산하지 않는다 — 화면마다 규칙이 갈라지지 않게.

import type { Occurrence } from "./types"

/** 1234567 → "1,234,567". null 이면 빈 문자열. */
export function won(n: number | null | undefined): string {
  if (n === null || n === undefined || Number.isNaN(n)) return ""
  return Math.trunc(n).toLocaleString("en-US")
}

/** "2026-09-18" → "09-18" */
export function md(date: string | null | undefined): string {
  return date ? date.slice(5, 10) : ""
}

/** 사용자가 친 금액 "1,234,500원" → 1234500. 숫자가 아니거나 음수·소수면 null. */
export function parseWon(text: string): number | null {
  const t = text.replace(/[,\s원]/g, "")
  if (!/^\d+$/.test(t)) return null
  const n = Number(t)
  return Number.isSafeInteger(n) ? n : null
}

export type StepState = "past" | "current" | "future"

/**
 * 지점 도형 상태 (AC-W45, W58, W70~W72).
 * stage = 마지막으로 끝낸 지점. 끝낸 지점은 회색, stage+1 은 파랑(지금 할 일), 그 뒤는 빈 원.
 * 다 끝났으면 전부 회색이다.
 */
export function stepStates(stage: number, count: number, done: boolean): StepState[] {
  const out: StepState[] = []
  for (let i = 0; i < count; i++) {
    if (done || i <= stage) out.push("past")
    else if (i === stage + 1) out.push("current")
    else out.push("future")
  }
  return out
}

/**
 * 화면에 그리는 지점. hideStart(신고 후 납부)면 시작점(0번)을 빼고, 끝낸 지점 번호도 하나 당긴다 (-1 = 아무것도 안 함).
 */
export function visibleSteps(o: { stages: string[]; stage: number; hideStart?: boolean }): { names: string[]; stage: number } {
  if (!o.hideStart || o.stages.length < 2) return { names: o.stages, stage: o.stage }
  return { names: o.stages.slice(1), stage: o.stage - 1 }
}

/** 카드의 진행 한 줄: "1/3 신고 완료". 시작점을 숨긴 흐름에서 아직 아무것도 안 했으면 "0/3 진행 전". */
export function progressText(o: { stages: string[]; stage: number; stageName: string; hideStart?: boolean; done?: boolean }): string {
  const { stage } = visibleSteps(o)
  // 앞의 'n/N' 숫자는 날짜처럼 보여 헷갈려서 뺀다 (사용자 요청). 단계 이름만 남긴다.
  if (stage < 0) return "진행 전"
  return `${o.stageName} 완료`
}

/** 말풍선 문구: "신고 · 진행중" */
export function stepTooltip(names: string[], i: number, state: StepState): string {
  const word = state === "past" ? "완료" : state === "current" ? "진행중" : "진행예정"
  return `${names[i]} · ${word}`
}

export type YearStatus = "done" | "overdue" | "progress" | "upcoming" | "before"

/**
 * 이번 달·연간 공통 상태 분류 (사용자 결정 Q3, ADR-0014 — 서버 집계와 같은 규칙).
 * 기한 지남 = 기한이 지난 미완료, 진행중 = 한 단계라도 밟은 미완료, 진행예정 = 아직 아무 단계도 안 밟은 미완료,
 * 시작 전 = 추적 시작일 이전 건(할 일 아님).
 */
export function yearStatus(o: Occurrence, _today?: string): YearStatus {
  if (o.done) return "done"
  if (o.beforeStart) return "before"
  if (o.severity === "overdue") return "overdue"
  return o.stage >= 1 ? "progress" : "upcoming"
}

/** 남은 건 정렬: 기한 지난 미완료가 맨 위 (AC-W55), 그다음 기한 오름차순. */
export function sortRemaining(list: Occurrence[]): Occurrence[] {
  return [...list].sort((a, b) => {
    const ao = a.severity === "overdue" ? 0 : 1
    const bo = b.severity === "overdue" ? 0 : 1
    if (ao !== bo) return ao - bo
    return a.payDue < b.payDue ? -1 : a.payDue > b.payDue ? 1 : a.id.localeCompare(b.id)
  })
}

export interface YearFilter {
  month: string // "" 또는 "1".."12"
  org: string
  status: "" | YearStatus
  q: string
}

export function matchYearFilter(o: Occurrence, f: YearFilter, today: string): boolean {
  if (f.month && Number(o.due.slice(5, 7)) !== Number(f.month)) return false
  if (f.org && o.org !== f.org) return false
  if (f.status && yearStatus(o, today) !== f.status) return false
  const q = f.q.trim().toLowerCase()
  if (q && !o.name.toLowerCase().includes(q) && !o.org.toLowerCase().includes(q)) return false
  return true
}

/** "2026-09" 에서 n 달 옮긴 달 */
export function shiftMonth(ym: string, n: number): string {
  const [y, m] = ym.split("-").map(Number)
  const total = y * 12 + (m - 1) + n
  const ny = Math.floor(total / 12)
  const nm = (total % 12) + 1
  return `${ny}-${String(nm).padStart(2, "0")}`
}

function iso(y: number, m: number, d: number): string {
  const dt = new Date(Date.UTC(y, m - 1, d))
  return dt.toISOString().slice(0, 10)
}

/** 이력 빠른 선택 (AC-W65). today = "YYYY-MM-DD" */
export function presetRange(kind: "this" | "last" | "12m", today: string): { from: string; to: string } {
  const [y, m, d] = today.split("-").map(Number)
  if (kind === "this") return { from: `${y}-01-01`, to: `${y}-12-31` }
  if (kind === "last") return { from: `${y - 1}-01-01`, to: `${y - 1}-12-31` }
  return { from: iso(y - 1, m, d + 1), to: today }
}

/** "5, 6,7" → [5,6,7]. 틀린 값이 있으면 error. 쉼표가 없으면 한 개. (AC-W32) */
export function parseMonths(text: string): { months: number[]; error?: string } {
  const parts = text.split(",").map((s) => s.trim()).filter((s) => s.length > 0)
  if (parts.length === 0) return { months: [], error: "월을 넣어 주세요." }
  const months: number[] = []
  for (const p of parts) {
    if (!/^\d{1,2}$/.test(p) || Number(p) < 1 || Number(p) > 12) return { months: [], error: `월은 1~12 입니다: ${p}` }
    const n = Number(p)
    if (months.includes(n)) return { months: [], error: `같은 월이 두 번 들어 있습니다: ${n}월` }
    months.push(n)
  }
  return { months: months.sort((a, b) => a - b) }
}

/** 회차 금액 표의 합계. 입력칸 값이 있으면 그것, 없으면 저장된 금액. 틀린 입력은 합에서 뺀다. (AC-W35) */
export function groupSum(rows: { id: string; amount: number | null }[], typed: Record<string, string>): number {
  let sum = 0
  for (const r of rows) {
    const t = typed[r.id]
    if (t !== undefined && t.trim() !== "") {
      const v = parseWon(t)
      if (v !== null) sum += v
    } else if (r.amount !== null) {
      sum += r.amount
    }
  }
  return sum
}

/** 브라우저 보기창에서 바로 보이는 형식인가 (AC-W105, W106) */
export function previewKind(fileName: string): "pdf" | "image" | "csv" | "none" {
  const ext = fileName.toLowerCase().split(".").pop() ?? ""
  if (ext === "pdf") return "pdf"
  if (["png", "jpg", "jpeg", "gif"].includes(ext)) return "image"
  if (ext === "csv") return "csv"
  return "none"
}

/**
 * 보관해 둔 원본 스케줄 CSV 를 표로 보여 주려고 읽는다.
 * BOM 을 떼고, 감싼 따옴표를 풀고, 수식 막기로 붙인 앞따옴표도 되돌린다.
 */
export function parseCsv(text: string): string[][] {
  const s = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text
  const rows: string[][] = []
  let row: string[] = []
  let cell = ""
  let quoted = false
  const 넣기 = () => {
    // 쓸 때 붙인 수식 막기 따옴표를 떼어 원래 값으로 보여 준다.
    row.push(/^'[=+\-@]/.test(cell) ? cell.slice(1) : cell)
    cell = ""
  }
  for (let i = 0; i < s.length; i++) {
    const c = s[i]
    if (quoted) {
      if (c !== '"') { cell += c; continue }
      if (s[i + 1] === '"') { cell += '"'; i++ } else quoted = false
      continue
    }
    if (c === '"') { quoted = true; continue }
    if (c === ",") { 넣기(); continue }
    if (c === "\r") continue
    if (c === "\n") { 넣기(); rows.push(row); row = []; continue }
    cell += c
  }
  if (cell.length > 0 || row.length > 0) { 넣기(); rows.push(row) }
  // 끝에 생긴 빈 줄은 버린다.
  while (rows.length > 0 && rows[rows.length - 1].every((v) => v === "")) rows.pop()
  return rows
}

/** 엑셀이 수식으로 읽지 않게 막고, 쉼표·따옴표·줄바꿈을 감싼다. */
export function csvCell(v: unknown): string {
  let s = v === null || v === undefined ? "" : String(v)
  if (/^[=+\-@\t\r]/.test(s)) s = "'" + s
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

/** UTF-8 BOM 을 붙인 CSV 본문. 엑셀에서 한글이 깨지지 않는다 (AC-W16). */
export function toCsv(header: string[], rows: unknown[][]): string {
  return "﻿" + [header, ...rows].map((r) => r.map(csvCell).join(",")).join("\r\n")
}

/** 발생 건 목록의 CSV 행 (이번 달·연간·이력 공통) */
export function occurrenceCsv(rows: Occurrence[]): { header: string[]; rows: unknown[][] } {
  return {
    header: ["연도", "원기한", "실납부기한", "기관", "비용명", "진행흐름", "지금 지점", "상태", "금액", "증빙"],
    rows: rows.map((o) => [
      o.year, o.due, o.payDue, o.org, o.name, o.flow, o.stageName, o.statusText,
      o.amount !== null ? o.amount : o.amountText, o.attachments,
    ]),
  }
}

/** 이력(처리 완료) CSV — 화면에 보이는 검색 결과 그대로 (AC-W66) */
export function historyCsv(rows: Occurrence[]): { header: string[]; rows: unknown[][] } {
  return {
    header: ["처리일", "연도", "원기한", "기관", "비용명", "진행흐름", "금액", "증빙"],
    rows: rows.map((o) => [o.doneAt ?? "", o.year, o.due, o.org, o.name, o.flow, o.amount ?? "", o.attachments]),
  }
}

/** http/https 만 링크로 쓴다. 서버도 막지만 화면도 한 번 더 막는다. */
export function safeUrl(url: string): string | null {
  try {
    const u = new URL(url)
    return u.protocol === "http:" || u.protocol === "https:" ? u.href : null
  } catch {
    return null
  }
}

/** 해시 경로 → 화면 이름. 모르는 값이면 첫 화면. */
export const VIEWS = ["alerts", "month", "year", "items", "history", "settings"] as const
export type View = (typeof VIEWS)[number]
export function viewFromHash(hash: string): View {
  const h = hash.replace(/^#\/?/, "").split("?")[0]
  return (VIEWS as readonly string[]).includes(h) ? (h as View) : "alerts"
}

/** 변경 기록 한 줄 요약: "전표발행 → 납부" 또는 내용 */
export function eventSummary(e: { action: string; from: string | null; to: string | null; detail: string }): string {
  if ((e.action === "진행" || e.action === "되돌리기") && e.from && e.to) return `${e.from} → ${e.to}`
  return e.detail
}

// ── 항목 추가 창 (ADR-0020) ──

/** 진행흐름 선택 카드: 저장 값은 그대로, 보이는 이름만 쉬운 말로. */
export const FLOW_CARDS: { flow: "신고납부" | "납부만" | "제출만" | "사용자설정"; label: string; steps: string }[] = [
  { flow: "신고납부", label: "신고 후 납부", steps: "신고 → 전표발행 → 납부" },
  { flow: "납부만", label: "납부만", steps: "고지서수령 → 전표발행 → 납부" },
  { flow: "제출만", label: "제출만", steps: "제출자료 작성 → 제출 · 금액 없음" },
  { flow: "사용자설정", label: "직접 정하기", steps: "단계 수와 이름을 정함" },
]

export function flowLabel(flow: string): string {
  return FLOW_CARDS.find((c) => c.flow === flow)?.label ?? flow
}

/** 기한일 선택지 표시: "말일" 은 그대로, 숫자는 "20일". */
export function dayLabel(day: string): string {
  return day === "말일" ? "말일" : `${day}일`
}

/**
 * 항목 창 맨 아래 한 줄 요약. 저장 전에 무엇이 만들어질지 확인한다.
 * 예) "회비 · 매년 5월 20일 · 납부만 · 변동 · 3영업일 전 알림"
 */
export function itemSummary(s: {
  name: string; month: string; day: string; flow: string; paid: boolean; rule: string; fixed: string; lead: string
}): string {
  const parts = [s.name.trim() || "이름 없음"]
  const m = s.month.trim() || "?"
  parts.push(`매년 ${m}월 ${dayLabel(s.day)}`)
  parts.push(flowLabel(s.flow))
  if (s.paid) {
    const fixed = parseWon(s.fixed)
    parts.push(s.rule === "고정" ? (fixed !== null ? `고정 ${won(fixed)}원` : "고정 (금액 필요)") : "변동")
  }
  parts.push(`${s.lead.trim() || "?"}영업일 전 알림`)
  return parts.join(" · ")
}
// ── 설정 화면의 되돌리기 어려운 동작 확인 (ADR-0021) ──

export type SettingsRisk = "apikey-delete" | "apikey-replace" | "start-clear"

/** 확인 창 문구. 잘못 눌러도 한 번 더 멈추게 한다. */
export function settingsConfirm(kind: SettingsRisk, startDate?: string | null): {
  title: string; description: string; action: string; destructive: boolean
} {
  switch (kind) {
    case "apikey-delete":
      return {
        title: "공휴일 인증키를 지울까요?",
        description: "지우면 새 해의 공휴일을 받아 올 수 없습니다. 이미 받은 공휴일 자료는 그대로 남고, 다시 쓰려면 키를 새로 붙여 넣어야 합니다.",
        action: "지우기",
        destructive: true,
      }
    case "apikey-replace":
      return {
        title: "인증키를 새 키로 바꿀까요?",
        description: "저장된 키를 방금 넣은 키로 바꿉니다. '한국천문연구원_특일 정보' 활용신청을 한 키인지 확인하세요.",
        action: "바꾸기",
        destructive: false,
      }
    case "start-clear":
      return {
        title: "추적 시작일 제한을 없앨까요?",
        description: `${startDate ? startDate + " 이전" : "예전"} 기한의 건도 다루게 됩니다. 처리 기록이 없는 예전 건이 '기한 지남' 으로 잡혀 받은 알림과 팝업에 한꺼번에 나올 수 있습니다.`,
        action: "제한 없애기",
        destructive: true,
      }
  }
}

// ── 잘못 누름·덮어쓰기 막기 (ADR-0022) ──

/** 입력하던 창을 닫으려 할 때의 확인 문구. */
export function discardConfirm(what: string) {
  return {
    title: "저장하지 않고 닫을까요?",
    description: `입력한 ${what}이 저장되지 않고 사라집니다.`,
    action: "닫기",
    destructive: true,
  }
}

/** 이미 넣은 금액을 다른 값으로 바꾸는지. 처음 넣거나 같은 값이면 null. */
export function amountOverwrite(existing: number | null, entered: boolean, next: number): { from: number; to: number } | null {
  if (!entered || existing === null || existing === next) return null
  return { from: existing, to: next }
}

/** 회차 금액을 한꺼번에 넣을 때 이미 들어간 금액이 바뀌는 회차. 빈 칸·같은 값·처음 넣는 회차는 뺀다. */
export function groupOverwrites(
  rows: { id: string; label: string; amount: number | null; entered: boolean }[],
  typed: Record<string, string>
): { label: string; from: number; to: number }[] {
  const out: { label: string; from: number; to: number }[] = []
  for (const r of rows) {
    const raw = (typed[r.id] ?? "").trim()
    if (!raw) continue
    const v = parseWon(raw)
    if (v === null) continue
    const c = amountOverwrite(r.amount, r.entered, v)
    if (c) out.push({ label: r.label, ...c })
  }
  return out
}

/** 금액 덮어쓰기 확인 문구. 바뀌는 회차가 많으면 앞 5개만 적는다. */
export function overwriteConfirm(changes: { label: string; from: number; to: number }[]) {
  const shown = changes.slice(0, 5).map((c) => `${c.label} ${won(c.from)}원 → ${won(c.to)}원`)
  const more = changes.length > 5 ? ` 외 ${changes.length - 5}건` : ""
  return {
    title: changes.length === 1 ? "이미 넣은 금액을 바꿀까요?" : `이미 넣은 금액 ${changes.length}건을 바꿀까요?`,
    description: shown.join(" · ") + more,
    action: "바꾸기",
    destructive: false,
  }
}

// ── 차입 스케줄 가져오기 (ADR-0023) ──

/** 항목 유효연도 표시. 제한이 없으면 빈 문자열. */
export function yearRange(start: number | null | undefined, end: number | null | undefined): string {
  const s = start ?? null
  const e = end ?? null
  if (s === null && e === null) return ""
  if (s !== null && e !== null) return s === e ? `${s}년만` : `${s}~${e}년`
  return s !== null ? `${s}년부터` : `${e}년까지`
}

/** 파일 + 시트로 차입건을 가리킨다. 파일 이름에는 | 를 쓸 수 없어 둘이 섞이지 않는다. */
export function loanKey(file: string, sheet: string): string {
  return `${file}|${sheet}`
}

type LoanLike = { sheet: string; ok: boolean; newCount?: number }

/** 처음 고를 차입건: 읽혔고 새로 넣을 회차가 있는 시트. */
/**
 * 처음에 골라 둘 시트: 읽을 수 있는 시트 전부.
 * 새 회차가 없는(이미 다 등록된) 시트도 고른다 — 올린 원본 스케줄을 증빙으로 보관해야 하기 때문이다
 * (같은 파일을 다시 올려 원본만 채우는 경우, 사용자 요청 2026-09-23).
 */
export function loanDefaultKeys(files: { file: string; loans: LoanLike[] }[]): string[] {
  const keys: string[] = []
  for (const f of files) for (const l of f.loans) if (l.ok) keys.push(loanKey(f.file, l.sheet))
  return keys
}

/** 한 파일에서 저장할 시트: 고른 것 전부. 비면 그 파일은 보내지 않는다. */
export function loanSaveSheets(file: string, loans: LoanLike[], selected: string[]): string[] {
  return loans.filter((l) => l.ok && selected.includes(loanKey(file, l.sheet))).map((l) => l.sheet)
}

/**
 * 등록 전에 고친 지급액 → 서버에 보낼 ov 값("지급일|금액|시트").
 * 새로 넣을 회차만, 스케줄 금액과 다를 때만 보낸다. 숫자가 아닌 칸은 invalid 로 돌려준다(지급일).
 */
export function loanOverrides(
  sheet: string,
  payments: { scheduled: string; amount: number; state: string }[],
  typed: Record<string, string>
): { ov: string[]; invalid: string[] } {
  const ov: string[] = []
  const invalid: string[] = []
  for (const p of payments) {
    if (p.state !== "new") continue
    const raw = typed[p.scheduled]
    if (raw === undefined) continue
    const v = parseWon(raw)
    if (v === null) invalid.push(p.scheduled)
    else if (v !== p.amount) ov.push(`${p.scheduled}|${v}|${sheet}`)
  }
  return { ov, invalid }
}

/** 금액 근거: "9/30 4,660,274 + 12/4 560,959" */
export function loanBasis(rows: { date: string; amount: number }[]): string {
  return rows.map((r) => `${Number(r.date.slice(5, 7))}/${Number(r.date.slice(8, 10))} ${won(r.amount)}`).join(" + ")
}

/** 차입건 이름 입력. 약칭을 비우면 차입명을 쓴다 (사용자 요청 2026-09-18). */
export type LoanName = { name: string; short: string }

/** 저장 요청의 nm 값: 시트|차입명|약칭. 차입명이 빈 시트는 missing 으로 돌려준다. */
export function loanNameParams(
  sheets: string[],
  names: Record<string, LoanName | undefined>
): { nm: string[]; missing: string[] } {
  const nm: string[] = []
  const missing: string[] = []
  for (const sheet of sheets) {
    const n = names[sheet]
    const name = (n?.name ?? "").trim()
    if (!name) {
      missing.push(sheet)
      continue
    }
    nm.push(`${sheet}|${name}|${(n?.short ?? "").trim()}`)
  }
  return { nm, missing }
}
