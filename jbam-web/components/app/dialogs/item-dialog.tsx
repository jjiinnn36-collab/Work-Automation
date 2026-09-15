"use client"

import * as React from "react"

import { errorMessage, post } from "@/lib/api"
import { parseMonths, parseWon, won } from "@/lib/logic"
import type { FlowName, Item } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldSeparator } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { Spinner } from "@/components/ui/spinner"
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"

interface Form {
  id: string
  org: string
  name: string
  flow: FlowName
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

function fromItem(it: Item | null, today: string): Form {
  if (!it)
    return {
      id: "", org: "", name: "", flow: "납부만", month: today ? String(Number(today.slice(5, 7))) : "1",
      day: "말일", lead: "3", rule: "변동", fixed: "", siteName: "", siteUrl: "", memo: "",
    }
  return {
    id: it.id, org: it.org, name: it.name, flow: it.flow, month: String(it.month), day: it.day,
    lead: String(it.lead), rule: it.rule, fixed: it.fixed !== null ? won(it.fixed) : "",
    siteName: it.siteName, siteUrl: it.siteUrl, memo: it.memo,
  }
}

/**
 * 항목 추가·수정 (AC-W7, W25~W27, W32, W38~W42, W92).
 * 월에 쉼표를 넣으면 분할납부 회차가 만들어지고 회차 금액 표가 폼 안에 나타난다.
 */
export function ItemDialog({ item, open, onOpenChange }: { item: Item | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const isNew = item === null
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

  const paid = f.flow !== "제출만"
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
    const data: Record<string, string> = {
      mode: isNew ? "new" : "edit",
      id: f.id.trim(), org: f.org, name: f.name, flow: f.flow, month: months.months.join(","), day: f.day,
      lead: f.lead, rule: paid ? f.rule : "변동", fixed: paid && f.rule === "고정" ? f.fixed : "",
      siteName: f.siteName, siteUrl: f.siteUrl, memo: f.memo,
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
      description: "진행 기록·금액·증빙은 남습니다. 같은 id 로 다시 만들면 그대로 이어집니다.",
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
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isNew ? "항목 추가" : `${item.name} 수정`}</DialogTitle>
          <DialogDescription>
            기관·비용명·기한·진행흐름을 정합니다. 그 해 실제 금액은 이번 달·연간 화면에서 넣습니다.
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
            <Field>
              <FieldLabel htmlFor="it-id">id</FieldLabel>
              <Input
                id="it-id"
                required
                maxLength={40}
                pattern="[A-Za-z0-9_\-]+"
                value={f.id}
                readOnly={!isNew}
                disabled={!isNew}
                placeholder="예: vat-q3"
                onChange={(e) => set("id", e.target.value)}
              />
              <FieldDescription>{isNew ? "영문·숫자·_·- . 만든 뒤에는 바꿀 수 없습니다 (기록이 id 로 이어집니다)." : "id 는 바꿀 수 없습니다."}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel htmlFor="it-flow">진행흐름</FieldLabel>
              <NativeSelect id="it-flow" className="w-full" value={f.flow} onChange={(e) => set("flow", e.target.value as FlowName)}>
                <NativeSelectOption value="신고납부">신고납부 · 신고서 작성 → 신고 → 전표발행 → 납부</NativeSelectOption>
                <NativeSelectOption value="납부만">납부만 · 고지서수령 → 전표발행 → 납부</NativeSelectOption>
                <NativeSelectOption value="제출만">제출만 · 제출자료 작성 → 제출</NativeSelectOption>
              </NativeSelect>
            </Field>
            <Field>
              <FieldLabel htmlFor="it-month">월</FieldLabel>
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
              <FieldDescription>{paid ? "변동은 금액을 비워 둔 채 저장합니다." : "제출만 흐름은 금액이 없습니다."}</FieldDescription>
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
                      <TableHead>id</TableHead>
                      <TableHead className="w-44 text-right">금액</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {months.months.map((m) => (
                      <TableRow key={m}>
                        <TableCell className="font-medium">{m}월</TableCell>
                        <TableCell className="text-muted-foreground">{`${f.id || "id"}-${String(m).padStart(2, "0")}`}</TableCell>
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
                      <TableCell colSpan={2}>회차 합계</TableCell>
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
