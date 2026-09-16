"use client"

import * as React from "react"
import { ChevronLeftIcon, ChevronRightIcon, DownloadIcon, SearchIcon } from "lucide-react"

import { downloadText, get } from "@/lib/api"
import { matchYearFilter, occurrenceCsv, sortRemaining, toCsv, yearStatus, type YearFilter } from "@/lib/logic"
import type { YearData } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { LoadError, PageHeader, StatCard } from "@/components/app/parts"
import { OccurrenceTable } from "@/components/app/pages/occurrence-table"
import { Button } from "@/components/ui/button"
import { ButtonGroup } from "@/components/ui/button-group"
import { InputGroup, InputGroupAddon, InputGroupInput } from "@/components/ui/input-group"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { Skeleton } from "@/components/ui/skeleton"

/** 연간 — 그 해 전체 일정표. 월·기관·상태·검색으로 거른다 (AC-W5, W82~W85). */
export function YearPage() {
  const app = useApp()
  const [year, setYear] = React.useState<number | null>(null)
  const [filter, setFilter] = React.useState<YearFilter>({ month: "", org: "", status: "", q: "" })
  const { data, error } = useLoad(() => get<YearData>("/api/year", { y: year }), [app.version, year])

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  const y = data.year
  const remaining = sortRemaining(data.remaining.filter((o) => matchYearFilter(o, filter, data.today)))
  const finished = data.finished.filter((o) => matchYearFilter(o, filter, data.today))
  const all = [...data.remaining, ...data.finished]
  const unknown = all.filter((o) => o.amount === null && o.paid && !o.beforeStart)
  const inProg = data.remaining.filter((o) => yearStatus(o, data.today) === "progress")
  const upcoming = sortRemaining(data.remaining.filter((o) => yearStatus(o, data.today) === "upcoming"))
  const setF = (patch: Partial<YearFilter>) => setFilter((f) => ({ ...f, ...patch }))
  const filtered = filter.month || filter.org || filter.status || filter.q

  function exportCsv() {
    const c = occurrenceCsv([...remaining, ...finished])
    downloadText(`납부기한_${y}년${filtered ? "_필터" : ""}.csv`, toCsv(c.header, c.rows))
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={`${y}년`}
        description={`전체 ${data.count}건`}
        actions={
          <>
            <ButtonGroup>
              <Button variant="outline" size="icon" aria-label="이전 해" onClick={() => setYear(y - 1)}>
                <ChevronLeftIcon />
              </Button>
              <Button variant="outline" size="icon" aria-label="다음 해" onClick={() => setYear(y + 1)}>
                <ChevronRightIcon />
              </Button>
            </ButtonGroup>
            {y !== Number(data.today.slice(0, 4)) && (
              <Button variant="secondary" onClick={() => setYear(null)}>
                올해로
              </Button>
            )}
            <Button variant="outline" onClick={exportCsv}>
              <DownloadIcon data-icon="inline-start" /> 엑셀 내려받기
            </Button>
          </>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <StatCard label="지난 건" value={data.past} hint={`완료 ${data.pastDone} · 지남 ${data.past - data.pastDone}`} />
        <StatCard label="진행중" value={data.inProgress} hint={inProg.map((o) => o.name).join(" · ") || "없음"} />
        <StatCard label="진행예정" value={data.upcoming} hint={upcoming[0] ? `가장 이른 건 ${upcoming[0].due.slice(5)}` : "없음"} />
        <StatCard label="기한 지남" value={data.overdue} tone="danger" hint={data.overdue > 0 ? "남은 건 맨 위에 고정" : "없음"} />
        <StatCard label="금액 미확인" value={data.amountUnknown} tone="action" hint={unknown.slice(0, 3).map((o) => o.name).join(" · ") || "없음"} />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <NativeSelect aria-label="월" value={filter.month} onChange={(e) => setF({ month: e.target.value })}>
          <NativeSelectOption value="">월 전체</NativeSelectOption>
          {Array.from({ length: 12 }, (_, i) => (
            <NativeSelectOption key={i} value={String(i + 1)}>{i + 1}월</NativeSelectOption>
          ))}
        </NativeSelect>
        <NativeSelect aria-label="기관" value={filter.org} onChange={(e) => setF({ org: e.target.value })}>
          <NativeSelectOption value="">기관 전체</NativeSelectOption>
          {data.orgs.map((g) => <NativeSelectOption key={g} value={g}>{g}</NativeSelectOption>)}
        </NativeSelect>
        <NativeSelect aria-label="상태" value={filter.status} onChange={(e) => setF({ status: e.target.value as YearFilter["status"] })}>
          <NativeSelectOption value="">상태 전체</NativeSelectOption>
          <NativeSelectOption value="progress">진행중</NativeSelectOption>
          <NativeSelectOption value="upcoming">진행예정</NativeSelectOption>
          <NativeSelectOption value="overdue">기한 지남</NativeSelectOption>
          <NativeSelectOption value="done">완료</NativeSelectOption>
          <NativeSelectOption value="before">추적 시작 전</NativeSelectOption>
        </NativeSelect>
        <InputGroup className="w-full sm:w-64">
          <InputGroupAddon><SearchIcon /></InputGroupAddon>
          <InputGroupInput placeholder="비용명·기관 검색" value={filter.q} onChange={(e) => setF({ q: e.target.value })} aria-label="검색" />
        </InputGroup>
        {filtered && (
          <Button variant="ghost" onClick={() => setFilter({ month: "", org: "", status: "", q: "" })}>
            거르기 해제
          </Button>
        )}
      </div>

      <section className="flex flex-col gap-3">
        <h3 className="flex items-baseline gap-2 text-base font-semibold">
          남은 건
          <span className="text-sm font-normal text-muted-foreground">
            {remaining.length}건 · 진행중 {remaining.filter((o) => yearStatus(o, data.today) === "progress").length} · 진행예정{" "}
            {remaining.filter((o) => yearStatus(o, data.today) === "upcoming").length}
          </span>
        </h3>
        <OccurrenceTable rows={remaining} empty="남은 건이 없습니다" />
      </section>

      <section className="flex flex-col gap-3">
        <h3 className="flex items-baseline gap-2 text-base font-semibold">
          지난 건 <span className="text-sm font-normal text-muted-foreground">{finished.length}건 · 끝난 건과 추적 시작 전 건 · 최근 것이 위로</span>
        </h3>
        <OccurrenceTable rows={finished} empty="지난 건이 없습니다" />
      </section>
    </div>
  )
}
