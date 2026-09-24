"use client"

import * as React from "react"
import { DownloadIcon, InfoIcon } from "lucide-react"

import { downloadText, get } from "@/lib/api"
import { eventSummary, historyCsv, presetRange, toCsv, won } from "@/lib/logic"
import type { EventRow, HistoryData } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { AmountButton, LoadError, OrgName, PageHeader, RowMenu, StatCard } from "@/components/app/parts"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@/components/ui/empty"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

/** 이력 — 처리 완료된 건을 기간으로 조회하고, 누가 무엇을 바꿨는지도 본다 (AC-W64~W68, ADR-0004). */
export function HistoryPage() {
  const app = useApp()
  const [range, setRange] = React.useState<{ from: string; to: string } | null>(null)
  const [draft, setDraft] = React.useState<{ from: string; to: string }>({ from: "", to: "" })
  const [tab, setTab] = React.useState("done")

  const { data, error } = useLoad(() => get<HistoryData>("/api/history", range ?? undefined), [app.version, range])
  const events = useLoad(
    () => (data ? get<{ rows: EventRow[] }>("/api/events", { from: data.from, to: data.to }) : Promise.resolve({ rows: [] as EventRow[] })),
    [app.version, data?.from, data?.to]
  )

  React.useEffect(() => {
    if (data) setDraft({ from: data.from, to: data.to })
  }, [data])

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  const whole = data.from.slice(5) === "01-01" && data.to.slice(5) === "12-31" && data.from.slice(0, 4) === data.to.slice(0, 4)
  const period = whole ? `${data.from.slice(0, 4)}년` : `${data.from} ~ ${data.to}`

  function exportCsv() {
    const c = historyCsv(data!.rows)
    downloadText(`처리이력_${data!.from}_${data!.to}.csv`, toCsv(c.header, c.rows))
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="이력"
        description="처리 완료된 건과 변경 기록"
        actions={
          <Button variant="outline" onClick={exportCsv} disabled={data.rows.length === 0}>
            <DownloadIcon data-icon="inline-start" /> 검색 결과 엑셀 내려받기
          </Button>
        }
      />

      <div className="grid gap-4 sm:grid-cols-3">
        <StatCard label="조회 기간" value={period} unit="" fit={!whole} />
        <StatCard label="처리 완료" value={data.rows.length} />
        <StatCard label="기간 합계" value={won(data.total)} unit="원" fit />
      </div>

      <form
        className="flex flex-wrap items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          if (draft.from && draft.to) setRange(draft)
        }}
      >
        <Input type="date" aria-label="시작일" className="w-40" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
        <span className="text-muted-foreground">~</span>
        <Input type="date" aria-label="종료일" className="w-40" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
        <Button type="submit">조회</Button>
        <Button type="button" variant="secondary" onClick={() => setRange(presetRange("this", data.today))}>당해년도</Button>
        <Button type="button" variant="secondary" onClick={() => setRange(presetRange("last", data.today))}>전년도</Button>
        <Button type="button" variant="secondary" onClick={() => setRange(presetRange("12m", data.today))}>최근 12개월</Button>
      </form>

      <Tabs value={tab} onValueChange={(v) => setTab(String(v))}>
        <TabsList>
          <TabsTrigger value="done">처리 완료 {data.rows.length}</TabsTrigger>
          <TabsTrigger value="events">변경 기록 {events.data ? events.data.rows.length : ""}</TabsTrigger>
        </TabsList>

        <TabsContent value="done" className="pt-3">
          {data.rows.length === 0 ? (
            <Empty className="border">
              <EmptyHeader>
                <EmptyTitle>이 기간에 처리 완료된 건이 없습니다</EmptyTitle>
                <EmptyDescription>기간을 넓혀 보세요.</EmptyDescription>
              </EmptyHeader>
            </Empty>
          ) : (
            <div className="rounded-xl border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-4">처리일</TableHead>
                    <TableHead>원기한</TableHead>
                    <TableHead>비용명</TableHead>
                    <TableHead>기관</TableHead>
                    <TableHead>흐름</TableHead>
                    <TableHead className="text-right">금액</TableHead>
                    <TableHead>증빙자료</TableHead>
                    <TableHead className="pr-4 text-right">처리</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.rows.map((o) => (
                    <TableRow key={`${o.year}-${o.id}`}>
                      <TableCell className="pl-4 font-medium tabular-nums">{o.doneAt}</TableCell>
                      <TableCell className="tabular-nums text-muted-foreground">{o.due}</TableCell>
                      <TableCell className="font-medium">{o.name}</TableCell>
                      <TableCell className="text-muted-foreground"><OrgName o={o} /></TableCell>
                      <TableCell className="text-muted-foreground">{o.flow}</TableCell>
                      <TableCell className="text-right"><AmountButton o={o} /></TableCell>
                      <TableCell>
                        <Button variant="ghost" size="xs" onClick={() => app.openDocs(o)}>
                          {o.attachments > 0 ? `열기 ${o.attachments}건` : "첨부"}
                        </Button>
                      </TableCell>
                      <TableCell className="pr-4">
                        <div className="flex items-center justify-end gap-1.5">
                          <RowMenu o={o} />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
                <TableFooter>
                  <TableRow>
                    <TableCell colSpan={5} className="pl-4">조회 기간 합계 · {data.rows.length}건</TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">{won(data.total)}원</TableCell>
                    <TableCell colSpan={2} />
                  </TableRow>
                </TableFooter>
              </Table>
            </div>
          )}
        </TabsContent>

        <TabsContent value="events" className="pt-3">
          {!events.data ? (
            <Skeleton className="h-40 rounded-xl" />
          ) : events.data.rows.length === 0 ? (
            <Empty className="border">
              <EmptyHeader>
                <EmptyTitle>이 기간의 변경 기록이 없습니다</EmptyTitle>
              </EmptyHeader>
            </Empty>
          ) : (
            <div className="rounded-xl border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-4">시각</TableHead>
                    <TableHead>항목</TableHead>
                    <TableHead>구분</TableHead>
                    <TableHead>내용</TableHead>
                    <TableHead className="pr-4">어디서</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {events.data.rows.map((e, i) => (
                    <TableRow key={i}>
                      <TableCell className="pl-4 tabular-nums text-muted-foreground">{e.at.slice(0, 16)}</TableCell>
                      <TableCell>
                        <span className="font-medium">{e.name}</span>
                        {e.org && <span className="ml-1.5 inline-flex items-center text-xs text-muted-foreground">{e.year}년 · <OrgName o={e} className="ml-1" /></span>}
                      </TableCell>
                      <TableCell>
                        <Badge variant={e.action === "되돌리기" || e.action.endsWith("삭제") ? "destructive" : "secondary"}>{e.action}</Badge>
                      </TableCell>
                      <TableCell className="whitespace-normal">{eventSummary(e)}</TableCell>
                      <TableCell className="pr-4 text-muted-foreground">{e.source}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </TabsContent>
      </Tabs>

      {data.startDate && (
        <Alert>
          <InfoIcon />
          <AlertDescription>
            추적 시작일이 <b>{data.startDate}</b> 입니다. 그 전 건은 진행 기록이 없어 이력에도 남지 않습니다 — 실제로 놓친 것이 아닙니다.
          </AlertDescription>
        </Alert>
      )}
    </div>
  )
}
