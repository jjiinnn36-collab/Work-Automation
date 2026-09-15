"use client"

import * as React from "react"
import { ArrowDownIcon, ArrowUpIcon, GripHorizontalIcon, PlusIcon, Trash2Icon } from "lucide-react"

import { errorMessage, post } from "@/lib/api"
import { parseMonths, parseWon, won } from "@/lib/logic"
import type { FlowName, Item } from "@/lib/types"
import { useDraggable } from "@/hooks/use-draggable"
import { useApp } from "@/components/app/app-context"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldSeparator } from "@/components/ui/field"
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

const PRESETS: Record<Exclude<FlowName, "사용자설정">, string[]> = {
  신고납부: ["신고서 작성", "신고", "전표발행", "납부"],
  납부만: ["고지서수령", "전표발행", "납부"],
  제출만: ["제출자료 작성", "제출"],
}

const DEFAULT_STEPS: Step[] = [
  { name: "안내 받음", action: "" },
  { name: "처리", action: "처리하기" },
  { name: "완료", action: "완료하기" },
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
 * 항목 추가·수정 (AC-W7, W25~W27, W32, W38~W42, W92, ADR-0015).
 * id 는 서버가 만든다. 머리 부분을 끌어 창을 옮길 수 있다.
 * 진행흐름 '사용자설정' 은 단계 수·이름·버튼 문구를 직접 정한다.
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

  React.useEffect(() => {
    if (!open) return
    setF(fromItem(item, app.today))
    setAmounts({})
    setAmountYear(app.today.slice(0, 4))
    setSource("")
    setError(null)
  }, [open, item, app.today])

  const set = <K extends keyof Form>(k: K, v: Form[K]) => {
    setF((prev) => ({ ...prev, [k]: v }))
    setError(null)
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
  const preview = custom ? f.steps.map((s) => s.name.trim() || "…") : PRESETS[f.flow as keyof typeof PRESETS]

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
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl" style={drag.style}>
        <DialogHeader {...drag.handleProps} className={`${drag.handleProps.className} -mx-4 -mt-4 rounded-t-xl px-4 pt-4 pb-1`}>
          <div className="flex items-center gap-2 pr-8">
            <DialogTitle>{isNew ? "항목 추가" : `${item.name} 수정`}</DialogTitle>
            <GripHorizontalIcon className="size-4 text-muted-foreground/60" aria-hidden />
          </div>
          <DialogDescription>
            기관·비용명·기한·진행흐름을 정합니다. 그 해 실제 금액은 이번 달·연간 화면에서 넣습니다.
            {!isNew && <span className="ml-1 tabular-nums">(id {item.id})</span>}
          </DialogDescription>
        </DialogHeader>
        <form
          onSubmit={(e) => {
            e.preventDefault()
            save()
          }}
          className="flex flex-col gap-5"
        >
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel htmlFor="it-org">기관</FieldLabel>
              <Input id="it-org" required maxLength={60} value={f.org} onChange={(e) => set("org", e.target.value)} />
            </Field>
            <Field>
              <FieldLabel htmlFor="it-name">비용명</FieldLabel>
              <Input id="it-name" required maxLength={60} value={f.name} onChange={(e) => set("name", e.target.value)} />
            </Field>
            <Field className="sm:col-span-2">
              <FieldLabel htmlFor="it-flow">진행흐름</FieldLabel>
              <NativeSelect id="it-flow" className="w-full" value={f.flow} onChange={(e) => set("flow", e.target.value as FlowName)}>
                <NativeSelectOption value="신고납부">신고납부 · 신고서 작성 → 신고 → 전표발행 → 납부</NativeSelectOption>
                <NativeSelectOption value="납부만">납부만 · 고지서수령 → 전표발행 → 납부</NativeSelectOption>
                <NativeSelectOption value="제출만">제출만 · 제출자료 작성 → 제출</NativeSelectOption>
                <NativeSelectOption value="사용자설정">사용자설정 · 단계를 직접 정함</NativeSelectOption>
              </NativeSelect>
              <FieldDescription className="flex flex-wrap items-center gap-1">
                {preview.map((s, i) => (
                  <React.Fragment key={i}>
                    {i > 0 && <span aria-hidden>→</span>}
                    <span className={i === 0 ? "" : "font-medium text-foreground"}>{s}</span>
                  </React.Fragment>
                ))}
              </FieldDescription>
            </Field>
          </FieldGroup>

          {custom && (
            <div className="flex flex-col gap-3 rounded-lg border bg-muted/30 p-3">
              <div className="flex items-center justify-between gap-2">
                <div>
                  <p className="text-sm font-medium">단계 {f.steps.length}개</p>
                  <p className="text-xs text-muted-foreground">
                    첫 단계는 시작 지점입니다. 버튼 문구는 알림에서 그 단계로 넘어갈 때 누르는 버튼 이름이며, 비우면 단계 이름을 씁니다.
                  </p>
                </div>
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
              <ol className="flex flex-col gap-2">
                {f.steps.map((s, i) => (
                  <li key={i} className="grid grid-cols-[1.5rem_1fr_auto] items-center gap-2 sm:grid-cols-[1.5rem_1fr_1fr_auto]">
                    <span className="text-center text-xs font-medium text-muted-foreground tabular-nums">{i + 1}</span>
                    <Input
                      aria-label={`${i + 1}번째 단계 이름`}
                      placeholder={i === 0 ? "시작 지점 (예: 안내 받음)" : "단계 이름"}
                      maxLength={20}
                      value={s.name}
                      onChange={(e) => setStep(i, { name: e.target.value })}
                    />
                    <Input
                      aria-label={`${i + 1}번째 버튼 문구`}
                      className="col-start-2 sm:col-start-auto"
                      placeholder={i === 0 ? "시작 지점은 버튼 없음" : `버튼 문구 (비우면 '${s.name.trim() || "단계 이름"}')`}
                      maxLength={20}
                      disabled={i === 0}
                      value={i === 0 ? "" : s.action}
                      onChange={(e) => setStep(i, { action: e.target.value })}
                    />
                    <div className="col-start-3 row-start-1 flex sm:col-start-4 sm:row-start-auto">
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

          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel htmlFor="it-month">기한 월</FieldLabel>
              <Input id="it-month" required value={f.month} onChange={(e) => set("month", e.target.value)} inputMode="numeric" />
              <FieldDescription>{isNew ? "분할납부는 쉼표로: 5,6,7,8,9,10 → 회차마다 항목이 생깁니다." : "1~12"}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel htmlFor="it-day">기한일</FieldLabel>
              <NativeSelect id="it-day" className="w-full" value={f.day} onChange={(e) => set("day", e.target.value)}>
                {DAYS.map((d) => (
                  <NativeSelectOption key={d} value={d}>
                    {d === "말일" ? "말일 (윤년 자동)" : `${d}일`}
                  </NativeSelectOption>
                ))}
              </NativeSelect>
              <FieldDescription>주말·공휴일이면 다음 영업일로 자동 보정됩니다.</FieldDescription>
            </Field>
            <Field>
              <FieldLabel htmlFor="it-lead">알림</FieldLabel>
              <Input id="it-lead" type="number" min={1} max={60} value={f.lead} onChange={(e) => set("lead", e.target.value)} />
              <FieldDescription>기한 몇 영업일 전에 알릴지</FieldDescription>
            </Field>
            <Field data-disabled={!paid ? true : undefined}>
              <FieldLabel htmlFor="it-rule">금액규칙</FieldLabel>
              <NativeSelect id="it-rule" className="w-full" disabled={!paid} value={f.rule} onChange={(e) => set("rule", e.target.value as "고정" | "변동")}>
                <NativeSelectOption value="변동">변동 · 해마다 그 해 금액을 넣음</NativeSelectOption>
                <NativeSelectOption value="고정">고정 · 매년 같은 금액</NativeSelectOption>
              </NativeSelect>
              <FieldDescription>{paid ? "변동은 금액을 비워 둔 채 저장합니다." : "금액이 없는 흐름입니다."}</FieldDescription>
            </Field>
            <Field data-disabled={!paid || f.rule !== "고정" ? true : undefined}>
              <FieldLabel htmlFor="it-fixed">고정금액 (원)</FieldLabel>
              <Input
                id="it-fixed"
                inputMode="numeric"
                className="text-right tabular-nums"
                disabled={!paid || f.rule !== "고정"}
                value={f.fixed}
                onChange={(e) => set("fixed", e.target.value)}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="it-site-name">신고 홈페이지 이름</FieldLabel>
              <Input id="it-site-name" maxLength={40} placeholder="예: 홈택스" value={f.siteName} onChange={(e) => set("siteName", e.target.value)} />
            </Field>
            <Field>
              <FieldLabel htmlFor="it-site-url">홈페이지 주소</FieldLabel>
              <Input id="it-site-url" type="url" placeholder="https://" value={f.siteUrl} onChange={(e) => set("siteUrl", e.target.value)} />
              <FieldDescription>비워 두면 그 자리에 &lsquo;문서 열기&rsquo; 가 나옵니다.</FieldDescription>
            </Field>
            <Field className="sm:col-span-2">
              <FieldLabel htmlFor="it-memo">비고</FieldLabel>
              <Textarea id="it-memo" maxLength={200} value={f.memo} onChange={(e) => set("memo", e.target.value)} />
            </Field>
          </FieldGroup>

          {multi && paid && (
            <>
              <FieldSeparator>회차별 금액 (선택)</FieldSeparator>
              <p className="-mt-2 text-sm text-muted-foreground">
                안내문에 적힌 회차 금액을 그대로 넣습니다. 비워 둔 회차는 &lsquo;미확인&rsquo; 으로 남고 나중에 입력할 수 있습니다.
                금액을 나눠 채우지 않습니다.
              </p>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel htmlFor="it-amount-year">연도</FieldLabel>
                  <Input id="it-amount-year" inputMode="numeric" value={amountYear} onChange={(e) => setAmountYear(e.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="it-source">출처</FieldLabel>
                  <Input id="it-source" maxLength={60} placeholder="예: 회비 안내문" value={source} onChange={(e) => setSource(e.target.value)} />
                </Field>
              </div>
              <div className="rounded-lg border">
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
                      <TableCell>회차 합계</TableCell>
                      <TableCell className="text-right font-semibold tabular-nums">{won(installmentSum)}원</TableCell>
                    </TableRow>
                  </TableFooter>
                </Table>
              </div>
            </>
          )}

          {error && <FieldError>{error}</FieldError>}

          <DialogFooter>
            {!isNew && (
              <Button type="button" variant="destructive" className="sm:mr-auto" onClick={remove}>
                항목 삭제
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              취소
            </Button>
            <Button type="submit" disabled={busy}>
              {busy && <Spinner />} {isNew ? (multi ? `회차 ${months.months.length}건 만들기` : "추가") : "저장"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
