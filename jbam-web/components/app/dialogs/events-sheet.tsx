"use client"

import * as React from "react"

import { errorMessage, get } from "@/lib/api"
import { eventSummary } from "@/lib/logic"
import type { EventRow } from "@/lib/types"
import { Badge } from "@/components/ui/badge"
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@/components/ui/empty"
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { Spinner } from "@/components/ui/spinner"

/** 한 건의 변경 기록 — 누가(팝업·웹·보드) 언제 무엇을 바꿨는지 (ADR-0004). */
export function EventsSheet({
  target, open, onOpenChange,
}: { target: { year: number; id: string; name: string } | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const [rows, setRows] = React.useState<EventRow[] | null>(null)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open || !target) return
    setRows(null)
    setError(null)
    get<{ rows: EventRow[] }>("/api/events", { y: target.year, id: target.id })
      .then((r) => setRows(r.rows))
      .catch((e) => setError(errorMessage(e)))
  }, [open, target])

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full sm:max-w-md">
        <SheetHeader>
          <SheetTitle>변경 기록</SheetTitle>
          <SheetDescription>
            {target?.name} · {target?.year}년
          </SheetDescription>
        </SheetHeader>
        <div className="flex-1 overflow-y-auto px-4 pb-6">
          {error && <p className="text-sm text-destructive">{error}</p>}
          {!rows && !error && <div className="flex justify-center py-8"><Spinner /></div>}
          {rows && rows.length === 0 && (
            <Empty>
              <EmptyHeader>
                <EmptyTitle>기록이 없습니다</EmptyTitle>
                <EmptyDescription>이 건은 아직 아무도 바꾸지 않았습니다.</EmptyDescription>
              </EmptyHeader>
            </Empty>
          )}
          {rows && rows.length > 0 && (
            <ol className="relative flex flex-col gap-4 border-l pl-4">
              {rows.map((e, i) => (
                <li key={i} className="relative">
                  <span className="absolute top-1.5 -left-[21px] size-2.5 rounded-full border-2 border-background bg-muted-foreground/60" />
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Badge variant={e.action === "되돌리기" || e.action.endsWith("삭제") ? "destructive" : "secondary"}>{e.action}</Badge>
                    <span className="text-xs text-muted-foreground">{e.source}</span>
                    <span className="ml-auto text-xs tabular-nums text-muted-foreground">{e.at.slice(0, 16)}</span>
                  </div>
                  <p className="mt-1 text-sm">{eventSummary(e) || "—"}</p>
                </li>
              ))}
            </ol>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
