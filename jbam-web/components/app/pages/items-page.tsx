"use client"

import { ArrowDownIcon, ArrowUpIcon, ExternalLinkIcon, InfoIcon, LayersIcon, MoreHorizontalIcon, PencilIcon, PlusIcon, Trash2Icon } from "lucide-react"

import { errorMessage, get, post } from "@/lib/api"
import { safeUrl, won } from "@/lib/logic"
import type { Item, ItemsData } from "@/lib/types"
import { useLoad } from "@/hooks/use-load"
import { useApp } from "@/components/app/app-context"
import { LoadError, PageHeader } from "@/components/app/parts"
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

  if (error && !data) return <LoadError message={error} />
  if (!data) return <Skeleton className="h-96 rounded-xl" />

  const year = data.today.slice(0, 4)

  async function move(it: Item, dir: "up" | "down") {
    try {
      await post("/api/item/move", { id: it.id, dir })
      app.refresh()
    } catch (e) {
      toast.add({ title: "순서를 옮기지 못했습니다", description: errorMessage(e), type: "error" })
    }
  }

  async function remove(it: Item) {
    const yes = await app.confirm({
      title: `'${it.name}' 항목을 지울까요?`,
      description: "진행 기록·금액·증빙은 남습니다. 같은 id 로 다시 만들면 그대로 이어집니다.",
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
        <AlertTitle>항목 관리에서 하는 일</AlertTitle>
        <AlertDescription>
          <ul className="mt-1 flex list-disc flex-col gap-1 pl-4">
            <li>기관 · 비용명 · 기한 · 진행흐름을 정합니다.</li>
            <li>
              그 해 실제 금액은 <b>이번 달</b>·<b>연간</b> 화면에서 금액을 눌러 넣고, 아래 <b>{year}년 금액</b> 칸에서 확인합니다.
            </li>
            <li>
              분할납부는 항목 추가의 기한 월에 <code className="rounded bg-muted px-1">5,6,7</code> 처럼 쉼표로 넣습니다.
            </li>
          </ul>
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
                <TableHead className="w-16 pl-4">순서</TableHead>
                <TableHead>기관</TableHead>
                <TableHead>비용명</TableHead>
                <TableHead>흐름</TableHead>
                <TableHead>기한</TableHead>
                <TableHead>알림</TableHead>
                <TableHead>금액규칙</TableHead>
                <TableHead className="text-right">{year}년 금액</TableHead>
                <TableHead>홈페이지</TableHead>
                <TableHead>비고</TableHead>
                <TableHead className="w-10 pr-4" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((it, i) => {
                const url = it.siteUrl ? safeUrl(it.siteUrl) : null
                return (
                  <TableRow key={it.id}>
                    <TableCell className="pl-4">
                      <div className="flex">
                        <Button variant="ghost" size="icon-xs" aria-label="위로" disabled={i === 0} onClick={() => move(it, "up")}>
                          <ArrowUpIcon />
                        </Button>
                        <Button variant="ghost" size="icon-xs" aria-label="아래로" disabled={i === data.items.length - 1} onClick={() => move(it, "down")}>
                          <ArrowDownIcon />
                        </Button>
                      </div>
                    </TableCell>
                    <TableCell className="text-muted-foreground">{it.org}</TableCell>
                    <TableCell>
                      <button type="button" className="text-left font-medium hover:underline" onClick={() => app.openItem(it)}>
                        {it.name}
                      </button>
                      {it.group && (
                        <Badge variant="outline" className="ml-1.5">
                          <LayersIcon /> {it.group}
                        </Badge>
                      )}
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
                    <TableCell className="tabular-nums">{it.month}월 {it.day === "말일" ? "말일" : `${it.day}일`}</TableCell>
                    <TableCell className="tabular-nums">{it.lead}영업일</TableCell>
                    <TableCell>
                      {!it.paid ? <span className="text-muted-foreground">—</span> : <Badge variant={it.rule === "고정" ? "outline" : "secondary"}>{it.rule}</Badge>}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {it.thisYearAmount !== null ? won(it.thisYearAmount) : it.thisYearUnknown ? <span className="font-medium text-action">미확인</span> : <span className="text-muted-foreground">—</span>}
                    </TableCell>
                    <TableCell>
                      {url ? (
                        <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-sm underline-offset-4 hover:underline">
                          {it.siteName || "홈페이지"} <ExternalLinkIcon className="size-3" />
                        </a>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </TableCell>
                    <TableCell className="max-w-48 truncate text-muted-foreground" title={it.memo}>{it.memo}</TableCell>
                    <TableCell className="pr-4">
                      <DropdownMenu>
                        <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={`${it.name} 동작`} />}>
                          <MoreHorizontalIcon />
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end" className="w-44">
                          <DropdownMenuItem onClick={() => app.openItem(it)}>
                            <PencilIcon /> 수정
                          </DropdownMenuItem>
                          {it.group && (
                            <DropdownMenuItem onClick={() => app.openGroup(Number(year), it.group)}>
                              <LayersIcon /> 회차 금액
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuSeparator />
                          <DropdownMenuItem variant="destructive" onClick={() => remove(it)}>
                            <Trash2Icon /> 삭제
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  )
}
