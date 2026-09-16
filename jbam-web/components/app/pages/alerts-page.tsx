"use client"

import { BellOffIcon, CalendarClockIcon, TriangleAlertIcon } from "lucide-react"

import { cn } from "@/lib/utils"
import { get } from "@/lib/api"
import { won } from "@/lib/logic"
import type { AlertsData, Occurrence } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { AdvanceButton, AmountButton, DueText, LoadError, PageHeader, RowMenu, SiteOrDocs, StatusBadge } from "@/components/app/parts"
import { NextAction, Steps } from "@/components/app/steps"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { Skeleton } from "@/components/ui/skeleton"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"

/** 받은 알림 — 알림일이 지난 처리할 건만 (웹 시안 Main). */
export function AlertsPage() {
  const app = useApp()
  const { data, error } = useLoad(() => get<AlertsData>("/api/alerts"), [app.version])

  if (error && !data) return <LoadError message={error} />
  if (!data)
    return (
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {[0, 1, 2].map((i) => <Skeleton key={i} className="h-56 rounded-xl" />)}
      </div>
    )

  const waiting = data.rows.length - data.pending

  return (
    <div className="flex flex-col gap-6">
      {data.warnings.map((w) => (
        <Alert key={w}>
          <TriangleAlertIcon />
          <AlertTitle>자료 확인이 필요합니다</AlertTitle>
          <AlertDescription>
            {w}{" "}
            <a href="#settings">설정 열기</a>
          </AlertDescription>
        </Alert>
      ))}

      <PageHeader
        title="지금 처리할 건"
        description={
          data.rows.length === 0
            ? "알림일이 지난 건이 없습니다."
            : `알림일이 지난 ${data.rows.length}건${waiting > 0 ? ` · 오늘 대기 ${waiting}건` : ""}`
        }
      />

      {data.rows.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon"><BellOffIcon /></EmptyMedia>
            <EmptyTitle>지금 처리할 건이 없습니다</EmptyTitle>
            <EmptyDescription>
              다음 기한은 <a href="#month">이번 달</a>과 <a href="#year">연간</a>에서 볼 수 있습니다.
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {data.rows.map((o) => <AlertCard key={`${o.year}-${o.id}`} o={o} />)}
        </div>
      )}

      {data.overdue.length > 0 && (
        <Card className="ring-destructive/30">
          <CardHeader>
            <CardTitle className="text-destructive">오래 밀린 건</CardTitle>
            <CardDescription>기한이 지나고 5영업일이 넘은 미처리 {data.overdue.length}건. 매일 묻지는 않지만 끝내야 조용해집니다.</CardDescription>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>기한</TableHead>
                  <TableHead>항목</TableHead>
                  <TableHead>지금 할 일</TableHead>
                  <TableHead className="text-right">금액</TableHead>
                  <TableHead className="text-right">동작</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.overdue.map((o) => (
                  <TableRow key={`${o.year}-${o.id}`}>
                    <TableCell><DueText o={o} /></TableCell>
                    <TableCell>
                      <div className="font-medium">{o.name}</div>
                      <div className="text-xs text-muted-foreground">{o.year}년 · {o.org} · <span className="text-destructive">{o.statusText}</span></div>
                    </TableCell>
                    <TableCell><NextAction o={o} /></TableCell>
                    <TableCell className="text-right"><AmountButton o={o} /></TableCell>
                    <TableCell>
                      <div className="flex items-center justify-end gap-1.5">
                        <SiteOrDocs o={o} size="xs" />
                        <RowMenu o={o} />
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <Alert>
        <CalendarClockIcon />
        <AlertDescription>
          올해 나머지 {won(data.notYet)}건은 아직 알림일이 아닙니다 — <a href="#month">이번 달</a>과 <a href="#year">연간</a>에서 볼 수 있습니다.
        </AlertDescription>
      </Alert>
    </div>
  )
}

function AlertCard({ o }: { o: Occurrence }) {
  const app = useApp()
  const overdue = o.severity === "overdue"
  return (
    <Card className={cn(overdue && "ring-destructive/40", o.confirmedToday && "opacity-70")}>
      <CardHeader>
        <CardDescription>{o.org}</CardDescription>
        <CardTitle className="text-lg">{o.name}</CardTitle>
        <CardAction><StatusBadge o={o} /></CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex items-baseline justify-between gap-2 text-sm">
          <span className="text-muted-foreground">
            기한 <DueText o={o} />
          </span>
          <AmountButton o={o} className="text-base" />
        </div>
        <div className="flex flex-col gap-2">
          <Steps o={o} showLabels />
          <div className="flex items-center justify-between text-xs text-muted-foreground">
            <span>{o.stage + 1}/{o.stages.length} {o.stageName} 끝남</span>
            {o.attachments > 0 && <Badge variant="outline">증빙 {o.attachments}</Badge>}
          </div>
        </div>
      </CardContent>
      <CardFooter className="flex-wrap gap-2">
        <AdvanceButton o={o} />
        {o.confirmedToday ? (
          <>
            <Badge variant="secondary">오늘 대기함</Badge>
            {o.deferredToday && (
              <Button variant="ghost" size="sm" onClick={() => app.act("undefer", o)}>
                대기 취소
              </Button>
            )}
          </>
        ) : (
          <Button variant="outline" size="sm" onClick={() => app.act("defer", o)}>
            오늘은 대기
          </Button>
        )}
        <SiteOrDocs o={o} />
        <div className="ml-auto"><RowMenu o={o} /></div>
      </CardFooter>
    </Card>
  )
}
