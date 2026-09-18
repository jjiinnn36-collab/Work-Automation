"use client"

import * as React from "react"
import { ArrowDownIcon, ArrowUpIcon, ChevronRightIcon, FileSpreadsheetIcon, GripHorizontalIcon, NotebookPenIcon, PlusIcon, Trash2Icon } from "lucide-react"

import { errorMessage, post } from "@/lib/api"
import { dayLabel, FLOW_CARDS, itemSummary, parseMonths, parseWon, won } from "@/lib/logic"
import { cn } from "@/lib/utils"
import type { FlowName, Item } from "@/lib/types"
import { useCloseGuard } from "@/hooks/use-close-guard"
import { useDraggable } from "@/hooks/use-draggable"
import { useApp } from "@/components/app/app-context"
import { LoanImportDialog } from "@/components/app/dialogs/loan-import-dialog"
import { BackButton } from "@/components/app/parts"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { Spinner } from "@/components/ui/spinner"
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"

interface Step {
  name: string
  action: string
}

interface Form {
  org: string
  name: string
  flow: FlowName
  steps: Step[]
  noAmount: boolean
  month: string
  day: string
  lead: string
  rule: "고정" | "변동"
  fixed: string
  siteName: string
  siteUrl: string
  memo: string
}

const DAYS = ["말일", ...Array.from({ length: 31 }, (_, i) => String(i + 1))]
const MIN_STEPS = 2
const MAX_STEPS = 8

const DEFAULT_STEPS: Step[] = [
  { name: "안내 받음", action: "" },
  { name: "처리", action: "처리" },
  { name: "완료", action: "완료" },
]

function fromItem(it: Item | null, today: string): Form {
  if (!it)
    return {
      org: "", name: "", flow: "납부만", steps: DEFAULT_STEPS, noAmount: false,
      month: today ? String(Number(today.slice(5, 7))) : "1",
      day: "말일", lead: "3", rule: "변동", fixed: "", siteName: "", siteUrl: "", memo: "",
    }
  return {
    org: it.org, name: it.name, flow: it.flow,
    steps: it.flow === "사용자설정" ? it.stages.map((name, i) => ({ name, action: i === 0 ? "" : it.actions[i] === name ? "" : it.actions[i] ?? "" })) : DEFAULT_STEPS,
    noAmount: it.noAmount, month: String(it.month), day: it.day,
    lead: String(it.lead), rule: it.rule, fixed: it.fixed !== null ? won(it.fixed) : "",
    siteName: it.siteName, siteUrl: it.siteUrl, memo: it.memo,
  }
}

/** 사용자 단계 검사. 서버(Stages.단계검사)와 같은 규칙. */
function stepsError(steps: Step[]): string | null {
  if (steps.length < MIN_STEPS || steps.length > MAX_STEPS) return `단계는 ${MIN_STEPS}~${MAX_STEPS}개여야 합니다.`
  const seen = new Set<string>()
  for (const [i, s] of steps.entries()) {
    const n = s.name.trim()
    if (!n) return `${i + 1}번째 단계 이름을 넣어 주세요.`
    if (n.length > 20 || s.action.trim().length > 20) return "단계 이름과 버튼 문구는 20자까지입니다."
    if (/[|\n\r]/.test(n + s.action)) return "단계 이름에 | 는 쓸 수 없습니다."
    if (seen.has(n)) return `같은 단계 이름이 두 번 있습니다: ${n}`
    seen.add(n)
  }
  return null
}

/**
 * 항목 추가·수정 (AC-W7, W25~W27, W32, W38~W42, W92, ADR-0015, ADR-0020).
 * 한 칸 흐름에 항목 · 기한 · 진행 방식 · 금액 네 묶음, 홈페이지·비고는 '더 보기' 로 접어 둔다.
 * id 는 서버가 만든다. 머리 부분을 끌어 창을 옮길 수 있다.
 * 진행흐름 '사용자설정' 은 단계 수·이름·버튼 문구를 직접 정한다.
 * 새 항목은 종류(일반 비용 / 차입금 이자)를 먼저 고른다. 차입금 이자는 이 창을 닫고 스케줄 올리기 창을 연다 (ADR-0023).
 */
export function ItemDialog({ item, open, onOpenChange }: { item: Item | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const isNew = item === null
  const drag = useDraggable(open)
  const [f, setF] = React.useState<Form>(() => fromItem(item, app.today))
  const [amounts, setAmounts] = React.useState<Record<number, string>>({})
  const [amountYear, setAmountYear] = React.useState("")
  const [source, setSource] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [more, setMore] = React.useState(false)
  const [snapshot, setSnapshot] = React.useState("")
  const [kind, setKind] = React.useState<"pick" | "normal">("normal")

  React.useEffect(() => {
    if (!open) return
    const init = fromItem(item, app.today)
    setF(init)
    setSnapshot(JSON.stringify(init))
    setAmounts({})
    setAmountYear(app.today.slice(0, 4))
    setSource("")
    setError(null)
    setKind(item ? "normal" : "pick")
    // 홈페이지·비고에 이미 값이 있으면 펼친 채로 연다.
    setMore(Boolean(item && (item.siteName || item.siteUrl || item.memo)))
  }, [open, item, app.today])

  const set = <K extends keyof Form>(k: K, v: Form[K]) => {
    setF((prev) => ({ ...prev, [k]: v }))
    setError(null)
  }

  // 연 뒤로 바꾼 것이 있으면 닫기 전에 묻는다 (ADR-0022).
  const dirty =
    open &&
    snapshot !== "" &&
    (JSON.stringify(f) !== snapshot || Object.values(amounts).some((v) => v.trim() !== "") || source.trim() !== "")
  const guardedClose = useCloseGuard(dirty, onOpenChange, "항목 내용")
  const [loanOpen, setLoanOpen] = React.useState(false)

  /** 차입금 이자를 고르면 이 창을 닫고 차입 스케줄 창을 연다. */
  function openLoanImport() {
    onOpenChange(false)
    setLoanOpen(true)
  }

  /** 차입 스케줄 창의 뒤로가기 — 종류 고르기로 돌아온다. */
  function backFromLoan() {
    setLoanOpen(false)
    app.openItem(null)
  }

  /** 일반 비용 입력에서 종류 고르기로. 입력한 내용이 있으면 먼저 묻는다 (ADR-0022). */
  async function backToPick() {
    if (dirty && !(await app.confirm(backConfirm))) return
    const init = fromItem(null, app.today)
    setF(init)
    setSnapshot(JSON.stringify(init))
    setAmounts({})
    setSource("")
    setError(null)
    setMore(false)
    setKind("pick")
  }

  const setStep = (i: number, patch: Partial<Step>) =>
    set("steps", f.steps.map((s, j) => (j === i ? { ...s, ...patch } : s)))
  const moveStep = (i: number, d: -1 | 1) => {
    const next = [...f.steps]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    set("steps", next)
  }

  const custom = f.flow === "사용자설정"
  const paid = f.flow === "제출만" ? false : custom ? !f.noAmount : true
  const months = parseMonths(f.month)
  const multi = isNew && months.months.length > 1
  const installmentSum = months.months.reduce((s, m) => s + (parseWon(amounts[m] ?? "") ?? 0), 0)

  async function save() {
    if (months.error) {
      setError(months.error)
      return
    }
    if (!isNew && months.months.length > 1) {
      setError("여러 회차는 새로 추가할 때만 만들 수 있습니다. 회차마다 따로 고치세요.")
      return
    }
    if (custom) {
      const e = stepsError(f.steps)
      if (e) {
        setError(e)
        return
      }
    }
    const data: Record<string, string> = {
      mode: isNew ? "new" : "edit",
      id: item?.id ?? "", org: f.org, name: f.name, flow: f.flow, month: months.months.join(","), day: f.day,
      lead: f.lead, rule: paid ? f.rule : "변동", fixed: paid && f.rule === "고정" ? f.fixed : "",
      siteName: f.siteName, siteUrl: f.siteUrl, memo: f.memo,
    }
    if (custom) {
      data.stages = f.steps.map((s) => s.name.trim()).join("\n")
      data.actions = f.steps.map((s, i) => (i === 0 ? "" : s.action.trim())).join("\n")
      data.noAmount = f.noAmount ? "1" : "0"
    }
    if (multi) {
      data.amountYear = amountYear
      data.source = source
      for (const m of months.months) if ((amounts[m] ?? "").trim()) data[`amt_${String(m).padStart(2, "0")}`] = amounts[m]
    }
    setBusy(true)
    try {
      const r = await post<{ popup?: boolean; ids?: string[] }>("/api/item", data)
      toast.add({
        title: multi ? `회차 ${r.ids?.length ?? months.months.length}건을 만들었습니다` : isNew ? "항목을 추가했습니다" : "항목을 저장했습니다",
        description: r.popup ? "오늘 알릴 건이라 알림 팝업을 띄웠습니다." : undefined,
        type: "success",
      })
      onOpenChange(false)
      app.refresh()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!item) return
    const yes = await app.confirm({
      title: `'${item.name}' 항목을 지울까요?`,
      description: "진행 기록·금액·증빙은 기록으로 남습니다.",
      action: "지우기",
      destructive: true,
    })
    if (!yes) return
    try {
      await post("/api/item/delete", { id: item.id })
      toast.add({ title: "항목을 지웠습니다", type: "success" })
      onOpenChange(false)
      app.refresh()
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  return (
    <>
    <LoanImportDialog open={loanOpen} onOpenChange={setLoanOpen} onBack={backFromLoan} />
    <Dialog open={open} onOpenChange={guardedClose}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-0 p-0 sm:max-w-[520px]" style={drag.style}>
        <DialogHeader {...drag.handleProps} className={`${drag.handleProps.className} rounded-t-xl px-5 pt-5 pb-2`}>
          {isNew && kind === "normal" && <BackButton onClick={backToPick} />}
          <div className="flex items-center gap-2 pr-8">
            <DialogTitle>{!isNew ? `${item.name} 수정` : kind === "pick" ? "항목 추가" : "일반 비용"}</DialogTitle>
            <GripHorizontalIcon className="size-4 text-muted-foreground/60" aria-hidden />
          </div>
          <DialogDescription>
            {!isNew ? (
              <span className="tabular-nums">id {item.id}</span>
            ) : kind === "pick" ? (
              "등록할 종류를 고르세요."
            ) : (
              "매년 돌아오는 납부·신고 기한"
            )}
          </DialogDescription>
        </DialogHeader>
        {kind === "pick" ? (
          <>
            <div className="flex flex-col gap-2.5 px-5 pt-2 pb-5" role="group" aria-label="등록할 종류">
              <KindButton
                icon={<NotebookPenIcon />}
                title="일반 비용"
                description="매년 돌아오는 납부·신고 기한"
                examples={["부가세", "분담금", "협회비"]}
                onClick={() => setKind("normal")}
              />
              <KindButton
                icon={<FileSpreadsheetIcon />}
                title="차입금 이자"
                description="ERP 스케줄 파일로 분기 이자 등록"
                onClick={openLoanImport}
              />
            </div>
            <DialogFooter className="m-0 rounded-b-xl px-5 py-3">
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                취소
              </Button>
            </DialogFooter>
          </>
        ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault()
            save()
          }}
          className="flex min-h-0 flex-1 flex-col"
        >
          <div className="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto px-5 pt-2 pb-5">
            <Group title="항목">
              <div className="grid grid-cols-[1fr_1.4fr] gap-3">
                <Field>
                  <FieldLabel htmlFor="it-org">기관</FieldLabel>
                  <Input id="it-org" required maxLength={60} placeholder="예: 국세청" value={f.org} onChange={(e) => set("org", e.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="it-name">비용명</FieldLabel>
                  <Input id="it-name" required maxLength={60} placeholder="예: 부가세" value={f.name} onChange={(e) => set("name", e.target.value)} />
                </Field>
              </div>
            </Group>

            <Group title="기한">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-2 text-sm">
                <span>매년</span>
                <Input
                  id="it-month"
                  required
                  aria-label="기한 월"
                  inputMode="numeric"
                  className={isNew && months.months.length > 1 ? "w-28 text-center" : "w-16 text-center"}
                  value={f.month}
                  onChange={(e) => set("month", e.target.value)}
                />
                <span>월</span>
                <NativeSelect id="it-day" aria-label="기한일" className="w-28" value={f.day} onChange={(e) => set("day", e.target.value)}>
                  {DAYS.map((d) => (
                    <NativeSelectOption key={d} value={d}>
                      {dayLabel(d)}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
                <span>까지</span>
              </div>
              <div className="flex flex-wrap items-center gap-x-2 gap-y-2 text-sm">
                <Input
                  id="it-lead"
                  type="number"
                  min={1}
                  max={60}
                  aria-label="알림 영업일"
                  className="w-16 text-center"
                  value={f.lead}
                  onChange={(e) => set("lead", e.target.value)}
                />
                <span>영업일 전에 알림</span>
              </div>
              <ul className="flex list-disc flex-col gap-0.5 pl-4 text-sm text-muted-foreground">
                <li>휴일이면 다음 영업일로 미뤄집니다.</li>
                {isNew && (
                  <li>
                    여러 달에 나눠 내면 <code className="rounded bg-muted px-1">5,6,7</code>처럼 쉼표로 적습니다.
                  </li>
                )}
              </ul>
            </Group>

            <Group title="진행 방식">
              <div className="grid grid-cols-2 gap-2" role="group" aria-label="진행흐름">
                {FLOW_CARDS.map((c) => {
                  const on = f.flow === c.flow
                  return (
                    <button
                      key={c.flow}
                      type="button"
                      aria-pressed={on}
                      onClick={() => set("flow", c.flow)}
                      className={cn(
                        "flex flex-col gap-0.5 rounded-lg border px-3 py-2.5 text-left transition-colors outline-none",
                        "hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50",
                        on ? "border-foreground ring-1 ring-foreground" : "border-border"
                      )}
                    >
                      <span className="text-sm font-semibold">{c.label}</span>
                      <span className="text-xs text-muted-foreground">
                        {c.flow === "사용자설정" && on ? f.steps.map((s) => s.name.trim() || "…").join(" → ") : c.steps}
                      </span>
                    </button>
                  )
                })}
              </div>

              {custom && (
                <div className="flex flex-col gap-3 rounded-lg border bg-muted/30 p-3">
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-sm font-medium">단계 {f.steps.length}개</p>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={f.steps.length >= MAX_STEPS}
                      onClick={() => set("steps", [...f.steps, { name: "", action: "" }])}
                    >
                      <PlusIcon /> 단계 추가
                    </Button>
                  </div>
                  <p className="-mt-2 text-xs text-muted-foreground">
                    첫 단계는 시작 지점입니다. 버튼 문구를 비우면 단계 이름을 씁니다.
                  </p>
                  <ol className="flex flex-col gap-2">
                    {f.steps.map((s, i) => (
                      <li key={i} className="grid grid-cols-[1.25rem_1fr_1fr_auto] items-center gap-2">
                        <span className="text-center text-xs font-medium text-muted-foreground tabular-nums">{i + 1}</span>
                        <Input
                          aria-label={`${i + 1}번째 단계 이름`}
                          placeholder={i === 0 ? "시작 지점" : "단계 이름"}
                          maxLength={20}
                          value={s.name}
                          onChange={(e) => setStep(i, { name: e.target.value })}
                        />
                        <Input
                          aria-label={`${i + 1}번째 버튼 문구`}
                          placeholder={i === 0 ? "버튼 없음" : "버튼 문구"}
                          maxLength={20}
                          disabled={i === 0}
                          value={i === 0 ? "" : s.action}
                          onChange={(e) => setStep(i, { action: e.target.value })}
                        />
                        <div className="flex">
                          <Button type="button" variant="ghost" size="icon-sm" aria-label="위로" disabled={i === 0} onClick={() => moveStep(i, -1)}>
                            <ArrowUpIcon />
                          </Button>
                          <Button type="button" variant="ghost" size="icon-sm" aria-label="아래로" disabled={i === f.steps.length - 1} onClick={() => moveStep(i, 1)}>
                            <ArrowDownIcon />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon-sm"
                            aria-label="단계 지우기"
                            disabled={f.steps.length <= MIN_STEPS}
                            onClick={() => set("steps", f.steps.filter((_, j) => j !== i))}
                          >
                            <Trash2Icon />
                          </Button>
                        </div>
                      </li>
                    ))}
                  </ol>
                  <label className="flex items-center gap-2 text-sm">
                    <Checkbox checked={f.noAmount} onCheckedChange={(v) => set("noAmount", v === true)} />
                    금액이 없는 건 (제출·보고처럼 돈을 내지 않음)
                  </label>
                </div>
              )}
            </Group>

            {paid && (
              <Group title="금액">
                <div className="flex flex-wrap items-center gap-3">
                  <div className="inline-flex rounded-lg bg-muted p-[3px]" role="group" aria-label="금액규칙">
                    {(["변동", "고정"] as const).map((r) => (
                      <button
                        key={r}
                        type="button"
                        aria-pressed={f.rule === r}
                        onClick={() => set("rule", r)}
                        className={cn(
                          "rounded-md px-4 py-1 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
                          f.rule === r ? "bg-background font-medium text-foreground shadow-xs" : "text-muted-foreground hover:text-foreground"
                        )}
                      >
                        {r}
                      </button>
                    ))}
                  </div>
                  {f.rule === "고정" && (
                    <div className="flex items-center gap-2 text-sm">
                      <Input
                        id="it-fixed"
                        aria-label="고정금액"
                        inputMode="numeric"
                        placeholder="0"
                        className="w-40 text-right tabular-nums"
                        value={f.fixed}
                        onChange={(e) => set("fixed", e.target.value)}
                      />
                      <span>원</span>
                    </div>
                  )}
                </div>
                <FieldDescription>
                  {f.rule === "고정"
                    ? "매년 같은 금액이 자동으로 들어갑니다."
                    : "해마다 그 해 금액을 이번 달·연간 화면에서 넣습니다."}
                </FieldDescription>

                {multi && (
                  <div className="flex flex-col gap-3 rounded-lg border p-3">
                    <p className="text-sm font-medium">회차별 금액 <span className="font-normal text-muted-foreground">(선택 · 비워 두면 미확인)</span></p>
                    <div className="grid grid-cols-[6rem_1fr] gap-3">
                      <Field>
                        <FieldLabel htmlFor="it-amount-year">연도</FieldLabel>
                        <Input id="it-amount-year" inputMode="numeric" value={amountYear} onChange={(e) => setAmountYear(e.target.value)} />
                      </Field>
                      <Field>
                        <FieldLabel htmlFor="it-source">출처</FieldLabel>
                        <Input id="it-source" maxLength={60} placeholder="예: 회비 안내문" value={source} onChange={(e) => setSource(e.target.value)} />
                      </Field>
                    </div>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>회차</TableHead>
                          <TableHead className="w-44 text-right">금액</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {months.months.map((m) => (
                          <TableRow key={m}>
                            <TableCell className="font-medium">{m}월</TableCell>
                            <TableCell>
                              <Input
                                inputMode="numeric"
                                aria-label={`${m}월 금액`}
                                className="text-right tabular-nums"
                                value={amounts[m] ?? ""}
                                onChange={(e) => setAmounts({ ...amounts, [m]: e.target.value })}
                              />
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                      <TableFooter>
                        <TableRow>
                          <TableCell>합계</TableCell>
                          <TableCell className="text-right font-semibold tabular-nums">{won(installmentSum)}원</TableCell>
                        </TableRow>
                      </TableFooter>
                    </Table>
                  </div>
                )}
              </Group>
            )}

            <div className="flex flex-col gap-3">
              <button
                type="button"
                aria-expanded={more}
                aria-controls="it-more"
                onClick={() => setMore(!more)}
                className="inline-flex w-fit items-center gap-1.5 rounded-md text-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                <ChevronRightIcon className={cn("size-4 text-muted-foreground transition-transform motion-reduce:transition-none", more && "rotate-90")} />
                더 보기 <span className="font-normal text-muted-foreground">홈페이지 · 비고 (선택)</span>
              </button>
              {more && (
                <div id="it-more" className="flex flex-col gap-3">
                  <div className="grid grid-cols-[1fr_1.4fr] gap-3">
                    <Field>
                      <FieldLabel htmlFor="it-site-name">홈페이지 이름</FieldLabel>
                      <Input id="it-site-name" maxLength={40} placeholder="예: 홈택스" value={f.siteName} onChange={(e) => set("siteName", e.target.value)} />
                    </Field>
                    <Field>
                      <FieldLabel htmlFor="it-site-url">주소</FieldLabel>
                      <Input id="it-site-url" type="url" placeholder="https://" value={f.siteUrl} onChange={(e) => set("siteUrl", e.target.value)} />
                    </Field>
                  </div>
                  <Field>
                    <FieldLabel htmlFor="it-memo">비고</FieldLabel>
                    <Textarea id="it-memo" rows={2} maxLength={200} value={f.memo} onChange={(e) => set("memo", e.target.value)} />
                  </Field>
                </div>
              )}
            </div>

            <p className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground" aria-live="polite">
              {itemSummary({ name: f.name, month: f.month, day: f.day, flow: f.flow, paid, rule: f.rule, fixed: f.fixed, lead: f.lead })}
            </p>

            {error && <FieldError>{error}</FieldError>}
          </div>

          <DialogFooter className="m-0 rounded-b-xl px-5 py-3">
            {!isNew && (
              <Button type="button" variant="destructive" className="sm:mr-auto" onClick={remove}>
                항목 삭제
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => guardedClose(false)}>
              취소
            </Button>
            <Button type="submit" disabled={busy}>
              {busy && <Spinner />} {isNew ? (multi ? `회차 ${months.months.length}건 만들기` : "추가") : "저장"}
            </Button>
          </DialogFooter>
        </form>
        )}
      </DialogContent>
    </Dialog>
    </>
  )
}

const backConfirm = {
  title: "입력한 내용을 버리고 돌아갈까요?",
  description: "입력한 항목 내용이 저장되지 않고 사라집니다.",
  action: "뒤로가기",
  destructive: true,
}

/** 종류 고르기의 한 칸. 누르면 바로 다음으로 넘어간다. */
function KindButton({
  icon, title, description, examples, onClick,
}: { icon: React.ReactNode; title: string; description: string; examples?: string[]; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex items-start gap-3 rounded-lg border px-4 py-3.5 text-left transition-colors outline-none hover:border-foreground/40 hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground [&_svg]:size-4">{icon}</span>
      <span className="flex min-w-0 flex-col gap-1">
        <span className="text-sm font-semibold">{title}</span>
        <span className="text-sm text-muted-foreground">{description}</span>
        {examples && (
          <span className="mt-1 flex flex-wrap gap-1">
            {examples.map((e) => (
              <span key={e} className="rounded-full border bg-muted/40 px-2 text-xs text-muted-foreground">{e}</span>
            ))}
          </span>
        )}
      </span>
      <ChevronRightIcon className="mt-2 ml-auto size-4 shrink-0 text-muted-foreground" />
    </button>
  )
}

/** 항목 창의 한 묶음: 작은 흐린 제목 + 가는 선 (ADR-0020). */
function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <fieldset className="flex min-w-0 flex-col gap-3">
      <legend className="mb-3 flex w-full items-center gap-2 text-xs font-semibold text-muted-foreground after:h-px after:flex-1 after:bg-border">
        {title}
      </legend>
      {children}
    </fieldset>
  )
}
