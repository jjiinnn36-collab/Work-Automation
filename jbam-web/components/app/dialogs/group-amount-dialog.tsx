"use client"

import * as React from "react"
import { ChevronLeftIcon, ChevronRightIcon } from "lucide-react"

import { errorMessage, get, post } from "@/lib/api"
import { discardConfirm, groupOverwrites, groupSum, md, overwriteConfirm, parseWon, won } from "@/lib/logic"
import type { GroupData } from "@/lib/types"
import { useCloseGuard } from "@/hooks/use-close-guard"
import { useApp } from "@/components/app/app-context"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { toast } from "@/components/ui/toast"

/**
 * 분할납부 회차 금액을 한 창에서 (AC-W33~W35). 출처는 한 번만, 합계는 늘 보인다.
 * 빈 칸은 건드리지 않는다 — 금액을 나눠 채우지 않는다 (AC-W37).
 */
export function GroupAmountDialog({
  target, open, onOpenChange,
}: { target: { year: number; group: string } | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const [year, setYear] = React.useState(0)
  const [data, setData] = React.useState<GroupData | null>(null)
  const [typed, setTyped] = React.useState<Record<string, string>>({})
  const [source, setSource] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)
  const [busy, setBusy] = React.useState(false)
  const firstRef = React.useRef<HTMLInputElement>(null)

  React.useEffect(() => {
    if (open && target) {
      setYear(target.year)
      setTyped({})
      setSource("")
      setError(null)
    }
  }, [open, target])

  React.useEffect(() => {
    if (!open || !target || !year) return
    setData(null)
    get<GroupData>("/api/group", { y: year, group: target.group })
      .then(setData)
      .catch((e) => setError(errorMessage(e)))
  }, [open, target, year])

  // 입력한 회차 금액·출처가 있으면 닫기 전에 묻는다 (ADR-0022).
  const dirty = open && (Object.values(typed).some((v) => v.trim() !== "") || source.trim() !== "")
  const guardedClose = useCloseGuard(dirty, onOpenChange, "회차 금액")

  if (!target) return null

  /** 해를 바꾸면 입력칸을 비운다 — 적어 둔 금액이 다른 해에 저장되지 않게. 비우기 전에 묻는다. */
  async function changeYear(d: number) {
    if (Object.values(typed).some((v) => v.trim() !== "") && !(await app.confirm(discardConfirm("회차 금액")))) return
    setTyped({})
    setError(null)
    setYear((y) => y + d)
  }

  const rows = data?.rows ?? []
  const invalid = Object.entries(typed).filter(([, v]) => v.trim() !== "" && parseWon(v) === null).map(([k]) => k)
  const sum = groupSum(rows.map((r) => ({ id: r.id, amount: r.amount })), typed)

  async function save() {
    const filled = Object.entries(typed).filter(([, v]) => v.trim() !== "")
    if (filled.length === 0) {
      firstRef.current?.focus()
      return
    }
    if (invalid.length > 0) {
      setError("원 단위 숫자로 넣어 주세요.")
      return
    }
    // 이미 넣은 회차 금액이 바뀌면 바뀌는 회차를 보여 주고 묻는다 (ADR-0022).
    const changes = groupOverwrites(
      rows.map((r) => ({ id: r.id, label: `${Number(r.due.slice(5, 7))}월`, amount: r.amount, entered: r.amountEntered })),
      typed
    )
    if (changes.length > 0 && !(await app.confirm(overwriteConfirm(changes)))) return
    setBusy(true)
    try {
      const payload: Record<string, string | number> = { y: year, group: target!.group, source }
      for (const [id, v] of filled) payload[`amt_${id}`] = String(parseWon(v))
      await post("/api/group/amounts", payload)
      toast.add({ title: `회차 금액 ${filled.length}건을 저장했습니다`, type: "success" })
      onOpenChange(false)
      app.refresh()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  let firstEditable = true
  return (
    <Dialog open={open} onOpenChange={guardedClose}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{data ? `${data.name} 회차 금액` : "회차 금액"}</DialogTitle>
          <DialogDescription className="flex items-center gap-1">
            <Button variant="ghost" size="icon-xs" aria-label="이전 해" onClick={() => changeYear(-1)}>
              <ChevronLeftIcon />
            </Button>
            <span className="tabular-nums">{year}년</span>
            <Button variant="ghost" size="icon-xs" aria-label="다음 해" onClick={() => changeYear(1)}>
              <ChevronRightIcon />
            </Button>
            {data && <span>· {data.org} · 안내문 한 장의 회차를 한 번에 넣습니다</span>}
          </DialogDescription>
        </DialogHeader>

        {!data ? (
          <div className="flex flex-col gap-2">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-8" />
            ))}
          </div>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault()
              save()
            }}
            className="flex flex-col gap-4"
          >
            <div className="max-h-[50vh] overflow-y-auto rounded-lg border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>회차</TableHead>
                    <TableHead>기한</TableHead>
                    <TableHead className="text-right">지금 금액</TableHead>
                    <TableHead className="w-40 text-right">새 금액</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((r) => {
                    const disabled = r.beforeStart === true || !r.paid
                    const isFirst = !disabled && firstEditable
                    if (isFirst) firstEditable = false
                    return (
                      <TableRow key={r.id}>
                        <TableCell className="font-medium">{Number(r.due.slice(5, 7))}월</TableCell>
                        <TableCell className="tabular-nums text-muted-foreground">{md(r.due)}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {r.amount !== null ? `${won(r.amount)}원` : <span className="text-action">미확인</span>}
                        </TableCell>
                        <TableCell>
                          <Input
                            ref={isFirst ? firstRef : undefined}
                            inputMode="numeric"
                            className="text-right tabular-nums"
                            disabled={disabled}
                            aria-label={`${Number(r.due.slice(5, 7))}월 금액`}
                            aria-invalid={invalid.includes(r.id) ? true : undefined}
                            placeholder={disabled ? "시작일 이전" : ""}
                            value={typed[r.id] ?? ""}
                            onChange={(e) => {
                              setTyped({ ...typed, [r.id]: e.target.value })
                              setError(null)
                            }}
                          />
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
                <TableFooter>
                  <TableRow>
                    <TableCell colSpan={3}>회차 합계 · 안내문 총액과 대조하세요</TableCell>
                    <TableCell className="text-right text-base font-semibold tabular-nums">{won(sum)}원</TableCell>
                  </TableRow>
                </TableFooter>
              </Table>
            </div>
            <Field>
              <FieldLabel htmlFor="group-source">출처</FieldLabel>
              <Input id="group-source" value={source} maxLength={60} placeholder="예: 2027년 회비 안내문" onChange={(e) => setSource(e.target.value)} />
            </Field>
            {error && <FieldError>{error}</FieldError>}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => guardedClose(false)}>
                취소
              </Button>
              <Button type="submit" disabled={busy}>
                {busy && <Spinner />} 한 번에 저장
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
