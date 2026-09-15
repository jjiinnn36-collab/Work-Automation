"use client"

import * as React from "react"
import {
  CheckIcon, ExternalLinkIcon, FileTextIcon, HistoryIcon, MoreHorizontalIcon, PaperclipIcon,
  RotateCcwIcon, WalletIcon, LayersIcon, ClockIcon, ArrowRightIcon,
} from "lucide-react"

import { cn } from "@/lib/utils"
import { md, safeUrl, won } from "@/lib/logic"
import type { Occurrence } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Badge } from "@/components/ui/badge"
import { Button, buttonVariants } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"

export function PageHeader({ title, description, actions }: { title: string; description?: React.ReactNode; actions?: React.ReactNode }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

/** 집계 칸. 0 건이면 조용히, 경고 칸은 건수가 있을 때만 빨갛게 (AC-W57, W79). */
export function StatCard({
  label, value, unit = "건", hint, tone = "default",
}: { label: string; value: number | string; unit?: string; hint?: React.ReactNode; tone?: "default" | "danger" | "action" }) {
  const zero = value === 0
  const danger = tone === "danger" && !zero
  const action = tone === "action" && !zero
  return (
    <Card size="sm" className={cn(danger && "ring-destructive/40")}>
      <CardHeader>
        <CardDescription className={cn(danger && "text-destructive", action && "text-action")}>{label}</CardDescription>
        <CardTitle
          className={cn(
            "text-3xl font-semibold tabular-nums",
            zero && "text-muted-foreground",
            danger && "text-destructive",
            action && "text-action"
          )}
        >
          {typeof value === "number" ? won(value) : value}
          {unit && <span className="ml-0.5 text-base font-medium">{unit}</span>}
        </CardTitle>
      </CardHeader>
      {hint && <CardContent className="truncate text-xs text-muted-foreground">{hint}</CardContent>}
    </Card>
  )
}

export function StatusBadge({ o }: { o: Occurrence }) {
  if (o.done)
    return (
      <Badge variant="outline" className="text-muted-foreground">
        <CheckIcon /> 처리 완료
      </Badge>
    )
  if (o.severity === "overdue") return <Badge variant="destructive">{o.statusText}</Badge>
  if (o.severity === "soon") return <Badge variant="secondary">{o.statusText}</Badge>
  return (
    <Badge variant="outline" className="text-muted-foreground">
      {o.statusText}
    </Badge>
  )
}

/** 원기한과 실제 납부일. 주말·공휴일로 밀리면 실제 날짜를 같이 적는다. */
export function DueText({ o, className }: { o: Occurrence; className?: string }) {
  return (
    <span className={cn("tabular-nums", className)}>
      {md(o.due)} ({o.dueDow})
      {o.shifted && (
        <span className="text-muted-foreground">
          {" "}
          → {md(o.payDue)} ({o.payDow})
        </span>
      )}
    </span>
  )
}

/** 금액 칸. 모르면 그 자리에서 [입력] (AC-W29), 시작일 이전 건은 입력을 숨긴다 (AC-W31). */
export function AmountButton({ o, className }: { o: Occurrence; className?: string }) {
  const app = useApp()
  if (!o.paid) return <span className={cn("text-muted-foreground", className)}>—</span>
  if (o.amount !== null)
    return (
      <Button
        variant="ghost"
        size="sm"
        className={cn("-mx-2 font-semibold tabular-nums", className)}
        onClick={() => app.openAmount(o)}
        title="금액 고치기"
      >
        {won(o.amount)}원
      </Button>
    )
  if (o.beforeStart) return <span className={cn("text-muted-foreground", className)}>미확인</span>
  return (
    <Button variant="ghost" size="sm" className={cn("-mx-2 font-semibold text-action", className)} onClick={() => app.openAmount(o)}>
      미확인 · 입력
    </Button>
  )
}

/** 신고 홈페이지가 있으면 그 링크, 없으면 받은 문서 열기 (AC-W93, W94). */
export function SiteOrDocs({ o, size = "sm" }: { o: Occurrence; size?: "sm" | "xs" }) {
  const app = useApp()
  const url = o.siteUrl ? safeUrl(o.siteUrl) : null
  if (url)
    return (
      <a href={url} target="_blank" rel="noopener noreferrer" className={buttonVariants({ variant: "outline", size })}>
        {o.siteName || "홈페이지"} <ExternalLinkIcon data-icon="inline-end" />
      </a>
    )
  return (
    <Button variant="outline" size={size} onClick={() => app.openDocs(o, "받은문서")}>
      <FileTextIcon data-icon="inline-start" /> 문서 열기
    </Button>
  )
}

/** 지금 해야 할 행동 버튼. 다 끝났으면 그리지 않는다. */
export function AdvanceButton({ o, size = "sm" }: { o: Occurrence; size?: "sm" | "default" | "xs" }) {
  const app = useApp()
  const [busy, setBusy] = React.useState(false)
  if (o.done || o.beforeStart) return null
  return (
    <Button
      size={size}
      disabled={busy}
      onClick={async () => {
        setBusy(true)
        try {
          await app.act("advance", o)
        } finally {
          setBusy(false)
        }
      }}
    >
      {o.nextAction} <ArrowRightIcon data-icon="inline-end" />
    </Button>
  )
}

/** 한 건에 대한 나머지 동작 모음 */
export function RowMenu({ o }: { o: Occurrence }) {
  const app = useApp()
  const url = o.siteUrl ? safeUrl(o.siteUrl) : null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={`${o.name} 동작`} />}>
        <MoreHorizontalIcon />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-52">
        <DropdownMenuGroup>
          <DropdownMenuLabel>
            {o.name} · {o.stageName}
          </DropdownMenuLabel>
          {!o.done && !o.beforeStart && (
            <DropdownMenuItem onClick={() => app.act("advance", o)}>
              <ArrowRightIcon /> {o.nextAction}
            </DropdownMenuItem>
          )}
          {!o.done && !o.beforeStart && !o.confirmedToday && (
            <DropdownMenuItem onClick={() => app.act("defer", o)}>
              <ClockIcon /> 오늘은 대기
            </DropdownMenuItem>
          )}
          {o.stage > 0 && (
            <DropdownMenuItem onClick={() => app.act("revert", o)}>
              <RotateCcwIcon /> 한 단계 되돌리기
            </DropdownMenuItem>
          )}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem onClick={() => app.openDocs(o, "받은문서")}>
            <FileTextIcon /> 받은 문서
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => app.openDocs(o, "증빙")}>
            <PaperclipIcon /> 증빙{o.attachments > 0 ? ` (${o.attachments})` : ""}
          </DropdownMenuItem>
          {o.paid && !o.beforeStart && (
            <DropdownMenuItem onClick={() => app.openAmount(o)}>
              <WalletIcon /> 금액 입력
            </DropdownMenuItem>
          )}
          {o.group && (
            <DropdownMenuItem onClick={() => app.openGroup(o.year, o.group)}>
              <LayersIcon /> 회차 금액 한 번에
            </DropdownMenuItem>
          )}
          <DropdownMenuItem onClick={() => app.openEvents({ year: o.year, id: o.id, name: o.name })}>
            <HistoryIcon /> 변경 기록
          </DropdownMenuItem>
          {url && (
            <DropdownMenuItem onClick={() => window.open(url, "_blank", "noopener,noreferrer")}>
              <ExternalLinkIcon /> {o.siteName || "홈페이지"} 열기
            </DropdownMenuItem>
          )}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

export function LoadError({ message }: { message: string }) {
  return (
    <div role="alert" className="rounded-lg border border-destructive/30 bg-destructive/5 p-4 text-sm text-destructive">
      자료를 불러오지 못했습니다: {message}
    </div>
  )
}
