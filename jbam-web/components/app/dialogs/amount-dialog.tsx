"use client"

import * as React from "react"
import { LayersIcon } from "lucide-react"

import { errorMessage, post } from "@/lib/api"
import { amountOverwrite, discardConfirm, overwriteConfirm, parseWon, won } from "@/lib/logic"
import type { Occurrence } from "@/lib/types"
import { useCloseGuard } from "@/hooks/use-close-guard"
import { useApp } from "@/components/app/app-context"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"

/**
 * 그 해 금액 직접 입력. 납부서 자동 판독은 없앴다 — 금액은 사람이 문서를 보고 넣는다 (사용자 결정 Q8, ADR-0012).
 * 빈 값으로 저장하면 지우지 않고 입력칸으로 초점만 돌린다 (AC-W89).
 */
export function AmountDialog({ o, open, onOpenChange }: { o: Occurrence | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const inputRef = React.useRef<HTMLInputElement>(null)
  const [amount, setAmount] = React.useState("")
  const [memo, setMemo] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [initial, setInitial] = React.useState("")

  React.useEffect(() => {
    if (!open || !o) return
    const start = o.amountEntered && o.amount !== null ? won(o.amount) : ""
    setAmount(start)
    setInitial(start)
    setMemo("")
    setError(null)
  }, [open, o])

  // 입력을 바꿨으면 닫기 전에 묻는다 (ADR-0022).
  const dirty = open && (amount !== initial || memo.trim() !== "")
  const guardedClose = useCloseGuard(dirty, onOpenChange, "금액")

  if (!o) return null

  const done = () => {
    onOpenChange(false)
    app.refresh()
  }

  async function save() {
    if (!o) return
    if (amount.trim() === "") {
      inputRef.current?.focus()
      return
    }
    const v = parseWon(amount)
    if (v === null) {
      setError("원 단위 숫자로 넣어 주세요. 쉼표는 있어도 됩니다.")
      inputRef.current?.focus()
      return
    }
    // 이미 넣은 금액을 다른 값으로 바꾸면 한 번 묻는다 (ADR-0022).
    const change = amountOverwrite(o.amount, o.amountEntered, v)
    if (change && !(await app.confirm(overwriteConfirm([{ label: `${o.year}년 ${o.name}`, ...change }])))) return
    setBusy(true)
    try {
      await post("/api/amount", { y: o.year, id: o.id, amount: String(v), memo })
      toast.add({ title: "금액을 저장했습니다", description: `${o.name} · ${won(v)}원`, type: "success" })
      done()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!o) return
    const ok = await app.confirm({
      title: "입력한 금액을 지울까요?",
      description: `${o.year}년 ${o.name} 에 넣은 금액이 지워지고 다시 '미확인' 이 됩니다.`,
      action: "지우기",
      destructive: true,
    })
    if (!ok) return
    try {
      await post("/api/amount/delete", { y: o.year, id: o.id })
      toast.add({ title: "입력한 금액을 지웠습니다", type: "success" })
      done()
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  return (
    <Dialog open={open} disablePointerDismissal onOpenChange={guardedClose}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{o.name} 금액</DialogTitle>
          <DialogDescription>
            {o.year}년 · {o.org} · 금액규칙 {o.amountRule}
          </DialogDescription>
        </DialogHeader>

        <form
          onSubmit={(e) => {
            e.preventDefault()
            save()
          }}
        >
          <FieldGroup>
            <Field data-invalid={error ? true : undefined}>
              <FieldLabel htmlFor="amount-input">금액 (원)</FieldLabel>
              <Input
                id="amount-input"
                ref={inputRef}
                inputMode="numeric"
                autoComplete="off"
                autoFocus
                value={amount}
                placeholder={o.amount !== null ? `${won(o.amount)} (지금 금액)` : "예: 1,234,500"}
                onChange={(e) => {
                  setAmount(e.target.value)
                  setError(null)
                }}
                aria-invalid={error ? true : undefined}
                className="text-right tabular-nums"
              />
              <FieldDescription>고지서·통보문·신고서에 적힌 금액을 그대로 넣습니다. 이 해에만 쓰입니다.</FieldDescription>
              {error && <FieldError>{error}</FieldError>}
            </Field>
            <Field>
              <FieldLabel htmlFor="amount-memo">메모</FieldLabel>
              <Input id="amount-memo" value={memo} maxLength={200} onChange={(e) => setMemo(e.target.value)} placeholder="출처 등 (선택)" />
            </Field>
            {o.group && (
              <Button
                type="button"
                variant="outline"
                onClick={async () => {
                  if (dirty && !(await app.confirm(discardConfirm("금액")))) return
                  onOpenChange(false)
                  app.openGroup(o.year, o.group)
                }}
              >
                <LayersIcon data-icon="inline-start" /> 이 항목의 모든 회차 한 번에 입력
              </Button>
            )}
          </FieldGroup>
          <DialogFooter className="mt-4">
            {o.amountEntered && (
              <Button type="button" variant="destructive" className="sm:mr-auto" onClick={remove}>
                입력한 금액 지우기
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => guardedClose(false)}>
              취소
            </Button>
            <Button type="submit" disabled={busy}>
              {busy && <Spinner />} 저장
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
