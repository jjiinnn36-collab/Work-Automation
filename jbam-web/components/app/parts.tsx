"use client"

import * as React from "react"
import {
  ArrowLeftIcon,
  CheckIcon, ExternalLinkIcon, FileTextIcon, HistoryIcon, MoreHorizontalIcon, PaperclipIcon,
  RotateCcwIcon, ClockIcon, ArrowRightIcon, Undo2Icon, PencilIcon,
} from "lucide-react"

import { cn } from "@/lib/utils"
import { errorMessage, post } from "@/lib/api"
import { md, parseWon, safeUrl, won } from "@/lib/logic"
import type { Occurrence } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { toast } from "@/components/ui/toast"

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
  label, value, unit = "건", tone = "default", fit = false,
}: {
  label: string; value: number | string; unit?: string; tone?: "default" | "danger" | "action"
  /**
   * 긴 값(금액·날짜 범위)을 한 줄에 맞춰 글자만 줄인다 — 카드 높이는 그대로 (사용자 요청 2026-09-18).
   * 글자 크기를 칸 너비와 글자 수로 함께 정하므로, 금액이 길어져도 좌우 여백이 그대로 남는다 (사용자 요청 2026-09-23).
   */
  fit?: boolean
}) {
  const zero = value === 0
  const danger = tone === "danger" && !zero
  const action = tone === "action" && !zero
  const 글 = typeof value === "number" ? won(value) : value

  // 칸 너비(100cqi = CardHeader 의 좌우 여백을 뺀 안쪽 너비)를 글자 수로 나눠 글자 크기를 정한다.
  // 0.55 는 '427,313,089' 처럼 쉼표가 섞인 숫자 한 글자의 평균 가로폭(em)을 실제로 재어 잡은 값이고,
  // 글꼴이 없는 PC 에서 대체 글꼴로 조금 넓어져도 넘치지 않도록 넉넉히 잡았다.
  // 단위('원')는 크기가 고정이라 그 폭만큼 미리 뺀다.
  const 글자수 = Math.max(4, String(글).length * 0.55)
  const 글자크기 = fit
    ? `clamp(0.75rem, calc((100cqi - ${unit ? "1.25rem" : "0.25rem"}) / ${글자수.toFixed(2)}), 1.875rem)`
    : undefined

  return (
    <Card size="sm" className={cn(danger && "ring-destructive/40")}>
      <CardHeader>
        <CardDescription className={cn(danger && "text-destructive", action && "text-action")}>{label}</CardDescription>
        {/* CardTitle 은 작은 카드에서 글자를 줄이므로 숫자는 따로 그린다. */}
        <div
          className={cn(
            "font-heading text-2xl leading-tight font-semibold tabular-nums sm:text-3xl",
            // 칸 높이는 한 줄 기준으로 고정하고, 글자 크기는 아래 style 이 정한다.
            fit && "flex h-[1.875rem] items-center overflow-hidden whitespace-nowrap sm:h-[2.34375rem]",
            zero && "text-muted-foreground",
            danger && "text-destructive",
            action && "text-action"
          )}
          style={글자크기 ? { fontSize: 글자크기 } : undefined}
        >
          {글}
          {unit && <span className="ml-0.5 text-sm font-medium">{unit}</span>}
        </div>
      </CardHeader>

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
  if (o.beforeStart)
    return (
      <Badge variant="outline" className="text-muted-foreground">
        추적 시작 전
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

/**
 * 금액 칸: ✎ 를 누르면 그 칸이 입력칸으로 바뀌고 저장·취소만 있다 (ADR-0023, 사용자 요청).
 * Enter = 저장, Esc = 취소.
 */
function InlineAmount({ o, className }: { o: Occurrence; className?: string }) {
  const app = useApp()
  const word = o.loan ? "지급액" : "금액"
  const [editing, setEditing] = React.useState(false)
  const [value, setValue] = React.useState("")
  const [busy, setBusy] = React.useState(false)
  const inputRef = React.useRef<HTMLInputElement>(null)

  React.useEffect(() => {
    if (editing) inputRef.current?.select()
  }, [editing])

  async function save() {
    const v = parseWon(value)
    if (v === null) {
      toast.add({ title: "원 단위 숫자로 넣어 주세요", type: "error" })
      inputRef.current?.focus()
      return
    }
    if (v === o.amount) {
      setEditing(false)
      return
    }
    setBusy(true)
    try {
      await post("/api/amount", { y: o.year, id: o.id, amount: v })
      toast.add({ title: `${word}을 저장했습니다`, type: "success" })
      setEditing(false)
      app.refresh()
    } catch (e) {
      toast.add({ title: "저장하지 못했습니다", description: errorMessage(e), type: "error" })
    } finally {
      setBusy(false)
    }
  }

  if (!editing)
    return (
      // ✎ 는 금액 앞에 둔다 — '원' 이 다른 금액·'미확인 · 입력' 과 같은 오른쪽 끝에 맞게 (사용자 요청 2026-09-18).
      <span className={cn("inline-flex items-center gap-1 font-semibold tabular-nums", className)}>
        <Button
          variant="ghost"
          size="icon-xs"
          aria-label={`${o.name} ${word} 고치기`}
          title={`${word} 고치기`}
          onClick={() => {
            setValue(won(o.amount))
            setEditing(true)
          }}
        >
          <PencilIcon />
        </Button>
        {won(o.amount)}원
      </span>
    )
  return (
    <form
      className={cn("inline-flex items-center gap-1", className)}
      onSubmit={(e) => {
        e.preventDefault()
        save()
      }}
    >
      <Input
        ref={inputRef}
        inputMode="numeric"
        aria-label={`${o.name} ${word}`}
        className="h-7 w-32 text-right tabular-nums"
        value={value}
        disabled={busy}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Escape") {
            e.preventDefault()
            e.stopPropagation()
            setEditing(false)
          }
        }}
      />
      <Button type="submit" size="xs" disabled={busy}>
        저장
      </Button>
      <Button type="button" size="xs" variant="outline" disabled={busy} onClick={() => setEditing(false)}>
        취소
      </Button>
    </form>
  )
}

/** 금액 칸. 모르면 그 자리에서 [입력] (AC-W29), 시작일 이전 건은 입력을 숨긴다 (AC-W31). */
/** readOnly: 금액을 보여 주기만 한다 (받은 알림, 사용자 요청 2026-09-18). 금액이 없을 때의 '미확인 · 입력' 은 그대로. */
export function AmountButton({ o, className, readOnly }: { o: Occurrence; className?: string; readOnly?: boolean }) {
  const app = useApp()
  if (!o.paid) return <None className={className}>해당 없음</None>
  if (readOnly && o.amount !== null) return <span className={cn("font-semibold tabular-nums", className)}>{won(o.amount)}원</span>
  // 금액이 있으면 모든 건이 같은 모양: ✎ + 금액, 그 칸에서 바로 고친다 (사용자 요청 2026-09-18).
  if (o.amount !== null && !o.beforeStart) return <InlineAmount o={o} className={className} />
  if (o.amount !== null) return <span className={cn("font-semibold tabular-nums", className)}>{won(o.amount)}원</span>
  if (o.beforeStart) return <span className={cn("text-muted-foreground", className)}>미확인</span>
  return (
    <Button variant="ghost" size="sm" className={cn("-mx-2.5 text-sm font-semibold text-action", className)} onClick={() => app.openAmount(o)}>
      미확인 · 입력
    </Button>
  )
}

/** 빈 칸 표시: 작고 흐린 글씨. 적을 수 있는데 비었으면 '없음', 원래 없는 칸이면 '해당 없음' (사용자 요청 2026-09-18). */
export function None({ children = "없음", className }: { children?: React.ReactNode; className?: string }) {
  return <span className={cn("text-xs font-normal text-muted-foreground/70", className)}>{children}</span>
}

type SiteSource = { siteUrl?: string; siteName?: string; orgSiteUrl?: string; orgSiteName?: string }

/**
 * 기관명 옆 작은 '바깥으로 열기' 아이콘. 새 창으로 연다.
 * 같은 기관의 한 건에만 주소가 있어도 서버가 orgSiteUrl 로 넘겨 모든 줄에 붙는다.
 */
export function SiteIcon({ o }: { o: SiteSource }) {
  const raw = o.orgSiteUrl || o.siteUrl || ""
  const url = raw ? safeUrl(raw) : null
  if (!url) return null
  const label = `${(o.orgSiteUrl ? o.orgSiteName : o.siteName) || "홈페이지"} 열기`
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={`${label} (새 창)`}
      title={label}
      className="inline-flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground outline-none hover:bg-muted hover:text-action focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <ExternalLinkIcon className="size-3.5" />
    </a>
  )
}

/** 기관명 + 홈페이지 아이콘. 목록·카드 어디서나 같은 모양. */
export function OrgName({ o, className }: { o: { org: string } & SiteSource; className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-0.5", className)}>
      {o.org}
      <SiteIcon o={o} />
    </span>
  )
}

/** 증빙자료 열기. 홈페이지는 기관명 옆 아이콘으로 옮겼다 (사용자 요청 2026-09-18). */
export function SiteOrDocs({ o, size = "sm" }: { o: Occurrence; size?: "sm" | "xs" }) {
  const app = useApp()
  return (
    <>
      {!o.done && !o.beforeStart && (
        <Button variant="outline" size={size} onClick={() => app.openDocs(o)}>
          <FileTextIcon data-icon="inline-start" /> 증빙자료{o.attachments > 0 ? ` ${o.attachments}` : ""}
        </Button>
      )}
    </>
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
      <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label={`${o.name} 더보기`} />}>
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
          {o.deferredToday && (
            <DropdownMenuItem onClick={() => app.act("undefer", o)}>
              <Undo2Icon /> &lsquo;오늘은 대기&rsquo; 취소
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
          <DropdownMenuItem onClick={() => app.openDocs(o)}>
            <PaperclipIcon /> 증빙자료{o.attachments > 0 ? ` (${o.attachments})` : ""}
          </DropdownMenuItem>
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

/** 창 왼쪽 위의 '← 뒤로가기'. 차입 스케줄 창도 같은 것을 쓴다. */
export function BackButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="-ml-1 mb-1 inline-flex w-fit items-center gap-1 rounded-md px-1 text-xs text-muted-foreground outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <ArrowLeftIcon className="size-3.5" /> 뒤로가기
    </button>
  )
}
