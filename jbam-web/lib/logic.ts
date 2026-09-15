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

/** 말풍선 문구 (AC-W48, W75a): "2/4 신고 · 진행중" */
export function stepTooltip(names: string[], i: number, state: StepState): string {
  const word = state === "past" ? "완료" : state === "current" ? "진행중" : "진행예정"
  return `${i + 1}/${names.length} ${names[i]} · ${word}`
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
export function previewKind(fileName: string): "pdf" | "image" | "none" {
  const ext = fileName.toLowerCase().split(".").pop() ?? ""
  if (ext === "pdf") return "pdf"
  if (["png", "jpg", "jpeg", "gif"].includes(ext)) return "image"
  return "none"
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
