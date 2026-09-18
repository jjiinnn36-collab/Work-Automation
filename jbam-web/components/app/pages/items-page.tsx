"use client"

import * as React from "react"
import {
  InfoIcon, LayersIcon, MenuIcon, MoreHorizontalIcon, PencilIcon, PlusIcon, Trash2Icon, UploadIcon,
} from "lucide-react"

import { errorMessage, get, post } from "@/lib/api"
import { won, yearRange } from "@/lib/logic"
import type { Item, ItemsData } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useRowDrag } from "@/hooks/use-row-drag"
import { useApp } from "@/components/app/app-context"
import { LoanImportDialog, type LoanExtendTarget } from "@/components/app/dialogs/loan-import-dialog"
import { LoadError, None, OrgName, PageHeader } from "@/components/app/parts"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyTitle } from "@/components/ui/empty"
import { Skeleton } from "@/components/ui/skeleton"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { toast } from "@/components/ui/toast"

/** 항목 관리 — 매년 반복되는 기한 정의. 편집은 이 화면에서만 한다 (AC-W7). 엑셀 내려받기는 두지 않는다 (AC-W63). */
export function ItemsPage() {
  const app = useApp()
  const { data, error } = useLoad(() => get<ItemsData>("/api/items"), [app.version])
  const [extend, setExtend] = React.useState<LoanExtendTarget | null>(null)
  // 끌어 옮긴 순서를 서버 응답 전에 먼저 보여 준다. 새 목록이 오면 버린다.
  const [order, setOrder] = React.useState<string[] | null>(null)
  React.useEffect(() => setOrder(null), [data])

  const byId = new Map((data?.items ?? []).map((it) => [it.id, it]))
  const items: Item[] = order
    ? order.map((id) => byId.get(id)).filter((it): it is Item => it !== undefined)
    : (data?.items ?? [])

  const drag = useRowDrag(items.length, (from, to) => {
    const ids = items.map((it) => it.id)
    const [moved] = ids.splice(from, 1)
    ids.splice(to, 0, moved)
    setOrder(ids)
    post("/api/item/move", { id: moved, to })
      .catch((e) => {
        setOrder(null)
        toast.add({ title: "순서를 옮기지 못했습니다", description: errorMessage(e), type: "error" })
      })
      .finally(() => app.refresh())
  })

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  const year = data.today.slice(0, 4)

  async function remove(it: Item) {
    const yes = await app.confirm({
      title: `'${it.name}' 항목을 지울까요?`,
      description: "목록과 알림에서 빠집니다. 진행 기록·금액·증빙은 기록으로 남습니다.",
      action: "지우기",
      destructive: true,
    })
    if (!yes) return
    try {
      await post("/api/item/delete", { id: it.id })
      toast.add({ title: "항목을 지웠습니다", type: "success" })
      app.refresh()
    } catch (e) {
      toast.add({ title: "지우지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="항목 관리"
        description={`전체 ${data.items.length}건`}
        actions={
          <Button onClick={() => app.openItem(null)}>
            <PlusIcon data-icon="inline-start" /> 항목 추가
          </Button>
        }
      />
      <Alert>
        <InfoIcon />
        <AlertTitle>항목 관리</AlertTitle>
        <AlertDescription>
          <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
            <dt className="font-medium text-foreground">일반 비용</dt>
            <dd>매년 돌아오는 기한을 등록</dd>
            <dt className="font-medium text-foreground">차입금 이자</dt>
            <dd>ERP 스케줄 파일로 등록</dd>
            <dt className="font-medium text-foreground">금액</dt>
            <dd>이번 달 · 연간 화면에서 입력</dd>
            <dt className="font-medium text-foreground">만기 연장</dt>
            <dd>
              <kbd className="rounded border bg-background px-1 text-xs">⋯</kbd> → 연장스케줄 업로드
            </dd>
          </dl>
        </AlertDescription>
      </Alert>

      {data.items.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyTitle>항목이 없습니다</EmptyTitle>
            <EmptyDescription>납부·신고 기한을 하나씩 추가하세요.</EmptyDescription>
          </EmptyHeader>
          <EmptyContent>
            <Button onClick={() => app.openItem(null)}>항목 추가</Button>
          </EmptyContent>
        </Empty>
      ) : (
        <div className="rounded-xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="pl-4">기관</TableHead>
                <TableHead>비용명</TableHead>
                <TableHead>흐름</TableHead>
                <TableHead>기한</TableHead>
                <TableHead>알림</TableHead>
                <TableHead>금액규칙</TableHead>
                <TableHead className="text-right">{year}년 금액</TableHead>
                <TableHead>비고</TableHead>
                <TableHead className="w-10" />
                <TableHead className="w-10 pr-3"><span className="sr-only">순서</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((it, i) => {
                return (
                  <TableRow key={it.id} {...drag.rowProps(i)} className="data-[dragging]:bg-background data-[dragging]:shadow-lg">
                    <TableCell className="pl-4 text-muted-foreground"><OrgName o={it} /></TableCell>
                    <TableCell>
                      <button type="button" className="text-left font-medium hover:underline" onClick={() => app.openItem(it)}>
                        {it.name}
                      </button>
                    </TableCell>
                    <TableCell>
                      {it.flow === "사용자설정" ? (
                        <span title={it.stages.join(" → ")}>
                          사용자설정 <span className="text-muted-foreground">· {it.stages.length}단계</span>
                        </span>
                      ) : (
                        it.flow
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {it.month}월 {it.day === "말일" ? "말일" : `${it.day}일`}
                      {!it.loan && yearRange(it.startYear, it.endYear) && (
                        <span className="ml-1 text-xs text-muted-foreground">· {yearRange(it.startYear, it.endYear)}</span>
                      )}
                    </TableCell>
                    <TableCell className="tabular-nums">{it.lead}영업일</TableCell>
                    <TableCell>
                      {!it.paid ? <None>해당 없음</None> : <Badge variant={it.rule === "고정" ? "outline" : "secondary"}>{it.rule}</Badge>}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {it.thisYearAmount !== null ? won(it.thisYearAmount) : it.thisYearUnknown ? <span className="font-medium text-action">미확인</span> : <None>{it.paid ? "없음" : "해당 없음"}</None>}
                    </TableCell>
                    <TableCell className="max-w-56 truncate text-xs text-muted-foreground tabular-nums" title={it.memo}>{it.memo || <None />}</TableCell>
                    <TableCell>
                      <DropdownMenu>
                        <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={`${it.name} 더보기`} />}>
                          <MoreHorizontalIcon />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" className="w-44">
                          <DropdownMenuItem onClick={() => app.openItem(it)}>
                            <PencilIcon /> 수정
                          </DropdownMenuItem>
                          {it.group && (
                            <DropdownMenuItem onClick={() => app.openGroup(it.startYear ?? Number(year), it.group)}>
                              <LayersIcon /> 회차 금액
                            </DropdownMenuItem>
                          )}
                          {it.loan && it.group && (
                            <DropdownMenuItem onClick={() => setExtend({ group: it.group, org: it.org })}>
                              <UploadIcon /> 연장스케줄 업로드
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuSeparator />
                          <DropdownMenuItem variant="destructive" onClick={() => remove(it)}>
                            <Trash2Icon /> 삭제
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                    <TableCell className="pr-3">
                      <button
                        type="button"
                        {...drag.handleProps(i)}
                        aria-label={`${it.name} 순서 옮기기 — 끌거나 ↑·↓ 키`}
                        title="끌어서 순서 바꾸기"
                        className="flex size-7 cursor-grab touch-none items-center justify-center rounded-md text-muted-foreground outline-none hover:bg-muted hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 active:cursor-grabbing"
                      >
                        <MenuIcon className="size-4" />
                      </button>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      )}
      <LoanImportDialog open={extend !== null} onOpenChange={(v) => !v && setExtend(null)} extend={extend} />
    </div>
  )
}
