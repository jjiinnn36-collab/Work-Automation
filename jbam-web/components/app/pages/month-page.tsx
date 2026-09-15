"use client"

import * as React from "react"
import { ChevronLeftIcon, ChevronRightIcon, DownloadIcon } from "lucide-react"

import { downloadText, get } from "@/lib/api"
import { occurrenceCsv, shiftMonth, sortRemaining, toCsv, won, yearStatus } from "@/lib/logic"
import type { MonthData, Occurrence } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { LoadError, PageHeader, StatCard } from "@/components/app/parts"
import { OccurrenceTable } from "@/components/app/pages/occurrence-table"
import { Button } from "@/components/ui/button"
import { ButtonGroup } from "@/components/ui/button-group"
import { Skeleton } from "@/components/ui/skeleton"

const names = (list: Occurrence[]) => list.slice(0, 3).map((o) => o.name).join(" · ") || "없음"

/** 이번 달 — 원기한이 이 달에 드는 건 (보드와 같은 기준, AC-W6). 낱말은 연간과 같다 (Q3). */
export function MonthPage() {
  const app = useApp()
  const [ym, setYm] = React.useState<string | null>(null)
  const { data, error } = useLoad(() => get<MonthData>("/api/month", { ym }), [app.version, ym])

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  const cur = `${data.year}-${String(data.month).padStart(2, "0")}`
  const thisMonth = cur === data.today.slice(0, 7)
  const remaining = sortRemaining(data.rows.filter((o) => !o.done && !o.beforeStart))
  // 지난 건 = 끝난 건 + 추적 시작일 이전 건, 최근 것이 위로 (AC-W56)
  const finished = data.rows.filter((o) => o.done || o.beforeStart).reverse()
  const progress = remaining.filter((o) => yearStatus(o) === "progress")
  const upcoming = remaining.filter((o) => yearStatus(o) === "upcoming")
  const overdue = remaining.filter((o) => yearStatus(o) === "overdue")
  const done = data.rows.filter((o) => o.done)

  function exportCsv() {
    const c = occurrenceCsv(data!.rows)
    downloadText(`납부기한_${cur}.csv`, toCsv(c.header, c.rows))
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={`${data.year}년 ${data.month}월`}
        description={`기한이 이 달에 드는 ${data.rows.length - data.beforeStart}건${data.beforeStart > 0 ? ` · 추적 시작 전 ${data.beforeStart}건은 흐리게` : ""} · 주말·공휴일로 밀린 건은 실제 납부일을 함께 적습니다`}
        actions={
          <>
            <ButtonGroup>
              <Button variant="outline" size="icon" aria-label="이전 달" onClick={() => setYm(shiftMonth(cur, -1))}>
                <ChevronLeftIcon />
              </Button>
              <Button variant="outline" size="icon" aria-label="다음 달" onClick={() => setYm(shiftMonth(cur, 1))}>
                <ChevronRightIcon />
              </Button>
            </ButtonGroup>
            {!thisMonth && (
              <Button variant="secondary" onClick={() => setYm(null)}>
                이번 달로
              </Button>
            )}
            <Button variant="outline" onClick={exportCsv}>
              <DownloadIcon data-icon="inline-start" /> 엑셀 내려받기
            </Button>
          </>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <StatCard label="진행중" value={data.inProgress} hint={names(progress)} />
        <StatCard label="진행예정" value={data.upcoming} hint={names(upcoming)} />
        <StatCard label="완료" value={data.done} hint={names(done)} />
        <StatCard label="기한 지남" value={data.overdue} tone="danger" hint={overdue.map((o) => `${o.due.slice(5)} ${o.name}`).join(" · ") || "없음"} />
        <StatCard
          label="이 달 납부 합계"
          value={won(data.total)}
          unit="원"
          hint={data.amountUnknown > 0 ? <span className="text-action">금액 미확인 {data.amountUnknown}건 제외</span> : "모든 금액 확인됨"}
        />
      </div>

      <section className="flex flex-col gap-3">
        <h3 className="flex flex-wrap items-baseline gap-2 text-base font-semibold">
          남은 건
          <span className="text-sm font-normal text-muted-foreground">
            {remaining.length}건 · 진행중 {progress.length} · 진행예정 {upcoming.length}
          </span>
          {overdue.length > 0 && <span className="text-sm text-destructive">기한 지남 {overdue.length}건</span>}
        </h3>
        <OccurrenceTable rows={remaining} empty="남은 건이 없습니다" />
      </section>

      <section className="flex flex-col gap-3">
        <h3 className="flex items-baseline gap-2 text-base font-semibold">
          지난 건 <span className="text-sm font-normal text-muted-foreground">{finished.length}건 · 최근 것이 위로</span>
        </h3>
        <OccurrenceTable rows={finished} empty="지난 건이 없습니다" />
      </section>
    </div>
  )
}
