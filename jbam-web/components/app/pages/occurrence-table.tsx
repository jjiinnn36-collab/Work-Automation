"use client"

import { cn } from "@/lib/utils"
import type { Occurrence } from "@/lib/types"
import { AdvanceButton, AmountButton, DueText, RowMenu, SiteOrDocs, StatusBadge } from "@/components/app/parts"
import { NextAction, Steps } from "@/components/app/steps"
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@/components/ui/empty"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"

/**
 * 이번 달·연간이 같이 쓰는 발생 건 표. 한 줄 안에서 날짜 → 비용명 → 진행 → 금액 → 동작 순으로 읽힌다 (AC-W69).
 * 기한 지난 미완료 건은 연한 빨간 바탕으로 먼저 보인다 (AC-W55).
 */
export function OccurrenceTable({ rows, empty, showYear = false }: { rows: Occurrence[]; empty: string; showYear?: boolean }) {
  if (rows.length === 0)
    return (
      <Empty className="border">
        <EmptyHeader>
          <EmptyTitle>{empty}</EmptyTitle>
          <EmptyDescription>조건을 바꾸거나 다른 달을 보세요.</EmptyDescription>
        </EmptyHeader>
      </Empty>
    )

  return (
    <div className="rounded-xl border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-32 pl-4">기한</TableHead>
            <TableHead>항목</TableHead>
            <TableHead className="w-56">진행</TableHead>
            <TableHead className="w-36 text-right">금액</TableHead>
            <TableHead className="w-64 pr-4 text-right">동작</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((o) => {
            const overdue = o.severity === "overdue"
            return (
              <TableRow
                key={`${o.year}-${o.id}`}
                className={cn(
                  overdue && "bg-destructive/5 hover:bg-destructive/10",
                  o.done && "text-muted-foreground",
                  // 추적 시작일 이전 건은 흐리게 — 일정표로는 보이되 할 일로 읽히지 않게 (Q1)
                  o.beforeStart && "opacity-55"
                )}
              >
                <TableCell className="pl-4 align-top">
                  <DueText o={o} className={cn("font-medium", overdue && "text-destructive")} />
                  {showYear && <div className="text-xs text-muted-foreground">{o.year}년</div>}
                </TableCell>
                <TableCell className="align-top whitespace-normal">
                  <div className="font-medium text-foreground">{o.name}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                    <span>{o.org}</span>
                    <StatusBadge o={o} />
                    {o.group && <span className="rounded bg-muted px-1">분할 {o.group}</span>}
                  </div>
                </TableCell>
                <TableCell className="align-top">
                  <Steps o={o} className="max-w-48" />
                  <NextAction o={o} className="mt-1.5 block" />
                </TableCell>
                <TableCell className="text-right align-top">
                  <AmountButton o={o} />
                </TableCell>
                <TableCell className="pr-4 align-top">
                  <div className="flex items-center justify-end gap-1.5">
                    <AdvanceButton o={o} size="xs" />
                    {!o.done && !o.beforeStart && <SiteOrDocs o={o} size="xs" />}
                    <RowMenu o={o} />
                  </div>
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
