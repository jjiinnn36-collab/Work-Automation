"use client"

import * as React from "react"
import { FileSearchIcon, LayersIcon } from "lucide-react"

import { errorMessage, post, upload } from "@/lib/api"
import { parseWon, won } from "@/lib/logic"
import type { ImportResult, Occurrence } from "@/lib/types"
import { useApp } from "@/components/app/app-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Spinner } from "@/components/ui/spinner"
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { toast } from "@/components/ui/toast"

/**
 * 그 해 금액 입력 (AC-W30). 직접 입력과 부가세 납부서 읽기를 한 창에서 고른다.
 * 빈 값으로 저장하면 지우지 않고 입력칸으로 초점만 돌린다 (AC-W89).
 */
export function AmountDialog({ o, open, onOpenChange }: { o: Occurrence | null; open: boolean; onOpenChange: (v: boolean) => void }) {
  const app = useApp()
  const inputRef = React.useRef<HTMLInputElement>(null)
  const [amount, setAmount] = React.useState("")
  const [memo, setMemo] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)
  const [busy, setBusy] = React.useState(false)

  const [file, setFile] = React.useState<File | null>(null)
  const [result, setResult] = React.useState<ImportResult | null>(null)
  const [keepPdf, setKeepPdf] = React.useState(true)

  React.useEffect(() => {
    if (!open || !o) return
    setAmount(o.amountEntered && o.amount !== null ? won(o.amount) : "")
    setMemo("")
    setError(null)
    setFile(null)
    setResult(null)
    setKeepPdf(true)
  }, [open, o])

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

  async function readNotice() {
    if (!file) return
    setBusy(true)
    setResult(null)
    try {
      setResult(await upload<ImportResult>("/api/import/vat", {}, file))
    } catch (e) {
      setResult({ ok: false, message: errorMessage(e) })
    } finally {
      setBusy(false)
    }
  }

  async function applyNotice() {
    if (!result?.ok || !result.id || result.year === undefined || result.amount === undefined || !file) return
    setBusy(true)
    try {
      await post("/api/amount", { y: result.year, id: result.id, amount: String(result.amount), memo: `납부서 판독 (${file.name})` })
      if (keepPdf) await upload("/api/attach", { y: result.year, id: result.id, kind: "받은문서" }, file)
      toast.add({ title: "납부서 금액을 반영했습니다", description: `${result.name} · ${won(result.amount)}원`, type: "success" })
      done()
    } catch (e) {
      setResult({ ...result, ok: false, message: errorMessage(e) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{o.name} 금액</DialogTitle>
          <DialogDescription>
            {o.year}년 · {o.org} · 금액규칙 {o.amountRule}
          </DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="manual">
          <TabsList className="w-full">
            <TabsTrigger value="manual">직접 입력</TabsTrigger>
            <TabsTrigger value="notice">납부서에서 읽기</TabsTrigger>
          </TabsList>

          <TabsContent value="manual" className="pt-3">
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
                  <FieldDescription>고지서·통보문에 적힌 금액을 그대로 넣습니다. 이 해에만 쓰입니다.</FieldDescription>
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
                    onClick={() => {
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
                <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                  취소
                </Button>
                <Button type="submit" disabled={busy}>
                  {busy && <Spinner />} 저장
                </Button>
              </DialogFooter>
            </form>
          </TabsContent>

          <TabsContent value="notice" className="flex flex-col gap-3 pt-3">
            <Field>
              <FieldLabel htmlFor="notice-file">국세청 부가가치세 납부서 (PDF)</FieldLabel>
              <Input
                id="notice-file"
                type="file"
                accept=".pdf,application/pdf"
                onChange={(e) => {
                  setFile(e.target.files?.[0] ?? null)
                  setResult(null)
                }}
              />
              <FieldDescription>
                세목 합계와 문서상 &lsquo;계&rsquo; 가 같을 때만 반영을 제안합니다. 스캔한 통보문·안내문은 직접 입력하세요.
              </FieldDescription>
            </Field>
            <Button variant="outline" disabled={!file || busy} onClick={readNotice}>
              {busy ? <Spinner /> : <FileSearchIcon data-icon="inline-start" />} 판독하기
            </Button>

            {result && !result.ok && (
              <Alert variant="destructive">
                <AlertTitle>반영하지 않습니다</AlertTitle>
                <AlertDescription>
                  {result.message}
                  {result.sum !== undefined && result.total !== undefined && (
                    <span className="mt-1 block tabular-nums">
                      세목 합계 {won(result.sum)}원 · 문서상 계 {won(result.total)}원
                    </span>
                  )}
                </AlertDescription>
              </Alert>
            )}

            {result?.ok && (
              <div className="flex flex-col gap-3">
                <Alert>
                  <AlertTitle>
                    {result.name} ({result.year}년) · 납부기한 {result.due}
                  </AlertTitle>
                  <AlertDescription>
                    검산 통과 — 세목 합계와 문서상 계가 같습니다.
                    {result.id !== o.id && <span className="block font-medium text-foreground">이 납부서는 지금 연 항목이 아니라 {result.name} 에 반영됩니다.</span>}
                    {result.existing != null && <span className="block">지금 저장된 금액 {won(result.existing)}원을 바꿉니다.</span>}
                  </AlertDescription>
                </Alert>
                <Table>
                  <TableBody>
                    {([
                      ["부가가치세", result.vat],
                      ["교육/방위세", result.edu],
                      ["농어촌특별세", result.farm],
                      ["가산금", result.surcharge],
                      ["계", result.total],
                    ] as const).map(([k, v]) => (
                      <TableRow key={k} className={k === "계" ? "font-semibold" : undefined}>
                        <TableCell>{k}</TableCell>
                        <TableCell className="text-right tabular-nums">{won(v ?? 0)}원</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox checked={keepPdf} onCheckedChange={(v) => setKeepPdf(v === true)} /> 이 PDF 를 받은 문서로 함께 보관
                </label>
                <Button disabled={busy} onClick={applyNotice}>
                  {busy && <Spinner />} {won(result.amount ?? 0)}원 반영
                </Button>
              </div>
            )}
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  )
}
